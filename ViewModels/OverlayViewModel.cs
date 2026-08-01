using Avalonia.Media;
using Avalonia.Media.Imaging;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services;

namespace EasyDesktopLyrics.ViewModels;

/// <summary>
/// 悬浮窗只读视图模型：组合 设置 + 协调器状态 → 文本/样式/可见性。
/// 所有属性为计算属性，来源变化时整体广播。
/// </summary>
public sealed class OverlayViewModel : ObservableObject
{
    private static readonly string[] AllProps =
    [
        nameof(MainText), nameof(TransText), nameof(ShowTransLine),
        nameof(FontFamilyValue), nameof(MainFontSize), nameof(EffectiveTransFontSize), nameof(WeightValue),
        nameof(Fill), nameof(InactiveFill), nameof(ActiveLayerInactiveFill), nameof(TextOpacity), nameof(MaxTextWidth), nameof(TextEffect), nameof(GlowEffect),
        nameof(WindowVisible), nameof(IsPlaying), nameof(Phase),
        nameof(StrokeEnabled), nameof(StrokeBrush), nameof(StrokeThickness), nameof(LineSpacing),
        nameof(TextAlignment), nameof(GlowEnabled),
        nameof(InactiveStrokeEnabled), nameof(InactiveStrokeBrush), nameof(InactiveStrokeThickness), nameof(InactiveGlowEffect),
        nameof(CoverImage), nameof(CoverEnabled), nameof(CoverCutAnimation), nameof(CoverSizePct),
        nameof(WordHighlightLength), nameof(WordHighlightFraction),
    ];

    private static readonly IBrush TransparentBrush = new SolidColorBrush(Colors.Transparent);

    private readonly SettingsService _settings;
    private readonly LyricsOrchestrator _orchestrator;
    private readonly SmtcService _smtc;

    private string _fillHexCache = "";
    private IBrush _fillCache = Brushes.White;
    private string _inactiveHexCache = "";
    private IBrush _inactiveFillCache = Brushes.White;
    private string _inactiveStrokeHexCache = "";
    private IBrush _inactiveStrokeBrushCache = Brushes.Black;
    private string _strokeHexCache = "";
    private IBrush _strokeBrushCache = Brushes.Black;
    private string _shadowKeyCache = "";
    private IEffect? _shadowCache;
    private string _glowKeyCache = "";
    private IEffect? _glowCache;
    private string _inactiveGlowKeyCache = "";
    private IEffect? _inactiveGlowCache;

    public OverlayViewModel(SettingsService settings, LyricsOrchestrator orchestrator, SmtcService smtc)
    {
        _settings = settings;
        _orchestrator = orchestrator;
        _smtc = smtc;
        _settings.Changed += () => RaiseMany(AllProps);
        _orchestrator.StateChanged += () =>
        {
            RefreshCover();
            RefreshCoverTrackKey();
            RaiseMany(AllProps);
        };

        PlayPauseCommand = new RelayCommand(() => _ = _smtc.TryTogglePlayPauseAsync());
        PrevCommand = new RelayCommand(() => _ = _smtc.TrySkipPreviousAsync());
        NextCommand = new RelayCommand(() => _ = _smtc.TrySkipNextAsync());
    }

    public RelayCommand PlayPauseCommand { get; }

    public RelayCommand PrevCommand { get; }

    public RelayCommand NextCommand { get; }

    public bool IsPlaying => _orchestrator.IsPlaying;

    /// <summary>歌词解析相位（切歌动画以此判定歌词加载完成）。</summary>
    public LyricsPhase Phase => _orchestrator.Phase;

    // ---------- 封面 ----------

    public bool CoverEnabled => S.CoverEnabled;

    public bool CoverCutAnimation => S.CoverCutAnimation;

    /// <summary>切歌封面最短显示时长（ms）。</summary>
    public int CoverAnimMinMs => Math.Clamp(S.CoverAnimMinMs, 300, 5000);

    /// <summary>切歌封面最长显示时长（ms）。</summary>
    public int CoverAnimMaxMs => Math.Clamp(S.CoverAnimMaxMs, 1000, 15000);

    /// <summary>封面占比：封面边长 = 歌词行高度 × 该百分比（40–120）。</summary>
    public double CoverSizePct => Math.Clamp(S.CoverSizePct, 40, 120);

    public IImage? CoverImage { get; private set; }

    /// <summary>当前曲目标识（Title|Artist）；仅真正切歌时变化。</summary>
    public string CoverTrackKey { get; private set; } = "";

    private string _coverKey = "";
    private string _trackKeyCache = "";
    private bool _hadTrack;
    private bool _cutAnimationFlag;

    /// <summary>
    /// 消费切歌动画标记：任何来源（播放器内切歌 / 悬浮窗按钮）的曲目变化都会置位，
    /// 由悬浮窗消费后清空。
    /// </summary>
    public bool ConsumeCutAnimationFlag()
    {
        var v = _cutAnimationFlag;
        _cutAnimationFlag = false;
        return v;
    }

