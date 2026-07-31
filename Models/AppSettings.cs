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
    public bool ShadowEnabled { get; set; } = true;
    public string ShadowColorHex { get; set; } = "#000000";
    public double ShadowBlurRadius { get; set; } = 8;
    public double ShadowOffsetY { get; set; } = 2;
    public bool StrokeEnabled { get; set; }
    public string StrokeColorHex { get; set; } = "#000000";
    public double StrokeThickness { get; set; } = 2;
    /// <summary>辉光（软光晕）：大模糊半径的彩色阴影，与硬边描边互补。</summary>
    public bool GlowEnabled { get; set; }
    public string GlowColorHex { get; set; } = "#FFFFFF";
    public double GlowRadius { get; set; } = 14;
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

    // ---- 行为 ----
    public bool ShowTranslation { get; set; }
    /// <summary>歌词对齐："Left" / "Center" / "Right"。</summary>
    public string Alignment { get; set; } = "Center";
    public bool HideWhenPaused { get; set; }
    public bool ShowTitleWhenNoLyric { get; set; } = true;
    public bool AutoStart { get; set; }
    /// <summary>托盘“显示歌词”开关。</summary>
    public bool LyricsVisible { get; set; } = true;

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
