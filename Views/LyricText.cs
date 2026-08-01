using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace EasyDesktopLyrics.Views;

/// <summary>
/// 自绘歌词文本：整行单色 TextLayout + 按分界线 PushClip 裁剪实现卡拉 OK 逐字高亮；
/// 已唱层裁左侧（ClipSide=Left）、未唱层裁右侧（ClipSide=Right），分界线在正在唱字符内部
/// 随 HighlightFraction 平滑移动（字符内同时存在已唱/未唱两种样式）。
/// 描边 = 8 向偏移整行绘制；辉光由外部 Effect 提供（作用于裁剪后的结果）。
/// HighlightLength = -1 或 h&gt;=len 时不裁剪（整行单色）。
/// </summary>
public sealed class LyricText : Control
{
    private static readonly (double dx, double dy)[] StrokeOffsets =
    {
        (0, -1), (0, 1), (-1, 0), (1, 0),
        (-0.7, -0.7), (0.7, 0.7), (-0.7, 0.7), (0.7, -0.7),
    };

    /// <summary>裁剪模式：0 = 不裁剪，1 = 裁分界线左侧（显示已唱段），2 = 裁分界线右侧（显示未唱段）。</summary>
    public enum ClipSideMode
    {
        None = 0,
        Left = 1,
        Right = 2,
    }

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<LyricText, string?>(nameof(Text));

    /// <summary>已唱字符数；-1 = 整行单色（无逐字数据）。</summary>
    public static readonly StyledProperty<int> HighlightLengthProperty =
        AvaloniaProperty.Register<LyricText, int>(nameof(HighlightLength), -1);

    /// <summary>正在唱字符的渐变进度 0~1（分界线在字内从左向右移动）。</summary>
    public static readonly StyledProperty<double> HighlightFractionProperty =
        AvaloniaProperty.Register<LyricText, double>(nameof(HighlightFraction), 0);

    /// <summary>裁剪模式（层角色固定，由宿主设置）。</summary>
    public static readonly StyledProperty<ClipSideMode> HighlightClipProperty =
        AvaloniaProperty.Register<LyricText, ClipSideMode>(nameof(HighlightClip), ClipSideMode.None);

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
            MaxTextWidthProperty);
        AffectsRender<LyricText>(
            TextProperty, FontFamilyProperty, FontWeightProperty, FontSizeProperty,
            FillProperty, InactiveFillProperty, StrokeBrushProperty, StrokeEnabledProperty,
            StrokeThicknessProperty, InactiveStrokeEnabledProperty, InactiveStrokeBrushProperty,
            TextAlignmentProperty, HighlightLengthProperty, HighlightFractionProperty,
            HighlightClipProperty, MaxTextWidthProperty);
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

    public double HighlightFraction
    {
        get => GetValue(HighlightFractionProperty);
        set => SetValue(HighlightFractionProperty, value);
    }

    public ClipSideMode HighlightClip
    {
        get => GetValue(HighlightClipProperty);
        set => SetValue(HighlightClipProperty, value);
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

    /// <summary>布局相关属性变化才重建；逐字进度/裁剪只重绘（布局为整行单色，无需重建）。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        var p = change.Property;
        if (p == TextProperty || p == FontFamilyProperty || p == FontWeightProperty || p == FontSizeProperty
            || p == MaxTextWidthProperty || p == TextAlignmentProperty
            || p == FillProperty || p == InactiveFillProperty
            || p == StrokeBrushProperty || p == InactiveStrokeBrushProperty
            || p == StrokeThicknessProperty || p == StrokeEnabledProperty || p == InactiveStrokeEnabledProperty)
        {
            _layoutDirty = true;
        }
        else
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        RebuildIfDirty();
        var body = _bodyLayout;
        if (body == null)
            return;

        var thickness = StrokeEnabled ? StrokeThickness : 0;

        var clipped = false;
        if (HighlightClip != ClipSideMode.None && body.Width > 0)
        {
            var clipX = ComputeClipX(body);
            if (clipX > 0 && clipX < body.Width)
            {
                var rect = HighlightClip == ClipSideMode.Left
                    ? new Rect(0, 0, clipX, body.Height)
                    : new Rect(clipX, 0, Math.Max(0, body.Width - clipX), body.Height);
                if (rect.Width > 0)
                {
                    using var _ = context.PushClip(rect);
                    clipped = true;
                    DrawBody(context, body, thickness);
                    return;
                }
            }
        }

        DrawBody(context, body, thickness);
    }

    private void DrawBody(DrawingContext context, TextLayout body, double thickness)
    {
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

        // 整行单色：逐字明暗由层裁剪（HighlightClip）实现，这里只需一种前景色
        _strokeLayout = StrokeEnabled && StrokeThickness > 0 && StrokeBrush != null
            ? CreateLayout(text, typeface, StrokeBrush, alignment, wrapping, maxWidth, maxHeight)
            : null;

        _bodyLayout = CreateLayout(text, typeface, Fill, alignment, wrapping, maxWidth, maxHeight);
    }

    /// <summary>分界线 X（正在唱字左缘 + 字宽 × 进度）；无逐字/整行完成时返回 -1（不裁剪）。</summary>
    private double ComputeClipX(TextLayout layout)
    {
        var h = HighlightLength;
        var text = Text;
        if (h < 0 || text == null)
            return -1;
        var len = text.Length;
        if (h >= len)
            return -1;

        var frac = Math.Clamp(HighlightFraction, 0, 1);
        var curLen = char.IsHighSurrogate(text[h]) ? 2 : 1;
        var x0 = layout.HitTestTextPosition(h).X;
        var x1 = h + curLen < len
            ? layout.HitTestTextPosition(h + curLen).X
            : layout.Width; // 正在唱字为最后一个字符：右缘 = 文本宽度
        return x0 + (x1 - x0) * frac;
    }

    private TextLayout CreateLayout(
        string text, Typeface typeface, IBrush? foreground, TextAlignment alignment,
        TextWrapping wrapping, double maxWidth, double maxHeight)
    {
        return new TextLayout(text, typeface, FontSize, foreground,
            textAlignment: alignment,
            textWrapping: wrapping,
            maxWidth: maxWidth,
            maxHeight: maxHeight);
    }
}
