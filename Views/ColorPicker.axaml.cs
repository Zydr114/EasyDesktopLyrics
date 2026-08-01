using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace EasyDesktopLyrics.Views;

/// <summary>
/// 自绘 HSV 颜色选取器：饱和度/明度面板 + 色相条 + 透明度条 + Hex 输入。
/// 无第三方依赖，仅用 Avalonia 原生渐变与指针事件。
/// </summary>
public sealed partial class ColorPicker : UserControl
{
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<ColorPicker, Color>(nameof(Color), Colors.White,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private double _h, _s, _v;
    private double _a = 1.0;
    private bool _suppress;
    private bool _dragging;
    private double _lastHue = double.NaN;
    private Color _lastPreview = default;

    // 静态渐变只建一次（不随颜色变化）；SV 面板白→hue 渐变在色相变化时重建
    private readonly LinearGradientBrush _hueBrush = BuildHueGradient();
    private readonly LinearGradientBrush _valueBrush = BuildValueGradient();
    private readonly LinearGradientBrush _shadeBrush = BuildShadeGradient();
    private LinearGradientBrush? _panelBrush;

    /// <summary>颜色被修改（用户交互或外部赋值）时触发。</summary>
    public event Action<Color>? ColorChanged;

    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public ColorPicker()
    {
        InitializeComponent();

        SvPanel.PointerPressed += OnSvPressed;
        SvPanel.PointerMoved += OnSvPointer;
        SvPanel.PointerReleased += OnReleased;

        HueBar.PointerPressed += OnHuePressed;
        HueBar.PointerMoved += OnHuePointer;
        HueBar.PointerReleased += OnReleased;

        ValueBar.PointerPressed += OnValuePressed;
        ValueBar.PointerMoved += OnValuePointer;
        ValueBar.PointerReleased += OnReleased;

        HexBox.TextChanged += OnHexChanged;
        ColorProperty.Changed.AddClassHandler<ColorPicker>((x, _) => x.OnColorExternallyChanged());
        SizeChanged += (_, _) => UpdateIndicators();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        OnColorExternallyChanged();
    }

    private void OnSvPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = true;
        e.Pointer.Capture(SvPanel);
        OnSvPointer(sender, e);
    }

