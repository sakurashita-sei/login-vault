using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace LoginVault.App.Clipboard;

/// <summary>
/// Win32 clipboard implementation. Writes CF_UNICODETEXT plus the registered opt-out and token formats in
/// one open/close cycle, and verifies the token while the clipboard is held open before emptying it.
/// </summary>
public sealed class Win32ClipboardDevice : IClipboardDevice
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;
    private const int MaxTokenBytes = 64;

    private readonly Func<IntPtr> _ownerWindow;

    /// <param name="ownerWindow">
    /// Returns the owner HWND (or <see cref="IntPtr.Zero"/>). It is evaluated on arbitrary threads (retry
    /// delays continue on the thread pool), so it must not touch WPF objects; pass a cached handle.
    /// </param>
    public Win32ClipboardDevice(Func<IntPtr>? ownerWindow = null)
    {
        _ownerWindow = ownerWindow ?? (() => IntPtr.Zero);
    }

    public ClipboardSetResult TrySet(ClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!Open())
        {
            return ClipboardSetResult.Busy;
        }

        // 本文のバイト列は一度だけ作り、システムへ渡した後に消去する
        var text = payload.UnicodeTextBytes;
        try
        {
            if (!NativeMethods.EmptyClipboard())
            {
                return ClipboardSetResult.Failed;
            }

            // 除外形式とトークンを先に置き、本文は最後に置く。途中で失敗したら本文が残らないよう空に戻す
            foreach (var entry in payload.RegisteredFormats)
            {
                var format = NativeMethods.RegisterClipboardFormatW(entry.FormatName);
                if (format == 0 || !PutData(format, entry.Data))
                {
                    NativeMethods.EmptyClipboard();
                    return ClipboardSetResult.Failed;
                }
            }

            if (!PutData(CfUnicodeText, text))
            {
                NativeMethods.EmptyClipboard();
                return ClipboardSetResult.Failed;
            }

            return ClipboardSetResult.Success;
        }
        finally
        {
            NativeMethods.CloseClipboard();
            CryptographicOperations.ZeroMemory(text);
        }
    }

    public ClipboardClearResult TryClearIfToken(Guid token)
    {
        var tokenFormat = NativeMethods.RegisterClipboardFormatW(ClipboardPayload.TokenFormat);
        if (tokenFormat == 0)
        {
            return ClipboardClearResult.Failed;
        }

        if (!Open())
        {
            return ClipboardClearResult.Busy;
        }

        try
        {
            // クリップボードを開いたまま照合することで、確認と消去の間に他アプリが割り込めない
            if (!NativeMethods.IsClipboardFormatAvailable(tokenFormat))
            {
                return ClipboardClearResult.NotOurs;
            }

            var handle = NativeMethods.GetClipboardData(tokenFormat);
            if (handle == IntPtr.Zero)
            {
                return ClipboardClearResult.NotOurs;
            }

            var stored = ReadGlobal(handle);
            if (stored is null || !stored.AsSpan().SequenceEqual(token.ToByteArray()))
            {
                return ClipboardClearResult.NotOurs;
            }

            return NativeMethods.EmptyClipboard() ? ClipboardClearResult.Cleared : ClipboardClearResult.Failed;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>Opens the clipboard with the owner window; if that handle is no longer valid (e.g. during exit after the window closed), falls back to no owner.</summary>
    private bool Open()
    {
        var owner = _ownerWindow();
        if (NativeMethods.OpenClipboard(owner))
        {
            return true;
        }

        return owner != IntPtr.Zero && NativeMethods.OpenClipboard(IntPtr.Zero);
    }

    private static bool PutData(uint format, byte[] data)
    {
        var handle = NativeMethods.GlobalAlloc(GmemMoveable, (nuint)Math.Max(data.Length, 1));
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            return false;
        }

        try
        {
            Marshal.Copy(data, 0, pointer, data.Length);
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }

        if (NativeMethods.SetClipboardData(format, handle) == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            return false;
        }

        // 成功した場合、メモリの所有権はシステムに移る
        return true;
    }

    private static byte[]? ReadGlobal(IntPtr handle)
    {
        var size = NativeMethods.GlobalSize(handle);
        if (size == 0 || size > MaxTokenBytes)
        {
            return null;
        }

        var pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new byte[(int)size];
            Marshal.Copy(pointer, buffer, 0, buffer.Length);
            return buffer;
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    // DLL の検索パスはアセンブリ属性（AssemblyInfo.cs）でシステムディレクトリに限定している
    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr GetClipboardData(uint uFormat);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern uint RegisterClipboardFormatW(string lpszFormat);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GlobalFree(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern nuint GlobalSize(IntPtr hMem);
    }
}
