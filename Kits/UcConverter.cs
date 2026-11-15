using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using YiKits;

namespace holey.Kits;

/// <summary>输出文件已存在时的处理策略</summary>
public enum UcConflict
{
    /// <summary>跳过，不动已存在的目标文件</summary>
    Skip,
    /// <summary>覆盖目标文件</summary>
    Overwrite,
    /// <summary>另存为 "名称 (n)"，绝不覆盖</summary>
    Mark
}

/// <summary>一条 .uc 的转换结局</summary>
public enum UcConvertStatus { Converted, Skipped, Unrecognized }

/// <summary>单次转换的结果</summary>
public sealed class UcConvertResult
{
    public UcConvertStatus Status;
    /// <summary>实际写出的文件完整路径；跳过或无法识别时为 null</summary>
    public string? OutputPath;
    /// <summary>非空表示标签写入失败（文件本身已转换成功）</summary>
    public string? TagError;
    /// <summary>目标已存在，按"加标记"策略改存为 名称 (n)</summary>
    public bool Marked;

    public static UcConvertResult Unrecognized() => new UcConvertResult { Status = UcConvertStatus.Unrecognized };
    public static UcConvertResult Skipped() => new UcConvertResult { Status = UcConvertStatus.Skipped };
}

/// <summary>
/// 网易云旧版 .uc 缓存转换（参考 NMTC/MucheXD）：整文件 XOR 0xA3 解密。
/// NMTC 解密后不写任何标签（音乐信息丢失），这里转换后用 ffmpeg -c copy 从
/// 同目录 {原名}.json 边车（LUD 风格 {"artist":..,"title":..,"album":..,"genre":..,"track":..}）回填 ID3。
/// 内置失败时可兜底调用第三方 NMTC_V1.5.exe。
/// </summary>
public static class UcConverter
{
    private const byte UcXorKey = 0xA3;
    private static bool? ffmpegFound;

    /// <summary>与项目其他功能一致，按 PATH 查找裸 ffmpeg.exe（结果缓存）</summary>
    public static bool FfmpegAvailable()
    {
        if (ffmpegFound.HasValue) return ffmpegFound.Value;
        bool found = false;
        try
        {
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    if (File.Exists(Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe"))) { found = true; break; }
                }
                catch { }
            }
        }
        catch { }
        ffmpegFound = found;
        return found;
    }

    public const string NmtcUrl =
        "https://github.com/MucheXD/NetEaseMusicCacheConverter/releases/download/1.5/NMTC_V1.5.exe";

    public static string NmtcPath =>
        Path.Combine(holey.App.GetCachePath(), "NMTC_V1.5.exe");

