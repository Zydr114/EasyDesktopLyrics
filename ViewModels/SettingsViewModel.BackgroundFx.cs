using Avalonia.Media;
using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.ViewModels;

/// <summary>
/// SettingsViewModel · 背景动效分区：帧率 + 频谱 / 飘雪 / 雾层。
/// 所有效果默认关闭，设置项与渲染层（BackgroundFxLayer）解耦。
/// </summary>
public sealed partial class SettingsViewModel
{
    // ---------- 全局 ----------

    public double BgFps
    {
        get => _settings.Current.BackgroundFx.Fps;
        set
        {
            var v = (int)Math.Round(value);
            if (v == _settings.Current.BackgroundFx.Fps) return;
            _settings.Update(s => s.BackgroundFx.Fps = Math.Clamp(v, 30, 120));
        }
    }

    // ---------- 频谱 ----------

    public bool SpectrumEnabled
    {
        get => _settings.Current.BackgroundFx.Spectrum.Enabled;
        set
        {
            if (value == SpectrumEnabled) return;
            _settings.Update(s => s.BackgroundFx.Spectrum.Enabled = value);
        }
    }

    private static readonly string[] SpectrumPositions = ["底部", "行中央", "顶部"];

    public IReadOnlyList<string> SpectrumPositionOptions => SpectrumPositions;

    public int SpectrumPositionIndex
    {
        get => _settings.Current.BackgroundFx.Spectrum.Position switch
        {
            "Center" => 1,
            "Top" => 2,
            _ => 0,
        };
        set
        {
            var p = value switch { 1 => "Center", 2 => "Top", _ => "Bottom" };
            if (p == _settings.Current.BackgroundFx.Spectrum.Position) return;
            _settings.Update(s => s.BackgroundFx.Spectrum.Position = p);
        }
    }

    private static readonly string[] SpectrumStyles = ["柱状图", "曲线", "单曲线"];

    public IReadOnlyList<string> SpectrumStyleOptions => SpectrumStyles;

    public int SpectrumStyleIndex
    {
        get => _settings.Current.BackgroundFx.Spectrum.Style switch
        {
            "Curve" => 1,
            "Line" => 2,
            _ => 0,
        };
        set
        {
            var p = value switch { 1 => "Curve", 2 => "Line", _ => "Bars" };
            if (p == _settings.Current.BackgroundFx.Spectrum.Style) return;
            _settings.Update(s => s.BackgroundFx.Spectrum.Style = p);
        }
    }

