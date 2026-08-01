using System.Text.Json;
using EasyDesktopLyrics.Models;

namespace EasyDesktopLyrics.Services.Providers;

/// <summary>
/// 网易云音乐（非官方公开接口）：
/// 搜索 GET  music.163.com/api/search/get/web（备用 POST /api/cloudsearch/pc）
/// 歌词 GET  music.163.com/api/song/lyric?os=pc&amp;id={id}&amp;lv=-1&amp;tv=-1
/// </summary>
public sealed class NeteaseLyricsProvider : ILyricsProvider
{
    private const string Referer = "https://music.163.com";

    public string Id => "netease";

    public string DisplayName => "网易云音乐";

    public async Task<IReadOnlyList<ProviderSong>> SearchAsync(string keyword, CancellationToken ct)
    {
        var list = new List<ProviderSong>();

        var url = $"https://music.163.com/api/search/get/web?s={Uri.EscapeDataString(keyword)}&type=1&offset=0&limit=10";
        using (var doc = await HttpHelper.GetJsonAsync(url, Referer, ct).ConfigureAwait(false))
        {
            if (doc != null)
                ParseSongs(doc.RootElement.Get("result").Get("songs"), list);
        }

        if (list.Count == 0)
        {
            // 备用端点（响应字段名不同：ar/al/dt）
            using var doc = await HttpHelper.PostFormJsonAsync(
                "https://music.163.com/api/cloudsearch/pc", Referer,
                new Dictionary<string, string> { ["s"] = keyword, ["type"] = "1", ["limit"] = "10", ["offset"] = "0" },
                ct).ConfigureAwait(false);
            if (doc != null)
                ParseSongs(doc.RootElement.Get("result").Get("songs"), list);
        }

        return list;
    }

    public async Task<RawLyric?> GetLyricAsync(string songId, CancellationToken ct)
    {
        // kv=-1 请求 klyric（旧版逐字）；实际逐字歌词走 weapi /lyric/v1 的 yrc 字段
        var url = $"https://music.163.com/api/song/lyric?os=pc&id={songId}&lv=-1&kv=-1&tv=-1";
        using var doc = await HttpHelper.GetJsonAsync(url, Referer, ct).ConfigureAwait(false);
        if (doc == null)
            return null;

        var root = doc.RootElement;
        if (root.Get("nolyric").GetBool() || root.Get("uncollected").GetBool())
            return null; // 纯音乐 / 未收录

        var lrc = root.Get("lrc").Get("lyric").GetStr();
        if (string.IsNullOrWhiteSpace(lrc))
            return null;

        var trans = root.Get("tlyric").Get("lyric").GetStr();
        // 逐字歌词（yrc）：匿名明文接口拿不到，需 weapi 加密 POST /lyric/v1（yv=0）
        var wordLrc = await GetYrcAsync(songId, ct).ConfigureAwait(false);
        return new RawLyric(lrc,
            string.IsNullOrWhiteSpace(trans) ? null : trans,
            string.IsNullOrWhiteSpace(wordLrc) ? null : wordLrc);
    }

    /// <summary>
    /// 逐字歌词（yrc，新格式）：weapi 匿名可获取；失败/无数据返回 null（不影响主歌词）。
    /// </summary>
    private static async Task<string?> GetYrcAsync(string songId, CancellationToken ct)
    {
        var json = $"{{\"id\":\"{songId}\",\"cp\":false,\"tv\":0,\"lv\":0,\"rv\":0,\"kv\":0,\"yv\":0,\"ytv\":0,\"yrv\":0}}";
        using var doc = await NeteaseWeapi.PostWeapiJsonAsync("/weapi/song/lyric/v1", json, ct).ConfigureAwait(false);
        var yrc = doc?.RootElement.Get("yrc").Get("lyric").GetStr();
        if (string.IsNullOrWhiteSpace(yrc))
            return null;
        // 仅当存在逐字行（[t,d](t,d,0)字）时有效；纯元数据 JSON 行视为无逐字
        return yrc.Contains("[") ? yrc : null;
    }

    private void ParseSongs(JsonElement? songs, List<ProviderSong> list)
    {
        foreach (var s in songs.Items())
        {
            var id = s.Get("id").GetLong();
            if (id <= 0)
                continue;

            var name = s.Get("name").GetStr();
            var artists = (s.Get("artists") ?? s.Get("ar")).Items()
                .Select(a => a.Get("name").GetStr())
                .Where(n => n.Length > 0);
            var duration = s.Get("duration").GetLong();
            if (duration <= 0)
                duration = s.Get("dt").GetLong();
            var album = (s.Get("album") ?? s.Get("al")).Get("name").GetStr();

            list.Add(new ProviderSong(Id, id.ToString(), name, string.Join("/", artists), album, duration));
        }
    }
}
