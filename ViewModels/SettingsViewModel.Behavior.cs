using System.Collections.ObjectModel;
using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.ViewModels;

/// <summary>
/// SettingsViewModel · 播放分区：行为开关/播放器优先级/歌词源/封面参数。
/// </summary>
public sealed partial class SettingsViewModel
{
    // ---------- 行为 ----------
    public bool ShowTranslation
    {
        get => _settings.Current.ShowTranslation;
        set
        {
            if (value == _settings.Current.ShowTranslation) return;
            _settings.Update(s => s.ShowTranslation = value);
        }
    }

    public bool WordByWord
    {
        get => _settings.Current.WordByWord;
        set
        {
            if (value == _settings.Current.WordByWord) return;
            _settings.Update(s => s.WordByWord = value);
        }
    }

    public IReadOnlyList<string> AlignmentOptions { get; } = ["居中", "左对齐", "右对齐"];

    private static readonly string[] AlignmentValues = ["Center", "Left", "Right"];

    public int AlignmentIndex
    {
        get => Math.Clamp(Array.IndexOf(AlignmentValues, _settings.Current.Alignment), 0, 2);
        set
        {
            if (value < 0 || value > 2 || AlignmentValues[value] == _settings.Current.Alignment) return;
            _settings.Update(s => s.Alignment = AlignmentValues[value]);
        }
    }

    public bool HideWhenPaused
    {
        get => _settings.Current.HideWhenPaused;
        set
        {
            if (value == _settings.Current.HideWhenPaused) return;
            _settings.Update(s => s.HideWhenPaused = value);
        }
    }

    public bool ShowTitleWhenNoLyric
    {
        get => _settings.Current.ShowTitleWhenNoLyric;
        set
        {
            if (value == _settings.Current.ShowTitleWhenNoLyric) return;
            _settings.Update(s => s.ShowTitleWhenNoLyric = value);
        }
    }

    public bool AutoStartEnabled
    {
        get => _settings.Current.AutoStart;
        set
        {
            if (value == _settings.Current.AutoStart) return;
            _settings.Update(s => s.AutoStart = value);
        }
    }

    /// <summary>窗口锁定：完全不可动且鼠标穿透；锁定后仅能通过托盘菜单或本设置解锁。</summary>
    public bool IsLocked
    {
        get => _settings.Current.Locked;
        set
        {
            if (value == _settings.Current.Locked) return;
            _settings.Update(s => s.Locked = value);
        }
    }

    // ---------- 播放器优先级 ----------
    public ObservableCollection<PlayerRuleItem> PlayerRules { get; } = [];

    public RelayCommand RefreshSessionsCommand { get; }

    /// <summary>拖拽排序：移动列表中 from → to。</summary>
    public void MovePlayerRule(int from, int to)
    {
        if (from < 0 || from >= PlayerRules.Count || to < 0 || to >= PlayerRules.Count || from == to)
            return;
        PlayerRules.Move(from, to);
        SavePlayerRules();
    }

    /// <summary>将当前列表持久化为设置（列表顺序 = 优先级）。</summary>
    private void SavePlayerRules()
    {
        _settings.Update(s =>
        {
            s.PlayerPriorities = PlayerRules
                .Select(r => new PlayerPriority { Aumid = r.Aumid, Enabled = r.IsEnabled })
                .ToList();
        });
    }

    /// <summary>合并持久化规则与当前活动会话，重建列表（顺序 = 优先级）。</summary>
    private void RefreshPlayerRules()
    {
        var displayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (aumid, display) in _smtc.GetSessions())
            displayNames[aumid] = display;

