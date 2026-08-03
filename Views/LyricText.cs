using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;

namespace EasyDesktopLyrics.Views;

/// <summary>
/// 自绘歌词文本：非逐字时整行单色 TextLayout 平涂；
/// 逐字（卡拉 OK）时整行一次绘制 + 线性渐变笔刷——每个字符按"已唱色 → 分界线 → 柔和过渡带 → 未唱色"
/// 平滑渐变，分界线在当前字内部随 HighlightFraction 平滑右移（真渐变填充，非硬裁剪）。
/// 描边 = 8 向偏移整行绘制（使用描边渐变）；辉光由外部 Effect 提供（作用于渐变结果）。
/// HighlightLength = -1 时整行单色（无逐字数据）。
/// </summary>
public sealed class LyricText : Control
{
    private static readonly (double dx, double dy)[] StrokeOffsets =
    {
        (0, -1), (0, 1), (-1, 0), (1, 0),
        (-0.7, -0.7), (0.7, 0.7), (-0.7, 0.7), (0.7, -0.7),
    };

    /// <summary>层角色：0 = 整行平涂；1 = 已唱层（渐变遮罩露已唱段）；2 = 未唱层（渐变遮罩露未唱段）。</summary>
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

    /// <summary>层角色（固定，由宿主设置）：Left=已唱层、Right=未唱层、None=平涂。</summary>
    public static readonly StyledProperty<ClipSideMode> HighlightClipProperty =
        AvaloniaProperty.Register<LyricText, ClipSideMode>(nameof(HighlightClip), ClipSideMode.None);

    /// <summary>行进入逐字显现进度 0~1（从左到右逐个字符点亮）；-1 = 禁用（正常渲染）。</summary>
    public static readonly StyledProperty<double> EnterProgressProperty =
        AvaloniaProperty.Register<LyricText, double>(nameof(EnterProgress), -1);

