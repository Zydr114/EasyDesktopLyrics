using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Services;

/// <summary>
/// 播放进度插值时钟。SMTC 的 Position 仅在 PositionAt（LastUpdatedTime）时刻准确，
/// 播放器上报间隔不定，本地用挂钟插值出连续位置。
/// </summary>
public sealed class PlaybackClock
{
    private TimeSpan _basePos;
    private DateTimeOffset _baseAt = DateTimeOffset.UtcNow;
    private double _rate = 1.0;
    private TimeSpan _duration;

    public bool IsPlaying { get; private set; }

    public void Sync(PlaybackSnapshot s)
    {
        _duration = s.Duration;
        _rate = s.Rate <= 0 ? 1.0 : s.Rate;

        // LastUpdatedTime 可能过期或为 0，做合法性校验后再采用
        var age = DateTimeOffset.UtcNow - s.PositionAt;
        var timelineUsable = age > TimeSpan.Zero && age < TimeSpan.FromSeconds(30);

        if (timelineUsable)
        {
            // 时间线有效：以 SMTC 上报为准重置插值基准
            _basePos = s.Position;
            _baseAt = s.PositionAt;
        }
        else if (IsPlaying != s.IsPlaying)
        {
            // 时间线不可用（如网易云 PC 版恒报 Position=0 / LastUpdatedTime 无效）：
            // 单调降级时钟，仅在播放/暂停状态边界重置基准，其余时间由挂钟驱动，
            // 避免高频事件把进度反复重置回 0
            _basePos = Estimate();
            _baseAt = DateTimeOffset.UtcNow;
        }
        // 时间线不可用且状态未变：保持原基准，挂钟继续驱动

        IsPlaying = s.IsPlaying;
    }

    public void Reset()
    {
        _basePos = TimeSpan.Zero;
        _baseAt = DateTimeOffset.UtcNow;
        _duration = TimeSpan.Zero;
    }

    public TimeSpan Estimate()
    {
        var pos = IsPlaying
            ? _basePos + (DateTimeOffset.UtcNow - _baseAt) * _rate
            : _basePos;

        if (_duration > TimeSpan.Zero && pos > _duration)
            pos = _duration;
        return pos < TimeSpan.Zero ? TimeSpan.Zero : pos;
    }
}