    public double SpectrumHeightVal
    {
        get => _settings.Current.BackgroundFx.Spectrum.Height;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.BackgroundFx.Spectrum.Height) < 1) return;
            _settings.Update(s => s.BackgroundFx.Spectrum.Height = v);
        }
    }

    public double SpectrumOpacityVal
    {
        get => _settings.Current.BackgroundFx.Spectrum.Opacity;
        set
        {
            var v = Math.Round(value, 2);
            if (Math.Abs(v - _settings.Current.BackgroundFx.Spectrum.Opacity) < 0.01) return;
            _settings.Update(s => s.BackgroundFx.Spectrum.Opacity = v);
        }
    }

    public Color SpectrumColorValue
    {
        get => Color.TryParse(_settings.Current.BackgroundFx.Spectrum.ColorHex, out var c) ? c : Color.FromRgb(0, 229, 255);
        set
        {
            var hex = value.ToString().ToUpperInvariant();
            if (hex == _settings.Current.BackgroundFx.Spectrum.ColorHex) return;
            _settings.Update(s => s.BackgroundFx.Spectrum.ColorHex = hex);
        }
    }

    public bool SpectrumGlowEnabled
    {
        get => _settings.Current.BackgroundFx.Spectrum.GlowEnabled;
        set
        {
            if (value == SpectrumGlowEnabled) return;
            _settings.Update(s => s.BackgroundFx.Spectrum.GlowEnabled = value);
        }
    }

    public double SpectrumGlowStrengthVal
    {
        get => _settings.Current.BackgroundFx.Spectrum.GlowStrength;
        set
        {
            var v = Math.Round(value, 2);
            if (Math.Abs(v - _settings.Current.BackgroundFx.Spectrum.GlowStrength) < 0.05) return;
            _settings.Update(s => s.BackgroundFx.Spectrum.GlowStrength = v);
        }
    }

    public double SpectrumSmoothingVal
    {
        get => _settings.Current.BackgroundFx.Spectrum.Smoothing;
        set
        {
            var v = (int)Math.Round(value);
            if (v == _settings.Current.BackgroundFx.Spectrum.Smoothing) return;
            _settings.Update(s => s.BackgroundFx.Spectrum.Smoothing = Math.Clamp(v, 1, 10));
        }
    }

    public bool SpectrumMirrorEnabled
    {
        get => _settings.Current.BackgroundFx.Spectrum.Mirror;
        set
        {
            if (value == SpectrumMirrorEnabled) return;
            _settings.Update(s => s.BackgroundFx.Spectrum.Mirror = value);
        }
    }

    // ---------- 飘雪 ----------

    public bool SnowEnabled
    {
        get => _settings.Current.BackgroundFx.Snow.Enabled;
        set
        {
            if (value == SnowEnabled) return;
            _settings.Update(s => s.BackgroundFx.Snow.Enabled = value);
        }
    }

    public double SnowIntensityVal
    {
        get => _settings.Current.BackgroundFx.Snow.Intensity;
        set
        {
            var v = (int)Math.Round(value);
            if (v == _settings.Current.BackgroundFx.Snow.Intensity) return;
            _settings.Update(s => s.BackgroundFx.Snow.Intensity = Math.Clamp(v, 20, 400));
        }
    }

    public double SnowWidthVal
    {
        get => _settings.Current.BackgroundFx.Snow.WidthPct;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.BackgroundFx.Snow.WidthPct) < 1) return;
            _settings.Update(s => s.BackgroundFx.Snow.WidthPct = Math.Clamp(v, 20, 100));
        }
    }

    public double SnowOpacityVal
    {
        get => _settings.Current.BackgroundFx.Snow.Opacity;
        set
        {
            var v = Math.Round(value, 2);
            if (Math.Abs(v - _settings.Current.BackgroundFx.Snow.Opacity) < 0.01) return;
            _settings.Update(s => s.BackgroundFx.Snow.Opacity = v);
        }
    }

    public double SnowSizeVal
    {
        get => _settings.Current.BackgroundFx.Snow.Size;
        set
        {
            var v = Math.Round(value, 2);
            if (Math.Abs(v - _settings.Current.BackgroundFx.Snow.Size) < 0.05) return;
            _settings.Update(s => s.BackgroundFx.Snow.Size = v);
        }
    }

    public double SnowSpeedVal
    {
        get => _settings.Current.BackgroundFx.Snow.Speed;
        set
        {
            var v = Math.Round(value, 2);
            if (Math.Abs(v - _settings.Current.BackgroundFx.Snow.Speed) < 0.05) return;
            _settings.Update(s => s.BackgroundFx.Snow.Speed = v);
        }
    }

    public Color SnowColorValue
    {
        get => Color.TryParse(_settings.Current.BackgroundFx.Snow.ColorHex, out var c) ? c : Colors.White;
        set
        {
            var hex = value.ToString().ToUpperInvariant();
            if (hex == _settings.Current.BackgroundFx.Snow.ColorHex) return;
            _settings.Update(s => s.BackgroundFx.Snow.ColorHex = hex);
        }
    }

    // ---------- 雾层 ----------

    public bool FogEnabled
    {
        get => _settings.Current.BackgroundFx.Fog.Enabled;
        set
        {
            if (value == FogEnabled) return;
            _settings.Update(s => s.BackgroundFx.Fog.Enabled = value);
        }
    }

    public double FogOpacityVal
    {
        get => _settings.Current.BackgroundFx.Fog.Opacity;
        set
        {
            var v = Math.Round(value, 2);
            if (Math.Abs(v - _settings.Current.BackgroundFx.Fog.Opacity) < 0.01) return;
            _settings.Update(s => s.BackgroundFx.Fog.Opacity = v);
        }
    }

    public double FogSoftnessVal
    {
        get => _settings.Current.BackgroundFx.Fog.Softness;
        set
        {
            var v = Math.Round(value, 2);
            if (Math.Abs(v - _settings.Current.BackgroundFx.Fog.Softness) < 0.05) return;
            _settings.Update(s => s.BackgroundFx.Fog.Softness = v);
        }
    }

    public double FogFlowVal
    {
        get => _settings.Current.BackgroundFx.Fog.FlowSpeed;
        set
        {
            var v = Math.Round(value, 2);
            if (Math.Abs(v - _settings.Current.BackgroundFx.Fog.FlowSpeed) < 0.05) return;
            _settings.Update(s => s.BackgroundFx.Fog.FlowSpeed = v);
        }
    }

    public bool FogUseCoverEnabled
    {
        get => _settings.Current.BackgroundFx.Fog.UseCoverColor;
        set
        {
            if (value == FogUseCoverEnabled) return;
            _settings.Update(s => s.BackgroundFx.Fog.UseCoverColor = value);
        }
    }

    public bool FogUseBackdropEnabled
    {
        get => _settings.Current.BackgroundFx.Fog.UseBackdropColor;
        set
        {
            if (value == FogUseBackdropEnabled) return;
            _settings.Update(s => s.BackgroundFx.Fog.UseBackdropColor = value);
        }
    }

    public double FogBlendVal
    {
        get => _settings.Current.BackgroundFx.Fog.Blend;
        set
        {
            var v = Math.Round(value, 2);
            if (Math.Abs(v - _settings.Current.BackgroundFx.Fog.Blend) < 0.01) return;
            _settings.Update(s => s.BackgroundFx.Fog.Blend = v);
        }
    }

    public bool FogAnimatedEnabled
    {
        get => _settings.Current.BackgroundFx.Fog.Animated;
        set
        {
            if (value == FogAnimatedEnabled) return;
            _settings.Update(s => s.BackgroundFx.Fog.Animated = value);
        }
    }
}
