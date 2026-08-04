using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services;

namespace EasyDesktopLyrics.Views;

/// <summary>
/// 雾层动效：在歌词窗口上铺一层柔和的彩色光晕。
/// 颜色 = 窗口背后屏幕采样色（低频 1Hz）与当前歌曲封面主色按比例混合；
/// 光晕位置随时间漂移形成“颜色流动”。纯视觉、不拦截鼠标。
/// </summary>
public sealed class FogFxControl : Control, IDisposable
{
    private const double CornerRadius = 6;
    private const int BlobCount = 3;

    private readonly Random _rng = new();
    private readonly double[] _phaseX = new double[BlobCount];
    private readonly double[] _phaseY = new double[BlobCount];
    private readonly Dictionary<string, IBrush> _brushCache = new();

    private Func<(int X, int Y, int W, int H)>? _screenRect;
    private DateTime _lastSample;
    private Color _backdrop = Color.FromRgb(22, 22, 30);
    private Color _cover = Color.FromRgb(22, 22, 30);

    private bool _enabled;
    private bool _useCover = true;
    private bool _useBackdrop = true;
    private bool _animated = true;
    private double _opacity = 0.35;
    private double _softness = 1;
    private double _blend = 0.5;
    private double _flow = 1;
    private double _t;

    public FogFxControl()
    {
        for (var i = 0; i < BlobCount; i++)
        {
            _phaseX[i] = _rng.NextDouble() * Math.PI * 2;
            _phaseY[i] = _rng.NextDouble() * Math.PI * 2;
        }
    }

    public void SetCoverImage(IImage? image)
    {
        var c = AverageCoverColor(image);
        if (c != null)
        {
            _cover = c.Value;
            _brushCache.Clear();
            InvalidateVisual();
        }
    }

    public void SetScreenRectProvider(Func<(int X, int Y, int W, int H)> provider) =>
        _screenRect = provider;

    public void ApplySettings(FogFxSettings s)
    {
        _enabled = s.Enabled;
        _useCover = s.UseCoverColor;
        _useBackdrop = s.UseBackdropColor;
        _animated = s.Animated;
        _opacity = Math.Clamp(s.Opacity, 0.05, 1);
        _softness = Math.Clamp(s.Softness, 0.5, 3);
        _blend = Math.Clamp(s.Blend, 0, 1);
        _flow = Math.Clamp(s.FlowSpeed, 0, 3);
        _brushCache.Clear();
        InvalidateVisual();
    }

    public void TickAndInvalidate(double dt)
    {
        if (_animated)
            _t += dt * _flow;
        if (_useBackdrop && _screenRect != null && (DateTime.UtcNow - _lastSample).TotalSeconds >= 1)
            SampleBackdrop();
        InvalidateVisual();
    }

    private void SampleBackdrop()
    {
        _lastSample = DateTime.UtcNow;
        try
        {
            var (x, y, w, h) = _screenRect!();
            if (w <= 0 || h <= 0)
                return;
            if (ScreenColorSampler.TrySample(x, y, w, h) is { } c)
            {
                var nc = Color.FromRgb(c.R, c.G, c.B);
                if (nc != _backdrop)
                {
                    _backdrop = nc;
                    _brushCache.Clear();
                }
            }
        }
        catch
        {
            // 采样失败静默，保留上次颜色
        }
    }

    private Color BaseColor()
    {
        var back = _useBackdrop ? _backdrop : Color.FromRgb(18, 18, 24);
        var cover = _useCover ? _cover : back;
        return Lerp(back, cover, _blend);
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!_enabled || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        using (context.PushClip(new RoundedRect(Bounds, CornerRadius)))
        {
            var w = Bounds.Width;
            var h = Bounds.Height;
            var baseC = BaseColor();

            var palette = new[]
            {
                baseC,
                _useCover ? Lerp(_backdrop, _cover, 0.15) : baseC,
                _useBackdrop ? Lerp(_backdrop, _cover, 0.85) : baseC,
            };

            for (var i = 0; i < BlobCount; i++)
            {
                var cx = _animated
                    ? w * (0.5 + 0.4 * Math.Sin(_t * (0.6 + 0.2 * i) + _phaseX[i]))
                    : w * (0.3 + 0.2 * i);
                var cy = _animated
                    ? h * (0.5 + 0.35 * Math.Sin(_t * (0.4 + 0.15 * i) + _phaseY[i]))
                    : h * 0.5;
                var radius = Math.Max(w, h) * (0.55 + 0.3 * _softness) * (0.8 + 0.2 * i);
                var brush = GetBlobBrush(palette[i], radius, w, h);
                context.DrawEllipse(brush, null, new Point(cx, cy), radius, radius);
            }
        }
    }

    private IBrush GetBlobBrush(Color color, double radius, double w, double h)
    {
        var key = $"{color.R}|{color.G}|{color.B}|{_opacity}|{_softness}|{radius:F0}|{w:F0}|{h:F0}";
        if (_brushCache.TryGetValue(key, out var b))
            return b;

        var alpha = (byte)Math.Round(color.A * _opacity);
        var mid = 1 - Math.Clamp(_softness - 1, 0, 2) * 0.28;
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromArgb(alpha, color.R, color.G, color.B), 0),
                new GradientStop(Color.FromArgb((byte)(alpha * 0.55), color.R, color.G, color.B), mid),
                new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
            },
        };
        _brushCache[key] = brush;
        return brush;
    }

    private static Color? AverageCoverColor(IImage? image)
    {
        if (image is not Bitmap bmp)
            return null;
        try
        {
            var w = bmp.PixelSize.Width;
            var h = bmp.PixelSize.Height;
            if (w <= 0 || h <= 0)
                return null;
            var stride = w * 4;
            var buf = new byte[stride * h];
            var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                bmp.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), buf.Length, stride);
            }
            finally
            {
                handle.Free();
            }
            long r = 0, g = 0, b = 0;
            for (var y = 0; y < h; y++)
            {
                var row = y * stride;
                for (var x = 0; x < w; x++)
                {
                    var o = row + x * 4;
                    b += buf[o];
                    g += buf[o + 1];
                    r += buf[o + 2];
                }
            }
            var n = w * h;
            return Color.FromRgb((byte)(r / n), (byte)(g / n), (byte)(b / n));
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _brushCache.Clear();
}
