using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Views;

/// <summary>
/// 飘雪动效：在整个歌词窗口上悬浮的雪花粒子。强度（数量）、范围（宽度百分比）、
/// 不透明度、大小/速度倍率均可配置，纯视觉、不拦截鼠标。
/// </summary>
public sealed class SnowFxControl : Control
{
    private const double CornerRadius = 6;

    private struct Particle
    {
        public double X;
        public double Y;
        public double Size;
        public double Speed;
        public double Phase;
        public double Sway;
    }

    private readonly Random _rng = new();
    private Particle[] _parts = Array.Empty<Particle>();
    private double _widthPct = 100;
    private double _opacity = 0.7;
    private double _sizeMul = 1;
    private double _speedMul = 1;
    private int _count;
    private IBrush? _brush;
    private string _brushKey = "";

    public void ApplySettings(SnowFxSettings s)
    {
        _count = Math.Clamp(s.Intensity, 20, 400);
        _widthPct = Math.Clamp(s.WidthPct, 20, 100);
        _opacity = Math.Clamp(s.Opacity, 0.05, 1);
        _sizeMul = Math.Clamp(s.Size, 0.4, 3);
        _speedMul = Math.Clamp(s.Speed, 0.2, 3);
        RebuildBrush(s.ColorHex);
        EnsureParticles();
        InvalidateVisual();
    }

    public void TickAndInvalidate(double dt)
    {
        Advance(dt);
        InvalidateVisual();
    }

    private void RebuildBrush(string colorHex)
    {
        var key = $"{colorHex}|{_opacity}";
        if (_brushKey == key)
            return;
        _brushKey = key;
        var c = Color.TryParse(colorHex, out var cc) ? cc : Colors.White;
        _brush = new ImmutableSolidColorBrush(Color.FromArgb(
            (byte)Math.Round(c.A * _opacity), c.R, c.G, c.B));
    }

    private void EnsureParticles()
    {
        if (_parts.Length == _count)
            return;
        _parts = new Particle[_count];
        var b = Bounds;
        var w = b.Width > 0 ? b.Width : 800;
        var h = b.Height > 0 ? b.Height : 100;
        for (var i = 0; i < _count; i++)
        {
            _parts[i] = new Particle
            {
                X = RandX(w),
                Y = _rng.NextDouble() * h,
                Size = 1 + _rng.NextDouble() * 2.2,
                Speed = 28 + _rng.NextDouble() * 48,
                Phase = _rng.NextDouble() * Math.PI * 2,
                Sway = 6 + _rng.NextDouble() * 14,
            };
        }
    }

    private double RandX(double w)
    {
        var range = w * _widthPct / 100;
        var x0 = (w - range) / 2;
        return x0 + _rng.NextDouble() * range;
    }

    private void Advance(double dt)
    {
        var b = Bounds;
        if (b.Width <= 0)
            return;
        var w = b.Width;
        var h = b.Height + 8;
        for (var i = 0; i < _parts.Length; i++)
        {
            var p = _parts[i];
            p.Y += p.Speed * _speedMul * dt;
            p.Phase += dt * 3.2;
            p.X += Math.Sin(p.Phase) * p.Sway * _speedMul * dt;
            if (p.Y > h)
            {
                p.Y = -6 - _rng.NextDouble() * 12;
                p.X = RandX(w);
            }
            else if (p.X < 0 || p.X > w)
            {
                p.X = RandX(w);
            }
            _parts[i] = p;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_brush == null || _parts.Length == 0 || Bounds.Width <= 0)
            return;
        using (context.PushClip(new RoundedRect(Bounds, CornerRadius)))
        {
            foreach (var p in _parts)
            {
                var r = Math.Max(0.8, p.Size * _sizeMul);
                context.DrawEllipse(_brush, null, new Point(p.X, p.Y), r, r);
            }
        }
    }
}