    /// <summary>只读 + 允许对方继续读写/删除：别人正播放或下载占用时也能打开源文件</summary>
    public static FileStream OpenReadShared(string file) =>
        new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>共享读打开；被短暂独占（杀软扫描、下载器刷盘）时退避重试</summary>
    private static FileStream OpenReadRetry(string file)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                return OpenReadShared(file);
            }
            catch (IOException) when (i < 5)
            {
                System.Threading.Thread.Sleep(150 * (i + 1));
            }
        }
    }

    /// <summary>
    /// 转换一条 .uc：先按元数据定下输出文件名，再按 conflict 决定覆盖/跳过/加标记，
    /// 最后解密写盘并用 ffmpeg 无损回填 ID3。
    /// meta 为 null 则仅解密；rename 为 false 则保留原文件名（仍可写标签）。
    /// </summary>
    public static UcConvertResult Convert(string ucFile, string outDir,
        Dictionary<string, string>? meta, bool rename, UcConflict conflict, string outDir1)
    {
        byte[] head = new byte[64];
        int hn;
        using (var fs = OpenReadRetry(ucFile))
            hn = fs.Read(head, 0, head.Length);
        if (hn < 8) return UcConvertResult.Unrecognized();

        // 已是明文容器直接拷贝，否则按 XOR-0xA3 解密
        string plainExt = SniffPlain(head, hn);
        bool isPlain = plainExt != null;
        string ext = isPlain ? plainExt! : SniffDecryptedHead(head, hn)!;
        string ofn = BuildName(ucFile, meta, rename) ;
        string wanted = Path.Combine(outDir, ofn + "." + ext);
        bool exists = File.Exists(wanted);
        // 跳过策略在解密前就短路，旧文件一个字节都不动
        if (exists && conflict == UcConflict.Skip) return UcConvertResult.Skipped();
        var res = new UcConvertResult();
        if (exists && conflict == UcConflict.Mark)
        {
            wanted = UniquePath(wanted);
            res.Marked = true;
        }

        Directory.CreateDirectory(outDir);
        try
        {
            WriteOutput(ucFile, wanted, isPlain);
        }
        catch (IOException) when (conflict == UcConflict.Overwrite)
        {
            // 目标被播放器/杀软占住删不掉也覆盖不了：退化为加标记另存，不丢新文件
            wanted = UniquePath(wanted);
            res.Marked = true;
            WriteOutput(ucFile, wanted, isPlain);
        }
        try
        {
            WriteOutput(ucFile, Path.Combine(outDir1,ofn+ "." + ext), isPlain);
        }
        catch (IOException) when (conflict == UcConflict.Overwrite)
        {
           
        }
        res.Status = UcConvertStatus.Converted;
        res.OutputPath = wanted;
        if (meta != null) res.TagError = WriteTags(wanted, meta);
        return res;
    }

    private static void WriteOutput(string ucFile, string dst, bool isPlain)
    {
        if (isPlain) CopyShared(ucFile, dst);
        else XorFile(ucFile, dst);
    }

    /// <summary>输出文件名（不含扩展名）：允许重命名且有歌手/标题时用"歌手 - 标题"，否则沿用原名</summary>
    private static string BuildName(string ucFile,
        System.Collections.Generic.Dictionary<string, string>? meta, bool rename)
    {
        string stem = Path.GetFileNameWithoutExtension(ucFile);
        if (!rename || meta == null) return stem;
        string artist = meta.TryGetValue("artist", out var a) ? a : "";
        string title = meta.TryGetValue("title", out var t) ? t : "";
        if (string.IsNullOrEmpty(artist) && string.IsNullOrEmpty(title)) return stem;
        string nice = (string.IsNullOrEmpty(artist) ? "" : artist + " - ") +
                      (string.IsNullOrEmpty(title) ? stem : title);
        foreach (char c in Path.GetInvalidFileNameChars()) nice = nice.Replace(c, '_');
        return nice;
    }

    /// <summary>共享读源文件复制输出（不用 File.Copy，避免源被占用时直接失败）</summary>
    private static void CopyShared(string src, string dst)
    {
        using var inFs = OpenReadRetry(src);
        using var outFs = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.Read);
        inFs.CopyTo(outFs);
    }

    private static void XorFile(string src, string dst)
    {
        const int Buf = 1 << 20;
        using var inFs = OpenReadRetry(src);
        using var outFs = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.Read);
        var buf = new byte[Buf];
        int n;
        while ((n = inFs.Read(buf, 0, Buf)) > 0)
        {
            for (int i = 0; i < n; i++) buf[i] ^= UcXorKey;
            outFs.Write(buf, 0, n);
        }
    }

    /// <summary>对明文头部嗅探容器类型；mp3 同时接受帧同步头（无 ID3）</summary>
    private static string? SniffPlain(byte[] head, int hn)
    {
        if (hn >= 3 && head[0] == 'I' && head[1] == 'D' && head[2] == '3') return "mp3";
        if (hn >= 4 && head[0] == 'O' && head[1] == 'g' && head[2] == 'g' && head[3] == 'S') return "ogg";
        if (hn >= 12 && head[4] == 'f' && head[5] == 't' && head[6] == 'y' && head[7] == 'p') return "m4a";
        if (hn >= 1 && (head[0] == 0xFF || head[0] == 'A')) return "mp3"; // MPEG帧同步 0xFFxx / ADTS 0xFF F1
        return null;
    }

    /// <summary>先 XOR 解密头部再嗅探（NMTC 的整文件 XOR 方案）；嗅不到时默认 mp3</summary>
    private static string? SniffDecryptedHead(byte[] head, int hn)
    {
        var dec = new byte[hn];
        for (int i = 0; i < hn; i++) dec[i] = (byte)(head[i] ^ UcXorKey);
        if (hn >= 3 && dec[0] == 'I' && dec[1] == 'D' && dec[2] == '3') return "mp3";
        if (hn >= 4 && dec[0] == 'O' && dec[1] == 'g' && dec[2] == 'g') return "ogg";
        if (hn >= 12 && dec[4] == 'f' && dec[5] == 't' && dec[6] == 'y' && dec[7] == 'p') return "m4a";
        return "mp3"; // 旧版 .uc 主体均为 mp3，直接按整文件 XOR
    }

    private static string UniquePath(string wanted)
    {
        string dir = Path.GetDirectoryName(wanted) ?? "";
        string stem = Path.GetFileNameWithoutExtension(wanted);
        string ext = Path.GetExtension(wanted);
        for (int i = 1; i < 1000; i++)
        {
            string cand = Path.Combine(dir, stem + " (" + i + ")" + ext);
            if (!File.Exists(cand)) return cand;
        }
        return wanted;
    }

    private static string[] MetaKeys = { "artist", "title", "album", "genre", "track", "date", "comment" };

    /// <summary>读取同名 .json 边车中的歌曲信息；无文件或解析失败返回 null</summary>
    public static System.Collections.Generic.Dictionary<string, string>? ReadSidecar(string ucFile)
    {
        string json = Path.ChangeExtension(ucFile, ".json");
        if (!File.Exists(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            var map = new System.Collections.Generic.Dictionary<string, string>();
            foreach (string key in MetaKeys)
                if (doc.RootElement.TryGetProperty(key, out var v) &&
                    v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
                    map[key] = v.GetString().Trim();
            return map.Count > 0 ? map : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>ffmpeg 无损写 ID3v2.3（-c copy）；成功返回 null，失败返回原因</summary>
    private static string? WriteTags(string file, System.Collections.Generic.Dictionary<string, string> meta)
    {
        try
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext != ".mp3") return null; // m4a/ogg 用各自容器标签命令差异大，暂只回填 mp3
            if (!FfmpegAvailable()) return "PATH 中未找到 ffmpeg.exe，标签未写入";

            // 临时文件必须保留可识别的容器后缀，否则 ffmpeg 无法选择封装器而直接失败
            string tmp = Path.Combine(Path.GetDirectoryName(file) ?? ".",
                Path.GetFileNameWithoutExtension(file) + ".tagging.tmp" + ext);
            // -metadata 是输出选项，必须放在 -i 之后
            var cmd = new StringBuilder("ffmpeg.exe -hide_banner -loglevel error -y -i \"")
                .Append(file).Append("\" ")
                .Append(BuildMetaArgs(meta))
                .Append(" -map_metadata 0 -id3v2_version 3 -write_id3v2 1 -c copy \"")
                .Append(tmp).Append("\"");
            string output = YiKit.RunCmd(cmd.ToString());
            if (File.Exists(tmp) && new FileInfo(tmp).Length > 0)
            {
                File.Delete(file);
                File.Move(tmp, file);
                return null;
            }
            if (File.Exists(tmp)) File.Delete(tmp);
            string reason = (output ?? "").Trim();
            return reason == "" ? "ffmpeg 未生成输出文件" :
                (reason.Length > 160 ? reason.Substring(0, 160) : reason);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string BuildMetaArgs(System.Collections.Generic.Dictionary<string, string> meta)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kv in meta)
        {
            string key = kv.Key == "date" ? "year" : kv.Key;
            sb.Append("-metadata ").Append(key).Append("=\"").Append(kv.Value.Replace("\"", "'")).Append("\" ");
        }
        return sb.ToString().Trim();
    }

    /// <summary>兜底：确保 NMTC_V1.5.exe 存在（不存在则下载），然后启动其 GUI</summary>
    public static bool EnsureAndLaunchNmtc()
    {
        try
        {
            if (!File.Exists(NmtcPath))
            {
                YiKit.ShowToastWaring("正在下载兜底工具 NMTC_V1.5 ...");
                YiKit.FileDownLoad(NmtcUrl, Path.GetFileName(NmtcPath), Path.GetDirectoryName(NmtcPath));
            }

            if (!File.Exists(NmtcPath))
            {
                YiKit.ShowToastError("NMTC 下载失败");
                return false;
            }

            Process.Start(new ProcessStartInfo(NmtcPath) { UseShellExecute = true });
            YiKit.ShowToast("已启动 NMTC 兜底工具，转换完成后可用“批量命名”恢复歌名歌手");
            return true;
        }
        catch (Exception ex)
        {
            YiKit.ShowToastError("NMTC 启动失败: " + ex.Message);
            return false;
        }
    }
}
