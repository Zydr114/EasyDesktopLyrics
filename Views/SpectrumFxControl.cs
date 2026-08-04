using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services;

namespace EasyDesktopLyrics.Views;

/// <summary>
/// 频谱动效：在歌词背面绘制柱状图 / 曲线 / 单曲线。
/// 数据来自 SpectrumEngine（真实环回或模拟回退），位置（底部/行中央/顶部）、
/// 高度、不透明度、颜色、辉光、镜像均可配置。
/// </summary>
public sealed class SpectrumFxControl : Control
{
    private const double CornerRadius = 6;

    private SpectrumEngine? _engine;
    private bool _enabled;
    private int _smoothing = 3;
    private string _style = "Bars";
    private string _position = "Bottom";
    private double _height = 60;
    private double _opacity = 0.8;
    private bool _mirror;
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
        _height = Math.Clamp(s.Height, 20, 400);
        _opacity = Math.Clamp(s.Opacity, 0.05, 1);
        _mirror = s.Mirror;
        _glowEnabled = s.GlowEnabled;
        _glowStrength = Math.Clamp(s.GlowStrength, 0, 2);
        _engine?.SetSmoothing(_smoothing);
        RebuildBrushes(s.ColorHex);
        InvalidateVisual();
    }

    public void Start()
    {
        if (_engine != null)
            return;
        var e = new SpectrumEngine();
        e.SetSmoothing(_smoothing);
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
        if (bands == null)
            return;
        using (context.PushClip(new RoundedRect(Bounds, CornerRadius)))
        {
            var rect = AnchorRect();
            if (rect.Width > 0 && rect.Height > 0)
            {
                if (_style == "Curve")
                    DrawCurve(context, bands, rect, filled: true);
                else if (_style == "Line")
                    DrawCurve(context, bands, rect, filled: false);
                else
                    DrawBars(context, bands, rect);
            }
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

    /// <summary>
    /// 频谱锚定区域：顶部 = 窗口顶部；底部 = 窗口底部；行中央 = 以歌词行垂直中心为准。
    /// 频谱绘制在歌词背面，与歌词重叠时自然位于文字后方。
    /// </summary>
    private Rect AnchorRect()
    {
        var b = Bounds;
        var h = Math.Min(_height, Math.Max(0, b.Height));
        if (h <= 0 || b.Width <= 0)
            return new Rect(0, 0, b.Width, 0);

        var centerY = LyricsRect is { Height: > 0 } lr
            ? lr.Y + lr.Height / 2
            : b.Height / 2;
        var y = _position switch
        {
            "Top" => 0,
            "Center" => centerY - h / 2,
            _ => b.Height - h,
        };
        y = Math.Clamp(y, 0, Math.Max(0, b.Height - h));
        return new Rect(0, y, b.Width, h);
    }

    private void DrawBars(DrawingContext context, float[] bands, Rect rect)
    {
        var n = bands.Length;
        var slot = rect.Width / n;
        var barW = Math.Max(1, slot * 0.68);
        var cx = rect.Y + rect.Height / 2;
        for (var i = 0; i < n; i++)
        {
            var v = bands[i];
            if (v <= 0.002)
                continue;
            var h = Math.Max(1.5, v * rect.Height);
            var x = rect.X + i * slot + (slot - barW) / 2;
            if (_mirror)
            {
                var half = h / 2;
                if (_glowEnabled)
                    context.DrawRectangle(_glowBrush, null, new Rect(x - barW * 0.3, cx - half - h * 0.2 * _glowStrength, barW * 1.6, h + h * 0.4 * _glowStrength));
                context.DrawRectangle(_fill, null, new Rect(x, cx - half, barW, h));
            }
            else
            {
                var top = rect.Bottom - h;
                if (_glowEnabled)
                    context.DrawRectangle(_glowBrush, null, new Rect(x - barW * 0.3, top - h * 0.16 * _glowStrength, barW * 1.6, h + h * 0.32 * _glowStrength));
                context.DrawRectangle(_fill, null, new Rect(x, top, barW, h));
            }
        }
    }

    private void DrawCurve(DrawingContext context, float[] bands, Rect rect, bool filled)
    {
        var n = bands.Length;
        if (n < 2)
            return;
        var step = rect.Width / (n - 1);
        var baselineY = _mirror ? rect.Y + rect.Height / 2 : rect.Bottom;

        var points = new Point[n];
        for (var i = 0; i < n; i++)
        {
            var amp = bands[i] * rect.Height;
            points[i] = new Point(rect.X + i * step, baselineY - amp);
        }

        var geo = BuildCurveGeometry(points, rect, baselineY, _mirror, filled);

        if (_glowEnabled)
        {
            var pen = new Pen(_glowBrush, Math.Max(1, 4 * _glowStrength + 1));
            context.DrawGeometry(null, pen, geo);
        }
        context.DrawGeometry(filled ? _fill : Brushes.Transparent, _strokePen, geo);
    }

    private static Geometry BuildCurveGeometry(Point[] points, Rect rect, double baselineY, bool mirror, bool filled)
    {
        var n = points.Length;
        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(points[0], filled);
            for (var i = 1; i < n; i++)
                gc.LineTo(points[i]);
            if (filled)
            {
                if (mirror)
                {
                    for (var i = n - 1; i >= 0; i--)
                        gc.LineTo(new Point(points[i].X, 2 * baselineY - points[i].Y));
                }
                else
                {
                    gc.LineTo(new Point(rect.Right, baselineY));
                    gc.LineTo(new Point(rect.X, baselineY));
                }
            }
            gc.EndFigure(filled);
        }
        return geo;
    }
}
