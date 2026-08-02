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
