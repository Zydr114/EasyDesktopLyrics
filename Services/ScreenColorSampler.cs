using System.Runtime.InteropServices;

namespace EasyDesktopLyrics.Services;

/// <summary>
/// 屏幕区域取色：低频（约 1Hz）抓取窗口背后屏幕区域并求平均主色，
/// 供雾层合成背景色。纯 P/Invoke，抓取失败返回 null（调用方回退封面色）。
/// </summary>
public static class ScreenColorSampler
{
    private const uint SRCCOPY = 0x00CC0020;
    private const int DIB_RGB_COLORS = 0;

    private static IntPtr _screenDc;

    /// <summary>抓取物理像素矩形 (x, y, w, h) 区域的平均颜色；失败返回 null。</summary>
    public static (byte R, byte G, byte B)? TrySample(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0)
            return null;
        var dc = GetScreenDc();
        if (dc == IntPtr.Zero)
            return null;

        var mem = CreateCompatibleDC(dc);
        if (mem == IntPtr.Zero)
            return null;
        try
        {
            var tw = Math.Min(w, 64);
            var th = Math.Max(1, (int)Math.Round(h * (double)tw / w));

            var info = new BitmapInfo { Header = new BitmapInfoHeader { BiSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(), BiWidth = tw, BiHeight = -th, BiPlanes = 1, BiBitCount = 32, BiCompression = 0 } };
            var hbmp = CreateDibSection(mem, ref info, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (hbmp == IntPtr.Zero)
                return null;

            var old = SelectObject(mem, hbmp);
            try
            {
                SetStretchBltMode(mem, 3 /* COLORONCOLOR */);
                if (!StretchBlt(mem, 0, 0, tw, th, dc, x, y, w, h, SRCCOPY))
                    return null;

                var buf = new byte[tw * th * 4];
                Marshal.Copy(bits, buf, 0, buf.Length);
                long r = 0, g = 0, b = 0;
                for (var i = 0; i < buf.Length; i += 4)
                {
                    b += buf[i];
                    g += buf[i + 1];
                    r += buf[i + 2];
                }
                var n = tw * th;
                return ((byte)(r / n), (byte)(g / n), (byte)(b / n));
            }
            finally
            {
                SelectObject(mem, old);
                DeleteObject(hbmp);
            }
        }
        finally
        {
            DeleteDC(mem);
        }
    }

    private static IntPtr GetScreenDc() =>
        _screenDc == IntPtr.Zero ? _screenDc = GetDC(IntPtr.Zero) : _screenDc;

    // ---------- 结构体 ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint BiSize;
        public int BiWidth;
        public int BiHeight;
        public ushort BiPlanes;
        public ushort BiBitCount;
        public uint BiCompression;
        public uint BiSizeImage;
        public int BiXPelsPerMeter;
        public int BiYPelsPerMeter;
        public uint BiClrUsed;
        public uint BiClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint BmiColors;
    }

    // ---------- P/Invoke ----------

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDibSection(IntPtr hdc, ref BitmapInfo pbmi, int usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int iStretchMode);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);
}
