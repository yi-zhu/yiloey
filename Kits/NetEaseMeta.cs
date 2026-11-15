using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace holey.Kits;

/// <summary>
/// 参考 CloudMusicDownloader：.uc 缓存文件名以"歌曲ID-"开头，
/// 用网易云公开接口 api/song/detail 按 ID 批量（20个/次）补齐 歌手/歌名/专辑。
/// 结果按 ID 缓存（含未命中），避免重复请求。
/// </summary>
public static class NetEaseMeta
{
    private static readonly Dictionary<long, Dictionary<string, string>> cache = new();
    private static readonly object gate = new();
    private static bool tlsReady;

    /// <summary>从 "123456-歌名.uc"/"123456【...】.uc!" 等文件名解析歌曲ID；取首个 '-' 前的数字段</summary>
    public static bool TryGetSongId(string fileName, out long id)
    {
        id = 0;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(stem)) return false;
        int dash = stem.IndexOf('-');
        string head = (dash > 0 ? stem.Substring(0, dash) : stem).Trim();
        return head.Length > 0 && head.Length <= 15 && long.TryParse(head, out id) && id > 0;
    }

    /// <summary>批量查询，返回 歌曲ID -> {artist,title,album,date}；网络失败返回空表不抛异常</summary>
    public static Dictionary<long, Dictionary<string, string>> Fetch(IList<long> ids)
    {
        var result = new Dictionary<long, Dictionary<string, string>>();
        var toQuery = new List<long>();
        lock (gate)
        {
            foreach (long id in ids)
            {
                if (cache.TryGetValue(id, out var hit))
                {
                    if (hit != null) result[id] = hit;
                    continue;
                }
                if (!toQuery.Contains(id)) toQuery.Add(id);
            }
        }

        for (int start = 0; start < toQuery.Count; start += 20)
        {
            var batch = toQuery.GetBatch(start, 20);
            Dictionary<long, Dictionary<string, string>> fetched;
            try
            {
                fetched = QueryDetail(batch);
            }
            catch
            {
                fetched = null;
            }

            lock (gate)
            {
                foreach (long id in batch)
                {
                    var m = fetched != null && fetched.TryGetValue(id, out var mm) ? mm : null;
                    cache[id] = m;
                    if (m != null) result[id] = m;
                }
            }
        }

        return result;
    }

    private static Dictionary<long, Dictionary<string, string>> QueryDetail(List<long> batch)
    {
        EnsureTls();
        string idsJson = "[" + string.Join(",", batch) + "]";
        string url = "http://music.163.com/api/song/detail/?ids=" + Uri.EscapeDataString(idsJson);
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = "GET";
        req.Timeout = 8000;
        req.ReadWriteTimeout = 8000;
        req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
        req.Referer = "http://music.163.com/";
        req.Accept = "application/json";

        using var resp = req.GetResponse();
        using var stream = resp.GetResponseStream();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        byte[] body = ms.ToArray();
        // 该接口偶发返回 GBK；先按 UTF8 解，出现替换字符则退回系统 ANSI
        string text = Encoding.UTF8.GetString(body);
        if (text.Contains("\uFFFD"))
            text = Encoding.Default.GetString(body);

        return ParseSongs(text);
    }

    private static Dictionary<long, Dictionary<string, string>> ParseSongs(string json)
    {
        var map = new Dictionary<long, Dictionary<string, string>>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("songs", out var songs) ||
            songs.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var s in songs.EnumerateArray())
        {
            long id = s.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                ? idEl.GetInt64() : 0;
            if (id == 0) continue;

            var m = new Dictionary<string, string>();
            if (s.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                m["title"] = name.GetString().Trim();
            if (s.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
            {
                var names = new List<string>();
                foreach (var a in artists.EnumerateArray())
                    if (a.TryGetProperty("name", out var an) && an.ValueKind == JsonValueKind.String)
                        names.Add(an.GetString().Trim());
                if (names.Count > 0) m["artist"] = string.Join(" / ", names);
            }
            if (s.TryGetProperty("album", out var album) && album.ValueKind == JsonValueKind.Object)
            {
                if (album.TryGetProperty("name", out var albumName) && albumName.ValueKind == JsonValueKind.String)
                    m["album"] = albumName.GetString().Trim();
                string yearKey = album.TryGetProperty("year", out var albumYear) ? NumOrString(albumYear) : null;
                if (yearKey == null && album.TryGetProperty("publishTime", out var pt) &&
                    pt.ValueKind == JsonValueKind.Number && pt.GetInt64() > 0)
                    yearKey = DateTimeOffset.FromUnixTimeMilliseconds(pt.GetInt64()).DateTime.Year.ToString();
                if (!string.IsNullOrEmpty(yearKey)) m["date"] = yearKey;
            }
            if (m.Count > 0) map[id] = m;
        }

        return map;
    }

    private static string NumOrString(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String) return el.GetString();
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out long v) && v > 0) return v.ToString();
        return null;
    }

    private static void EnsureTls()
    {
        if (tlsReady) return;
        tlsReady = true;
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }
        catch
        {
        }
    }
}

internal static class ListBatchExt
{
    public static List<T> GetBatch<T>(this List<T> list, int start, int size)
    {
        var batch = new List<T>();
        for (int i = start; i < list.Count && i < start + size; i++) batch.Add(list[i]);
        return batch;
    }
}
