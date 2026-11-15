using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace holey.Kits;

/// <summary>
/// mp3 标签信息（歌手/歌曲/流派），读取顺序：ID3v2 -> ID3v1 -> 文件名/目录名兜底推断
/// </summary>
public class Mp3TagInfo
{
    public string Artist = "";
    public string Title = "";
    public string Genre = "";
    public string Album = "";
}

/// <summary>
/// 轻量 mp3 标签读取：解析 ID3v2 帧（TPE1/TIT2/TCON）与 ID3v1，缺失字段用文件名拆分、
/// 父目录名（流派关键词）兜底，供批量命名使用
/// </summary>
public static class Mp3TagReader
{
    // net462 无 Encoding.Latin1，ID3 latin1 文本用 cp1252 解码（高位字符更常见）
    private static readonly Encoding Latin1 = Encoding.GetEncoding(1252);

    // ID3v1 流派表（WinAmp 列表），TCON "(13)" 或纯数字索引时翻译用
    private static readonly string[] Genres =
    {
        "Blues","Classic Rock","Country","Dance","Disco","Funk","Grunge","Hip-Hop","Jazz","Metal",
        "New Age","Oldies","Other","Pop","R&B","Rap","Reggae","Rock","Techno","Industrial",
        "Alternative","Ska","Death Metal","Pranks","Soundtrack","Euro-Techno","Ambient","Trip-Hop",
        "Vocal","Jazz+Funk","Fusion","Live","Commercial","Goth","Darkwave","Techno-Industrial",
        "Electronic","Pop-Folk","Eurodance","Dream","Southern Rock","Comedy","Cult","Gangsta",
        "Top 40","Christian Rap","Pop/Funk","Jungle","Native American","Cabaret","New Wave",
        "Psychadelic","Rave","Goa","Drum & Bass","Club","House","Game","Sound Clip","Gothic Rock",
        "Progressive Rock","Psychedelic Rock","Symphonic Rock","Slow Rock","Big Band","Chorus",
        "Easy Listening","Acoustic","Humour","Speech","Funky","Indie","BritPop","Negerpunk",
        "Punk","Modern Rock","Conceptual","Ethnic","Gothic","Darkwave","Techno","Industrial"
    };

    // 目录名中出现这些关键词时视为流派
    private static readonly string[] GenreKeywords =
    {
        "pop","rock","jazz","hip","rap","metal","electronic","edm","classical","古典","流行",
        "摇滚","电子","民谣","乡村","蓝调","舞曲","说唱","嘻哈","metal","country","folk","blues",
        "dance","techno","house","ambient","opera","soundtrack","原声","纯音乐","轻音乐"
    };

    public static Mp3TagInfo Read(string file)
    {
        var info = new Mp3TagInfo();
        try
        {
            var tags = ReadId3v2(file);
            if (tags != null)
            {
                tags.TryGetValue("TPE1", out string artist);
                tags.TryGetValue("TIT2", out string title);
                tags.TryGetValue("TCON", out string genre);
                tags.TryGetValue("TALB", out string album);
                info.Artist = Clean(artist);
                info.Title = Clean(title);
                info.Genre = CleanGenre(genre);
                info.Album = Clean(album);
            }

            if (string.IsNullOrEmpty(info.Artist) || string.IsNullOrEmpty(info.Title)
                || string.IsNullOrEmpty(info.Genre))
            {
                var v1 = ReadId3v1(file);
                if (v1 != null)
                {
                    if (string.IsNullOrEmpty(info.Artist)) info.Artist = Clean(v1.Item1);
                    if (string.IsNullOrEmpty(info.Title)) info.Title = Clean(v1.Item2);
                    if (string.IsNullOrEmpty(info.Genre)) info.Genre = Clean(v1.Item3);
                }
            }
        }
        catch
        {
            // 标签解析失败时走文件名兜底
        }

        FillFromName(file, info);
        return info;
    }