    private void OnHuePressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = true;
        e.Pointer.Capture(HueBar);
        OnHuePointer(sender, e);
    }

    private void OnValuePressed(object? sender, PointerPressedEventArgs e)
    {
        _dragging = true;
        e.Pointer.Capture(ValueBar);
        OnValuePointer(sender, e);
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void OnSvPointer(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(SvPanel).Properties.IsLeftButtonPressed)
            return;

        var p = e.GetCurrentPoint(SvPanel).Position;
        _s = Math.Clamp(p.X / Math.Max(1, SvPanel.Bounds.Width), 0, 1);
        _v = 1 - Math.Clamp(p.Y / Math.Max(1, SvPanel.Bounds.Height), 0, 1);
        ApplyFromHsv();
    }

    private void OnHuePointer(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(HueBar).Properties.IsLeftButtonPressed)
            return;

        var p = e.GetCurrentPoint(HueBar).Position;
        _h = Math.Clamp(p.X / Math.Max(1, HueBar.Bounds.Width), 0, 1) * 360;
        ApplyFromHsv();
    }

    private void OnValuePointer(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(ValueBar).Properties.IsLeftButtonPressed)
            return;

        var p = e.GetCurrentPoint(ValueBar).Position;
        _v = Math.Clamp(p.X / Math.Max(1, ValueBar.Bounds.Width), 0, 1);
        ApplyFromHsv();
    }

    private void ApplyFromHsv()
    {
        _suppress = true;
        Color = HsvToRgb(_h, _s, _v, _a);
        _suppress = false;

        // 拖动路径轻量刷新：仅重建必要内容（色相变 → SV 面板渐变；预览/Hex 带缓存与防递归）
        if (_lastHue != _h)
        {
            _lastHue = _h;
            SvPanel.Background = BuildPanelBrush(HsvToRgb(_h, 1, 1, 1));
        }
        var c = Color;
        if (_lastPreview != c)
        {
            _lastPreview = c;
            PreviewBlock.Background = new SolidColorBrush(c);
        }
        var hex = FormatHex(c);
        if (HexBox.Text != hex)
        {
            _suppress = true;
            HexBox.Text = hex;
            _suppress = false;
        }
        UpdateIndicators();
        ColorChanged?.Invoke(c);
    }

    private void OnColorExternallyChanged()
    {
        if (_suppress)
            return;
        // 拖动中绑定回写的是本控件刚写入的值：跳过 hsv 重算，避免 RGB 量化导致手柄抖动
        if (_dragging)
            return;
        SyncHsvFromColor(Color);
        RefreshUi();
        ColorChanged?.Invoke(Color);
    }

    /// <summary>
    /// 由 RGB 同步 HSV 状态；灰/黑/白（无饱和度或明度）时保留当前色相——
    /// RGB 无法还原色相，否则拖动明暗到端点会重置色相条。
    /// </summary>
    private void SyncHsvFromColor(Color c)
    {
        var (h, s, v) = RgbToHsv(c);
        if (s > 0.001 && v > 0.001)
            _h = h;
        _s = s;
        _v = v;
        _a = c.A / 255.0;
    }

    private void OnHexChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppress || HexBox.Text is null)
            return;
        if (Color.TryParse(HexBox.Text.Trim(), out var c))
        {
            _suppress = true;
            Color = c;
            _suppress = false;
            SyncHsvFromColor(c);
            RefreshUi();
            ColorChanged?.Invoke(c);
        }
    }

    private void RefreshUi()
    {
        var hue = HsvToRgb(_h, 1, 1, 1);
        _lastHue = _h;
        _lastPreview = Color;

        SvPanel.Background = BuildPanelBrush(hue);
        SvShade.Background = _shadeBrush;
        HueBar.Background = _hueBrush;
        ValueBar.Background = _valueBrush;
        PreviewBlock.Background = new SolidColorBrush(Color);
        SetHexSilently(FormatHex(Color));
        UpdateIndicators();
    }

    /// <summary>设置 Hex 文本但不触发 OnHexChanged 递归刷新。</summary>
    private void SetHexSilently(string hex)
    {
        if (HexBox.Text == hex)
            return;
        _suppress = true;
        HexBox.Text = hex;
        _suppress = false;
    }

    private static LinearGradientBrush BuildPanelBrush(Color hue) => new()
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Colors.White, 0),
            new GradientStop(hue, 1),
        },
    };

    /// <summary>明暗层：透明→黑（静态，不随颜色变化）。</summary>
    private static LinearGradientBrush BuildShadeGradient() => new()
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
            new GradientStop(Colors.Black, 1),
        },
    };

    /// <summary>明暗条：黑→白渐变（静态）。</summary>
    private static LinearGradientBrush BuildValueGradient() => new()
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Colors.Black, 0),
            new GradientStop(Colors.White, 1),
        },
    };

    private static LinearGradientBrush BuildHueGradient()
    {
        static Color C(double h) => HsvToRgb(h, 1, 1, 1);
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(C(0), 0.00),
                new GradientStop(C(60), 0.17),
                new GradientStop(C(120), 0.33),
                new GradientStop(C(180), 0.50),
                new GradientStop(C(240), 0.67),
                new GradientStop(C(300), 0.83),
                new GradientStop(C(360), 1.00),
            },
        };
    }

    private void UpdateIndicators()
    {
        var svw = SvPanel.Bounds.Width;
        var svh = SvPanel.Bounds.Height;
        Canvas.SetLeft(SvIndicator, Math.Clamp(_s * svw - 6, -2, Math.Max(0, svw - 10)));
        Canvas.SetTop(SvIndicator, Math.Clamp((1 - _v) * svh - 6, -2, Math.Max(0, svh - 10)));
        Canvas.SetLeft(HueIndicator, _h / 360.0 * Math.Max(0, HueBar.Bounds.Width - 6));
        Canvas.SetLeft(ValueIndicator, _v * Math.Max(0, ValueBar.Bounds.Width - 6));
    }

    private static string FormatHex(Color c) =>
        c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color HsvToRgb(double h, double s, double v, double a = 1.0)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromArgb(
            (byte)Math.Round(a * 255),
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    private static (double H, double S, double V) RgbToHsv(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double h;
        if (d == 0)
        {
            h = 0;
        }
        else if (max == r)
        {
            h = 60 * (((g - b) / d) % 6);
        }
        else if (max == g)
        {
            h = 60 * ((b - r) / d + 2);
        }
        else
        {
            h = 60 * ((r - g) / d + 4);
        }
        if (h < 0)
            h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }
}
