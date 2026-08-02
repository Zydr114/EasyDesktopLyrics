using System.Collections.ObjectModel;
using EasyDesktopLyrics.Infrastructure;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services;

namespace EasyDesktopLyrics.ViewModels;

/// <summary>
/// SettingsViewModel · 校正分区：当前曲目/手动搜索/应用/清除校正/单曲偏移/清缓存。
/// </summary>
public sealed partial class SettingsViewModel
{
    private string _searchKeyword = "";
    private bool _keywordDirty;
    private SearchResultItem? _selectedResult;
    private bool _isBusy;
    private string _statusText = "";
    private decimal? _songOffsetMs;

    public string CurrentTrackText
    {
        get
        {
            var t = _orchestrator.Track;
            if (t == null)
                return "（当前无播放会话）";
            var phase = _orchestrator.Phase switch
            {
                LyricsPhase.Ready => "已匹配歌词",
                LyricsPhase.NoLyric => "未找到歌词",
                LyricsPhase.Resolving => "正在匹配…",
                _ => "",
            };
            var head = string.IsNullOrEmpty(t.Artist) ? t.Title : $"{t.Artist} - {t.Title}";
            return $"{head}　[{phase}]";
        }
    }

    /// <summary>当前歌曲是否带逐字（卡拉 OK）歌词。</summary>
    public string CurrentWordDataText
    {
        get
        {
            if (_orchestrator.Phase != LyricsPhase.Ready)
                return "逐字歌词：—";
            return _orchestrator.HasWordData
                ? "逐字歌词：有（卡拉 OK 逐字高亮）"
                : "逐字歌词：无（整行高亮）";
        }
    }

    public string SearchKeyword
    {
        get => _searchKeyword;
        set
        {
            if (Set(ref _searchKeyword, value))
                _keywordDirty = true;
        }
    }

    public ObservableCollection<SearchResultItem> SearchResults { get; } = [];

    public SearchResultItem? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (Set(ref _selectedResult, value))
                ApplyResultCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSearchResults => SearchResults.Count > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
                SearchCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public decimal? SongOffsetMs
    {
        get => _songOffsetMs;
        set => Set(ref _songOffsetMs, value);
    }

    public RelayCommand SearchCommand { get; }

    public RelayCommand ApplyResultCommand { get; }

    public RelayCommand ClearOverrideCommand { get; }

    public RelayCommand ApplySongOffsetCommand { get; }

    public RelayCommand ClearCacheCommand { get; }

    private void OnOrchestratorChanged()
    {
        Raise(nameof(CurrentTrackText));
        Raise(nameof(CurrentWordDataText));
        SyncFromTrack(force: false);
    }

    private void SyncFromTrack(bool force)
    {
        var t = _orchestrator.Track;
        if (!force && Equals(t, _lastTrack))
            return;
        _lastTrack = t;

        ApplyResultCommand.RaiseCanExecuteChanged();
        ClearOverrideCommand.RaiseCanExecuteChanged();
        ApplySongOffsetCommand.RaiseCanExecuteChanged();

        if (t != null)
        {
            if (!_keywordDirty || string.IsNullOrWhiteSpace(_searchKeyword))
            {
                _searchKeyword = LyricsMatcher.BuildKeyword(t);
                _keywordDirty = false;
                Raise(nameof(SearchKeyword));
            }
            SongOffsetMs = _overrides.Get(LyricsMatcher.TrackKey(t))?.OffsetMs ?? 0;
        }
    }

    private async Task RunSearchAsync()
    {
        var keyword = _searchKeyword.Trim();
        if (keyword.Length == 0)
        {
            StatusText = "请输入搜索关键词。";
            return;
        }

        IsBusy = true;
        StatusText = "搜索中…";
        SearchResults.Clear();
        Raise(nameof(HasSearchResults));
        try
        {
            var track = _orchestrator.Track;
            var items = new List<SearchResultItem>();
            foreach (var provider in _providers)
            {
                try
                {
                    var found = await provider.SearchAsync(keyword, CancellationToken.None);
                    items.AddRange(found.Select(s => new SearchResultItem(
                        provider.Id, provider.DisplayName, s.SongId, s.Title, s.Artist, s.DurationMs,
                        track != null ? LyricsMatcher.Score(track, s) : 0)));
                }
                catch (Exception ex)
                {
                    Log.Error($"manual search ({provider.Id})", ex);
                }
            }

            foreach (var item in items.OrderByDescending(i => i.Score))
                SearchResults.Add(item);

            Raise(nameof(HasSearchResults));
            StatusText = SearchResults.Count == 0 ? "未搜索到结果。" : $"共 {SearchResults.Count} 条结果，选中后点击“应用所选歌词”。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplySelectedResult()
    {
        var r = _selectedResult;
        var t = _orchestrator.Track;
        if (r == null || t == null)
            return;

        var key = LyricsMatcher.TrackKey(t);
        var existing = _overrides.Get(key);
        _overrides.Set(key, new LyricOverride
        {
            Provider = r.ProviderId,
            SongId = r.SongId,
            OffsetMs = existing?.OffsetMs ?? 0,
        });
        _orchestrator.RefreshCurrent();
        StatusText = $"已应用 [{r.ProviderName}] {r.Title}，本曲将始终使用该歌词。";
    }

    private void ClearOverride()
    {
        var t = _orchestrator.Track;
        if (t == null)
            return;
        _overrides.Remove(LyricsMatcher.TrackKey(t));
        SongOffsetMs = 0;
        _orchestrator.RefreshCurrent();
        StatusText = "已清除本曲校正，恢复自动匹配。";
    }

    private void ApplySongOffset()
    {
        var t = _orchestrator.Track;
        if (t == null)
            return;

        var key = LyricsMatcher.TrackKey(t);
        var offset = (int)(_songOffsetMs ?? 0);
        var existing = _overrides.Get(key);

        if (offset == 0 && existing?.SongId is null or { Length: 0 })
        {
            _overrides.Remove(key);
        }
        else
        {
            _overrides.Set(key, new LyricOverride
            {
                Provider = existing?.Provider,
                SongId = existing?.SongId,
                OffsetMs = offset,
            });
        }
        _orchestrator.NotifySongOffsetChanged();
        StatusText = $"本曲偏移已设为 {offset} ms（正值 = 歌词提前）。";
    }

    private void ClearCache()
    {
        _cache.Clear();
        StatusText = "歌词缓存已清空。";
    }
}