    /// <summary>缺失字段兜底：按 歌手-歌曲(-专辑)(-流派) 常见文件名拆分；流派还可从父目录名推断</summary>
    private static void FillFromName(string file, Mp3TagInfo info)
    {
        string stem = Path.GetFileNameWithoutExtension(file);
        if (string.IsNullOrEmpty(info.Artist) || string.IsNullOrEmpty(info.Title))
        {
            var parts = stem.Split(new[] { " - ", "--", "-", "_ " }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            // 去掉疑似曲目编号的段
            var named = parts.Where(s => !IsTrackNo(s)).ToList();
            if (named.Count >= 2)
            {
                if (string.IsNullOrEmpty(info.Artist)) info.Artist = named[0];
                if (string.IsNullOrEmpty(info.Title)) info.Title = string.Join("-", named.Skip(1));
            }
            else if (string.IsNullOrEmpty(info.Title))
            {
                info.Title = stem.Trim();
            }
        }

        if (string.IsNullOrEmpty(info.Genre)) info.Genre = GuessGenreFromFolders(file);
        if (string.IsNullOrEmpty(info.Album))
            try { info.Album = Path.GetFileName(Path.GetDirectoryName(file) ?? "") ?? ""; } catch { }
    }

    private static bool IsTrackNo(string s)
    {
        s = s.Trim();
        return s.Length <= 4 && s.All(char.IsDigit);
    }

    private static string GuessGenreFromFolders(string file)
    {
        try
        {
            var dir = Path.GetDirectoryName(file);
            while (!string.IsNullOrEmpty(dir) && Path.GetDirectoryName(dir) != dir)
            {
                string name = Path.GetFileName(dir).ToLowerInvariant();
                foreach (string kw in GenreKeywords)
                    if (name.Contains(kw)) return Path.GetFileName(dir);
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch
        {
        }

        return "";
    }

    private static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Trim('\0', ' ', '\t', '\r', '\n');
    }

    private static string CleanGenre(string s)
    {
        s = Clean(s);
        if (string.IsNullOrEmpty(s)) return "";
        // "(13)" / "13/Pop" / "13" 等索引格式
        if (s.StartsWith("(") && s.EndsWith(")"))
            s = s.Substring(1, s.Length - 2);
        int slash = s.IndexOf('/');
        if (slash > 0 && int.TryParse(s.Substring(0, slash), out int idx))
            return idx >= 0 && idx < Genres.Length ? Genres[idx] : s.Substring(slash + 1);
        if (int.TryParse(s, out int i2))
            return i2 >= 0 && i2 < Genres.Length ? Genres[i2] : s;
        return s;
    }

    private static Dictionary<string, string> ReadId3v2(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
        var head = new byte[10];
        if (fs.Read(head, 0, 10) < 10) return null;
        if (head[0] != 'I' || head[1] != 'D' || head[2] != '3') return null;
        int major = head[3];
        bool extended = (head[5] & 0x40) != 0;
        int size = SyncSafe(head, 6);
        int bodyStart = 10 + (extended ? 4 : 0);
        var body = new byte[size];
        fs.Position = bodyStart;
        int read = 0;
        while (read < size)
        {
            int n = fs.Read(body, read, size - read);
            if (n <= 0) break;
            read += n;
        }

        var result = new Dictionary<string, string>();
        int p = 0;
        int frameHdr = major == 2 ? 6 : 10; // v2.2 帧头 6 字节，v2.3/v2.4 为 10 字节
        while (p + frameHdr <= read)
        {
            string id = Encoding.ASCII.GetString(body, p, 4);
            if (id[0] < 'A' || id[0] > 'Z') break; // 填充区结束
            int frameSize;
            if (major >= 4)
                frameSize = SyncSafe(body, p + 4); // v2.4 帧长度为 syncsafe
            else if (major == 3)
                frameSize = (body[p + 4] << 24) | (body[p + 5] << 16) | (body[p + 6] << 8) | body[p + 7];
            else
                frameSize = (body[p + 4] << 16) | (body[p + 5] << 8) | body[p + 6];
            int dataStart = p + frameHdr;
            if (frameSize <= 0 || dataStart + frameSize > read) break;
            if (!result.ContainsKey(id))
            {
                string text = DecodeText(body, dataStart, frameSize);
                if (!string.IsNullOrEmpty(text)) result[id] = text;
            }

            p = dataStart + frameSize;
        }

        return result;
    }

    private static string DecodeText(byte[] body, int start, int size)
    {
        if (size <= 0) return "";
        byte enc = body[start];
        int s = start + 1, len = size - 1;
        // ID3v2.2 帧无编码字节，默认 latin1（部分文件写 utf16，靠 BOM 纠正）
        if (enc > 3) { s = start; len = size; enc = 0; }
        if (len <= 0) return "";
        try
        {
            switch (enc)
            {
                case 0: return Latin1.GetString(body, s, len);
                case 1:
                {
                    // ffmpeg/多数打谱器写 UTF-16 带 BOM：FF FE=小端，FE FF=大端；无 BOM 时按小端
                    if (len >= 2 && body[s] == 0xFE && body[s + 1] == 0xFF)
                        return Encoding.BigEndianUnicode.GetString(body, s + 2, len - 2);
                    if (len >= 2 && body[s] == 0xFF && body[s + 1] == 0xFE)
                        return Encoding.Unicode.GetString(body, s + 2, len - 2);
                    if ((len & 1) != 0) len--; // UTF-16 需偶数字节
                    return Encoding.Unicode.GetString(body, s, len);
                }
                case 2: return Encoding.BigEndianUnicode.GetString(body, s, len);
                case 3:
                {
                    var utf8 = new UTF8Encoding(false, false);
                    return utf8.GetString(body, s, len);
                }
            }
        }
        catch
        {
        }

        return "";
    }

    private static Tuple<string, string, string> ReadId3v1(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read);
        if (fs.Length < 135) return null;
        fs.Position = fs.Length - 128;
        var t = new byte[128];
        if (fs.Read(t, 0, 128) < 128) return null;
        if (t[0] != 'T' || t[1] != 'A' || t[2] != 'G') return null;
        var latin = Latin1;
        string title = latin.GetString(t, 3, 30);
        string artist = latin.GetString(t, 33, 30);
        int gi = t[127];
        string genre = gi >= 0 && gi < Genres.Length ? Genres[gi] : "";
        return Tuple.Create(artist, title, genre);
    }

    private static int SyncSafe(byte[] b, int at)
        => (b[at] << 21) | (b[at + 1] << 14) | (b[at + 2] << 7) | b[at + 3];
}
