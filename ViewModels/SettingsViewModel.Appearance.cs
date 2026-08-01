using Avalonia.Media;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services;

namespace EasyDesktopLyrics.ViewModels;

/// <summary>
/// SettingsViewModel · 外观分区：字体/颜色/描边/辉光/阴影/行距/不透明度/宽度。
/// </summary>
public sealed partial class SettingsViewModel
{
    private static readonly int[] WeightValues = [400, 500, 600, 700];

    // ---------- 字体 ----------

    public IReadOnlyList<string> FontOptions { get; }

    public string SelectedFont
    {
        get => _settings.Current.FontFamily;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == _settings.Current.FontFamily)
                return;
            _settings.Update(s => s.FontFamily = value);
        }
    }

    public double FontSize
    {
        get => _settings.Current.FontSize;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.FontSize) < 0.5)
                return;
            _settings.Update(s => s.FontSize = v);
        }
    }

    public IReadOnlyList<string> WeightOptions { get; } = ["常规 (400)", "中等 (500)", "半粗 (600)", "粗体 (700)"];

    public int WeightIndex
    {
        get
        {
            var i = Array.IndexOf(WeightValues, _settings.Current.FontWeight);
            return i >= 0 ? i : 2;
        }
        set
        {
            if (value < 0 || value >= WeightValues.Length || WeightValues[value] == _settings.Current.FontWeight)
                return;
            _settings.Update(s => s.FontWeight = WeightValues[value]);
        }
    }

    // ---------- 颜色 ----------

    public string ColorHex
    {
        get => _settings.Current.ColorHex;
        set
        {
            if (value == _settings.Current.ColorHex || !Color.TryParse(value, out _))
                return;
            _settings.Update(s => s.ColorHex = value);
            Raise(nameof(ColorValue));
        }
    }

    public Color ColorValue
    {
        get => Color.TryParse(_settings.Current.ColorHex, out var c) ? c : Colors.White;
        set
        {
            var hex = value.ToString().ToUpperInvariant();
            if (hex == _settings.Current.ColorHex) return;
            _settings.Update(s => s.ColorHex = hex);
        }
    }

    public bool ShadowEnabled
    {
        get => _settings.Current.ShadowEnabled;
        set
        {
            if (value == _settings.Current.ShadowEnabled) return;
            _settings.Update(s => s.ShadowEnabled = value);
        }
    }

    public bool StrokeEnabled
    {
        get => _settings.Current.StrokeEnabled;
        set
        {
            if (value == _settings.Current.StrokeEnabled) return;
            _settings.Update(s => s.StrokeEnabled = value);
        }
    }

    public string StrokeColorHex
    {
        get => _settings.Current.StrokeColorHex;
        set
        {
            if (value == _settings.Current.StrokeColorHex || !Color.TryParse(value, out _)) return;
            _settings.Update(s => s.StrokeColorHex = value);
            Raise(nameof(StrokeColorValue));
        }
    }

    public Color StrokeColorValue
    {
        get => Color.TryParse(_settings.Current.StrokeColorHex, out var c) ? c : Colors.Black;
        set
        {
            var hex = value.ToString().ToUpperInvariant();
            if (hex == _settings.Current.StrokeColorHex) return;
            _settings.Update(s => s.StrokeColorHex = hex);
        }
    }

    public double StrokeThicknessVal
    {
        get => _settings.Current.StrokeThickness;
        set
        {
            var v = Math.Round(value, 1);
            if (Math.Abs(v - _settings.Current.StrokeThickness) < 0.05) return;
            _settings.Update(s => s.StrokeThickness = v);
        }
    }

    // ---------- 未唱段（逐字模式未演唱部分）独立样式 ----------

    /// <summary>独立设置未唱颜色；关闭 = 跟随主色（主色 45% 透明）。</summary>
    public bool InactiveColorIndependent
    {
        get => _settings.Current.InactiveColorHex.Length > 0;
        set
        {
            if (value == InactiveColorIndependent) return;
            _settings.Update(s => s.InactiveColorHex = value ? DefaultInactiveHex() : "");
        }
    }

    public Color InactiveColorValue
    {
        get
        {
            if (Color.TryParse(_settings.Current.InactiveColorHex, out var c))
                return c;
            var main = Color.TryParse(_settings.Current.ColorHex, out var m) ? m : Colors.White;
            return new SolidColorBrush(main, 0.45).Color;
        }
        set
        {
            var hex = value.ToString().ToUpperInvariant();
            if (hex == _settings.Current.InactiveColorHex) return;
            _settings.Update(s => s.InactiveColorHex = hex);
            Raise(nameof(InactiveColorIndependent));
        }
    }

    /// <summary>跟随主色时的未唱颜色：主色 45% 透明（与旧行为一致）。</summary>
    private string DefaultInactiveHex()
    {
        var main = Color.TryParse(_settings.Current.ColorHex, out var c) ? c : Colors.White;
        return new SolidColorBrush(main, 0.45).Color.ToString().ToUpperInvariant();
    }

    /// <summary>未唱段不透明度比例（相对全局透明度；1.0 = 与已唱段相同亮度）。</summary>
    public double InactiveOpacityVal
    {
        get => _settings.Current.InactiveOpacity;
        set
        {
            var v = Math.Round(value, 2);
            if (Math.Abs(v - _settings.Current.InactiveOpacity) < 0.01) return;
            _settings.Update(s => s.InactiveOpacity = v);
        }
    }

    public bool InactiveStrokeEnabled
    {
        get => _settings.Current.InactiveStrokeEnabled;
        set
        {
            if (value == _settings.Current.InactiveStrokeEnabled) return;
            _settings.Update(s => s.InactiveStrokeEnabled = value);
        }
    }

    public Color InactiveStrokeColorValue
    {
        get => Color.TryParse(_settings.Current.InactiveStrokeColorHex, out var c) ? c : Colors.Black;
        set
        {
            var hex = value.ToString().ToUpperInvariant();
            if (hex == _settings.Current.InactiveStrokeColorHex) return;
            _settings.Update(s => s.InactiveStrokeColorHex = hex);
        }
    }

    public double InactiveStrokeThicknessVal
    {
        get => _settings.Current.InactiveStrokeThickness;
        set
        {
            var v = Math.Round(value, 1);
            if (Math.Abs(v - _settings.Current.InactiveStrokeThickness) < 0.05) return;
            _settings.Update(s => s.InactiveStrokeThickness = v);
        }
    }

    public bool InactiveGlowEnabled
    {
        get => _settings.Current.InactiveGlowEnabled;
        set
        {
            if (value == _settings.Current.InactiveGlowEnabled) return;
            _settings.Update(s => s.InactiveGlowEnabled = value);
        }
    }

    public Color InactiveGlowColorValue
    {
        get => Color.TryParse(_settings.Current.InactiveGlowColorHex, out var c) ? c : Colors.White;
        set
        {
            var hex = value.ToString().ToUpperInvariant();
            if (hex == _settings.Current.InactiveGlowColorHex) return;
            _settings.Update(s => s.InactiveGlowColorHex = hex);
        }
    }

    public double InactiveGlowRadiusVal
    {
        get => _settings.Current.InactiveGlowRadius;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.InactiveGlowRadius) < 0.5) return;
            _settings.Update(s => s.InactiveGlowRadius = v);
        }
    }

    public bool GlowEnabled
    {
        get => _settings.Current.GlowEnabled;
        set
        {
            if (value == _settings.Current.GlowEnabled) return;
            _settings.Update(s => s.GlowEnabled = value);
        }
    }

    public Color GlowColorValue
    {
        get => Color.TryParse(_settings.Current.GlowColorHex, out var c) ? c : Colors.White;
        set
        {
            var hex = value.ToString().ToUpperInvariant();
            if (hex == _settings.Current.GlowColorHex) return;
            _settings.Update(s => s.GlowColorHex = hex);
        }
    }

    public double GlowRadiusVal
    {
        get => _settings.Current.GlowRadius;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.GlowRadius) < 0.5) return;
            _settings.Update(s => s.GlowRadius = v);
        }
    }

    public Color ShadowColorValue
    {
        get => Color.TryParse(_settings.Current.ShadowColorHex, out var c) ? c : Colors.Black;
        set
        {
            var hex = value.ToString().ToUpperInvariant();
            if (hex == _settings.Current.ShadowColorHex) return;
            _settings.Update(s => s.ShadowColorHex = hex);
        }
    }

    public double ShadowBlurVal
    {
        get => _settings.Current.ShadowBlurRadius;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.ShadowBlurRadius) < 0.5) return;
            _settings.Update(s => s.ShadowBlurRadius = v);
        }
    }

    public double ShadowOffsetYVal
    {
        get => _settings.Current.ShadowOffsetY;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.ShadowOffsetY) < 0.5) return;
            _settings.Update(s => s.ShadowOffsetY = v);
        }
    }

    public double TransFontSizeVal
    {
        get => _settings.Current.TransFontSize;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.TransFontSize) < 1) return;
            _settings.Update(s => s.TransFontSize = v);
        }
    }

    public double LineSpacingVal
    {
        get => _settings.Current.LineSpacing;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.LineSpacing) < 0.5) return;
            _settings.Update(s => s.LineSpacing = v);
        }
    }

    public double TextOpacity
    {
        get => _settings.Current.Opacity;
        set
        {
            if (Math.Abs(value - _settings.Current.Opacity) < 0.01) return;
            _settings.Update(s => s.Opacity = Math.Round(value, 2));
        }
    }

    public double MaxWidth
    {
        get => _settings.Current.MaxWidth;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.MaxWidth) < 1) return;
            _settings.Update(s => s.MaxWidth = v);
        }
    }

    // ---------- 字体预览 ----------
    public string FontPreviewText => "预览文字 AaBbCc 晴";

    public FontFamily SelectedFontFamily
    {
        get
        {
            try { return new FontFamily(SelectedFont); }
            catch { return FontFamily.Default; }
        }
    }
}
