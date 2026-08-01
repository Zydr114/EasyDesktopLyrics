using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace EasyDesktopLyrics.Views;

/// <summary>
/// 自绘歌词文本：TextLayout 分段着色实现卡拉 OK 逐字高亮；
/// 描边 = 8 向偏移整行绘制（无需逐字）；辉光由外部 Effect 提供。
/// HighlightLength = -1 时整行单色（无逐字数据兜底）。
/// </summary>
public sealed class LyricText : Control
{
    private static readonly (double dx, double dy)[] StrokeOffsets =
    {
        (0, -1), (0, 1), (-1, 0), (1, 0),
        (-0.7, -0.7), (0.7, 0.7), (-0.7, 0.7), (0.7, -0.7),
    };

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<LyricText, string?>(nameof(Text));

    /// <summary>已唱字符数；-1 = 整行单色。</summary>
    public static readonly StyledProperty<int> HighlightLengthProperty =
        AvaloniaProperty.Register<LyricText, int>(nameof(HighlightLength), -1);

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<LyricText, FontFamily>(nameof(FontFamily), FontFamily.Default);

    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        AvaloniaProperty.Register<LyricText, FontWeight>(nameof(FontWeight), FontWeight.Normal);

    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<LyricText, double>(nameof(FontSize), 12);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<LyricText, IBrush?>(nameof(Fill));

    /// <summary>未唱段颜色（逐字模式）。</summary>
    public static readonly StyledProperty<IBrush?> InactiveFillProperty =
        AvaloniaProperty.Register<LyricText, IBrush?>(nameof(InactiveFill));

    public static readonly StyledProperty<IBrush?> StrokeBrushProperty =
        AvaloniaProperty.Register<LyricText, IBrush?>(nameof(StrokeBrush));

    /// <summary>描边总开关（false 时即使 StrokeThickness&gt;0 也不绘制）。</summary>
    public static readonly StyledProperty<bool> StrokeEnabledProperty =
        AvaloniaProperty.Register<LyricText, bool>(nameof(StrokeEnabled), false);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<LyricText, double>(nameof(StrokeThickness), 0);

    /// <summary>未唱段描边开关（逐字模式；false 时未唱段描边透明，避免整行描边抹平明暗差）。</summary>
    public static readonly StyledProperty<bool> InactiveStrokeEnabledProperty =
        AvaloniaProperty.Register<LyricText, bool>(nameof(InactiveStrokeEnabled), false);

    /// <summary>未唱段描边画笔（逐字模式）。</summary>
    public static readonly StyledProperty<IBrush?> InactiveStrokeBrushProperty =
        AvaloniaProperty.Register<LyricText, IBrush?>(nameof(InactiveStrokeBrush));

    public static readonly StyledProperty<double> MaxTextWidthProperty =
        AvaloniaProperty.Register<LyricText, double>(nameof(MaxTextWidth), double.PositiveInfinity);

    public static readonly StyledProperty<TextAlignment> TextAlignmentProperty =
        AvaloniaProperty.Register<LyricText, TextAlignment>(nameof(TextAlignment), TextAlignment.Center);

    private TextLayout? _bodyLayout;
    private TextLayout? _strokeLayout;
    private bool _layoutDirty = true;

