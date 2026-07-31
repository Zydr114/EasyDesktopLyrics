using System.Runtime.InteropServices;
using System.Text;
using EasyDesktopLyrics.Models;
using EasyDesktopLyrics.Services;
using EasyDesktopLyrics.Services.Providers;

namespace EasyDesktopLyrics;

/// <summary>隐藏诊断入口（--probe / --probe-smtc），在 WinExe 下强制接回控制台。</summary>
internal static class DebugProbes
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    private const uint ATTACH_PARENT_PROCESS = unchecked((uint)-1);

    public static int Run(string[] args)
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
        }
        catch
        {
            // 无控制台也不抛
        }

        try
        {
            if (args[0] == "--probe")
                return RunProbe(args).GetAwaiter().GetResult();
            if (args[0] == "--probe-smtc")
                return RunProbeSmtc().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"fatal: {ex}");
            return 2;
        }

        return 0;
    }

    private static async Task<int> RunProbe(string[] args)
    {
        string title = "晴天";
        string artist = "";

        if (args.Length > 1) title = args[1];
        if (args.Length > 2) artist = args[2];

        Console.WriteLine($"EasyDesktopLyrics 歌词 probe");
        Console.WriteLine($"  艺术家: {(artist.Length > 0 ? artist : "(无)")}");
        Console.WriteLine($"  标题:   {title}");
        Console.WriteLine();

        var providers = new ILyricsProvider[]
        {
            new NeteaseLyricsProvider(),
            new QQMusicLyricsProvider(),
            new LrclibLyricsProvider(),
            new LocalLrcProvider(new SettingsService()),
        };
        var matcher = LyricsMatcher.Normalize;

        foreach (var provider in providers)
        {
            Console.WriteLine($"── {provider.DisplayName} ──");

            List<ProviderSong> results;
            try
            {
                results = (await provider.SearchAsync(title + " " + artist, CancellationToken.None)).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [搜索失败] {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            Console.WriteLine($"  搜索关键词: \"{title} {artist}\"");
            if (results.Count == 0)
            {
                Console.WriteLine("  (无结果)");
                continue;
            }

            var t = new TrackInfo(title, artist, "", 0, "");
            foreach (var song in results)
            {
                var score = LyricsMatcher.Score(t, song);
                var dur = song.DurationMs > 0 ? $"{(int)TimeSpan.FromMilliseconds(song.DurationMs).TotalMinutes}:{TimeSpan.FromMilliseconds(song.DurationMs).Seconds:D2}" : "??:??";
                Console.WriteLine($"  [{score:F2}] {song.Artist} — {song.Title} [{dur}] #{song.SongId}");
            }

            // 取最高分拉歌词
            var best = results.MaxBy(s => LyricsMatcher.Score(t, s));
            if (best != null)
            {
                Console.WriteLine($"  → 取最高分 [{LyricsMatcher.Score(t, best):F2}] 拉歌词…");
                try
                {
                    var raw = await provider.GetLyricAsync(best.SongId, CancellationToken.None);
                    if (raw != null)
                    {
                        var doc = LrcParser.Parse(raw.Lrc, raw.TranslationLrc);
                        if (doc != null)
                        {
                            Console.WriteLine($"  √ 共 {doc.Lines.Count} 行");
                            foreach (var line in doc.Lines.Take(8))
                                Console.WriteLine($"    [{TimeSpan.FromMilliseconds(line.TimeMs):mm\\:ss\\.fff}] {line.Text}{(line.Translation != null ? " / " + line.Translation : "")}");
                            if (doc.Lines.Count > 8)
                                Console.WriteLine($"    … (余 {doc.Lines.Count - 8} 行)");
                        }
                        else
                        {
                            Console.WriteLine("  × 解析失败");
                        }
                    }
                    else
                    {
                        Console.WriteLine("  × 无歌词/请求失败");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  × 异常: {ex.GetType().Name}: {ex.Message}");
                }
            }
            Console.WriteLine();
        }

        Console.WriteLine("done.");
        return 0;
    }

    private static async Task<int> RunProbeSmtc()
    {
        Console.WriteLine("EasyDesktopLyrics SMTC probe");
        try
        {
            var manager = await Windows.Media.Control
                .GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            Console.WriteLine($"当前系统有 {manager.GetSessions().Count} 个 SMTC 会话");

            for (var i = 0; i < 5; i++)
            {
                var session = manager.GetCurrentSession();
                if (session == null)
                {
                    Console.WriteLine($"[{i}] 无当前会话");
                }
                else
                {
                    var props = await session.TryGetMediaPropertiesAsync();
                    var tl = session.GetTimelineProperties();
                    var pb = session.GetPlaybackInfo();

                    Console.WriteLine($"[{i}] {props?.Artist} — {props?.Title}");
                    Console.WriteLine($"      AUMID: {session.SourceAppUserModelId}");
                    Console.WriteLine($"      Status: {pb.PlaybackStatus}, Rate: {pb.PlaybackRate}");
                    Console.WriteLine($"      Pos: {tl.Position:hh\\:mm\\:ss\\.fff} / {tl.EndTime:hh\\:mm\\:ss\\.fff}");
                    Console.WriteLine($"      LastUpdated: {tl.LastUpdatedTime.LocalDateTime:HH:mm:ss.fff}");
                }
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex}");
            return 1;
        }

        Console.WriteLine("done.");
        return 0;
    }
}
