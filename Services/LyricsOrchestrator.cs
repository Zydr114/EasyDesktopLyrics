using System.Text.RegularExpressions;
using Avalonia.Threading;
using EasyDesktopLyrics.Infrastructure;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services.Providers;

namespace EasyDesktopLyrics.Services;

public enum LyricsPhase
{
    NoSession,
    Resolving,
    Ready,
    NoLyric,
}

/// <summary>
/// 协调层：曲目变化 → override/缓存/搜索匹配 → LyricDocument；
/// 100ms 定时器 → 二分定位当前行，仅行号变化时通知 UI。
/// 除 ResolveCoreAsync 内部外全部运行在 UI 线程。
/// </summary>
public sealed partial class LyricsOrchestrator
{
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromDays(3);

    /// <summary>纯音乐占位行：整首歌词仅含此类文本时视为无歌词（显示标题）。</summary>
    [GeneratedRegex("纯音乐|伴奏|演奏曲|instrumental|no lyrics|music only", RegexOptions.IgnoreCase)]
    private static partial Regex InstrumentalPattern();

    /// <summary>作词/作曲等元信息行（纯音乐中保留显示，显示完后切回标题）。</summary>
    [GeneratedRegex("^(作词|作曲|编曲|制作人?|OP|SP|原曲|演唱|和声|混音|母带|录音|发行|版权|企划|出品)", RegexOptions.IgnoreCase)]
    private static partial Regex MetaInfoPattern();

    private readonly SettingsService _settings;
    private readonly LyricsCache _cache;
    private readonly OverridesStore _overrides;
    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly PlaybackClock _clock = new();
    private readonly DispatcherTimer _timer;

    private CancellationTokenSource? _cts;
    private LyricDocument? _doc;
    private int _lineIndex = -1;
    private int _songOffsetMs;
    private bool _instrumentalEnded;
    private bool _hasWords;
    private int _currentWordDone = -1;

    public LyricsOrchestrator(
        SmtcService smtc,
        SettingsService settings,
        LyricsCache cache,
        OverridesStore overrides,
        IReadOnlyList<ILyricsProvider> providers)
    {
        _settings = settings;
        _cache = cache;
        _overrides = overrides;
        _providers = providers;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Tick(forceRaise: false);

        smtc.TrackChanged += OnTrackChanged;
        smtc.PlaybackChanged += OnPlaybackChanged;
    }

    public LyricsPhase Phase { get; private set; } = LyricsPhase.NoSession;

    public TrackInfo? Track { get; private set; }

    public bool IsPlaying => _clock.IsPlaying;

    public string CurrentMain { get; private set; } = "";

    public string CurrentTrans { get; private set; } = "";

    /// <summary>true = 当前歌词为纯音乐（仅元信息行）。</summary>
    public bool IsInstrumental => _doc?.IsInstrumental == true;

    /// <summary>纯音乐元信息行已显示完毕（时间超过最后一行 5 秒）→ 切回标题。</summary>
    public bool InstrumentalEnded => _instrumentalEnded;

    /// <summary>当前行已唱字符数；-1 = 非逐字模式（整行高亮）。</summary>
    public int CurrentWordDone => _currentWordDone;

    /// <summary>任何展示相关状态变化（相位/当前行/播放状态）。UI 线程回调。</summary>
    public event Action? StateChanged;

    /// <summary>手动校正后强制重新解析当前曲目（绕过磁盘缓存）。</summary>
    public void RefreshCurrent()
    {
        var t = Track;
        if (t == null)
            return;
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;
        Phase = LyricsPhase.Resolving;
        _doc = null;
        _hasWords = false;
        _lineIndex = -1;
        _currentWordDone = -1;
        CurrentMain = "";
        CurrentTrans = "";
        UpdateTimer();
        Raise();
        _ = ResolveAndApplyAsync(t, force: true, cts.Token);
    }

    /// <summary>单曲偏移变更后即时生效（不重新拉取歌词）。</summary>
    public void NotifySongOffsetChanged()
    {
        var t = Track;
        if (t == null)
            return;
        _songOffsetMs = _overrides.Get(LyricsMatcher.TrackKey(t))?.OffsetMs ?? 0;
        Tick(forceRaise: true);
    }

