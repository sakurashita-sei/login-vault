using System.IO;
using LoginVault.App.Clipboard;
using LoginVault.App.Locking;
using LoginVault.App.Services;
using LoginVault.App.ViewModels;
using LoginVault.Core.Time;

namespace LoginVault.App.Tests.Support;

/// <summary>A clock and one-shot timer scheduler driven manually by tests.</summary>
internal sealed class FakeTime : IClock, ITimerScheduler
{
    private readonly List<Entry> _timers = new();

    public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 28, 3, 0, 0, TimeSpan.Zero);

    public int PendingTimers => _timers.Count(t => !t.Cancelled);

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var entry = new Entry(UtcNow + delay, callback);
        _timers.Add(entry);
        return entry;
    }

    /// <summary>Moves time forward, firing due timers in order.</summary>
    public void Advance(TimeSpan delta)
    {
        var target = UtcNow + delta;
        while (true)
        {
            var next = _timers.Where(t => !t.Cancelled && t.Due <= target).OrderBy(t => t.Due).FirstOrDefault();
            if (next is null)
            {
                break;
            }

            UtcNow = next.Due;
            _timers.Remove(next);
            next.Callback();
        }

        UtcNow = target;
    }

    private sealed class Entry : IDisposable
    {
        public Entry(DateTimeOffset due, Action callback)
        {
            Due = due;
            Callback = callback;
        }

        public DateTimeOffset Due { get; }

        public Action Callback { get; }

        public bool Cancelled { get; private set; }

        public void Dispose() => Cancelled = true;
    }
}

/// <summary>An in-memory clipboard that remembers who wrote to it.</summary>
internal sealed class FakeClipboardDevice : IClipboardDevice
{
    public ClipboardPayload? Current { get; private set; }

    public bool HoldsForeignContent { get; private set; }

    public int BusySetCount { get; set; }

    public bool FailSet { get; set; }

    public int BusyClearCount { get; set; }

    public int SetAttempts { get; private set; }

    public int ClearAttempts { get; private set; }

    public bool ThrowOnClear { get; set; }

    public int? FirstClearThreadId { get; private set; }

    public List<ClipboardPayload> SetHistory { get; } = new();

    public string? CurrentText => HoldsForeignContent ? "foreign" : Current?.Text;

    public ClipboardSetResult TrySet(ClipboardPayload payload)
    {
        SetAttempts++;
        if (BusySetCount > 0)
        {
            BusySetCount--;
            return ClipboardSetResult.Busy;
        }

        if (FailSet)
        {
            return ClipboardSetResult.Failed;
        }

        Current = payload;
        HoldsForeignContent = false;
        SetHistory.Add(payload);
        return ClipboardSetResult.Success;
    }

    public ClipboardClearResult TryClearIfToken(Guid token)
    {
        ClearAttempts++;
        FirstClearThreadId ??= Environment.CurrentManagedThreadId;
        if (ThrowOnClear)
        {
            throw new InvalidOperationException("テスト用のクリップボード例外");
        }

        if (BusyClearCount > 0)
        {
            BusyClearCount--;
            return ClipboardClearResult.Busy;
        }

        if (HoldsForeignContent || Current is null || Current.Token != token)
        {
            return ClipboardClearResult.NotOurs;
        }

        Current = null;
        return ClipboardClearResult.Cleared;
    }

    /// <summary>Simulates another application copying something.</summary>
    public void SimulateForeignCopy()
    {
        Current = null;
        HoldsForeignContent = true;
    }
}

/// <summary>Scripted dialog answers; records what was asked.</summary>
internal sealed class FakeDialogService : IDialogService
{
    public Queue<string?> TextAnswers { get; } = new();

    public Queue<bool> ConfirmAnswers { get; } = new();

    public bool DefaultConfirmAnswer { get; set; } = true;

    /// <summary>When set, decides confirmations (and can run side effects such as a lock) instead of the queue.</summary>
    public Func<string, bool>? ConfirmHandler { get; set; }

    public List<(string Title, string Message, IReadOnlyList<string>? Details)> Confirms { get; } = new();

    public List<(string Title, string Message, IReadOnlyList<string>? Details)> Messages { get; } = new();

    public Func<AccountEditorViewModel, bool>? EditorHandler { get; set; }

    /// <summary>Async variant of <see cref="EditorHandler"/> for tests that drive the editor's own async operations (for example the generator).</summary>
    public Func<AccountEditorViewModel, Task<bool>>? EditorHandlerAsync { get; set; }

    public Func<EmailChangeImpactViewModel, bool>? ImpactHandler { get; set; }

    public Func<SettingsViewModel, bool>? SettingsHandler { get; set; }

    public Func<PasswordGeneratorViewModel, bool>? GeneratorHandler { get; set; }

    public ChangeMasterPasswordRequest? MasterPasswordAnswer { get; set; }

    public RestoreRequest? RestoreAnswer { get; set; }

    public string? BackupDestinationAnswer { get; set; }

    public int CloseAllCalls { get; private set; }

    /// <summary>When set, <see cref="CloseAll"/> throws after counting the call (simulates a failure while closing dialogs).</summary>
    public bool ThrowOnCloseAll { get; set; }

    public Task<string?> PromptTextAsync(string title, string message, string? initialValue)
    {
        return Task.FromResult(TextAnswers.Count > 0 ? TextAnswers.Dequeue() : null);
    }

    public Task<bool> ConfirmAsync(string title, string message, IReadOnlyList<string>? details = null, string confirmText = "OK", bool isDestructive = false)
    {
        Confirms.Add((title, message, details));
        if (ConfirmHandler is not null)
        {
            return Task.FromResult(ConfirmHandler(message));
        }

        return Task.FromResult(ConfirmAnswers.Count > 0 ? ConfirmAnswers.Dequeue() : DefaultConfirmAnswer);
    }

    public Task ShowMessageAsync(string title, string message, IReadOnlyList<string>? details = null)
    {
        Messages.Add((title, message, details));
        return Task.CompletedTask;
    }

    public Task<bool> ShowAccountEditorAsync(AccountEditorViewModel editor) =>
        EditorHandlerAsync is not null ? EditorHandlerAsync(editor) : Task.FromResult(EditorHandler?.Invoke(editor) ?? false);

    public Task<bool> ShowEmailChangeImpactAsync(EmailChangeImpactViewModel impact) => Task.FromResult(ImpactHandler?.Invoke(impact) ?? false);

    public Task<bool> ShowSettingsAsync(SettingsViewModel settings) => Task.FromResult(SettingsHandler?.Invoke(settings) ?? false);

    public Task<bool> ShowPasswordGeneratorAsync(PasswordGeneratorViewModel generator) => Task.FromResult(GeneratorHandler?.Invoke(generator) ?? false);

    public Task<ChangeMasterPasswordRequest?> ShowChangeMasterPasswordAsync() => Task.FromResult(MasterPasswordAnswer);

    public Task<RestoreRequest?> ShowRestoreAsync() => Task.FromResult(RestoreAnswer);

    public Task<string?> PickBackupDestinationAsync(string suggestedFileName) => Task.FromResult(BackupDestinationAnswer);

    public void CloseAll()
    {
        CloseAllCalls++;
        if (ThrowOnCloseAll)
        {
            throw new InvalidOperationException("テスト用のダイアログ終了失敗");
        }
    }
}

internal sealed class TempDataDir : IDisposable
{
    public TempDataDir()
    {
        Path = System.IO.Path.Combine(AppContext.BaseDirectory, "testdata", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
