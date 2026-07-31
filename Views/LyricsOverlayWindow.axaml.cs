using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
    private readonly UiDebouncer _anchorDebouncer = new();

    private IntPtr _hwnd;
    private PixelPoint _anchor;
    private bool _suppressPositionUpdate;
    private bool _dragStarted;
    private bool _coverAnimating;

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

        _coverStageTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _coverStageTimer.Tick += (_, _) => OnCoverStage();

        PointerEntered += (_, _) => { _hideControlsTimer.Stop(); SetControlsVisible(true); };
        PointerExited += (_, _) => _hideControlsTimer.Start();

        RootPanel.SizeChanged += (_, _) => UpdateCoverSize();
        _vm.PropertyChanged += OnVmChanged;
        SizeChanged += OnSizeChanged;
    }

    /// <summary>hover 显示播放控制条（歌词下方展开）；移出 200ms 后收起。</summary>
    private void SetControlsVisible(bool visible)
    {
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
        else if (e.PropertyName == nameof(OverlayViewModel.CoverImage))
            OnCoverChanged();
        else if (e.PropertyName is nameof(OverlayViewModel.CoverEnabled)
                 or nameof(OverlayViewModel.CoverPosition))
            ApplyCoverLayout();
        else if (e.PropertyName == nameof(OverlayViewModel.CoverSizePct))
            UpdateCoverSize();
        else if (e.PropertyName is nameof(OverlayViewModel.StrokeEnabled)
                 or nameof(OverlayViewModel.StrokeThickness))
            ApplyStroke();
        else if (e.PropertyName == nameof(OverlayViewModel.GlowEffect))
            ApplyGlow();
        else if (e.PropertyName == nameof(OverlayViewModel.TextAlignment))
            ApplyAlignment();
    }

    // ---------- 封面 ----------

    /// <summary>
    /// 切歌动画状态机：
    /// T0 歌词淡出 + 封面居中淡入 → 0.8s 后分支（常驻=移动到常驻位 / 不常驻=封面淡出）→ 歌词淡入。
    /// </summary>
    private void OnCoverChanged()
    {
        ApplyCoverLayout();
        UpdateCoverSize();

        if (_vm.CoverImage == null)
        {
            // 无封面：直接显示歌词
            SetLyricsOpacity(1);
            return;
        }

        _coverAnimating = true;
        _coverStageTimer.Stop();
        SetLyricsOpacity(0);
        CoverImage.Source = _vm.CoverImage;

        // 封面居中淡入
        Cover.RenderTransform = new TranslateTransform(CenterOffsetX(), CenterOffsetY());
        Cover.Opacity = 1;
        _coverStageTimer.Start();
    }

    private void OnCoverStage()
    {
        _coverStageTimer.Stop();
        if (!_coverAnimating)
            return;

        if (_vm.CoverEnabled)
        {
            // 分支 A：封面平滑移动到常驻位置
            var target = CoverSlotOffset();
            Cover.RenderTransform = new TranslateTransform(target.X, target.Y);
            SetLyricsOpacity(1);
            _coverAnimating = false;
        }
        else
        {
            // 分支 B：封面淡出，恢复歌词
            Cover.Opacity = 0;
            SetLyricsOpacity(1);
            _coverAnimating = false;
        }
    }

    private void SetLyricsOpacity(double opacity) => LyricsViewbox.Opacity = opacity;

    private double CenterOffsetX() => Math.Max(0, (OuterGrid.Bounds.Width - CoverSlot.Width) / 2);
    private double CenterOffsetY() => Math.Max(0, (OuterGrid.Bounds.Height - CoverSlot.Height) / 2);

    /// <summary>常驻位置（CoverSlot 左上角）在外层 Grid 内的坐标（与封面元素同一坐标系）。</summary>
    private Point CoverSlotOffset()
    {
        var p = CoverSlot.TranslatePoint(new Point(0, 0), OuterGrid) ?? default;
        return new Point(Math.Max(0, p.X), Math.Max(0, p.Y));
    }

    /// <summary>封面尺寸 = 歌词实际宽度 × 占比；更新占位列与封面元素尺寸。</summary>
    private void UpdateCoverSize()
    {
        var w = Math.Max(0, RootPanel.Bounds.Width * _vm.CoverSizePct / 100.0);
        CoverSlot.Width = _vm.CoverEnabled ? w : 0;
        CoverSlot.Height = _vm.CoverEnabled ? w : 0;
        CoverImage.Width = w;
        CoverImage.Height = w;

        if (!_coverAnimating && Cover.Opacity > 0)
        {
            var target = CoverSlotOffset();
            Cover.RenderTransform = new TranslateTransform(target.X, target.Y);
        }
    }

    /// <summary>应用封面位置布局：占位列 ↔ 歌词列。</summary>
    private void ApplyCoverLayout()
    {
        var left = _vm.CoverPosition != "right";
        Grid.SetColumn(CoverSlot, left ? 0 : 1);
        Grid.SetColumn(LyricsArea, left ? 1 : 0);

        if (!_coverAnimating && _vm.CoverEnabled && Cover.Opacity > 0)
        {
            var target = CoverSlotOffset();
            Cover.RenderTransform = new TranslateTransform(target.X, target.Y);
        }
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
