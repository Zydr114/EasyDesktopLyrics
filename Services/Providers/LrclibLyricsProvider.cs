using System.Text.Json;
using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Services.Providers;

/// <summary>
/// LRCLIB（开放社区歌词库，lrclib.net，免费无鉴权）：
/// 搜索 GET  /api/search?q=...
/// 歌词 GET  /api/get/{id}（syncedLyrics = LRC，plainLyrics = 无时间戳文本）
/// 对欧美/日韩歌曲覆盖好，与中文源互补。
/// </summary>
public sealed class LrclibLyricsProvider : ILyricsProvider
{
    public string Id => "lrclib";

    public string DisplayName => "LRCLIB";

    public async Task<IReadOnlyList<ProviderSong>> SearchAsync(string keyword, CancellationToken ct)
    {
        var list = new List<ProviderSong>();
        var url = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(keyword)}";
        using var doc = await HttpHelper.GetJsonAsync(url, null, ct).ConfigureAwait(false);
        if (doc?.RootElement is { ValueKind: JsonValueKind.Array } arr)
        {
            foreach (var s in arr.EnumerateArray())
            {
                var id = s.Get("id").GetLong();
                if (id <= 0)
                    continue;

                var duration = s.Get("duration").GetLong();
                if (duration > 0)
                    duration *= 1000;

                list.Add(new ProviderSong(Id, id.ToString(),
                    s.Get("trackName").GetStr(),
                    s.Get("artistName").GetStr(),
                    s.Get("albumName").GetStr(), duration));
            }
        }
        return list;
    }

    public async Task<RawLyric?> GetLyricAsync(string songId, CancellationToken ct)
    {
        var url = $"https://lrclib.net/api/get/{Uri.EscapeDataString(songId)}";
        using var doc = await HttpHelper.GetJsonAsync(url, null, ct).ConfigureAwait(false);
        if (doc == null)
            return null;

        var lrc = doc.RootElement.Get("syncedLyrics").GetStr();
        if (lrc.Length < 10)
        {
            // 无同步行级时间戳 → 视为无可用歌词
            lrc = doc.RootElement.Get("plainLyrics").GetStr();
            if (lrc.Length < 10)
                return null;
            return new RawLyric(lrc, null);
        }
        return new RawLyric(lrc, null);
    }
}
