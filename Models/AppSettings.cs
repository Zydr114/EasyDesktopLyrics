namespace EasyDesktopLyrics.Models;

/// <summary>
/// 全部设置项（封闭集合，对应架构文档 §1.3，不再扩充）。
/// </summary>
public sealed class AppSettings
{
    // ---- 外观 ----
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public double FontSize { get; set; } = 34;
    /// <summary>100–900，常用 400/500/600/700。</summary>
    public int FontWeight { get; set; } = 600;
    public string ColorHex { get; set; } = "#FFFFFF";
    /// <summary>未唱段颜色（逐字模式未演唱部分）；空 = 跟随主色。</summary>
    public string InactiveColorHex { get; set; } = "";
    /// <summary>未唱段不透明度比例（相对全局透明度；1.0 = 与已唱段相同亮度）。</summary>
    public double InactiveOpacity { get; set; } = 0.45;
    public bool ShadowEnabled { get; set; } = true;
    public string ShadowColorHex { get; set; } = "#000000";
    public double ShadowBlurRadius { get; set; } = 8;
    public double ShadowOffsetY { get; set; } = 2;
    public bool StrokeEnabled { get; set; }
    public string StrokeColorHex { get; set; } = "#000000";
    public double StrokeThickness { get; set; } = 2;
    /// <summary>未唱段描边开关（默认关：未唱段不描边，避免整行描边覆盖逐字明暗对比）。</summary>
    public bool InactiveStrokeEnabled { get; set; }
    public string InactiveStrokeColorHex { get; set; } = "#000000";
    /// <summary>未唱段描边宽度（独立于已唱段）。</summary>
    public double InactiveStrokeThickness { get; set; } = 2;
    /// <summary>辉光（软光晕）：大模糊半径的彩色阴影，与硬边描边互补。</summary>
    public bool GlowEnabled { get; set; }
    public string GlowColorHex { get; set; } = "#FFFFFF";
    public double GlowRadius { get; set; } = 14;
    /// <summary>未唱段辉光开关（默认关：光晕只照亮已唱段）。</summary>
    public bool InactiveGlowEnabled { get; set; }
    public string InactiveGlowColorHex { get; set; } = "#FFFFFF";
    /// <summary>未唱段辉光强度（与已唱段辉光独立）。</summary>
    public double InactiveGlowRadius { get; set; } = 14;
    /// <summary>翻译行字号，=0 时自动取正文字号的 0.6 倍。</summary>
    public double TransFontSize { get; set; }
    /// <summary>两行歌词间距（DIP）。</summary>
    public double LineSpacing { get; set; } = 4;
    public double Opacity { get; set; } = 1.0;
    /// <summary>歌词行最大宽度（DIP），超宽自动等比缩小。</summary>
    public double MaxWidth { get; set; } = 1100;
    /// <summary>窗口中心锚点（虚拟桌面物理像素坐标）；null = 主屏底部默认位置。</summary>
    public double? AnchorX { get; set; }
    public double? AnchorY { get; set; }
    /// <summary>窗口高度变化时的扩展方向：1=向下（顶部固定，默认），2=向上（底部固定），0=上下均扩展（中心固定）。</summary>
    public int HeightGrowMode { get; set; } = 1;

    // ---- 行为 ----
    public bool ShowTranslation { get; set; }
    /// <summary>逐字歌词（卡拉 OK）：有逐字时间戳（网易 yrc）时按字高亮，无则整行。</summary>
    public bool WordByWord { get; set; } = true;
    /// <summary>歌词对齐："Left" / "Center" / "Right"。</summary>
    public string Alignment { get; set; } = "Center";
    public bool HideWhenPaused { get; set; }
    public bool ShowTitleWhenNoLyric { get; set; } = true;
    public bool AutoStart { get; set; }
    /// <summary>托盘“显示歌词”开关。</summary>
    public bool LyricsVisible { get; set; } = true;
    /// <summary>窗口锁定：完全不可动且鼠标穿透（WS_EX_TRANSPARENT），仅托盘/设置可解锁。</summary>
    public bool Locked { get; set; }