    /// <summary>曲目变化时解码封面（限制解码尺寸，UI 线程执行）。</summary>
    private void RefreshCover()
    {
        var track = _orchestrator.Track;
        var bytes = track?.Cover;
        var key = bytes == null ? "" : $"{track!.Title}|{track.Artist}|{bytes.Length}";
        if (key == _coverKey)
            return;
        _coverKey = key;

        IImage? image = null;
        if (bytes is { Length: > 0 })
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                image = Bitmap.DecodeToWidth(ms, 512);
            }
            catch
            {
                // 封面解码失败 → 无封面
            }
        }
        CoverImage = image;
    }

    /// <summary>真正的切歌（Title|Artist 变化）才更新 CoverTrackKey；并标记切歌动画。</summary>
    private void RefreshCoverTrackKey()
    {
        var t = _orchestrator.Track;
        var key = t == null ? "" : $"{t.Title}|{t.Artist}";
        if (key == _trackKeyCache)
            return;
        _trackKeyCache = key;
        // 切歌动画：任何来源的曲目变化都播；首次获得曲目（应用启动/绑定新会话）不播
        _cutAnimationFlag = t != null && _hadTrack;
        if (t != null)
            _hadTrack = true;
        CoverTrackKey = key;
        Raise(nameof(CoverTrackKey));
    }

    private AppSettings S => _settings.Current;

    public string MainText
    {
        get
        {
            string text;
            if (_orchestrator.Phase == LyricsPhase.Ready)
            {
                // 纯音乐：作词/作曲元信息显示完后切回标题
                text = _orchestrator.IsInstrumental && _orchestrator.InstrumentalEnded
                    ? FallbackText()
                    : _orchestrator.CurrentMain;
            }
            else
            {
                text = FallbackText();
            }
            return text;
        }
    }

    public string TransText => _orchestrator.Phase == LyricsPhase.Ready ? _orchestrator.CurrentTrans : "";

    public bool ShowTransLine => S.ShowTranslation && TransText.Length > 0;

    public FontFamily FontFamilyValue
    {
        get
        {
            try
            {
                return new FontFamily(S.FontFamily);
            }
            catch
            {
                return FontFamily.Default;
            }
        }
    }

    public double MainFontSize => Math.Clamp(S.FontSize, 10, 200);

    public double EffectiveTransFontSize
    {
        get
        {
            var ts = S.TransFontSize > 0 ? S.TransFontSize : Math.Round(MainFontSize * 0.6);
            return Math.Max(10, ts);
        }
    }

    public double LineSpacing => Math.Clamp(S.LineSpacing, 0, 50);

    public TextAlignment TextAlignment
    {
        get
        {
            try { return (TextAlignment)Enum.Parse(typeof(TextAlignment), S.Alignment); }
            catch { return Avalonia.Media.TextAlignment.Center; }
        }
    }

    public FontWeight WeightValue => (FontWeight)Math.Clamp(S.FontWeight, 100, 950);

    public IBrush Fill
    {
        get
        {
            if (_fillHexCache != S.ColorHex)
            {
                _fillHexCache = S.ColorHex;
                _fillCache = Color.TryParse(S.ColorHex, out var c) ? new SolidColorBrush(c) : Brushes.White;
            }
            return _fillCache;
        }
    }

    /// <summary>逐字未唱段颜色：独立设置时用指定色；否则主色。最终 alpha = 颜色 alpha × 未唱不透明度比例。</summary>
    public IBrush InactiveFill
    {
        get
        {
            var key = $"{(S.InactiveColorHex.Length > 0 ? "I|" + S.InactiveColorHex : "F|" + S.ColorHex)}|{S.InactiveOpacity}";
            if (_inactiveHexCache != key)
            {
                _inactiveHexCache = key;
                var c = Color.TryParse(S.InactiveColorHex, out var ic)
                    ? ic
                    : Color.TryParse(S.ColorHex, out var mc) ? mc : Colors.White;
                var pct = Math.Clamp(S.InactiveOpacity, 0.05, 1.0);
                _inactiveFillCache = new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Round(c.A * pct), c.R, c.G, c.B));
            }
            return _inactiveFillCache;
        }
    }

    /// <summary>已唱层未唱段填充：完全透明（只让已唱段字形上色，未唱段由下层负责）。</summary>
    public IBrush ActiveLayerInactiveFill => TransparentBrush;

    /// <summary>未唱段描边开关（默认关：未唱段不描边，逐字明暗对比不被抹平）。</summary>
    public bool InactiveStrokeEnabled => S.InactiveStrokeEnabled;

    /// <summary>未唱段描边宽度（独立于已唱段）。</summary>
    public double InactiveStrokeThickness => Math.Clamp(S.InactiveStrokeThickness, 0.1, 4);

    public bool InactiveGlowEnabled => S.InactiveGlowEnabled;

    /// <summary>未唱段描边画笔（已唱段描边沿用 StrokeBrush）。</summary>
    public IBrush InactiveStrokeBrush
    {
        get
        {
            if (_inactiveStrokeHexCache != S.InactiveStrokeColorHex)
            {
                _inactiveStrokeHexCache = S.InactiveStrokeColorHex;
                _inactiveStrokeBrushCache = Color.TryParse(S.InactiveStrokeColorHex, out var c)
                    ? new SolidColorBrush(c)
                    : Brushes.Black;
            }
            return _inactiveStrokeBrushCache;
        }
    }

    /// <summary>未唱段辉光：独立于已唱段辉光（颜色/强度均可独立设置，默认关）。</summary>
    public IEffect? InactiveGlowEffect
    {
        get
        {
            if (!S.InactiveGlowEnabled)
                return null;
            var key = $"{S.InactiveGlowColorHex}|{S.InactiveGlowRadius}";
            if (_inactiveGlowKeyCache != key)
            {
                _inactiveGlowKeyCache = key;
                var color = Color.TryParse(S.InactiveGlowColorHex, out var c) ? c : Colors.White;
                _inactiveGlowCache = new DropShadowEffect
                {
                    OffsetX = 0,
                    OffsetY = 0,
                    BlurRadius = Math.Clamp(S.InactiveGlowRadius, 4, 60),
                    Opacity = 0.85,
                    Color = color,
                };
            }
            return _inactiveGlowCache;
        }
    }

    /// <summary>当前行已唱字符数；-1 = 非逐字模式（整行高亮）。</summary>
    public int WordHighlightLength => _orchestrator.CurrentWordDone;

    /// <summary>正在唱字符的平滑渐变进度 0~1（与 WordHighlightLength 配合实现字符内点亮动画）。</summary>
    public double WordHighlightFraction => _orchestrator.CurrentWordFraction;

    public double TextOpacity => Math.Clamp(S.Opacity, 0.05, 1.0);

    public double MaxTextWidth => Math.Clamp(S.MaxWidth, 200, 4000);

    /// <summary>投影：颜色/模糊半径/垂直偏移均可配置。</summary>
    public IEffect? TextEffect
    {
        get
        {
            if (!S.ShadowEnabled)
                return null;
            var key = $"{S.ShadowColorHex}|{S.ShadowBlurRadius}|{S.ShadowOffsetY}";
            if (_shadowKeyCache != key)
            {
                _shadowKeyCache = key;
                var color = Color.TryParse(S.ShadowColorHex, out var c) ? c : Colors.Black;
                _shadowCache = new DropShadowEffect
                {
                    OffsetX = 0,
                    OffsetY = S.ShadowOffsetY,
                    BlurRadius = Math.Clamp(S.ShadowBlurRadius, 1, 60),
                    Opacity = 0.9,
                    Color = color,
                };
            }
            return _shadowCache;
        }
    }

    /// <summary>辉光：大模糊半径的彩色光晕（叠加在文字下层）。</summary>
    public IEffect? GlowEffect
    {
        get
        {
            if (!S.GlowEnabled)
                return null;
            var key = $"{S.GlowColorHex}|{S.GlowRadius}";
            if (_glowKeyCache != key)
            {
                _glowKeyCache = key;
                var color = Color.TryParse(S.GlowColorHex, out var c) ? c : Colors.White;
                _glowCache = new DropShadowEffect
                {
                    OffsetX = 0,
                    OffsetY = 0,
                    BlurRadius = Math.Clamp(S.GlowRadius, 4, 60),
                    Opacity = 0.85,
                    Color = color,
                };
            }
            return _glowCache;
        }
    }

    public bool StrokeEnabled => S.StrokeEnabled;

    public bool GlowEnabled => S.GlowEnabled;

    public IBrush StrokeBrush
    {
        get
        {
            if (_strokeHexCache != S.StrokeColorHex)
            {
                _strokeHexCache = S.StrokeColorHex;
                _strokeBrushCache = Color.TryParse(S.StrokeColorHex, out var c) ? new SolidColorBrush(c) : Brushes.Black;
            }
            return _strokeBrushCache;
        }
    }

    public double StrokeThickness => Math.Clamp(S.StrokeThickness, 0.1, 4);

    public bool WindowVisible
    {
        get
        {
            if (!S.LyricsVisible)
                return false;
            if (_orchestrator.Phase == LyricsPhase.NoSession)
                return false;
            if (S.HideWhenPaused && !_orchestrator.IsPlaying)
                return false;
            if (MainText.Length == 0 && !ShowTransLine)
                return false;
            return true;
        }
    }

    private string FallbackText()
    {
        var t = _orchestrator.Track;
        if (t == null || !S.ShowTitleWhenNoLyric)
            return "";
        return string.IsNullOrEmpty(t.Artist) ? t.Title : $"{t.Artist} - {t.Title}";
    }
}
