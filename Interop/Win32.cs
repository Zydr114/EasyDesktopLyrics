using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EasyDesktopLyrics.Interop;

internal static class Win32
{
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TRANSPARENT = 0x00000020;
    private const uint WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr64(IntPtr hWnd, int nIndex, nint dwNewLong);

    /// <summary>物理窗口矩形（GetWindowRect，像素）；失败返回 (0,0,err)。</summary>
    public static (int W, int H, int Err) GetWindowSize(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return (0, 0, 0);
        if (!GetWindowRect(hwnd, out var r)) return (0, 0, Marshal.GetLastWin32Error());
        return (r.Right - r.Left, r.Bottom - r.Top, 0);
    }

    /// <summary>物理客户区矩形（GetClientRect，像素）；失败返回 (0,0,err)。</summary>
    public static (int W, int H, int Err) GetClientSize(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return (0, 0, 0);
        if (!GetClientRect(hwnd, out var r)) return (0, 0, Marshal.GetLastWin32Error());
        return (r.Right - r.Left, r.Bottom - r.Top, 0);
    }

    /// <summary>低频重申置顶，防止被其他置顶窗口覆盖。</summary>
    public static void AssertTopmost(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>
    /// 锁定/解锁窗口穿透：锁定时设置 WS_EX_TRANSPARENT（点击穿透到底层窗口），
    /// 配合 FRAMECHANGED 让 DWM 重新合成样式。解锁仅清除 TRANSPARENT（保留 LAYERED）。
    /// </summary>
    public static void SetClickThrough(IntPtr hwnd, bool enable)
    {
        if (hwnd == IntPtr.Zero) return;

        var style = GetExStyle(hwnd);
        if (enable)
            style |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
        else
            style &= ~WS_EX_TRANSPARENT;
        SetExStyle(hwnd, style);

        // 关键：触发 FRAMECHANGED 让 DWM 重新合成窗口样式，穿透即时生效
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    private static uint GetExStyle(IntPtr hwnd)
    {
        var v = IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE) : GetWindowLong32(hwnd, GWL_EXSTYLE);
        return unchecked((uint)v);
    }

    private static void SetExStyle(IntPtr hwnd, uint style)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hwnd, GWL_EXSTYLE, (nint)style);
        else
            SetWindowLong32(hwnd, GWL_EXSTYLE, (int)style);
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