    // ---- 行间切换动效 ----
    /// <summary>行间切换动效总开关（默认关 = 保持当前版本的即时切换）。</summary>
    public bool LineTransitionEnabled { get; set; }
    /// <summary>动效类型："Fade" 淡入 / "Slide" 位移淡入 / "Scale" 缩放弹入 / "Crossfade" 交叉淡化 / "Reveal" 逐字显现 / "Shine" 扫光。</summary>
    public string LineTransitionType { get; set; } = "Fade";
    /// <summary>动效时长（ms；1.0 倍速参考基准 = 400ms）。</summary>
    public int LineTransitionDurationMs { get; set; } = 400;
    /// <summary>缩放弹入的起始缩放比例（仅"Scale"类型使用）。</summary>
    public double LineTransitionScale { get; set; } = 0.85;
    /// <summary>缩放弹入的旧行退场：true=放大淡出，false=直接淡出。</summary>
    public bool LineTransitionScaleExitGrow { get; set; } = true;
    /// <summary>位移淡入入场模糊开关（仅"Slide"类型使用）。</summary>
    public bool LineTransitionSlideBlurEnabled { get; set; } = true;
    /// <summary>位移淡入入场模糊半径（px）。</summary>
    public double LineTransitionSlideBlurRadius { get; set; } = 4;
    /// <summary>缓动曲线：Linear / Quadratic / Cubic / Sine / Exponential / Back。</summary>
    public string LineTransitionEasing { get; set; } = "Quadratic";
    /// <summary>滑入方向：0=无位移，1=上，2=下，3=左，4=右（与封面动画同语义）。</summary>
    public int LineTransitionDirection { get; set; } = 2;
    /// <summary>滑入位移强度（px）。</summary>
    public double LineTransitionDistance { get; set; } = 10;

    // ---- 封面 ----
    /// <summary>播放时常驻显示封面。</summary>
    public bool CoverEnabled { get; set; }
    /// <summary>切歌时封面居中淡入动画（独立于常驻显示）。</summary>
    public bool CoverCutAnimation { get; set; } = true;
    /// <summary>封面占比：封面尺寸 = 歌词宽度 × 该百分比（40–120）。</summary>
    public double CoverSizePct { get; set; } = 80;
    /// <summary>切歌封面最短显示时长（ms，歌词提前加载完成时保底）。</summary>
    public int CoverAnimMinMs { get; set; } = 1200;
    /// <summary>切歌封面最长显示时长（ms，歌词加载超时时强制结束）。</summary>
    public int CoverAnimMaxMs { get; set; } = 6000;
    /// <summary>切歌封面动画缓动曲线：Linear / Quadratic / Cubic / Sine / Exponential / Back。</summary>
    public string CoverAnimEasing { get; set; } = "Quadratic";
    /// <summary>切歌封面移动/淡入淡出动画时长（ms）。</summary>
    public int CoverAnimDurationMs { get; set; } = 450;
    /// <summary>切歌封面淡入淡出方向：0=无位移，1=上，2=下，3=左，4=右。</summary>
    public int CoverAnimDirection { get; set; }
    /// <summary>切歌封面淡入淡出位移强度（px）。</summary>
    public double CoverAnimSlideDistance { get; set; } = 12;

    // ---- 切歌封面歌名 ----
    /// <summary>切歌动画中在封面旁显示歌名（默认关）。</summary>
    public bool CoverTitleEnabled { get; set; }
    /// <summary>歌名下方同时显示歌手（分行）。</summary>
    public bool CoverTitleShowArtist { get; set; } = true;
    /// <summary>歌名淡入淡出方向：0=无位移，1=上，2=下，3=左，4=右。</summary>
    public int CoverTitleAnimDirection { get; set; }
    /// <summary>歌名滑入/滑出位移强度（px）。</summary>
    public double CoverTitleSlideDistance { get; set; } = 12;
    // 歌名行阴影
    public bool CoverTitleShadowEnabled { get; set; }
    public string CoverTitleShadowColorHex { get; set; } = "#000000";
    public double CoverTitleShadowBlurRadius { get; set; } = 6;
    public double CoverTitleShadowOffsetY { get; set; } = 2;
    // 歌手行阴影
    public bool CoverArtistShadowEnabled { get; set; }
    public string CoverArtistShadowColorHex { get; set; } = "#000000";
    public double CoverArtistShadowBlurRadius { get; set; } = 6;
    public double CoverArtistShadowOffsetY { get; set; } = 2;
    // 歌名行
    public string CoverTitleFont { get; set; } = "Microsoft YaHei UI";
    public string CoverTitleColorHex { get; set; } = "#FFFFFF";
    public double CoverTitleFontSize { get; set; } = 26;
    public double CoverTitleOpacity { get; set; } = 1.0;
    public bool CoverTitleStrokeEnabled { get; set; }
    public string CoverTitleStrokeColorHex { get; set; } = "#000000";
    public double CoverTitleStrokeThickness { get; set; } = 2;
    // 歌手行
    public string CoverArtistFont { get; set; } = "Microsoft YaHei UI";
    public string CoverArtistColorHex { get; set; } = "#FFFFFF";
    public double CoverArtistFontSize { get; set; } = 20;
    public double CoverArtistOpacity { get; set; } = 0.8;
    public bool CoverArtistStrokeEnabled { get; set; }
    public string CoverArtistStrokeColorHex { get; set; } = "#000000";
    public double CoverArtistStrokeThickness { get; set; } = 2;

    // ---- SMTC ----
    /// <summary>锁定监听的播放器 AUMID；null/空 = 自动跟随系统当前会话。</summary>
    public string? LockedSessionAumid { get; set; }

