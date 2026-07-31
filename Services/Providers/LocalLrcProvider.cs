using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Services.Providers;

/// <summary>
/// 本地 LRC 文件源：扫描设置目录下的 *.lrc 文件（一层），
/// 文件名按 "艺术家 - 标题.lrc" 解析，匹配打分复用 LyricsMatcher。
/// 修改本地歌词文件后需清空缓存（清空歌词缓存按钮）。
/// </summary>
public sealed class LocalLrcProvider : ILyricsProvider
{
    private readonly SettingsService _settings;

    public LocalLrcProvider(SettingsService settings) => _settings = settings;

    public string Id => "local";

    public string DisplayName => "本地歌词文件";

    public Task<IReadOnlyList<ProviderSong>> SearchAsync(string keyword, CancellationToken ct)
    {
        var list = new List<ProviderSong>();
        var folder = _settings.Current.LyricsFolder;
        if (string.IsNullOrWhiteSpace(folder))
            return Task.FromResult<IReadOnlyList<ProviderSong>>(list);

        foreach (var dir in folder.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.lrc", SearchOption.TopDirectoryOnly))
            {
                if (ct.IsCancellationRequested)
                    return Task.FromResult<IReadOnlyList<ProviderSong>>(list);
                var (artist, title) = SplitName(Path.GetFileNameWithoutExtension(file));
                if (title.Length == 0)
                    continue;
                list.Add(new ProviderSong(Id, file, title, artist, "", 0));
            }
        }
        return Task.FromResult<IReadOnlyList<ProviderSong>>(list);
    }

    public Task<RawLyric?> GetLyricAsync(string songId, CancellationToken ct)
    {
        try
        {
            return Task.FromResult<RawLyric?>(new RawLyric(File.ReadAllText(songId), null));
        }
        catch (Exception)
        {
            return Task.FromResult<RawLyric?>(null);
        }
    }

    /// <summary>解析 "艺术家 - 标题"（兼容全角连字符）。</summary>
    private static (string Artist, string Title) SplitName(string name)
    {
        var idx = name.IndexOf(" - ", StringComparison.Ordinal);
        if (idx <= 0 || idx >= name.Length - 3)
        {
            var full = name.IndexOf("－", StringComparison.Ordinal);
            if (full <= 0 || full >= name.Length - 1)
                return ("", name);
            return (name[..full].Trim(), name[(full + 1)..].Trim());
        }
        return (name[..idx].Trim(), name[(idx + 3)..].Trim());
    }
}
