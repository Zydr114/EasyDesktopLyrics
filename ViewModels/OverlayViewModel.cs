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
        nameof(Fill), nameof(TextOpacity), nameof(MaxTextWidth), nameof(TextEffect), nameof(GlowEffect),
        nameof(WindowVisible), nameof(IsPlaying),
        nameof(StrokeEnabled), nameof(StrokeBrush), nameof(StrokeThickness), nameof(LineSpacing),
        nameof(TextAlignment), nameof(GlowEnabled),
        nameof(CoverImage), nameof(CoverMode), nameof(CoverPosition), nameof(CoverSize),
    ];

    private readonly SettingsService _settings;
    private readonly LyricsOrchestrator _orchestrator;
    private readonly SmtcService _smtc;

    private string _fillHexCache = "";
    private IBrush _fillCache = Brushes.White;
    private string _strokeHexCache = "";
    private IBrush _strokeBrushCache = Brushes.Black;
    private string _shadowKeyCache = "";
    private IEffect? _shadowCache;
    private string _glowKeyCache = "";
    private IEffect? _glowCache;

    public OverlayViewModel(SettingsService settings, LyricsOrchestrator orchestrator, SmtcService smtc)
    {
        _settings = settings;
        _orchestrator = orchestrator;
        _smtc = smtc;
        _settings.Changed += () => RaiseMany(AllProps);
        _orchestrator.StateChanged += () =>
        {
            RefreshCover();
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

    // ---------- 封面 ----------

    public string CoverMode => S.CoverMode;

    public string CoverPosition => S.CoverPosition;

    public double CoverSize => Math.Clamp(S.CoverSize, 64, 256);

    public IImage? CoverImage { get; private set; }

    private string _coverKey = "";

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

    public double StrokeThickness => Math.Clamp(S.StrokeThickness, 1, 8);

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
