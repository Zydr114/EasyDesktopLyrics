namespace EasyDesktopLyrics.Models;

/// <summary>SMTC 提供的曲目元数据快照。</summary>
public sealed record TrackInfo(
    string Title,
    string Artist,
    string Album,
    long DurationMs,
    string SourceAumid,
    byte[]? Cover = null);

/// <summary>SMTC 播放状态 + 时间线快照（PositionAt 为 Position 的采样时刻）。</summary>
public readonly record struct PlaybackSnapshot(
    bool IsPlaying,
    double Rate,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset PositionAt);
