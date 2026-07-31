namespace EasyDesktopLyrics.Models;

/// <summary>一行歌词（毫秒时间戳 + 原文 + 可选翻译）。</summary>
public sealed record LyricLine(long TimeMs, string Text, string? Translation);

/// <summary>解析后的整首歌词，行按时间升序。</summary>
public sealed class LyricDocument
{
    public LyricDocument(IReadOnlyList<LyricLine> lines, bool isInstrumental = false)
    {
        Lines = lines;
        IsInstrumental = isInstrumental;
    }

    public IReadOnlyList<LyricLine> Lines { get; }

    /// <summary>true = 纯音乐：仅含作词/作曲等元信息行，显示完后切回标题。</summary>
    public bool IsInstrumental { get; }
}

/// <summary>歌词源搜索结果条目。</summary>
public sealed record ProviderSong(
    string ProviderId,
    string SongId,
    string Title,
    string Artist,
    string Album,
    long DurationMs);

/// <summary>歌词源返回的原始 LRC 文本。</summary>
public sealed record RawLyric(string Lrc, string? TranslationLrc);

/// <summary>磁盘缓存条目（正缓存或“未找到”负缓存）。</summary>
public sealed class CachedLyric
{
    public string? Source { get; set; }
    public string? SongId { get; set; }
    public string? Lrc { get; set; }
    public string? TransLrc { get; set; }
    public bool NotFound { get; set; }
    public DateTimeOffset FetchedAt { get; set; }

    public static CachedLyric Positive(string source, string songId, RawLyric raw) => new()
    {
        Source = source,
        SongId = songId,
        Lrc = raw.Lrc,
        TransLrc = raw.TranslationLrc,
        FetchedAt = DateTimeOffset.UtcNow,
    };

    public static CachedLyric Negative() => new()
    {
        NotFound = true,
        FetchedAt = DateTimeOffset.UtcNow,
    };
}

/// <summary>手动校正记录：指定歌词（Provider+SongId）与/或单曲偏移。</summary>
public sealed class LyricOverride
{
    public string? Provider { get; set; }
    public string? SongId { get; set; }
    /// <summary>单曲偏移（ms），正值 = 歌词提前。</summary>
    public int OffsetMs { get; set; }
}