    /// <summary>播放器歌词优先级规则：列表顺序即优先级，Enabled=false 表示忽略该播放器。</summary>
    public List<PlayerPriority> PlayerPriorities { get; set; } = new();

    // ---- 歌词源 ----
    /// <summary>歌词源优先级规则：列表顺序即优先级，Enabled=false 表示不使用该源。</summary>
    public List<LyricSourceRule> LyricSources { get; set; } = new();

    /// <summary>本地歌词目录（多个用分号分隔），空 = 不使用本地源。</summary>
    public string LyricsFolder { get; set; } = "";

    /// <summary>全局时间偏移（ms），正值 = 歌词提前。</summary>
    public int GlobalOffsetMs { get; set; }

    // ---- 背景动效 ----
    public BackgroundFxSettings BackgroundFx { get; set; } = new();

    // ---- 兼容旧版（v0.1）----
    public bool NeteaseEnabled { get; set; } = true;
    public bool QQMusicEnabled { get; set; } = true;
    public bool NeteaseFirst { get; set; } = true;
}

/// <summary>单个歌词源的优先级规则。</summary>
public sealed class LyricSourceRule
{
    public string SourceId { get; set; } = "";

    /// <summary>false = 不使用该歌词源。</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>单个播放器的歌词优先级规则。</summary>
public sealed class PlayerPriority
{
    public string Aumid { get; set; } = "";

    /// <summary>false = 忽略该播放器的 SMTC 会话。</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>背景动效设置（频谱/飘雪/雾层），均默认关闭，与主功能解耦。</summary>
public sealed class BackgroundFxSettings
{
    /// <summary>渲染帧率（30–120）。</summary>
    public int Fps { get; set; } = 30;

    public SpectrumFxSettings Spectrum { get; set; } = new();

    public SnowFxSettings Snow { get; set; } = new();

    public FogFxSettings Fog { get; set; } = new();
}

/// <summary>频谱动效设置。</summary>
public sealed class SpectrumFxSettings
{
    public bool Enabled { get; set; }

    /// <summary>位置："Bottom" 底部（单侧，自下而上）/ "Center" 行中央（双侧，沿 x 轴对称）/ "Top" 顶部（单侧，自上而下）。</summary>
    public string Position { get; set; } = "Bottom";

    /// <summary>样式："Bars" 柱状图 / "Curve" 曲线 / "Line" 单曲线。</summary>
    public string Style { get; set; } = "Bars";

    /// <summary>强度：频谱最高峰高度 = 窗口高度百分比（10–90）。</summary>
    public double Intensity { get; set; } = 40;

    /// <summary>数量：频谱采样（柱/曲线点）总数（16–128）。</summary>
    public int BandCount { get; set; } = 32;

    /// <summary>宽度范围 = 窗口宽度百分比（20–100）。</summary>
    public double WidthPct { get; set; } = 100;

    public double Opacity { get; set; } = 0.8;

    public string ColorHex { get; set; } = "#00E5FF";

    public bool GlowEnabled { get; set; } = true;

    /// <summary>辉光强度倍率（0–2）。</summary>
    public double GlowStrength { get; set; } = 1.0;

    /// <summary>FFT 平滑帧数（1–10，越大越柔和）。</summary>
    public int Smoothing { get; set; } = 3;
}

/// <summary>飘雪动效设置。</summary>
public sealed class SnowFxSettings
{
    public bool Enabled { get; set; }

    /// <summary>粒子数量上限（20–400）。</summary>
    public int Intensity { get; set; } = 80;

    /// <summary>飘雪范围 = 窗口宽度百分比（20–100）。</summary>
    public double WidthPct { get; set; } = 100;

    public double Opacity { get; set; } = 0.7;

    /// <summary>雪花大小倍率（0.4–3）。</summary>
    public double Size { get; set; } = 1.0;

    /// <summary>下落速度倍率（0.2–3）。</summary>
    public double Speed { get; set; } = 1.0;

    public string ColorHex { get; set; } = "#FFFFFF";
}

/// <summary>雾层动效设置。</summary>
public sealed class FogFxSettings
{
    public bool Enabled { get; set; }

    public double Opacity { get; set; } = 0.35;

    /// <summary>伪模糊强度（光晕柔和度，0.5–3）。</summary>
    public double Softness { get; set; } = 1.0;

    /// <summary>颜色流动速度（0–3，0 = 静止）。</summary>
    public double FlowSpeed { get; set; } = 1.0;

    /// <summary>是否使用封面主色。</summary>
    public bool UseCoverColor { get; set; } = true;

    /// <summary>是否使用窗口背后采样色。</summary>
    public bool UseBackdropColor { get; set; } = true;

    /// <summary>封面色 / 背后色混合比例（0–1，1 = 全封面）。</summary>
    public double Blend { get; set; } = 0.5;

    /// <summary>颜色流动开关（false = 静态）。</summary>
    public bool Animated { get; set; } = true;
}
