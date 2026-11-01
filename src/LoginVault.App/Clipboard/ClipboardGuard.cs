using LoginVault.App.Locking;

namespace LoginVault.App.Clipboard;

public enum CopyOutcome
{
    Copied,
    Busy,
    Failed,

    /// <summary>A newer copy or a lock/exit clear happened while this copy was waiting for a busy clipboard; nothing of it remains.</summary>
    Superseded,
}

/// <summary>
/// Copies secrets with a fresh token per copy and clears the clipboard later only if that same copy
/// is still present. A newer copy cancels the older timer, and foreign content is never touched.
/// </summary>
public sealed class ClipboardGuard
{
    public const int MaxSetAttempts = 5;
    public const int MaxClearAttempts = 3;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    private readonly IClipboardDevice _device;
    private readonly ITimerScheduler _scheduler;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly object _sync = new();
    private IDisposable? _clearTimer;
    private Guid? _currentToken;
    private int _epoch;

    public ClipboardGuard(IClipboardDevice device, ITimerScheduler scheduler, Func<TimeSpan, Task>? delay = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _delay = delay ?? Task.Delay;
    }

    /// <summary>Raised after a timed clear attempt completes (for status display; never carries the text).</summary>
    public event Action<ClipboardClearResult>? ClearAttempted;

    /// <summary>The token of the copy we currently believe is on the clipboard, or null.</summary>
    public Guid? CurrentToken
    {
        get
        {
            lock (_sync)
            {
                return _currentToken;
            }
        }
    }

    public async Task<CopyOutcome> CopyAsync(string text, TimeSpan clearAfter)
    {
        ArgumentNullException.ThrowIfNull(text);
        var token = Guid.NewGuid();
        var payload = ClipboardPayload.ForSecret(text, token);
        int epoch;
        lock (_sync)
        {
            epoch = _epoch;
        }

        var result = ClipboardSetResult.Failed;
        for (var attempt = 1; attempt <= MaxSetAttempts; attempt++)
        {
            if (IsSuperseded(epoch))
            {
                // 待っている間にロック・終了時の消去か新しいコピーがあった。古い要求は書き込まずに終わる
                return CopyOutcome.Superseded;
            }

            try
            {
                result = _device.TrySet(payload);
            }
            catch (Exception)
            {
                // 装置側の例外（P/Invoke 失敗など）は「コピー失敗」として扱い、画面に値を出さず通知する
                result = ClipboardSetResult.Failed;
            }

            if (result != ClipboardSetResult.Busy)
            {
                break;
            }

            if (attempt < MaxSetAttempts)
            {
                await _delay(RetryDelay).ConfigureAwait(false);
            }
        }

        if (result != ClipboardSetResult.Success)
        {
            return result == ClipboardSetResult.Busy ? CopyOutcome.Busy : CopyOutcome.Failed;
        }

        lock (_sync)
        {
            if (_epoch == epoch)
            {
                // 前のコピーのタイマーは破棄し、新しいコピーのトークンだけを有効にする。待機中の古い要求はここで無効になる
                _epoch++;
                _clearTimer?.Dispose();
                _currentToken = token;
                _clearTimer = _scheduler.Schedule(clearAfter, () => _ = RunTimerClearAsync(token));
                return CopyOutcome.Copied;
            }
        }

        // 書き込みと消去（または新しいコピー）が入れ違った: 自分の書き込みだけを取り消して、秘密を残さない
        try
        {
            _device.TryClearIfToken(token);
        }
        catch (Exception)
        {
            // 取り消せなくても呼び出し元に値を返さない。トークンは登録していないのでタイマーも残らない
        }

        return CopyOutcome.Superseded;
    }

    private bool IsSuperseded(int epoch)
    {
        lock (_sync)
        {
            return _epoch != epoch;
        }
    }

    /// <summary>Clears the clipboard now if it still holds our latest copy (used on lock and exit).</summary>
    public async Task<ClipboardClearResult> ClearNowIfOwnAsync()
    {
        Guid token;
        lock (_sync)
        {
            // ロック・終了の合図: 以後、Busy 待ちの古いコピー要求は書き込まずに中止される
            _epoch++;
            if (_currentToken is not { } current)
            {
                return ClipboardClearResult.NotOurs;
            }

            token = current;
        }

        return await ClearTokenAsync(token, fromTimer: false).ConfigureAwait(false);
    }

    private async Task RunTimerClearAsync(Guid token)
    {
        try
        {
            await ClearTokenAsync(token, fromTimer: true).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // タイマー経由の消去は呼び出し元がいないため、例外は通知イベントに変換する
            ClearAttempted?.Invoke(ClipboardClearResult.Failed);
        }
    }

    private async Task<ClipboardClearResult> ClearTokenAsync(Guid token, bool fromTimer)
    {
        lock (_sync)
        {
            if (_currentToken != token)
            {
                // 既に新しいコピーが行われているため、古いタイマーでは何もしない
                return ClipboardClearResult.NotOurs;
            }
        }

        var result = ClipboardClearResult.Failed;
        for (var attempt = 1; attempt <= MaxClearAttempts; attempt++)
        {
            try
            {
                result = _device.TryClearIfToken(token);
            }
            catch (Exception)
            {
                result = ClipboardClearResult.Failed;
            }

            if (result != ClipboardClearResult.Busy)
            {
                break;
            }

            if (attempt < MaxClearAttempts)
            {
                await _delay(RetryDelay).ConfigureAwait(false);
            }
        }

        // Busy や失敗で終わった場合はトークンを残し、ロック・終了時にもう一度消去を試みられるようにする
        if (result is ClipboardClearResult.Cleared or ClipboardClearResult.NotOurs)
        {
            lock (_sync)
            {
                if (_currentToken == token)
                {
                    _currentToken = null;
                    _clearTimer?.Dispose();
                    _clearTimer = null;
                }
            }
        }

        if (fromTimer)
        {
            ClearAttempted?.Invoke(result);
        }

        return result;
    }
}
