using Avalonia.Threading;
using EasyDesktopLyrics.Infrastructure;
using EasyDesktopLyrics.Models;
using Windows.Foundation;
using Windows.Media.Control;

namespace EasyDesktopLyrics.Services;

/// <summary>
/// SMTC 封装。所有 WinRT 事件（MTA 线程）统一转投 UI 线程后再对外发布，
/// 下游（Orchestrator/VM）全部免锁。
/// </summary>
public sealed class SmtcService : IDisposable
{
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs> _onCurrentSession;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs> _onSessionsChanged;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _onMedia;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _onPlaybackInfo;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> _onTimeline;
    private readonly UiDebouncer _mediaDebouncer = new();

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private TrackInfo? _current;
    private string? _preferredAumid;
    private IReadOnlyList<PlayerPriority> _rules = [];
    private bool _disposed;

    public SmtcService()
    {
        _onCurrentSession = (_, _) => Post(RebindSession);
        _onSessionsChanged = (_, _) => Post(() =>
        {
            SessionsChanged?.Invoke();
            RebindSession();
        });
        _onMedia = (_, _) => Post(() => ScheduleTrackRefresh(immediate: false));
        _onPlaybackInfo = (_, _) => Post(PushPlayback);
        _onTimeline = (_, _) => Post(PushPlayback);
    }

    /// <summary>当前曲目变化（null = 无会话）。UI 线程回调。</summary>
    public event Action<TrackInfo?>? TrackChanged;

    /// <summary>播放状态/时间线快照。UI 线程回调。</summary>
    public event Action<PlaybackSnapshot>? PlaybackChanged;