        var items = new List<PlayerRuleItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in _settings.Current.PlayerPriorities)
        {
            if (string.IsNullOrEmpty(p.Aumid) || !seen.Add(p.Aumid))
                continue;
            items.Add(new PlayerRuleItem(p.Aumid, displayNames.GetValueOrDefault(p.Aumid) ?? p.Aumid, p.Enabled));
        }

        foreach (var (aumid, display) in _smtc.GetSessions())
        {
            if (!seen.Add(aumid))
                continue;
            items.Add(new PlayerRuleItem(aumid, display, true));
        }

        foreach (var item in items)
            item.PropertyChanged += OnRuleItemChanged;

        PlayerRules.Clear();
        foreach (var item in items)
            PlayerRules.Add(item);
    }

    private void OnRuleItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerRuleItem.IsEnabled))
            SavePlayerRules();
    }

    // ---------- 歌词源 ----------
    public ObservableCollection<LyricSourceItem> LyricSources { get; } = [];

    public string LyricsFolder
    {
        get => _settings.Current.LyricsFolder;
        set
        {
            if (value == _settings.Current.LyricsFolder) return;
            _settings.Update(s => s.LyricsFolder = value);
        }
    }

    public decimal? GlobalOffsetMs
    {
        get => _settings.Current.GlobalOffsetMs;
        set
        {
            var v = (int)(value ?? 0);
            if (v == _settings.Current.GlobalOffsetMs) return;
            _settings.Update(s => s.GlobalOffsetMs = v);
        }
    }

    // ---------- 封面 ----------
    public bool CoverEnabled
    {
        get => _settings.Current.CoverEnabled;
        set
        {
            if (value == _settings.Current.CoverEnabled) return;
            _settings.Update(s => s.CoverEnabled = value);
        }
    }

    public bool CoverCutAnimation
    {
        get => _settings.Current.CoverCutAnimation;
        set
        {
            if (value == _settings.Current.CoverCutAnimation) return;
            _settings.Update(s => s.CoverCutAnimation = value);
        }
    }

    /// <summary>窗口高度扩展方向选项：索引 0=向下、1=向上、2=上下均扩展。</summary>
    public IReadOnlyList<string> HeightGrowOptions { get; } = ["向下扩展", "向上扩展", "上下均扩展"];

    public int HeightGrowIndex
    {
        get => _settings.Current.HeightGrowMode switch { 1 => 0, 2 => 1, _ => 2 };
        set => _settings.Update(s => s.HeightGrowMode = value switch { 0 => 1, 1 => 2, _ => 0 });
    }

    public double CoverSizePctVal
    {
        get => _settings.Current.CoverSizePct;
        set
        {
            var v = Math.Round(value);
            if (Math.Abs(v - _settings.Current.CoverSizePct) < 1) return;
            _settings.Update(s => s.CoverSizePct = v);
        }
    }

    /// <summary>切歌封面最短显示时长（秒）。</summary>
    public double CoverAnimMinSec
    {
        get => _settings.Current.CoverAnimMinMs / 1000.0;
        set
        {
            var ms = (int)Math.Round(value * 1000);
            if (ms == _settings.Current.CoverAnimMinMs) return;
            _settings.Update(s => s.CoverAnimMinMs = ms);
        }
    }

    /// <summary>切歌封面最长显示时长（秒）。</summary>
    public double CoverAnimMaxSec
    {
        get => _settings.Current.CoverAnimMaxMs / 1000.0;
        set
        {
            var ms = (int)Math.Round(value * 1000);
            if (ms == _settings.Current.CoverAnimMaxMs) return;
            _settings.Update(s => s.CoverAnimMaxMs = ms);
        }
    }

    /// <summary>拖拽排序：移动列表中 from → to。</summary>
    public void MoveLyricSource(int from, int to)
    {
        if (from < 0 || from >= LyricSources.Count || to < 0 || to >= LyricSources.Count || from == to)
            return;
        LyricSources.Move(from, to);
        SaveLyricSources();
    }

    private void SaveLyricSources()
    {
        _settings.Update(s =>
        {
            s.LyricSources = LyricSources
                .Select(r => new LyricSourceRule { SourceId = r.SourceId, Enabled = r.IsEnabled })
                .ToList();
        });
    }

    /// <summary>从设置重建歌词源列表（固定 5 源，顺序 = 优先级）。</summary>
    private void RefreshLyricSources()
    {
        var items = new List<LyricSourceItem>();
        foreach (var r in _settings.Current.LyricSources)
        {
            if (!SourceNames.TryGetValue(r.SourceId, out var name))
                continue;
            items.Add(new LyricSourceItem(r.SourceId, name, r.Enabled));
        }
        foreach (var item in items)
            item.PropertyChanged += OnSourceItemChanged;

        LyricSources.Clear();
        foreach (var item in items)
            LyricSources.Add(item);
    }

    private void OnSourceItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LyricSourceItem.IsEnabled))
            SaveLyricSources();
    }

    private static readonly Dictionary<string, string> SourceNames = new()
    {
        ["netease"] = "网易云音乐",
        ["qq"] = "QQ 音乐",
        ["lrclib"] = "LRCLIB",
        ["local"] = "本地歌词文件",
    };
}