    private void OnTrackChanged(TrackInfo? t)
    {
        _cts?.Cancel();
        _cts = null;
        Track = t;
        _doc = null;
        _hasWords = false;
        _lineIndex = -1;
        _songOffsetMs = 0;
        _currentWordDone = -1;
        CurrentMain = "";
        CurrentTrans = "";
        _clock.Reset();

        if (t == null)
        {
            Phase = LyricsPhase.NoSession;
            UpdateTimer();
            Raise();
            return;
        }

        Phase = LyricsPhase.Resolving;
        UpdateTimer();
        Raise();

        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = ResolveAndApplyAsync(t, force: false, cts.Token);
    }

    private void OnPlaybackChanged(PlaybackSnapshot snapshot)
    {
        var posBefore = _clock.Estimate();
        _clock.Sync(snapshot);
        UpdateTimer();
        // seek/时间轴跳变（>2s）时立即强制重定位当前行，不等下一次 timer tick
        var jump = _clock.Estimate() - posBefore;
        var seekJumped = jump > TimeSpan.FromSeconds(2) || jump < -TimeSpan.FromSeconds(2);
        Tick(forceRaise: seekJumped);
        Raise(); // IsPlaying 可能变化
    }

    private async Task ResolveAndApplyAsync(TrackInfo t, bool force, CancellationToken ct)
    {
        LyricDocument? doc = null;
        var songOffset = 0;
        try
        {
            (doc, songOffset) = await Task.Run(() => ResolveCoreAsync(t, force, ct), ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Error($"resolve failed: {t.Title}", ex);
        }

        if (ct.IsCancellationRequested)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (ct.IsCancellationRequested || !ReferenceEquals(Track, t))
                return;
            _doc = doc;
            _hasWords = doc?.Lines.Any(l => l.Words is { Count: > 0 }) == true;
            _songOffsetMs = songOffset;
            _lineIndex = -1;
            _currentWordDone = -1;
            CurrentMain = "";
            CurrentTrans = "";
            Phase = doc != null ? LyricsPhase.Ready : LyricsPhase.NoLyric;
            Log.Info($"lyric {(doc != null ? $"ready ({doc.Lines.Count} lines, words={_hasWords})" : "not found")}: {t.Title}");
            UpdateTimer();
            Tick(forceRaise: true);
            Raise();
        });
    }

    private async Task<(LyricDocument? Doc, int SongOffsetMs)> ResolveCoreAsync(TrackInfo t, bool force, CancellationToken ct)
    {
        var key = LyricsMatcher.TrackKey(t);
        var ov = _overrides.Get(key);
        var songOffset = ov?.OffsetMs ?? 0;

        // 1. 磁盘缓存
        if (!force)
        {
            var cached = await _cache.GetAsync(key).ConfigureAwait(false);
            if (cached != null)
            {
                var ovMismatch = ov?.SongId is { Length: > 0 }
                                 && (cached.SongId != ov.SongId || cached.Source != ov.Provider);
                if (!ovMismatch)
                {
                    if (cached.NotFound)
                    {
                        if (ov?.SongId is null && DateTimeOffset.UtcNow - cached.FetchedAt < NegativeCacheTtl)
                            return (null, songOffset);
                    }
                    else
                    {
                        var cachedDoc = ParseLyric(cached.Lrc, cached.TransLrc, cached.WordLrc);
                        if (cachedDoc != null)
                        {
                            // 缓存无逐字（上次 yrc 获取失败/无数据）→ 后台补拉 yrc，不阻塞歌词显示；
                            // 网易云 yrc 间歇性不返回，重试可能成功
                            if (string.IsNullOrEmpty(cached.WordLrc)
                                && !string.IsNullOrEmpty(cached.SongId)
                                && _providers.FirstOrDefault(p => p.Id == "netease") is { } yrcProvider)
                            {
                                RefreshYrcAsync(key, t, yrcProvider, cached.SongId, ct);
                            }
                            return (cachedDoc, songOffset);
                        }
                    }
                }
            }
        }

        // 2. 手动校正指定的歌词
        if (ov is { SongId.Length: > 0, Provider.Length: > 0 })
        {
            var provider = _providers.FirstOrDefault(p => p.Id == ov.Provider);
            if (provider != null)
            {
                var raw = await SafeGetLyricAsync(provider, ov.SongId, ct).ConfigureAwait(false);
                var doc = raw != null ? ParseLyric(raw.Lrc, raw.TranslationLrc, raw.WordLrc) : null;
                if (doc != null && raw != null)
                {
                    await _cache.SetAsync(key, CachedLyric.Positive(provider.Id, ov.SongId, raw)).ConfigureAwait(false);
                    return (doc, songOffset);
                }
            }
            // override 失效 → 继续走自动匹配
        }

        // 3. 自动搜索 + 打分
        var enabled = EnabledProviders();
        var candidates = new List<(ILyricsProvider Provider, ProviderSong Song, double Score)>();
        for (var i = 0; i < enabled.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var provider = enabled[i];
            IReadOnlyList<ProviderSong> found;
            try
            {
                found = await provider.SearchAsync(LyricsMatcher.BuildKeyword(t), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error($"search failed ({provider.Id})", ex);
                continue;
            }

            foreach (var song in found)
                candidates.Add((provider, song, LyricsMatcher.Score(t, song)));

            // 第一源已有高置信度命中，省掉第二源的请求
            if (i == 0 && candidates.Any(c => c.Score >= LyricsMatcher.EarlyAcceptThreshold))
                break;
        }

        foreach (var c in candidates
                     .Where(c => c.Score >= LyricsMatcher.AcceptThreshold)
                     .OrderByDescending(c => c.Score)
                     .Take(3))
        {
            ct.ThrowIfCancellationRequested();
            var raw = await SafeGetLyricAsync(c.Provider, c.Song.SongId, ct).ConfigureAwait(false);
            if (raw == null)
                continue;
            var doc = ParseLyric(raw.Lrc, raw.TranslationLrc, raw.WordLrc);
            if (doc == null)
                continue;

            await _cache.SetAsync(key, CachedLyric.Positive(c.Provider.Id, c.Song.SongId, raw)).ConfigureAwait(false);
            Log.Info($"matched [{c.Provider.Id}] {c.Song.Artist} - {c.Song.Title} score={c.Score:F2}");
            return (doc, songOffset);
        }

        await _cache.SetAsync(key, CachedLyric.Negative()).ConfigureAwait(false);
        return (null, songOffset);
    }

    /// <summary>
    /// 解析 LRC（可选逐字 klyric 合并）。纯音乐判定：排除元信息行后，无内容或全部为纯音乐占位字样。
    /// 纯音乐且存在元信息行（作词/作曲等）→ 保留元信息行短暂显示，之后切回标题；
    /// 否则返回 null（直接显示标题）。
    /// </summary>
    private static LyricDocument? ParseLyric(string? lrc, string? transLrc, string? wordLrc)
    {
        var doc = LrcParser.Parse(lrc, transLrc);
        if (doc == null)
            return null;

        if (!string.IsNullOrWhiteSpace(wordLrc))
            doc = MergeWordLines(doc, LrcParser.ParseWordLines(wordLrc));

        var texts = doc.Lines.Select(l => l.Text).Where(t => t.Length > 0).ToList();
        if (texts.Count == 0)
            return null;

        var content = texts.Where(t => !MetaInfoPattern().IsMatch(t)).ToList();
        if (content.Count > 0 && !content.All(t => InstrumentalPattern().IsMatch(t)))
            return doc; // 正常歌词

        // 纯音乐：保留元信息行供短暂显示
        var meta = doc.Lines
            .Where(l => l.Text.Length > 0 && MetaInfoPattern().IsMatch(l.Text))
            .ToList();
        return meta.Count > 0
            ? new LyricDocument(meta, isInstrumental: true)
            : null;
    }

    /// <summary>
    /// 逐字补充层合并：klyric 按时间（±800ms 内取最近）挂到主歌词行；
    /// 匹配率 ≥60% 才视为整首有效（否则整体回退整行高亮）。
    /// </summary>
    private static LyricDocument MergeWordLines(
        LyricDocument doc, IReadOnlyList<(long TimeMs, IReadOnlyList<LyricWord> Words)> wordLines)
    {
        if (wordLines.Count == 0)
            return doc;

        var lines = doc.Lines.ToList();
        var matched = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            long bestDelta = long.MaxValue;
            IReadOnlyList<LyricWord>? best = null;
            foreach (var wl in wordLines)
            {
                var d = Math.Abs(wl.TimeMs - l.TimeMs);
                if (d >= bestDelta)
                    continue;
                bestDelta = d;
                best = wl.Words;
            }
            if (best != null && bestDelta < 800)
            {
                lines[i] = l with { Words = best };
                matched++;
            }
        }
        return matched * 10 >= lines.Count * 6
            ? new LyricDocument(lines, doc.IsInstrumental)
            : doc;
    }

    /// <summary>
    /// 后台补拉逐字歌词：缓存命中但无 yrc 时异步重试（网易云接口不稳定）；
    /// 成功后替换内存文档启用逐字高亮，并更新磁盘缓存。失败静默（保持整行高亮）。
    /// </summary>
    private async void RefreshYrcAsync(string key, TrackInfo t, ILyricsProvider provider, string songId, CancellationToken ct)
    {
        LyricDocument? withWords = null;
        RawLyric? raw = null;
        try
        {
            raw = await SafeGetLyricAsync(provider, songId, ct).ConfigureAwait(false);
            if (raw?.WordLrc != null)
                withWords = ParseLyric(raw.Lrc, raw.TranslationLrc, raw.WordLrc);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            Log.Error($"yrc refresh failed: {t.Title}", ex);
            return;
        }

        if (ct.IsCancellationRequested || withWords == null || raw == null)
            return;

        var hasWords = withWords.Lines.Any(l => l.Words is { Count: > 0 });
        if (!hasWords)
            return;

        try { await _cache.SetAsync(key, CachedLyric.Positive(provider.Id, songId, raw)).ConfigureAwait(false); }
        catch (Exception ex) { Log.Error("cache update (yrc)", ex); }

        Dispatcher.UIThread.Post(() =>
        {
            if (ct.IsCancellationRequested || !ReferenceEquals(Track, t) || _hasWords)
                return;
            Log.Info($"yrc refreshed in background: {t.Title}");
            _doc = withWords;
            _hasWords = true;
            Tick(forceRaise: true);
            Raise();
        });
    }

    private static async Task<RawLyric?> SafeGetLyricAsync(ILyricsProvider provider, string songId, CancellationToken ct)
    {
        try
        {
            return await provider.GetLyricAsync(songId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error($"get lyric failed ({provider.Id}/{songId})", ex);
            return null;
        }
    }

    /// <summary>按设置的歌词源优先级顺序返回启用的 Provider。</summary>
    private List<ILyricsProvider> EnabledProviders()
    {
        var s = _settings.Current;
        var list = new List<ILyricsProvider>(s.LyricSources.Count);
        foreach (var r in s.LyricSources)
        {
            if (!r.Enabled)
                continue;
            var p = _providers.FirstOrDefault(x => x.Id == r.SourceId);
            if (p != null)
                list.Add(p);
        }
        return list;
    }

    /// <summary>逐字模式：设置开启且当前歌词含逐字数据。</summary>
    private bool WordMode => _settings.Current.WordByWord && _hasWords;

    private void UpdateTimer()
    {
        var shouldRun = Phase == LyricsPhase.Ready && _clock.IsPlaying;
        if (shouldRun && !_timer.IsEnabled)
        {
            _timer.Interval = WordMode ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromMilliseconds(100);
            _timer.Start();
        }
        else if (!shouldRun && _timer.IsEnabled)
        {
            _timer.Stop();
        }
    }

    private void Tick(bool forceRaise)
    {
        if (_doc == null)
            return;

        var pos = (long)_clock.Estimate().TotalMilliseconds + _settings.Current.GlobalOffsetMs + _songOffsetMs;
        var idx = LrcParser.FindIndex(_doc.Lines, pos);

        // 纯音乐：时间超过最后一行元信息 5 秒 → 结束显示（切回标题）
        var ended = _doc.IsInstrumental && _doc.Lines.Count > 0
                    && pos > _doc.Lines[^1].TimeMs + 5000;

        var done = WordMode && idx >= 0 ? CountDoneChars(_doc.Lines[idx], pos) : -1;

        if (idx == _lineIndex && ended == _instrumentalEnded && done == _currentWordDone && !forceRaise)
            return;

        _lineIndex = idx;
        _instrumentalEnded = ended;
        _currentWordDone = done;
        CurrentMain = ResolveMainText(idx);
        CurrentTrans = idx >= 0 ? _doc.Lines[idx].Translation ?? "" : "";
        Raise();
    }

    /// <summary>统计当前行已唱字符数（逐字时间戳 ≤ 播放位置的字符累计）；无逐字 → -1。</summary>
    private static int CountDoneChars(LyricLine line, long posMs)
    {
        var words = line.Words;
        if (words == null || words.Count == 0)
            return -1;
        var n = 0;
        foreach (var w in words)
        {
            if (w.TimeMs > posMs)
                break;
            n += w.Text.Length;
        }
        return n;
    }

    /// <summary>伴奏/间奏/前奏一律显示 "···"，始终保持有内容。</summary>
    private string ResolveMainText(int idx)
    {
        if (idx < 0)
            return "\u00B7\u00B7\u00B7"; // 前奏
        var text = _doc!.Lines[idx].Text;
        return text.Length > 0 ? text : "\u00B7\u00B7\u00B7";
    }

    private void Raise() => StateChanged?.Invoke();
}