    static LyricText()
    {
        AffectsMeasure<LyricText>(
            TextProperty, FontFamilyProperty, FontWeightProperty, FontSizeProperty,
            MaxTextWidthProperty, HighlightLengthProperty);
        AffectsRender<LyricText>(
            TextProperty, FontFamilyProperty, FontWeightProperty, FontSizeProperty,
            FillProperty, InactiveFillProperty, StrokeBrushProperty, StrokeEnabledProperty,
            StrokeThicknessProperty, InactiveStrokeEnabledProperty, InactiveStrokeBrushProperty,
            TextAlignmentProperty, HighlightLengthProperty, MaxTextWidthProperty);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public int HighlightLength
    {
        get => GetValue(HighlightLengthProperty);
        set => SetValue(HighlightLengthProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? InactiveFill
    {
        get => GetValue(InactiveFillProperty);
        set => SetValue(InactiveFillProperty, value);
    }

    public IBrush? StrokeBrush
    {
        get => GetValue(StrokeBrushProperty);
        set => SetValue(StrokeBrushProperty, value);
    }

    public bool StrokeEnabled
    {
        get => GetValue(StrokeEnabledProperty);
        set => SetValue(StrokeEnabledProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool InactiveStrokeEnabled
    {
        get => GetValue(InactiveStrokeEnabledProperty);
        set => SetValue(InactiveStrokeEnabledProperty, value);
    }

    public IBrush? InactiveStrokeBrush
    {
        get => GetValue(InactiveStrokeBrushProperty);
        set => SetValue(InactiveStrokeBrushProperty, value);
    }

    public double MaxTextWidth
    {
        get => GetValue(MaxTextWidthProperty);
        set => SetValue(MaxTextWidthProperty, value);
    }

    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>任何属性变化都标记布局陈旧：绘制前重建（颜色/字号等实时生效）。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        _layoutDirty = true;
    }

    public override void Render(DrawingContext context)
    {
        RebuildIfDirty();
        var body = _bodyLayout;
        if (body == null)
            return;

        var thickness = StrokeEnabled ? StrokeThickness : 0;
        if (thickness > 0 && _strokeLayout != null)
        {
            foreach (var (dx, dy) in StrokeOffsets)
                _strokeLayout.Draw(context, new Point(dx * thickness, dy * thickness));
        }

        body.Draw(context, new Point());
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        RebuildIfDirty();
        var body = _bodyLayout;
        return body != null ? new Size(body.Width, body.Height) : new Size();
    }

    private void RebuildIfDirty()
    {
        if (!_layoutDirty)
            return;
        _layoutDirty = false;
        Rebuild();
    }

    private void Rebuild()
    {
        var text = Text;
        if (string.IsNullOrEmpty(text))
        {
            _bodyLayout = null;
            _strokeLayout = null;
            return;
        }

        var typeface = new Typeface(FontFamily, FontStyle.Normal, FontWeight, FontStretch.Normal);
        var alignment = TextAlignment;
        var wrapping = TextWrapping.NoWrap;
        var maxWidth = double.PositiveInfinity;
        var maxHeight = double.PositiveInfinity;

        var h = HighlightLength;
        var len = text.Length;
        // 逐字模式下的分段样式；整行单色（h<0 或 h>=len）时为 null
        IReadOnlyList<ValueSpan<TextRunProperties>>? styles = null;
        if (h >= 0 && h < len && Fill != null && InactiveFill != null)
        {
            styles = h > 0
                ? [new ValueSpan<TextRunProperties>(0, h, RunProps(typeface, FontSize, Fill)),
                   new ValueSpan<TextRunProperties>(h, len - h, RunProps(typeface, FontSize, InactiveFill))]
                : [new ValueSpan<TextRunProperties>(0, len, RunProps(typeface, FontSize, InactiveFill))];
        }

        // 描边：与正文同分段——已唱段用 StrokeBrush，未唱段用 InactiveStrokeBrush
        // （未唱描边关闭时未唱段描边透明，避免整行描边抹平逐字明暗差）
        var strokeStyles = styles;
        if (strokeStyles != null && (!InactiveStrokeEnabled || InactiveStrokeBrush == null))
        {
            var inactiveStroke = Brushes.Transparent;
            strokeStyles = h > 0
                ? [new ValueSpan<TextRunProperties>(0, h, RunProps(typeface, FontSize, StrokeBrush)),
                   new ValueSpan<TextRunProperties>(h, len - h, RunProps(typeface, FontSize, inactiveStroke))]
                : [new ValueSpan<TextRunProperties>(0, len, RunProps(typeface, FontSize, inactiveStroke))];
        }
        else if (strokeStyles != null)
        {
            strokeStyles = h > 0
                ? [new ValueSpan<TextRunProperties>(0, h, RunProps(typeface, FontSize, StrokeBrush)),
                   new ValueSpan<TextRunProperties>(h, len - h, RunProps(typeface, FontSize, InactiveStrokeBrush))]
                : [new ValueSpan<TextRunProperties>(0, len, RunProps(typeface, FontSize, InactiveStrokeBrush))];
        }

        _strokeLayout = StrokeEnabled && StrokeThickness > 0 && StrokeBrush != null
            ? CreateLayout(text, typeface, StrokeBrush, alignment, wrapping, maxWidth, maxHeight, strokeStyles)
            : null;

        _bodyLayout = CreateLayout(text, typeface, Fill, alignment, wrapping, maxWidth, maxHeight, styles);
    }

    private TextLayout CreateLayout(
        string text, Typeface typeface, IBrush? foreground, TextAlignment alignment,
        TextWrapping wrapping, double maxWidth, double maxHeight,
        IReadOnlyList<ValueSpan<TextRunProperties>>? styles)
    {
        return new TextLayout(text, typeface, FontSize, foreground,
            textAlignment: alignment,
            textWrapping: wrapping,
            maxWidth: maxWidth,
            maxHeight: maxHeight,
            textStyleOverrides: styles);
    }

    private static TextRunProperties RunProps(Typeface typeface, double fontSize, IBrush brush) =>
        new GenericTextRunProperties(typeface, fontSize, foregroundBrush: brush);
}
