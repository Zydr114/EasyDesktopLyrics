using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Threading;
using EasyDesktopLyrics.Interop;
using EasyDesktopLyrics.Infrastructure;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services;
using EasyDesktopLyrics.ViewModels;

namespace EasyDesktopLyrics.Views;

public sealed partial class LyricsOverlayWindow : Window
{
    private static readonly (double dx, double dy)[] StrokeOffsets =
    {
        (0, -1), (0, 1), (-1, 0), (1, 0),
        (-0.7, -0.7), (0.7, 0.7), (-0.7, 0.7), (0.7, -0.7),
    };

    private readonly OverlayViewModel _vm;
    private readonly SettingsService _settingsService;
    private readonly DispatcherTimer _topmostTimer;
    private readonly DispatcherTimer _hideControlsTimer;
    private readonly DispatcherTimer _coverStageTimer;
    private readonly DispatcherTimer _coverTimeoutTimer;
    private readonly UiDebouncer _anchorDebouncer = new();

    private IntPtr _hwnd;
    private bool _locked;
    private bool _allowClose;
    private PixelPoint _anchor;
    private bool _suppressPositionUpdate;
    private bool _dragStarted;
    private bool _coverAnimating;
    private double _coverAnimSize;
    private DateTimeOffset _coverAnimStart;
    private bool _coverPosDirty;
    private bool _firstSizeApplied;
    private int _lastGrowBottom;
    private bool _lastCoverEnabled;
    private double _lastCoverSizePct;

    // 动态文本层
    private Grid _mainGrid = null!;
    private LyricText _inactiveLyric = null!;
    private LyricText _mainLyric = null!;
    private Grid _transGrid = null!;
    private TextBlock _transTb = null!;
    private readonly List<TextBlock> _strokeLayers = [];
    private readonly List<Control> _glowLayers = [];

    public event Action<double, double>? AnchorChanged;

    /// <summary>
    /// 锁定/解锁窗口：锁定时整个窗口鼠标穿透（WS_EX_TRANSPARENT）且禁止拖动，
    /// 解锁仅能通过托盘菜单或设置页（穿透后窗口本身无法接收点击）。
    /// </summary>
    public void SetLocked(bool locked)
    {
        _locked = locked;
        if (_hwnd == IntPtr.Zero)
            return;
        Win32.SetClickThrough(_hwnd, locked);
        Log.Info($"overlay locked={locked}");
    }

    /// <summary>允许真正关闭窗口（仅程序退出时由 Cleanup 调用，此后窗口不可再显示）。</summary>
    public void AllowClose() => _allowClose = true;

    public LyricsOverlayWindow(OverlayViewModel vm, SettingsService settingsService)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        _settingsService = settingsService;

        BuildTextLayers();

        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _topmostTimer.Tick += (_, _) => Win32.AssertTopmost(_hwnd);
        _topmostTimer.Start();

        _hideControlsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _hideControlsTimer.Tick += (_, _) =>
        {
            _hideControlsTimer.Stop();
            SetControlsVisible(false);
        };

        _coverStageTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _coverStageTimer.Tick += (_, _) => OnCoverStage();

        _coverTimeoutTimer = new DispatcherTimer { Interval = CoverAnimTimeout };
        _coverTimeoutTimer.Tick += (_, _) => OnCoverStage();

        PointerEntered += (_, _) => { _hideControlsTimer.Stop(); SetControlsVisible(true); };
        PointerExited += (_, _) => _hideControlsTimer.Start();

        // 窗口高度显式管理：Avalonia 的 SizeToContent 高度跟随在高分屏环境不可靠
        // （窗口高度不随内容增长 → 封面/胶囊被窗口边缘截断），宽度仍由 SizeToContent 自动。
        LayoutUpdated += (_, _) =>
        {
            SyncWindowHeight();
            // 歌名在首轮布局后按实测宽度定位（封面下方），之后不再跟随封面动画路径
            if (_coverAnimating && _coverPosDirty)
            {
                _coverPosDirty = false;
                UpdateCoverTitlePosition();
            }
        };

        _lastCoverEnabled = _vm.CoverEnabled;
        _lastCoverSizePct = _vm.CoverSizePct;
        LyricsArea.MinWidth = _vm.MaxTextWidth;

