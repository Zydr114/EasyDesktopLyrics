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
    private PixelPoint _anchor;
    private bool _suppressPositionUpdate;
    private bool _dragStarted;
    private bool _coverAnimating;
    private double _coverAnimSize;
    private double _animWindowWidth;
    private DateTimeOffset _coverAnimStart;
    private bool _coverPendingLayout;
    private int _layoutPasses;
    private bool _pendingAnimStart;
    private bool _pendingPersistent;
    private readonly DispatcherTimer _layoutWaitTimer;
    private bool _lastCoverEnabled;
    private double _lastCoverSizePct;

    // 动态文本层
    private Grid _mainGrid = null!;
    private TextBlock _mainTb = null!;
    private Grid _transGrid = null!;
    private TextBlock _transTb = null!;
    private readonly List<TextBlock> _strokeLayers = [];
    private readonly List<TextBlock> _glowLayers = [];

    public event Action<double, double>? AnchorChanged;

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

        // 布局等待兜底：两轮 LayoutUpdated 未完成时强制继续（封面位置用已知宽度，仍正确）
        _layoutWaitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _layoutWaitTimer.Tick += (_, _) => CompleteCoverLayout();
        LayoutUpdated += (_, _) =>
        {
            if (!_coverPendingLayout)
                return;
            if (_layoutPasses++ == 0)
                return; // 第一轮布局后窗口可能再调整，两轮确认
            CompleteCoverLayout();
        };

        PointerEntered += (_, _) => { _hideControlsTimer.Stop(); SetControlsVisible(true); };
        PointerExited += (_, _) => _hideControlsTimer.Start();

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

    private void UpdatePlayPauseIcon()
    {
        PlayPauseIcon.Text = _vm.IsPlaying ? "\uE769" : "\uE768"; // Pause / Play
    }

    private void BuildTextLayers()
    {
        RootPanel.Children.Clear();
        _strokeLayers.Clear();

        _mainGrid = new Grid();
        _mainTb = CreateBoundTb("MainText");
        _mainGrid.Children.Add(_mainTb);
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
        SyncTb(_mainTb, "MainFontSize", "Fill", "TextOpacity");
        SyncTb(_transTb, "EffectiveTransFontSize", "Fill", "TextOpacity");
        _transTb.Bind(TextBlock.IsVisibleProperty, new Avalonia.Data.Binding("ShowTransLine"));

        foreach (var s in _strokeLayers)
        {
            SyncTb(s, s == _mainTb ? "MainFontSize" : "EffectiveTransFontSize", null, "TextOpacity");
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

    private void ApplyStroke()
    {
        foreach (var s in _strokeLayers)
        {
            _mainGrid.Children.Remove(s);
            _transGrid.Children.Remove(s);
        }
        _strokeLayers.Clear();

        if (!_vm.StrokeEnabled || _vm.StrokeThickness <= 0) return;

        double t = _vm.StrokeThickness;
        foreach (var (dx, dy) in StrokeOffsets)
        {
            var sm = new TextBlock
            {
                RenderTransform = new TranslateTransform(dx * t, dy * t),
            };
            BindTb(sm, "MainText");
            sm.Bind(TextBlock.ForegroundProperty, new Avalonia.Data.Binding("StrokeBrush"));
            sm.Bind(TextBlock.FontSizeProperty, new Avalonia.Data.Binding("MainFontSize"));
            sm.Bind(TextBlock.OpacityProperty, new Avalonia.Data.Binding("TextOpacity"));
            _mainGrid.Children.Insert(0, sm);
            _strokeLayers.Add(sm);

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
    }

    /// <summary>辉光层：大模糊半径的彩色光晕副本，位于描边层之下。</summary>
    private void ApplyGlow()
    {
        foreach (var s in _glowLayers)
        {
            _mainGrid.Children.Remove(s);
            _transGrid.Children.Remove(s);
        }
        _glowLayers.Clear();

        if (!_vm.GlowEnabled)
            return;

        var gm = CreateGlowLayer("MainText", "MainFontSize");
        _mainGrid.Children.Insert(0, gm);
        _glowLayers.Add(gm);

        var gt = CreateGlowLayer("TransText", "EffectiveTransFontSize");
        gt.Bind(TextBlock.IsVisibleProperty, new Avalonia.Data.Binding("ShowTransLine"));
        _transGrid.Children.Insert(0, gt);
        _glowLayers.Add(gt);
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
        else if (e.PropertyName == nameof(OverlayViewModel.GlowEffect))
            ApplyGlow();
        else if (e.PropertyName == nameof(OverlayViewModel.TextAlignment))
            ApplyAlignment();
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

    /// <summary>动画封面尺寸 = 歌词行高度 × 该倍数（掩盖歌词加载，居中于歌词行位置）。</summary>
    private const double CoverAnimScale = 3.0;

    /// <summary>歌词加载完成的等待超时（防封面卡死）。</summary>
    private static readonly TimeSpan CoverAnimTimeout = TimeSpan.FromSeconds(6);

    /// <summary>
    /// 切歌（仅"上一首/下一首"触发）动画状态机：
    /// 封面先于歌词到达 → 封面以行高 300% 显示在歌词行中央（窗口收缩为封面大小，中心=歌词行锚点），
    /// 掩盖歌词加载 → 歌词 Ready/NoLyric 后（或超时）执行分支：
    /// 常驻=恢复布局并缩小移动到常驻位 / 不常驻=淡出。动画使用二次缓出。
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

            // 窗口宽度锁定不变（封面水平居中于歌词行位置），高度撑到封面高；
            // 布局稳定后封面从顶边滑入（避免窗口跳变暴露）
            _animWindowWidth = ClientSize.Width;
            MinWidth = _animWindowWidth;
            LyricsArea.IsVisible = false;
            LyricsArea.MinWidth = 0;
            CoverSlot.Width = 0;
            CoverSlot.Height = _coverAnimSize;
            RightPad.Width = 0;
            RightPad.Height = 0;
            CoverImage.Width = _coverAnimSize;
            CoverImage.Height = _coverAnimSize;
            Cover.Opacity = 0;

            // 布局稳定后（LayoutUpdated 两轮确认）再滑入封面，确保窗口尺寸/位置正确
            _pendingAnimStart = true;
            _pendingPersistent = false;
            _coverPendingLayout = true;
            _layoutPasses = 0;
            _layoutWaitTimer.Start();
            Log.Info($"cover: animation started, size={_coverAnimSize:F0}");
            return;
        }

        // 非手动切歌：直接定位
        _coverAnimating = false;
        SetLyricsOpacity(1);
        if (_vm.CoverEnabled)
        {
            var target = CoverSlotOffset();
            Cover.RenderTransform = Translate(target.X, target.Y);
            Cover.Opacity = 1;
        }
        else
        {
            Cover.Opacity = 0;
        }
    }

    /// <summary>
    /// 布局稳定后封面从顶边滑入。水平居中用锁定的窗口宽度（已知值，不依赖布局测量）。
    /// </summary>
    private async Task StartCoverSlideInAsync()
    {
        if (!_coverAnimating)
            return;

        var cx = Math.Max(0, (_animWindowWidth - _coverAnimSize) / 2);
        Cover.RenderTransform = Translate(cx, -_coverAnimSize); // 顶边外
        Cover.Opacity = 1;
        Cover.RenderTransform = Translate(cx, 0);               // 顶边滑入（缓出）
        _coverTimeoutTimer.Start();
    }

    /// <summary>布局稳定（两轮 LayoutUpdated 或超时兜底）后执行待办：封面滑入 / 常驻淡入。</summary>
    private void CompleteCoverLayout()
    {
        _coverPendingLayout = false;
        _layoutWaitTimer.Stop();
        if (_pendingAnimStart)
        {
            _pendingAnimStart = false;
            _ = StartCoverSlideInAsync();
        }
        else if (_pendingPersistent)
        {
            _pendingPersistent = false;
            _ = ShowPersistentCoverDelayedAsync();
        }
    }

    /// <summary>常驻封面：等封面淡出完成后在常驻位淡入。</summary>
    private async Task ShowPersistentCoverDelayedAsync()
    {
        await Task.Delay(350);
        if (!_vm.CoverEnabled)
            return;
        CoverImage.Width = _coverSize;
        CoverImage.Height = _coverSize;
        var target = CoverSlotOffset();
        Cover.RenderTransform = Translate(target.X, target.Y);
        Cover.Opacity = 1;
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

        if (_vm.CoverEnabled)
        {
            // 分支 A：封面在中心淡出并恢复窗口布局；常驻封面随后在常驻位淡入（交叉淡化）
            LyricsArea.IsVisible = true;
            LyricsArea.MinWidth = _vm.MaxTextWidth;
            CoverSlot.Width = _coverSize;
            CoverSlot.Height = _coverSize;
            RightPad.Width = _coverSize;
            RightPad.Height = _coverSize;
            MinWidth = 0;
            Cover.Opacity = 0;
            SetLyricsOpacity(1);
            _coverAnimating = false;
            _pendingAnimStart = false;
            _pendingPersistent = true;
            _coverPendingLayout = true;
            _layoutPasses = 0;
            _layoutWaitTimer.Start();
        }
        else
        {
            // 分支 B：封面原地淡出（不缩放、不移动），恢复窗口布局
            Cover.Opacity = 0;
            LyricsArea.IsVisible = true;
            LyricsArea.MinWidth = _vm.MaxTextWidth;
            CoverSlot.Width = 0;
            CoverSlot.Height = 0;
            RightPad.Width = 0;
            RightPad.Height = 0;
            MinWidth = 0;
            SetLyricsOpacity(1);
            _coverAnimating = false;
            // 淡出完成后恢复常驻尺寸（不可见状态下设置，无动画干扰）
            CoverImage.Width = _coverSize;
            CoverImage.Height = _coverSize;
        }
    }

    private void SetLyricsOpacity(double opacity) => LyricsViewbox.Opacity = opacity;

    /// <summary>动画封面尺寸：歌词行高度 × 300%（兜底 40px）。</summary>
    private double AnimCoverSize()
    {
        var h = _mainGrid.Bounds.Height;
        if (h <= 0)
            h = 40;
        return h * CoverAnimScale;
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
        _mainTb.TextAlignment = alig;
        _transTb.TextAlignment = alig;
        foreach (var s in _strokeLayers) s.TextAlignment = alig;
        foreach (var s in _glowLayers) s.TextAlignment = alig;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        Log.Info($"overlay opened, hwnd={_hwnd:X8}");
        if (_hwnd != IntPtr.Zero)
            Win32.AssertTopmost(_hwnd);
        UpdatePlayPauseIcon();
        ApplyAnchor();
        UpdateVisibility();
    }

    protected override void OnClosed(EventArgs e)
    {
        _topmostTimer.Stop();
        _hideControlsTimer.Stop();
        _coverStageTimer.Stop();
        _coverTimeoutTimer.Stop();
        _layoutWaitTimer.Stop();
        base.OnClosed(e);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!_suppressPositionUpdate && ClientSize.Width > 0 && ClientSize.Height > 0)
            RepositionToAnchor();
    }

    /// <summary>按下即拖动（控制条区域除外，避免与按钮点击冲突）。</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
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
