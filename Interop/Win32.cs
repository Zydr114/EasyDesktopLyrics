using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EasyDesktopLyrics.Interop;

internal static class Win32
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    /// <summary>低频重申置顶，防止被其他置顶窗口覆盖。</summary>
    public static void AssertTopmost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
}

internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EasyDesktopLyrics";

    public static void Sync(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        var current = key.GetValue(ValueName) as string;
        if (enabled)
        {
            var expected = $"\"{Environment.ProcessPath}\" --autostart";
            if (current != expected)
                key.SetValue(ValueName, expected);
        }
        else if (current != null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