        _mainGrid.SizeChanged += (_, _) => UpdateCoverSize();
        // 歌词行宽度变化（不同行文本长度）时封面位置跟随（靠右布局依赖歌词宽度）
        RootPanel.SizeChanged += (_, _) => UpdateCoverPosition();
        _vm.PropertyChanged += OnVmChanged;
        SizeChanged += OnSizeChanged;
    }

    /// <summary>hover 显示播放控制条（内容下方展开）；动画期间不显示（避免窗口高度变化干扰）。</summary>
    private void SetControlsVisible(bool visible)
    {
        if (visible && _coverAnimating)
            return; // 切歌动画期间保持隐藏
        ControlBar.Height = visible ? 34 : 0;
        ControlBar.Opacity = visible ? 1 : 0;
        ControlBar.IsHitTestVisible = visible;
    }

    /// <summary>
    /// 窗口高度 = 内容布局期望高度（主行 + 翻译行 + spacing + 封面覆盖层 + 歌名区 + 控制条 + padding）。
    /// 各组件 Bounds 均与窗口高度无关（宽度由 SizeToContent 决定，缩放由此确定）→ 收敛无循环。
    /// </summary>
    private double ContentHeight()
    {
        var h = _mainGrid.Bounds.Height;
        if (_transGrid.Bounds.Height > 0)
            h += _transGrid.Bounds.Height + _vm.LineSpacing;
        h = Math.Max(h, Cover.Bounds.Height);
        // 动画期间歌名显示在封面下方：窗口容纳封面 + 间距 + 歌名区
        if (_coverAnimating && CoverTitleBox.IsVisible && CoverTitleBox.Bounds.Height > 0)
            h = Math.Max(h, Cover.Bounds.Height + 8 + CoverTitleBox.Bounds.Height);
        return h + ControlBar.Height + 12; // 12 = Border padding 6×2
    }

    /// <summary>显式同步窗口高度（差异 > 0.5 才设置，避免抖动/循环）。</summary>
    private void SyncWindowHeight()
    {
        if (!IsVisible || Height <= 0)
            return;
        var want = ContentHeight();
        if (want > 0 && Math.Abs(want - Height) > 0.5)
            Height = want;
    }

    private void UpdatePlayPauseIcon()
    {
        PlayPauseIcon.Text = _vm.IsPlaying ? "\uE769" : "\uE768"; // Pause / Play
    }

    private void BuildTextLayers()
    {
        RootPanel.Children.Clear();
        _strokeLayers.Clear();

        _mainGrid = new Grid();
        // 未唱层（下）：整行未唱色 + 独立未唱描边 + 阴影；逐字明暗差不被描边抹平
        _inactiveLyric = new LyricText();
        BindInactiveLyric(_inactiveLyric);
        _mainGrid.Children.Add(_inactiveLyric);
        // 已唱层（上）：已唱段实色、未唱段透明（透出下层），已唱描边
        _mainLyric = new LyricText();
        BindLyric(_mainLyric);
        _mainGrid.Children.Add(_mainLyric);
        RootPanel.Children.Add(_mainGrid);

        _transGrid = new Grid();
        _transTb = CreateBoundTb("TransText");
        _transGrid.Children.Add(_transTb);
        RootPanel.Children.Add(_transGrid);

        ApplyStroke();
        ApplyGlow();
        ApplyAlignment();
        SyncAllBindings();
    }

    /// <summary>已唱层：整行已唱色 + 已唱描边，裁剪分界线左侧；阴影作用于整行。</summary>
    private void BindLyric(LyricText lt)
    {
        BindLyricBase(lt);
        lt.Bind(LyricText.HighlightLengthProperty, new Avalonia.Data.Binding("WordHighlightLength"));
        lt.Bind(LyricText.FillProperty, new Avalonia.Data.Binding("Fill"));
        lt.Bind(LyricText.StrokeEnabledProperty, new Avalonia.Data.Binding("StrokeEnabled"));
        lt.Bind(LyricText.StrokeBrushProperty, new Avalonia.Data.Binding("StrokeBrush"));
        lt.Bind(LyricText.StrokeThicknessProperty, new Avalonia.Data.Binding("StrokeThickness"));
        lt.Bind(Visual.EffectProperty, new Avalonia.Data.Binding("TextEffect"));
        lt.HighlightClip = LyricText.ClipSideMode.Left;
    }

    /// <summary>未唱层：整行未唱色 + 独立未唱描边，裁剪分界线右侧（未唱段）+ 阴影。</summary>
    private void BindInactiveLyric(LyricText lt)
    {
        BindLyricBase(lt);
        lt.Bind(LyricText.HighlightLengthProperty, new Avalonia.Data.Binding("WordHighlightLength"));
        lt.Bind(LyricText.FillProperty, new Avalonia.Data.Binding("InactiveFill"));
        lt.Bind(LyricText.StrokeEnabledProperty, new Avalonia.Data.Binding("InactiveStrokeEnabled"));
        lt.Bind(LyricText.StrokeBrushProperty, new Avalonia.Data.Binding("InactiveStrokeBrush"));
        lt.Bind(LyricText.StrokeThicknessProperty, new Avalonia.Data.Binding("InactiveStrokeThickness"));
        lt.Bind(Visual.EffectProperty, new Avalonia.Data.Binding("TextEffect"));
        lt.HighlightClip = LyricText.ClipSideMode.Right;
    }

    private static void BindLyricBase(LyricText lt)
    {
        lt.Bind(LyricText.TextProperty, new Avalonia.Data.Binding("MainText"));
        lt.Bind(LyricText.FontFamilyProperty, new Avalonia.Data.Binding("FontFamilyValue"));
        lt.Bind(LyricText.FontWeightProperty, new Avalonia.Data.Binding("WeightValue"));
        lt.Bind(LyricText.FontSizeProperty, new Avalonia.Data.Binding("MainFontSize"));
        lt.Bind(LyricText.TextAlignmentProperty, new Avalonia.Data.Binding("TextAlignment"));
        lt.Bind(LyricText.OpacityProperty, new Avalonia.Data.Binding("TextOpacity"));
        lt.Bind(LyricText.HighlightFractionProperty, new Avalonia.Data.Binding("WordHighlightFraction"));
    }

    private TextBlock CreateBoundTb(string textProp)
    {
        var tb = new TextBlock();
        BindTb(tb, textProp);
        return tb;
    }

    private void BindTb(TextBlock tb, string textProp)
    {
        tb.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(textProp));
        tb.Bind(TextBlock.FontFamilyProperty, new Avalonia.Data.Binding("FontFamilyValue"));
        tb.Bind(TextBlock.FontWeightProperty, new Avalonia.Data.Binding("WeightValue"));
        tb.Bind(TextBlock.EffectProperty, new Avalonia.Data.Binding("TextEffect"));
    }

    private void SyncAllBindings()
    {
        SyncTb(_transTb, "EffectiveTransFontSize", "Fill", "TextOpacity");
        _transTb.Bind(TextBlock.IsVisibleProperty, new Avalonia.Data.Binding("ShowTransLine"));

        foreach (var s in _strokeLayers)
        {
            SyncTb(s, "EffectiveTransFontSize", null, "TextOpacity");
            s.Bind(TextBlock.ForegroundProperty, new Avalonia.Data.Binding("StrokeBrush"));
        }
    }

    private void SyncTb(TextBlock tb, string sizeProp, string? fillProp, string opacityProp)
    {
        tb.Bind(TextBlock.FontSizeProperty, new Avalonia.Data.Binding(sizeProp));
        if (fillProp != null)
            tb.Bind(TextBlock.ForegroundProperty, new Avalonia.Data.Binding(fillProp));
        tb.Bind(TextBlock.OpacityProperty, new Avalonia.Data.Binding(opacityProp));
    }

    /// <summary>描边层（仅翻译行；主行描边由自绘控件内部处理）。</summary>
    private void ApplyStroke()
    {
        foreach (var s in _strokeLayers)
        {
            _transGrid.Children.Remove(s);
        }
        _strokeLayers.Clear();

        if (!_vm.StrokeEnabled || _vm.StrokeThickness <= 0) return;

        double t = _vm.StrokeThickness;
        foreach (var (dx, dy) in StrokeOffsets)
        {
            var st = new TextBlock
            {
                RenderTransform = new TranslateTransform(dx * t, dy * t),
            };
            BindTb(st, "TransText");
            st.Bind(TextBlock.ForegroundProperty, new Avalonia.Data.Binding("StrokeBrush"));
            st.Bind(TextBlock.FontSizeProperty, new Avalonia.Data.Binding("EffectiveTransFontSize"));
            st.Bind(TextBlock.OpacityProperty, new Avalonia.Data.Binding("TextOpacity"));
            st.Bind(TextBlock.IsVisibleProperty, new Avalonia.Data.Binding("ShowTransLine"));
            _transGrid.Children.Insert(0, st);
            _strokeLayers.Add(st);
        }
        ApplyAlignment();
    }

    /// <summary>辉光层：大模糊半径的彩色光晕副本，位于描边层之下。
    /// 主行辉光按逐字分段（未唱段透明，光晕只照亮已唱段）；未唱辉光为独立可选项。</summary>
    private void ApplyGlow()
    {
        foreach (var s in _glowLayers)
        {
            _mainGrid.Children.Remove(s);
            _transGrid.Children.Remove(s);
        }
        _glowLayers.Clear();

        // 未唱辉光层（整行未唱色 + 未唱辉光，裁分界线右侧；默认关）
        if (_vm.InactiveGlowEnabled)
        {
            var ig = CreateMainGlowLyric(null);
            ig.Bind(LyricText.HighlightLengthProperty, new Avalonia.Data.Binding("WordHighlightLength"));
            ig.Bind(LyricText.FillProperty, new Avalonia.Data.Binding("InactiveFill"));
            ig.Bind(Visual.EffectProperty, new Avalonia.Data.Binding("InactiveGlowEffect"));
            ig.HighlightClip = LyricText.ClipSideMode.Right;
            _mainGrid.Children.Insert(0, ig);
            _glowLayers.Add(ig);
        }

        if (_vm.GlowEnabled)
        {
            // 主行已唱辉光：裁分界线左侧 → DropShadowEffect 只照亮已唱段（含正在唱字已唱半）
            var gm = CreateMainGlowLyric("Fill");
            gm.Bind(LyricText.HighlightLengthProperty, new Avalonia.Data.Binding("WordHighlightLength"));
            gm.Bind(Visual.EffectProperty, new Avalonia.Data.Binding("GlowEffect"));
            gm.HighlightClip = LyricText.ClipSideMode.Left;
            _mainGrid.Children.Insert(0, gm);
            _glowLayers.Add(gm);

            var gt = CreateGlowLayer("TransText", "EffectiveTransFontSize");
            gt.Bind(TextBlock.IsVisibleProperty, new Avalonia.Data.Binding("ShowTransLine"));
            _transGrid.Children.Insert(0, gt);
            _glowLayers.Add(gt);
        }

        ApplyAlignment();
    }

    /// <summary>主行辉光副本（自绘分段，可绑定未唱段透明）；fillProp=null 时不绑定 Fill（由调用方自行分段）。</summary>
    private static LyricText CreateMainGlowLyric(string? fillProp)
    {
        var lt = new LyricText();
        lt.Bind(LyricText.TextProperty, new Avalonia.Data.Binding("MainText"));
        lt.Bind(LyricText.FontFamilyProperty, new Avalonia.Data.Binding("FontFamilyValue"));
        lt.Bind(LyricText.FontWeightProperty, new Avalonia.Data.Binding("WeightValue"));
        lt.Bind(LyricText.FontSizeProperty, new Avalonia.Data.Binding("MainFontSize"));
        lt.Bind(LyricText.TextAlignmentProperty, new Avalonia.Data.Binding("TextAlignment"));
        lt.Bind(LyricText.OpacityProperty, new Avalonia.Data.Binding("TextOpacity"));
        if (fillProp != null)
            lt.Bind(LyricText.FillProperty, new Avalonia.Data.Binding(fillProp));
        return lt;
    }

    private static TextBlock CreateGlowLayer(string textProp, string sizeProp)
    {
        var tb = new TextBlock();
        tb.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(textProp));
        tb.Bind(TextBlock.FontFamilyProperty, new Avalonia.Data.Binding("FontFamilyValue"));
        tb.Bind(TextBlock.FontWeightProperty, new Avalonia.Data.Binding("WeightValue"));
        tb.Bind(TextBlock.FontSizeProperty, new Avalonia.Data.Binding(sizeProp));
        tb.Bind(TextBlock.ForegroundProperty, new Avalonia.Data.Binding("Fill"));
        tb.Bind(TextBlock.OpacityProperty, new Avalonia.Data.Binding("TextOpacity"));
        tb.Bind(TextBlock.EffectProperty, new Avalonia.Data.Binding("GlowEffect"));
        return tb;
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.WindowVisible))
            UpdateVisibility();
        else if (e.PropertyName == nameof(OverlayViewModel.IsPlaying))
            UpdatePlayPauseIcon();
        else if (e.PropertyName == nameof(OverlayViewModel.Phase))
            OnLyricsPhaseChanged();
        else if (e.PropertyName == nameof(OverlayViewModel.CoverTrackKey))
            OnCoverChanged();
        else if (e.PropertyName == nameof(OverlayViewModel.CoverImage))
        {
            // 封面内容刷新（同歌重取等），不重放切歌动画
            if (_vm.CoverImage != null)
                CoverImage.Source = _vm.CoverImage;
        }
        else if (e.PropertyName == nameof(OverlayViewModel.MaxTextWidth))
        {
            // 歌词区域宽度上限变化：常驻时同步（动画期间由动画状态管理）
            if (!_coverAnimating)
                LyricsArea.MinWidth = _vm.MaxTextWidth;
        }
        else if (e.PropertyName == nameof(OverlayViewModel.CoverEnabled))
        {
            // 播放状态广播频繁且值可能未变：仅值真正变化时才处理
            if (_vm.CoverEnabled == _lastCoverEnabled)
                return;
            _lastCoverEnabled = _vm.CoverEnabled;
            ApplyCoverLayout();
            UpdateCoverSize();
        }
        else if (e.PropertyName == nameof(OverlayViewModel.CoverSizePct))
        {
            if (Math.Abs(_vm.CoverSizePct - _lastCoverSizePct) < 0.5)
                return;
            _lastCoverSizePct = _vm.CoverSizePct;
            UpdateCoverSize();
        }
        else if (e.PropertyName is nameof(OverlayViewModel.StrokeEnabled)
                 or nameof(OverlayViewModel.StrokeThickness))
            ApplyStroke();
        else if (e.PropertyName is nameof(OverlayViewModel.GlowEffect)
                 or nameof(OverlayViewModel.InactiveGlowEffect))
            ApplyGlow();
        else if (e.PropertyName == nameof(OverlayViewModel.TextAlignment))
            ApplyAlignment();
        else if (e.PropertyName is nameof(OverlayViewModel.CoverTitleEnabled))
        {
            // 动画中切换开关：即时进入/退出歌名
            if (_coverAnimating)
            {
                if (_vm.CoverTitleEnabled)
                    EnterCoverTitle();
                else
                    ExitCoverTitle();
            }
        }
    }

    // ---------- 封面 ----------

    private double _coverSize;

    /// <summary>构建 translate 变换（Parse 不接受小数，用 Builder）。</summary>
    private static TransformOperations Translate(double x, double y)
    {
        var b = new TransformOperations.Builder(1);
        b.AppendTranslate(x, y);
        return b.Build();
    }

    /// <summary>歌词加载完成的等待超时（防封面卡死）。</summary>
    private static readonly TimeSpan CoverAnimTimeout = TimeSpan.FromSeconds(6);

    /// <summary>
    /// 切歌（任意来源：播放器内切歌 / 悬浮窗上一首下一首）动画状态机（方案二：窗口内叠加）：
    /// 窗口尺寸全程不变（不隐藏歌词、不撑高窗口、不锁定 MinWidth）；
    /// 封面作为覆盖层放大到行高尺寸显示在歌词行中央（歌词透明被掩盖），
    /// 歌词 Ready/NoLyric 后（或超时）执行分支：
    /// 常驻=封面缩小移动到常驻位 / 不常驻=淡出。尺寸与位置均由 Transitions 平滑过渡。
    /// </summary>
    private void OnCoverChanged()
    {
        ApplyCoverLayout();
        UpdateCoverSize();
        var animate = _vm.CoverCutAnimation && _vm.ConsumeCutAnimationFlag();
        Log.Info($"cover: track={_vm.CoverTrackKey} image={( _vm.CoverImage != null)} animate={animate} enabled={_vm.CoverEnabled}");

        if (_vm.CoverImage == null)
        {
            // 无封面：直接显示歌词
            _coverAnimating = false;
            Cover.Opacity = 0;
            ExitCoverTitle();
            SetLyricsOpacity(1);
            return;
        }

        CoverImage.Source = _vm.CoverImage;

        if (animate)
        {
            _coverAnimating = true;
            _coverStageTimer.Stop();
            _coverTimeoutTimer.Stop();
            SetLyricsOpacity(0);
            _coverAnimSize = AnimCoverSize();
            _coverAnimStart = DateTimeOffset.UtcNow;
            _coverTimeoutTimer.Interval = TimeSpan.FromMilliseconds(_vm.CoverAnimMaxMs);
            _coverTimeoutTimer.Start();

            // 窗口内叠加：封面恢复纯 300% 行高正方形、居中于歌词行，淡入掩盖歌词；
            // 歌名/歌手为独立覆盖层（封面下方），淡入淡出、不跟随封面动画路径
            CoverImage.Width = _coverAnimSize;
            CoverImage.Height = _coverAnimSize;
            var cx = Math.Max(0, (OuterGrid.Bounds.Width - _coverAnimSize) / 2);
            Cover.RenderTransform = Translate(cx, 0);
            Cover.Opacity = 1;
            EnterCoverTitle();
            _coverPosDirty = true;
            Log.Info($"cover: animation started, winW={ClientSize.Width:F0} size={_coverAnimSize:F0} titleEnabled={_vm.CoverTitleEnabled}");
            return;
        }

        // 非手动切歌：直接定位
        _coverAnimating = false;
        SetLyricsOpacity(1);
        ExitCoverTitle();
        if (_vm.CoverEnabled)
        {
            var target = CoverSlotOffset();
            Cover.RenderTransform = Translate(target.X, target.Y);
            CoverImage.Width = _coverSize;
            CoverImage.Height = _coverSize;
            Cover.Opacity = 1;
        }
        else
        {
            Cover.Opacity = 0;
        }
    }

    /// <summary>歌词加载完成（Ready/NoLyric）→ 动画进入结束阶段；未达到最短时长则等待补齐。</summary>
    private void OnLyricsPhaseChanged()
    {
        if (!_coverAnimating)
            return;
        if (_vm.Phase is LyricsPhase.Ready or LyricsPhase.NoLyric)
        {
            var elapsed = (DateTimeOffset.UtcNow - _coverAnimStart).TotalMilliseconds;
            var wait = Math.Max(400, _vm.CoverAnimMinMs - elapsed);
            _coverStageTimer.Interval = TimeSpan.FromMilliseconds(wait);
            _coverStageTimer.Start();
        }
    }

    private void OnCoverStage()
    {
        _coverStageTimer.Stop();
        _coverTimeoutTimer.Stop();
        if (!_coverAnimating)
            return;

        _coverAnimating = false;
        _coverPosDirty = false;
        SetLyricsOpacity(1);
        ExitCoverTitle(); // 歌名淡出（250ms）后退出布局
        if (_vm.CoverEnabled)
        {
            // 常驻：封面缩小回常驻尺寸并移动到常驻位（尺寸/位置过渡动画）
            CoverImage.Width = _coverSize;
            CoverImage.Height = _coverSize;
            var target = CoverSlotOffset();
            Cover.RenderTransform = Translate(target.X, target.Y);
            Cover.Opacity = 1;
        }
        else
        {
            // 不常驻：封面淡出并恢复常驻尺寸
            Cover.Opacity = 0;
            CoverImage.Width = _coverSize;
            CoverImage.Height = _coverSize;
            Cover.RenderTransform = Translate(0, 0);
        }
    }

    /// <summary>歌名进入布局（仅动画期间调用）：显示在封面下方并淡入；开关关闭时不进入。</summary>
    private void EnterCoverTitle()
    {
        if (!_vm.CoverTitleEnabled)
            return;
        CoverTitleBox.IsVisible = true;
        UpdateCoverTitlePosition();
        CoverTitleBox.Opacity = 1;
    }

    /// <summary>歌名淡出（250ms）后退出布局；快速切歌时跳过，避免误关新歌名。</summary>
    private void ExitCoverTitle()
    {
        CoverTitleBox.Opacity = 0;
        _ = Task.Run(async () =>
        {
            await Task.Delay(300); // 等待淡出过渡完成
            Dispatcher.UIThread.Post(() =>
            {
                if (_coverAnimating)
                    return; // 新动画已开始：跳过退出
                CoverTitleBox.IsVisible = false;
            });
        });
    }

    /// <summary>歌名定位：水平居中于窗口（与封面同轴），垂直位于封面下边框下方（不跟随封面动画路径）。</summary>
    private void UpdateCoverTitlePosition()
    {
        if (!CoverTitleBox.IsVisible)
            return;
        const double gap = 8;
        var x = Math.Max(0, (OuterGrid.Bounds.Width - CoverTitleBox.Bounds.Width) / 2);
        var y = Cover.Bounds.Height + gap;
        CoverTitleBox.RenderTransform = Translate(x, y);
    }

    private void SetLyricsOpacity(double opacity) => LyricsViewbox.Opacity = opacity;

    /// <summary>动画封面尺寸 = 行高 × 300%（掩盖歌词加载的大封面；窗口高度由 ContentHeight 自动容纳）。</summary>
    private double AnimCoverSize()
    {
        var h = _mainGrid.Bounds.Height;
        if (h <= 0)
            h = 40;
        return Math.Max(h * 3, _coverSize);
    }

    /// <summary>常驻位置（CoverSlot 左上角）在外层 Grid 内的坐标（与封面元素同一坐标系）。</summary>
    private Point CoverSlotOffset()
    {
        var p = CoverSlot.TranslatePoint(new Point(0, 0), OuterGrid) ?? default;
        return new Point(Math.Max(0, p.X), Math.Max(0, p.Y));
    }

    /// <summary>封面位置跟随布局（歌词宽度变化等）；动画期间不移动。</summary>
    private void UpdateCoverPosition()
    {
        if (_coverAnimating)
            return;
        if (_vm.CoverEnabled && Cover.Opacity > 0)
        {
            var target = CoverSlotOffset();
            Cover.RenderTransform = Translate(target.X, target.Y);
        }
    }

    /// <summary>
    /// 封面尺寸 = 主歌词行实际高度 × 占比（正方形）。
    /// 基准行高只受字号等设置控制（行文本长度变化不影响行高），设置变化时由 SizeChanged 驱动。
    /// </summary>
    private void UpdateCoverSize()
    {
        if (_coverAnimating)
            return; // 动画期间冻结尺寸，避免布局波动打断动画/引起抖动
        var baseH = _mainGrid.Bounds.Height;
        if (baseH <= 0)
            baseH = 40;
        _coverSize = Math.Max(0, baseH * _vm.CoverSizePct / 100.0);
        var w = _vm.CoverEnabled ? _coverSize : 0;
        CoverSlot.Width = w;
        CoverSlot.Height = w;
        RightPad.Width = w;
        RightPad.Height = w;
        CoverImage.Width = _coverSize;
        CoverImage.Height = _coverSize;
    }

    /// <summary>封面位置固定左侧（占位列 0，歌词列 1，对称占位列 2 → 歌词保持居中）。</summary>
    private void ApplyCoverLayout()
    {
        Grid.SetColumn(CoverSlot, 0);
        Grid.SetColumn(LyricsArea, 1);
        Grid.SetColumn(RightPad, 2);
        UpdateCoverPosition();
    }

    private void ApplyAlignment()
    {
        var alig = _vm.TextAlignment;
        _mainLyric.TextAlignment = alig;
        _transTb.TextAlignment = alig;
        foreach (var s in _strokeLayers) s.TextAlignment = alig;
        foreach (var s in _glowLayers)
        {
            if (s is TextBlock tb) tb.TextAlignment = alig;
            else if (s is LyricText lt) lt.TextAlignment = alig;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        Log.Info($"overlay opened, hwnd={_hwnd:X8}");
        if (_hwnd != IntPtr.Zero)
            Win32.AssertTopmost(_hwnd);
        SetLocked(_settingsService.Current.Locked);
        UpdatePlayPauseIcon();
        ApplyAnchor();
        UpdateVisibility();
        LogSizeDiagnostics();
        ScheduleSizeDiagnostics();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // 桌面歌词窗口不允许用户关闭：关闭 = 隐藏并同步设置（托盘勾选），
        // 避免窗口销毁后托盘再次 Show() 崩溃；程序退出时由 Cleanup 先置 AllowClose。
        if (!_allowClose)
        {
            e.Cancel = true;
            if (_settingsService.Current.LyricsVisible)
                _settingsService.Update(s => s.LyricsVisible = false);
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _topmostTimer.Stop();
        _hideControlsTimer.Stop();
        _coverStageTimer.Stop();
        _coverTimeoutTimer.Stop();
        base.OnClosed(e);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!_suppressPositionUpdate && ClientSize.Width > 0 && ClientSize.Height > 0)
        {
            var grow = _settingsService.Current.HeightGrowMode;
            if (_firstSizeApplied && e.HeightChanged && !e.WidthChanged && grow != 0)
            {
                // 纯高度变化（胶囊展开/封面动画/字体变化）：
                // 1=向下扩展（顶部固定，Position 不变）；2=向上扩展（底部固定，顶部上移）
                if (grow == 2)
                {
                    var s = Screens.ScreenFromWindow(this)?.Scaling ?? 1.0;
                    _suppressPositionUpdate = true;
                    Position = new PixelPoint(Position.X, (int)(_lastGrowBottom - ClientSize.Height * s));
                    _suppressPositionUpdate = false;
                }
            }
            else
            {
                RepositionToAnchor();
                if (e.WidthChanged || !_firstSizeApplied)
                    _firstSizeApplied = true;
            }
        }
        // 记录当前窗口底边（物理像素），供"向上扩展"模式使用
        var scale = Screens.ScreenFromWindow(this)?.Scaling ?? 1.0;
        _lastGrowBottom = Position.Y + (int)Math.Round(ClientSize.Height * scale);
        LogSizeDiagnostics();
    }

    private DateTimeOffset _lastSizeDiag = DateTimeOffset.MinValue;

    /// <summary>
    /// 尺寸诊断（节流 1s）：对比 DIP 期望物理尺寸与窗口实际物理尺寸，
    /// 验证 2K 高分屏下 SizeToContent 物理换算是否正确（客户区 = ClientSize × 缩放？）。
    /// RenderScaling = 窗口实际渲染缩放，若与屏幕缩放不一致即为 DPI 换算错误的直接证据。
    /// </summary>
    private void LogSizeDiagnostics()
    {
        if (DateTimeOffset.UtcNow - _lastSizeDiag < TimeSpan.FromSeconds(1))
            return;
        _lastSizeDiag = DateTimeOffset.UtcNow;
        if (ClientSize.Width <= 0)
            return;
        var screenScale = Screens.ScreenFromWindow(this)?.Scaling ?? 1.0;
        var renderScale = RenderScaling;
        var expectByScreen = (ClientSize.Width * screenScale, ClientSize.Height * screenScale);
        var expectByRender = (ClientSize.Width * renderScale, ClientSize.Height * renderScale);
        var win = Win32.GetWindowSize(_hwnd);
        var client = Win32.GetClientSize(_hwnd);
        Log.Info($"diag: client={ClientSize.Width:F1}x{ClientSize.Height:F1} " +
                 $"screenScale={screenScale:F3} renderScale={renderScale:F3} " +
                 $"expectByScreen={expectByScreen.Item1:F0}x{expectByScreen.Item2:F0} " +
                 $"expectByRender={expectByRender.Item1:F0}x{expectByRender.Item2:F0} " +
                 $"winPhys={win.W}x{win.H} err={win.Err} clientPhys={client.W}x{client.H} err={client.Err} " +
                 $"minW={MinWidth:F0} maxW={(double.IsPositiveInfinity(MaxWidth) ? -1 : MaxWidth):F0}");
    }

    /// <summary>窗口首次布局完成后再采集一次尺寸诊断（OnOpened 时 SizeToContent 可能未完成）。</summary>
    private void ScheduleSizeDiagnostics()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(800);
            Dispatcher.UIThread.Post(LogSizeDiagnostics);
        });
    }

    /// <summary>按下即拖动（控制条区域除外，避免与按钮点击冲突）；锁定状态下禁止拖动。</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_locked)
            return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (IsInsideControlBar(e.Source as Avalonia.Visual))
            return;
        _dragStarted = true;
        BeginMoveDrag(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragStarted)
            WriteAnchorFromPosition();
        _dragStarted = false;
    }

    private bool IsInsideControlBar(Avalonia.Visual? source)
    {
        while (source != null)
        {
            if (ReferenceEquals(source, ControlBar))
                return true;
            source = source.Parent as Avalonia.Visual;
        }
        return false;
    }

    public void UpdateVisibility()
    {
        if (_vm.WindowVisible) Show(); else Hide();
    }

    private void ApplyAnchor()
    {
        var s = _settingsService.Current;
        if (s.AnchorX.HasValue && s.AnchorY.HasValue)
            _anchor = new PixelPoint((int)s.AnchorX.Value, (int)s.AnchorY.Value);
        else
        {
            var area = Screens.ScreenFromWindow(this)?.WorkingArea
                       ?? Screens.Primary?.WorkingArea
                       ?? new PixelRect(100, 100, 1720, 880);
            _anchor = new PixelPoint(area.X + area.Width / 2, (int)(area.Y + area.Height * 0.85));
        }
        ClampAnchorToScreens();
        RepositionToAnchor();
    }

    private void RepositionToAnchor()
    {
        if (!IsInitialized) return;
        var s = Screens.ScreenFromWindow(this)?.Scaling ?? 1.0;
        var pw = ClientSize.Width * s;
        var ph = ClientSize.Height * s;
        _suppressPositionUpdate = true;
        Position = new PixelPoint((int)(_anchor.X - pw / 2), (int)(_anchor.Y - ph / 2));
        _suppressPositionUpdate = false;
    }

    private void WriteAnchorFromPosition()
    {
        var s = Screens.ScreenFromWindow(this)?.Scaling ?? 1.0;
        _anchor = new PixelPoint(
            (int)(Position.X + ClientSize.Width * s / 2),
            (int)(Position.Y + ClientSize.Height * s / 2));
        var ax = _anchor.X; var ay = _anchor.Y;
        _anchorDebouncer.Schedule(TimeSpan.FromMilliseconds(500), () => AnchorChanged?.Invoke(ax, ay));
    }

    private void ClampAnchorToScreens()
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var scr in Screens.All) { var b = scr.Bounds; if (b.X < minX) minX = b.X; if (b.Y < minY) minY = b.Y; if (b.Right > maxX) maxX = b.Right; if (b.Bottom > maxY) maxY = b.Bottom; }
        if (minX >= maxX) return;
        _anchor = new PixelPoint(Math.Clamp(_anchor.X, minX + 80, maxX - 80), Math.Clamp(_anchor.Y, minY + 80, maxY - 80));
    }

    public void SetAnchor(double x, double y)
    {
        _anchor = new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
        RepositionToAnchor();
        _anchorDebouncer.Cancel();
    }

    public void SnapToPreset(PositionPreset preset)
    {
        var area = Screens.ScreenFromWindow(this)?.WorkingArea
                   ?? Screens.Primary?.WorkingArea
                   ?? new PixelRect(100, 100, 1720, 880);
        int cx = area.X + area.Width / 2;
        int cy = area.Y + area.Height / 2;
        var m = 80;
        int x = preset switch
        {
            PositionPreset.TopLeft or PositionPreset.MiddleLeft or PositionPreset.BottomLeft => area.X + m,
            PositionPreset.TopCenter or PositionPreset.Center or PositionPreset.BottomCenter => cx,
            _ => area.X + area.Width - m,
        };
        int y = preset switch
        {
            PositionPreset.TopLeft or PositionPreset.TopCenter or PositionPreset.TopRight => area.Y + m,
            PositionPreset.MiddleLeft or PositionPreset.Center or PositionPreset.MiddleRight => cy,
            _ => area.Y + area.Height - m,
        };
        _anchor = new PixelPoint(x, y);
        RepositionToAnchor();
        _anchorDebouncer.Cancel();
        AnchorChanged?.Invoke(x, y);
    }
}
