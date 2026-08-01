using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyDesktopLyrics.Infrastructure;

namespace EasyDesktopLyrics.Services;

/// <summary>
/// 网易云 weapi 加密请求（与 NeteaseCloudMusicApi 的 util/crypto.js 一致）：
/// 双层 AES-128-CBC（presetKey → 随机 base62 密钥）+ raw RSA 加密密钥（无填充、密钥反转）。
/// 仅用于获取匿名接口拿不到的数据（如逐字歌词 yrc）。
/// </summary>
internal static class NeteaseWeapi
{
    private const string PresetKey = "0CoJUm6Qyw8W8jud";
    private static readonly byte[] Iv = Encoding.UTF8.GetBytes("0102030405060708");
    private const string ModulusHex =
        "00e0b509f6259df8642dbc35662901477df22677ec152b5ff68ace615bb7b725152b3ab17a876aea8a5aa76d2e417629ec4ee341f56135fccf695280104e0312ecbda92557c93870114af6c9d05c4f7f0c3685b7a46bee255932575cce10b424d813cfe4875d3e82047b97ddef52741d546b8e289dc6935b3ece0462db0a22b8e7";
    private const string Base62 = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    private static readonly Random Random = new();

    /// <summary>生成 weapi 请求体（params + encSecKey）。</summary>
    public static (string Params, string EncSecKey) Encode(string jsonBody)
    {
        var secretKey = new char[16];
        lock (Random)
        {
            for (var i = 0; i < 16; i++)
                secretKey[i] = Base62[Random.Next(62)];
        }
        var key = new string(secretKey);

        var first = AesEncrypt(jsonBody, PresetKey);
        var second = AesEncrypt(first, key);
        var reversed = new string(key.Reverse().ToArray());
        return (second, RsaRawEncrypt(reversed));
    }

    /// <summary>POST /weapi/* 并解析 JSON；失败返回 null（静默降级）。</summary>
    public static async Task<JsonDocument?> PostWeapiJsonAsync(string path, string jsonBody, CancellationToken ct)
    {
        try
        {
            var (paramsValue, encSecKey) = Encode(jsonBody);
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://music.163.com" + path)
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("params", paramsValue),
                    new KeyValuePair<string, string>("encSecKey", encSecKey),
                }),
            };
            req.Headers.Referrer = new Uri("https://music.163.com/");
            req.Headers.TryAddWithoutValidation("Origin", "https://music.163.com");
            req.Headers.TryAddWithoutValidation("Cookie", "os=pc; appver=2.9.7");

            using var resp = await HttpHelper.Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct)
                .ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Info($"http {(int)resp.StatusCode} POST {path}");
                return null;
            }
            var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
                return null;
            return JsonDocument.Parse(text);
        }
        catch (Exception ex)
        {
            Log.Error("weapi", ex);
            return null;
        }
    }

    private static string AesEncrypt(string text, string key)
    {
        using var aes = Aes.Create();
        aes.Key = Encoding.UTF8.GetBytes(key);
        aes.IV = Iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var encryptor = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToBase64String(encryptor.TransformFinalBlock(bytes, 0, bytes.Length));
    }

    /// <summary>raw RSA（无填充）：大端无符号输入，结果 128 字节补零 hex（与 forge 'NONE' 一致）。</summary>
    private static string RsaRawEncrypt(string text)
    {
        var modulus = BigInteger.Parse(ModulusHex, System.Globalization.NumberStyles.HexNumber);
        var input = new BigInteger(Encoding.UTF8.GetBytes(text), isUnsigned: true, isBigEndian: true);
        var result = BigInteger.ModPow(input, 0x10001, modulus);
        return result.ToString("x").PadLeft(256, '0');
    }
}
