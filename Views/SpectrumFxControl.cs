using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services;

namespace EasyDesktopLyrics.Views;

/// <summary>
/// 频谱动效：在歌词背面绘制柱状图 / 曲线 / 单曲线。
/// 位置语义：顶部/底部 = 单侧频谱（自边缘向内生长）；行中央 = 双侧频谱（沿 x 轴对称）。
/// 强度为窗口高度百分比，宽度范围为窗口宽度百分比，采样数量可配置。
/// 数据来自 SpectrumEngine（真实环回或模拟回退）。
/// </summary>
public sealed class SpectrumFxControl : Control
{
    private const double CornerRadius = 6;

    private SpectrumEngine? _engine;
    private bool _enabled;
    private int _smoothing = 3;
    private string _style = "Bars";
    private string _position = "Bottom";
    private double _intensity = 40;
    private int _bandCount = 32;
    private double _widthPct = 100;
    private double _opacity = 0.8;
    private bool _glowEnabled = true;
    private double _glowStrength = 1;

    private string _brushKey = "";
    private IBrush _fill = Brushes.Transparent;
    private IBrush _glowBrush = Brushes.Transparent;
    private Pen? _strokePen;

    /// <summary>歌词行区域（layer 本地坐标）；null = 用整个区域定位。</summary>
    public Rect? LyricsRect { get; set; }

    public bool IsRunning => _engine != null;

    public void SetPlaying(bool playing) => _engine?.SetPlaying(playing);

    public void ApplySettings(SpectrumFxSettings s)
    {
        _enabled = s.Enabled;
        _smoothing = Math.Clamp(s.Smoothing, 1, 10);
        _style = s.Style;
        _position = s.Position;
        _intensity = Math.Clamp(s.Intensity, 10, 90);
        _bandCount = Math.Clamp(s.BandCount, 16, 128);
        _widthPct = Math.Clamp(s.WidthPct, 20, 100);
        _opacity = Math.Clamp(s.Opacity, 0.05, 1);
        _glowEnabled = s.GlowEnabled;
        _glowStrength = Math.Clamp(s.GlowStrength, 0, 2);
        _engine?.SetSmoothing(_smoothing);
        _engine?.SetBandCount(_bandCount);
        RebuildBrushes(s.ColorHex);
        InvalidateVisual();
    }

    public void Start()
    {
        if (_engine != null)
            return;
        var e = new SpectrumEngine();
        e.SetSmoothing(_smoothing);
        e.SetBandCount(_bandCount);
        _engine = e;
        _ = e.StartAsync();
    }

    public void Stop()
    {
        _engine?.Dispose();
        _engine = null;
    }

