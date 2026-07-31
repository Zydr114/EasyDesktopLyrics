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
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.StrokeThickness) < 0.5) return;
            _settings.Update(s => s.StrokeThickness = v);
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