    /// <summary>系统会话列表变化（供设置页刷新下拉框）。UI 线程回调。</summary>
    public event Action? SessionsChanged;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += _onCurrentSession;
        _manager.SessionsChanged += _onSessionsChanged;
        Post(RebindSession);
        Log.Info("SMTC manager ready");
    }

    /// <summary>锁定监听某播放器；null = 自动跟随系统当前会话。</summary>
    public void SetPreferredSession(string? aumid)
    {
        aumid = string.IsNullOrWhiteSpace(aumid) ? null : aumid;
        if (_preferredAumid == aumid)
            return;
        _preferredAumid = aumid;
        if (_manager != null)
            RebindSession();
    }

    /// <summary>设置播放器优先级规则（列表顺序即优先级）；空列表 = 自动跟随当前会话。</summary>
    public void SetPlayerRules(IReadOnlyList<PlayerPriority> rules)
    {
        _rules = rules ?? [];
        if (_manager != null)
            RebindSession();
    }

    /// <summary>当前系统内的活动会话列表（去重后的 AUMID + 友好名）。</summary>
    public IReadOnlyList<(string Aumid, string DisplayName)> GetSessions()
    {
        if (_manager == null)
            return [];
        try
        {
            return _manager.GetSessions()
                .Select(s => s.SourceAppUserModelId)
                .Where(a => !string.IsNullOrEmpty(a))
                .Distinct()
                .Select(a => (a, FriendlyName(a)))
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("GetSessions", ex);
            return [];
        }
    }

    // ---------- 播放控制 ----------

    public Task<bool> TryTogglePlayPauseAsync() => ControlAsync(s => s.TryTogglePlayPauseAsync());

    public Task<bool> TrySkipNextAsync() => ControlAsync(s => s.TrySkipNextAsync());

    public Task<bool> TrySkipPreviousAsync() => ControlAsync(s => s.TrySkipPreviousAsync());

    /// <summary>跳转到指定位置（100ns 单位）。</summary>
    public Task<bool> TrySeekAsync(TimeSpan position) =>
        ControlAsync(s => s.TryChangePlaybackPositionAsync(position.Ticks));

    private async Task<bool> ControlAsync(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> op)
    {
        var s = _session;
        if (s == null)
            return false;
        try
        {
            return await op(s);
        }
        catch (Exception ex)
        {
            Log.Error("smtc control", ex);
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _mediaDebouncer.Cancel();
        DetachSession();
        if (_manager != null)
        {
            try
            {
                _manager.CurrentSessionChanged -= _onCurrentSession;
                _manager.SessionsChanged -= _onSessionsChanged;
            }
            catch
            {
                // ignore
            }
            _manager = null;
        }
    }

    private static string FriendlyName(string aumid)
    {
        try
        {
            var name = Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(aumid)?.DisplayInfo?.DisplayName;
            if (!string.IsNullOrWhiteSpace(name))
                return name!;
        }
        catch
        {
            // Win32 AUMID 常无 AppInfo，回退原始字符串
        }
        return aumid;
    }

    private static void Post(Action action) => Dispatcher.UIThread.Post(action);

    private void RebindSession()
    {
        if (_disposed || _manager == null)
            return;

        GlobalSystemMediaTransportControlsSession? target = null;
        try
        {
            target = PickSession();
        }
        catch (Exception ex)
        {
            Log.Error("pick session", ex);
        }

        DetachSession();
        _session = target;
        if (_session != null)
        {
            _session.MediaPropertiesChanged += _onMedia;
            _session.PlaybackInfoChanged += _onPlaybackInfo;
            _session.TimelinePropertiesChanged += _onTimeline;
        }

        ScheduleTrackRefresh(immediate: true);
        PushPlayback();
    }

    /// <summary>按 锁定 → 优先级规则 → 当前会话 的顺序选择监听目标，禁用的播放器被排除。</summary>
    private GlobalSystemMediaTransportControlsSession? PickSession()
    {
        var sessions = _manager!.GetSessions().ToList();

        // 1. 锁定播放器
        if (_preferredAumid != null)
            return sessions.FirstOrDefault(s =>
                string.Equals(s.SourceAppUserModelId, _preferredAumid, StringComparison.OrdinalIgnoreCase));

        // 2. 优先级规则：启用的、且有活动会话的播放器，按列表顺序取第一个
        foreach (var r in _rules)
        {
            if (!r.Enabled || string.IsNullOrEmpty(r.Aumid))
                continue;
            var s = sessions.FirstOrDefault(x =>
                string.Equals(x.SourceAppUserModelId, r.Aumid, StringComparison.OrdinalIgnoreCase));
            if (s != null)
                return s;
        }

        // 3. 回退：当前会话（若被禁用则取第一个未被禁用的会话）
        var current = _manager.GetCurrentSession();
        if (current != null && !IsDisabled(current.SourceAppUserModelId))
            return current;
        foreach (var s in sessions)
        {
            if (!IsDisabled(s.SourceAppUserModelId))
                return s;
        }
        return null;
    }

    private bool IsDisabled(string? aumid) =>
        !string.IsNullOrEmpty(aumid)
        && _rules.Any(r => !r.Enabled && string.Equals(r.Aumid, aumid, StringComparison.OrdinalIgnoreCase));

    private void DetachSession()
    {
        if (_session == null)
            return;
        try
        {
            _session.MediaPropertiesChanged -= _onMedia;
            _session.PlaybackInfoChanged -= _onPlaybackInfo;
            _session.TimelinePropertiesChanged -= _onTimeline;
        }
        catch
        {
            // 会话可能已失效
        }
        _session = null;
    }

    private void ScheduleTrackRefresh(bool immediate)
    {
        _mediaDebouncer.Cancel();
        if (immediate)
        {
            _ = RefreshTrackAsync();
            return;
        }
        // 播放器切歌常连发多次 MediaPropertiesChanged（封面异步加载），300ms 去抖
        _mediaDebouncer.Schedule(TimeSpan.FromMilliseconds(300), () => _ = RefreshTrackAsync());
    }

    private async Task RefreshTrackAsync()
    {
        var s = _session;
        if (s == null)
        {
            PublishTrack(null);
            return;
        }

        // 切歌瞬间可能抛 COMException 或返回空标题，小步重试
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (_disposed || !ReferenceEquals(s, _session))
                return;
            try
            {
                var p = await s.TryGetMediaPropertiesAsync();
                var title = p?.Title?.Trim() ?? "";
                if (title.Length == 0)
                {
                    await Task.Delay(300);
                    continue;
                }

                var artist = p!.Artist?.Trim();
                if (string.IsNullOrEmpty(artist))
                    artist = p.AlbumArtist?.Trim();

                long durationMs = 0;
                try { durationMs = (long)s.GetTimelineProperties().EndTime.TotalMilliseconds; } catch { }

                PublishTrack(new TrackInfo(title, artist ?? "", p.AlbumTitle?.Trim() ?? "", durationMs, s.SourceAppUserModelId ?? ""));
                return;
            }
            catch (Exception ex)
            {
                Log.Error($"TryGetMediaProperties attempt {attempt}", ex);
                await Task.Delay(300);
            }
        }
    }

    private void PublishTrack(TrackInfo? t)
    {
        if (t == null && _current == null)
            return;
        if (t != null && _current != null
            && t.Title == _current.Title && t.Artist == _current.Artist && t.SourceAumid == _current.SourceAumid)
            return;

        _current = t;
        Log.Info(t == null ? "track: <none>" : $"track: {t.Artist} - {t.Title} [{t.SourceAumid}] {t.DurationMs}ms");
        TrackChanged?.Invoke(t);
    }

    private void PushPlayback()
    {
        var s = _session;
        if (s == null)
            return;
        try
        {
            var tl = s.GetTimelineProperties();
            var pb = s.GetPlaybackInfo();
            PlaybackChanged?.Invoke(new PlaybackSnapshot(
                pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                pb.PlaybackRate ?? 1.0,
                tl.Position,
                tl.EndTime,
                tl.LastUpdatedTime));
        }
        catch (Exception ex)
        {
            Log.Error("PushPlayback", ex);
        }
    }
}