    /// <summary>扫光带位置进度 0~1（亮带沿行从左到右扫描）；-1 = 禁用。</summary>
    public static readonly StyledProperty<double> ShineProgressProperty =
        AvaloniaProperty.Register<LyricText, double>(nameof(ShineProgress), -1);

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
    private double[] _charLeft = [];

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
            HighlightClipProperty, MaxTextWidthProperty,
            EnterProgressProperty, ShineProgressProperty);
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

    public double EnterProgress
    {
        get => GetValue(EnterProgressProperty);
        set => SetValue(EnterProgressProperty, value);
    }

    public double ShineProgress
    {
        get => GetValue(ShineProgressProperty);
        set => SetValue(ShineProgressProperty, value);
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

        // 扫光层：整行按亮带遮罩绘制（亮带位置由 ShineProgress 驱动，独立于逐字/描边分支）。
        if (ShineProgress >= 0 && ShineProgress <= 1)
        {
            var band = BuildBand(ShineProgress, body.Width);
            if (band != null)
            {
                using (context.PushOpacityMask(band, new Rect(0, 0, body.Width, body.Height)))
                    body.Draw(context, new Point());
            }
            else
            {
                DrawBody(context, body, thickness);
            }
            return;
        }

        // 平涂路径（无逐字数据 / 单色层）：整行单色。
        // 无逐字数据时没有"未唱段"概念 → 未唱侧层（Right：未唱层/未唱辉光）整体不绘制，
        // 否则未唱色/未唱辉光会作用到整行（非逐字歌词也被未唱辉光照亮）。
        if (IsFlatMode || HighlightClip == ClipSideMode.None)
        {
            if (IsFlatMode && HighlightClip == ClipSideMode.Right)
                return;
            DrawBody(context, body, thickness);
            return;
        }

        // 逐字渐变路径：整行缓存的已唱/未唱纯色布局 + PushOpacityMask 逐字渐变遮罩，
        // 遮罩在当前字内带柔和过渡带、分界线随 HighlightFraction 平滑右移 → 字内真渐变过渡（非硬裁剪）。
        var mask = BuildMask(body, HighlightClip == ClipSideMode.Left);
        if (mask == null)
        {
            DrawBody(context, body, thickness);
            return;
        }

        using (context.PushOpacityMask(mask, new Rect(0, 0, body.Width, body.Height)))
        {
            if (thickness > 0 && _strokeLayout != null)
            {
                foreach (var (dx, dy) in StrokeOffsets)
                    _strokeLayout.Draw(context, new Point(dx * thickness, dy * thickness));
            }
            body.Draw(context, new Point());
        }
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
            _charLeft = [];
            return;
        }

        var typeface = new Typeface(FontFamily, FontStyle.Normal, FontWeight, FontStretch.Normal);
        var alignment = TextAlignment;
        var wrapping = TextWrapping.NoWrap;
        var maxWidth = double.PositiveInfinity;
        var maxHeight = double.PositiveInfinity;

        // 整行单色：逐字明暗由渐变遮罩（BuildMask/BuildGradient）实现，这里只需一种前景色
        _strokeLayout = StrokeEnabled && StrokeThickness > 0 && StrokeBrush != null
            ? CreateLayout(text, typeface, StrokeBrush, alignment, wrapping, maxWidth, maxHeight)
            : null;

        _bodyLayout = CreateLayout(text, typeface, Fill, alignment, wrapping, maxWidth, maxHeight);
        _charLeft = BuildCharLefts(_bodyLayout, text);
    }

    /// <summary>平涂模式：无逐字数据（HighlightLength &lt; 0）且未处于逐字显现进入中 → 整行单色。</summary>
    private bool IsFlatMode
    {
        get
        {
            var text = Text;
            if (string.IsNullOrEmpty(text))
                return true;
            var entering = EnterProgress >= 0 && EnterProgress <= 1;
            return HighlightLength < 0 && !entering;
        }
    }

    /// <summary>
    /// 有效逐字进度：进入显现中由 EnterProgress 驱动（从左到右点亮字符），
    /// 否则用卡拉 OK 的 HighlightLength/HighlightFraction。
    /// </summary>
    private (int Done, double Frac) EffectiveHighlight(int len)
    {
        if (EnterProgress >= 0 && EnterProgress <= 1)
        {
            var p = Math.Clamp(EnterProgress, 0, 1);
            var total = p * len;
            var done = (int)total;
            return (done, total - done);
        }
        return (Math.Clamp(HighlightLength, 0, len), Math.Clamp(HighlightFraction, 0, 1));
    }

    /// <summary>扫光带渐变（亮带遮罩，只取 alpha）：位置随 progress 0~1 从左到右，软边光带。</summary>
    private static IBrush? BuildBand(double progress, double width)
    {
        if (width <= 0)
            return null;
        var pos = Math.Clamp(progress, 0, 1) * width;
        var bandW = Math.Max(24, width * 0.08);
        var stops = new List<GradientStop>(5);
        AddStop(stops, 0, Colors.Transparent);
        AddStop(stops, Math.Max(0, (pos - bandW) / width), Colors.Transparent);
        AddStop(stops, pos / width, Colors.Black);
        AddStop(stops, Math.Min(1, (pos + bandW) / width), Colors.Transparent);
        AddStop(stops, 1, Colors.Transparent);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(width, 0, RelativeUnit.Absolute),
        };
        brush.GradientStops.AddRange(stops);
        return brush;
    }

    /// <summary>逐字左缘 X（相对行首），末位为整行宽度；供渐变 stop 定位。</summary>
    private static double[] BuildCharLefts(TextLayout layout, string text)
    {
        var left = new double[text.Length + 1];
        for (var i = 0; i < text.Length; i++)
            left[i] = layout.HitTestTextPosition(i).X;
        left[text.Length] = layout.Width;
        return left;
    }

    /// <summary>
    /// 逐字渐变遮罩（PushOpacityMask 用，只取 alpha）：已唱段不透明、未唱段透明、正在唱字内
    /// 从分界线到过渡带平滑渐变。maskPlayed=true → 遮住未唱段（已唱层用）；false → 遮住已唱段（未唱层用）。
    /// </summary>
    private IBrush? BuildMask(TextLayout layout, bool maskPlayed)
    {
        return maskPlayed
            ? BuildGradient(layout, Colors.Black, Colors.Transparent)
            : BuildGradient(layout, Colors.Transparent, Colors.Black);
    }

    /// <summary>逐字渐变：每个字符左边界放一个颜色 stop（已唱=played / 未唱=unplayed），
    /// 正在唱字内放"played 实色 → 分界线 → 柔和过渡带 → unplayed"。
    /// 渐变两端用 Absolute 点（0..width）、stop offset 用 0..1 归一化 → 与目标矩形尺寸无关，随字形精确定位。</summary>
    private LinearGradientBrush? BuildGradient(TextLayout layout, Color played, Color unplayed)
    {
        var len = _charLeft.Length - 1;
        if (len <= 0)
            return null;

        var (done, frac) = EffectiveHighlight(len);
        var width = layout.Width;
        if (width <= 0)
            return null;

        var stops = new List<GradientStop>(len * 2 + 4);
        AddStop(stops, 0, played);

        for (var i = 0; i < len; i++)
        {
            var x0 = _charLeft[i];
            var x1 = _charLeft[i + 1];
            if (i < done)
            {
                AddStop(stops, x0 / width, played);
            }
            else if (i > done)
            {
                AddStop(stops, x0 / width, unplayed);
            }
            else
            {
                // 正在唱字：played 实色铺到分界线，再经过渡带渐变到 unplayed
                var charW = Math.Max(x1 - x0, 0.001);
                var boundary = x0 + charW * frac;
                var fadeW = Math.Min(charW * 0.35, 8);
                var fadeEnd = Math.Min(x1, boundary + fadeW);
                AddStop(stops, x0 / width, played);
                AddStop(stops, boundary / width, played);
                AddStop(stops, fadeEnd / width, unplayed);
            }
        }
        AddStop(stops, 1, done >= len ? played : unplayed);

        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(width, 0, RelativeUnit.Absolute),
        };
        brush.GradientStops.AddRange(stops);
        return brush;
    }

    /// <summary>合并相邻等位置 stop（代理对/零宽字符会共用左缘）。</summary>
    private static void AddStop(List<GradientStop> stops, double offset, Color color)
    {
        if (stops.Count > 0)
        {
            var last = stops[^1];
            if (Math.Abs(last.Offset - offset) < 0.0005)
            {
                if (last.Color != color)
                    stops[^1] = new GradientStop(color, offset);
                return;
            }
        }
        stops.Add(new GradientStop(color, offset));
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