    public void TickAndInvalidate() => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!_enabled || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var bands = _engine?.GetBands();
        if (bands == null || bands.Length == 0)
            return;
        using (context.PushClip(new RoundedRect(Bounds, CornerRadius)))
        {
            var area = DrawingArea();
            if (area.Width <= 0 || area.Height <= 0)
                return;
            var mode = _position switch { "Top" => 1, "Bottom" => -1, _ => 0 };
            if (_style == "Curve")
                DrawCurve(context, bands, area, mode, filled: true);
            else if (_style == "Line")
                DrawCurve(context, bands, area, mode, filled: false);
            else
                DrawBars(context, bands, area, mode);
        }
    }

    private void RebuildBrushes(string colorHex)
    {
        var key = $"{colorHex}|{_opacity}";
        if (_brushKey == key)
            return;
        _brushKey = key;
        var c = Color.TryParse(colorHex, out var cc) ? cc : Color.FromRgb(0, 229, 255);
        var solid = new ImmutableSolidColorBrush(Color.FromArgb((byte)Math.Round(c.A * _opacity), c.R, c.G, c.B));
        _fill = solid;
        _glowBrush = new ImmutableSolidColorBrush(Color.FromArgb(
            (byte)Math.Round(c.A * _opacity * 0.32), c.R, c.G, c.B));
        _strokePen = new Pen(solid, 2);
    }

    /// <summary>歌词行垂直中心（行中央频谱锚定基准）。</summary>
    private double LyricsCenter()
    {
        if (LyricsRect is { Height: > 0 } lr)
            return lr.Y + lr.Height / 2;
        return Bounds.Height / 2;
    }

    /// <summary>
    /// 频谱绘制区域（以窗口边界为基准，永不越界）：
    /// 顶部 = 窗口顶向下延伸强度高度；底部 = 窗口底向上延伸；行中央 = 以歌词行中心为轴、上下各延伸强度高度。
    /// </summary>
    private Rect DrawingArea()
    {
        var b = Bounds;
        var areaH = Math.Min(b.Height * _intensity / 100.0, b.Height);
        double top, height;
        switch (_position)
        {
            case "Top":
                top = 0;
                height = areaH;
                break;
            case "Center":
                var cy = LyricsCenter();
                var half = Math.Min(areaH, Math.Min(cy, b.Height - cy));
                top = cy - half;
                height = half * 2;
                break;
            default: // Bottom
                top = b.Height - areaH;
                height = areaH;
                break;
        }
        top = Math.Clamp(top, 0, Math.Max(0, b.Height));
        height = Math.Min(height, Math.Max(0, b.Height - top));

        var widthRange = b.Width * _widthPct / 100.0;
        var x0 = (b.Width - widthRange) / 2;
        return new Rect(x0, top, widthRange, height);
    }

    private void DrawBars(DrawingContext context, float[] bands, Rect area, int mode)
    {
        var n = Math.Max(1, bands.Length);
        var slot = area.Width / n;
        var barW = Math.Max(1, slot * 0.68);
        var baseline = mode switch { 1 => area.Top, -1 => area.Bottom, _ => area.Y + area.Height / 2 };
        var maxPerSide = mode == 0 ? area.Height / 2 : area.Height;

        for (var i = 0; i < n; i++)
        {
            var v = bands[i];
            if (v <= 0.002)
                continue;
            var h = Math.Max(1.5, v * maxPerSide);
            var x = area.X + i * slot + (slot - barW) / 2;
            if (mode == 0)
            {
                if (_glowEnabled)
                    context.DrawRectangle(_glowBrush, null, new Rect(x - barW * 0.3, baseline - h - h * 0.2 * _glowStrength, barW * 1.6, h * 2 + h * 0.4 * _glowStrength));
                context.DrawRectangle(_fill, null, new Rect(x, baseline - h, barW, h * 2));
            }
            else if (mode == 1) // 顶部：从顶边向下生长
            {
                if (_glowEnabled)
                    context.DrawRectangle(_glowBrush, null, new Rect(x - barW * 0.3, baseline, barW * 1.6, h + h * 0.32 * _glowStrength));
                context.DrawRectangle(_fill, null, new Rect(x, baseline, barW, h));
            }
            else // 底部：从底边向上生长
            {
                if (_glowEnabled)
                    context.DrawRectangle(_glowBrush, null, new Rect(x - barW * 0.3, baseline - h - h * 0.16 * _glowStrength, barW * 1.6, h + h * 0.32 * _glowStrength));
                context.DrawRectangle(_fill, null, new Rect(x, baseline - h, barW, h));
            }
        }
    }

    private void DrawCurve(DrawingContext context, float[] bands, Rect area, int mode, bool filled)
    {
        var n = bands.Length;
        if (n < 2)
            return;

        // 单曲线（Line）在行中央不做 x 轴对称镜像，但仍以歌词行中心为基线单侧生长（居中生效）
        var baseline = mode switch { 1 => area.Top, -1 => area.Bottom, _ => area.Y + area.Height / 2 };
        var maxPerSide = mode == 0 ? area.Height / 2 : area.Height;
        var step = area.Width / (n - 1);

        var points = new Point[n];
        for (var i = 0; i < n; i++)
        {
            var dy = bands[i] * maxPerSide;
            points[i] = new Point(area.X + i * step, mode == 1 ? baseline + dy : baseline - dy);
        }

        var geo = BuildCurveGeometry(points, baseline, mode, filled, area);

        if (_glowEnabled)
        {
            var pen = new Pen(_glowBrush, Math.Max(1, 4 * _glowStrength + 1));
            context.DrawGeometry(null, pen, geo);
        }
        context.DrawGeometry(filled ? _fill : Brushes.Transparent, _strokePen, geo);
    }

    /// <summary>
    /// 曲线几何（Catmull-Rom → 三次贝塞尔平滑）：填充模式在 顶部/底部 时闭合到基线（单侧），
    /// 行中央时沿 x 轴对称闭合（双侧）；非填充（单曲线）为平滑开放曲线。
    /// </summary>
    private static Geometry BuildCurveGeometry(Point[] points, double baseline, int mode, bool filled, Rect area)
    {
        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(points[0], filled);
            AddSmoothThrough(gc, points);
            if (filled)
            {
                if (mode == 0)
                {
                    var mirror = MirrorPoints(points, baseline);
                    gc.LineTo(mirror[^1]);
                    var reversed = new Point[mirror.Length];
                    for (var i = 0; i < mirror.Length; i++)
                        reversed[i] = mirror[mirror.Length - 1 - i];
                    AddSmoothThrough(gc, reversed);
                }
                else
                {
                    gc.LineTo(new Point(area.Right, baseline));
                    gc.LineTo(new Point(area.X, baseline));
                }
            }
            gc.EndFigure(filled);
        }
        return geo;
    }

    /// <summary>从当前点（= pts[0]）出发，用 Catmull-Rom → 三次贝塞尔平滑连接全部点。</summary>
    private static void AddSmoothThrough(StreamGeometryContext gc, Point[] pts)
    {
        for (var i = 0; i < pts.Length - 1; i++)
        {
            var p1 = pts[i];
            var p2 = pts[i + 1];
            var p0 = i > 0 ? pts[i - 1] : p1;
            var p3 = i + 2 < pts.Length ? pts[i + 2] : p2;
            gc.CubicBezierTo(p1 + (p2 - p0) / 6, p2 - (p3 - p1) / 6, p2);
        }
    }

    private static Point[] MirrorPoints(Point[] points, double baseline)
    {
        var m = new Point[points.Length];
        for (var i = 0; i < points.Length; i++)
            m[i] = new Point(points[i].X, 2 * baseline - points[i].Y);
        return m;
    }
}
