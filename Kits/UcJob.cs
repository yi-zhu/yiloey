using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YiKits;

namespace holey.Kits;

/// <summary>
/// uc 转换的一整套选项：转换窗口与自动监听共用，保存在 exe 同名 ini 的 [uc] 区，
/// 这样关了窗口、重启程序后左键仍能按上次设置自动转换。
/// </summary>
public enum UcToastPos { Center, TopLeft, TopRight, BottomLeft, BottomRight }

public sealed class UcOptions
{
    private const string Area = "uc";

    public string SrcDir = "";
    public string OutDir = "";
    public string OutDir1 = "";
    public string Ext = "uc";
    public bool SubDirs = true;
    public bool UseSidecar = true;
    public bool UseApi = true;
    public bool Rename = true;
    public UcConflict Conflict = UcConflict.Skip;

    // 自动监听：提示、扫描、转换完成后的可选动作
    public bool ShowToast = true;
    public UcToastPos ToastPos = UcToastPos.Center;
    /// <summary>文件最后一次写入后静默多少秒才开始转换</summary>
    public double ScanSeconds = 2;
    public bool ClickAfter;
    public int ClickX = -1;
    public int ClickY = -1;
    public bool KeysAfter;
    public string Keys = "";

    /// <summary>去掉前导点；空则回落 uc</summary>
    public string ExtNorm
    {
        get
        {
            string e = (Ext ?? "").Trim().TrimStart('.');
            return e.Length == 0 ? "uc" : e;
        }
    }

    public string EffectiveOutDir =>
        string.IsNullOrWhiteSpace(OutDir) ? Path.Combine(SrcDir, "uc_out") : OutDir.Trim();

    public string EffectiveOutDir1 =>
        string.IsNullOrWhiteSpace(OutDir1) ? Path.Combine(SrcDir, "uc_out") : OutDir1.Trim();

    public static UcOptions Load()
    {
        var o = new UcOptions
        {
            SrcDir = YiKit.IniGet("src", Area, def: ""),
            OutDir = YiKit.IniGet("out", Area, def: ""),
            OutDir1 = YiKit.IniGet("out1", Area, def: ""),
            Ext = YiKit.IniGet("ext", Area, def: "uc"),
            SubDirs = YiKit.IniGetBool("sub", true, Area),
            UseSidecar = YiKit.IniGetBool("sidecar", true, Area),
            UseApi = YiKit.IniGetBool("api", true, Area),
            Rename = YiKit.IniGetBool("rename", true, Area),
            ShowToast = YiKit.IniGetBool("toast", true, Area),
            ClickAfter = YiKit.IniGetBool("click", false, Area),
            KeysAfter = YiKit.IniGetBool("keys", false, Area),
            Keys = YiKit.IniGet("keys_text", Area, def: ""),
        };
        o.Conflict = ParseConflict(YiKit.IniGet("conflict", Area, def: "Skip"));
        o.ToastPos = ParseToastPos(YiKit.IniGet("toast_pos", Area, def: "Center"));
        if (double.TryParse(YiKit.IniGet("scan", Area, def: "2"), out double scan))
            o.ScanSeconds = Math.Min(120, Math.Max(0.5, scan));
        if (int.TryParse(YiKit.IniGet("click_x", Area, def: ""), out int cx)) o.ClickX = cx;
        if (int.TryParse(YiKit.IniGet("click_y", Area, def: ""), out int cy)) o.ClickY = cy;
        return o;
    }

    public void Save()
    {
        YiKit.IniWrite("src", SrcDir, Area);
        YiKit.IniWrite("out", OutDir ?? "", Area);
        YiKit.IniWrite("out1", OutDir1 ?? "", Area);
        YiKit.IniWrite("ext", ExtNorm, Area);
        YiKit.IniWrite("sub", SubDirs.ToString(), Area);
        YiKit.IniWrite("sidecar", UseSidecar.ToString(), Area);
        YiKit.IniWrite("api", UseApi.ToString(), Area);
        YiKit.IniWrite("rename", Rename.ToString(), Area);
        YiKit.IniWrite("conflict", Conflict.ToString(), Area);
        YiKit.IniWrite("toast", ShowToast.ToString(), Area);
        YiKit.IniWrite("toast_pos", ToastPos.ToString(), Area);
        YiKit.IniWrite("scan", ScanSeconds.ToString("0.##"), Area);
        YiKit.IniWrite("click", ClickAfter.ToString(), Area);
        YiKit.IniWrite("click_x", ClickX.ToString(), Area);
        YiKit.IniWrite("click_y", ClickY.ToString(), Area);
        YiKit.IniWrite("keys", KeysAfter.ToString(), Area);
        YiKit.IniWrite("keys_text", Keys ?? "", Area);
    }

    public static UcConflict ParseConflict(string? s) =>
        Enum.TryParse(s, out UcConflict c) ? c : UcConflict.Skip;

    public static UcToastPos ParseToastPos(string? s) =>
        Enum.TryParse(s, out UcToastPos p) ? p : UcToastPos.Center;
}

/// <summary>一批转换的统计结果</summary>
public sealed class UcSummary
{
    public int Files;
    public int Done;
    public int Ok;
    public int Skipped;
    public int Marked;
    public int MetaCount;
    public int ApiHit;
    public int TagFail;
    public string? FirstTagErr;
    public string? LastOutput;
    public readonly List<string> Errors = new List<string>();

    public string Head()
    {
        string s = $"完成 成功:{Ok} 跳过:{Skipped}";
        if (Marked > 0) s += $" 加标记:{Marked}";
        if (Errors.Count > 0) s += $" 失败:{Errors.Count}";
        return s;
    }

    public string Note(UcOptions o)
    {
        string srcs = o.UseSidecar ? (o.UseApi ? "边车+联网" : "仅边车")
                                  : o.UseApi ? "仅联网" : "未启用标签";
        string note = "取得信息 " + MetaCount + " 首（" + srcs +
                      (o.UseApi ? "，联网命中 " + ApiHit : "") + "）";
        if (o.Rename && MetaCount < Files)
            note += $"，{Files - MetaCount} 个无信息保留原名";
        if (Skipped > 0 && o.Conflict == UcConflict.Skip)
            note += "，重名的已跳过（可改用覆盖或加标记防重复）";
        if (Marked > 0) note += $"，{Marked} 个加标记另存";
        if (TagFail > 0) note += $"\n标签写入失败 {TagFail} 个: {FirstTagErr}";
        return note;
    }
}

/// <summary>批量转换的执行体：枚举、按ID联网取信息、逐文件转换，供窗口与自动监听共用</summary>
public static class UcJob
{
    /// <summary>枚举源文件夹下待转换文件；输出目录在源目录内时排除其下文件，避免互相转换</summary>
    public static List<string> Enumerate(UcOptions o, out string? error)
    {
        error = null;
        try
        {
            IEnumerable<string> files = Directory.GetFiles(o.SrcDir, "*." + o.ExtNorm,
                o.SubDirs ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            if (IsOutInsideSrc(o))
                files = files.Where(f => !IsInDir(f, o.EffectiveOutDir));
            return files.ToList();
        }
        catch (Exception ex)
        {
            error = "枚举失败: " + ex.Message;
            return new List<string>();
        }
    }

    /// <summary>
    /// 输出目录是源目录的子目录时才需要排除转出的文件；
    /// 输出在源之外（如输出是源的父目录）或与源相同都不做目录排除，
    /// 否则会把所有源文件误当输出忽略（源目录相同时靠扩展名区分输入输出）。
    /// </summary>
    public static bool IsOutInsideSrc(UcOptions o)
    {
        string src = NormDir(o.SrcDir);
        string dst = NormDir(o.EffectiveOutDir);
        return dst.StartsWith(src + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>path 位于 dir 目录之下（不含 dir 本身）</summary>
    public static bool IsInDir(string path, string dir)
    {
        string d = NormDir(dir);
        return path.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormDir(string dir) =>
        Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static UcSummary RunBatch(UcOptions o, Action<int, int>? progress, Action<string>? status,
        out string? enumError)
    {
        var files = Enumerate(o, out enumError);
        if (enumError != null) return new UcSummary();
        return RunFiles(files, o, progress, status);
    }

    /// <summary>逐文件转换；progress(done,total) 与 status(msg) 在工作线程上调用</summary>
    public static UcSummary RunFiles(IReadOnlyList<string> files, UcOptions o,
        Action<int, int>? progress, Action<string>? status)
    {
        var sum = new UcSummary { Files = files.Count };
        var apiMap = new Dictionary<long, Dictionary<string, string>>();
        if (o.UseApi && files.Count > 0)
        {
            var ids = new List<long>();
            foreach (string f in files)
                if (NetEaseMeta.TryGetSongId(Path.GetFileName(f), out long id)) ids.Add(id);
            if (ids.Count > 0)
            {
                status?.Invoke($"正在联网查询歌曲信息（{ids.Count} 个ID）...");
                apiMap = NetEaseMeta.Fetch(ids);
                if (apiMap.Count == 0)
                    status?.Invoke("联网查询无结果（离线或ID不匹配），继续仅解密");
            }
        }

        progress?.Invoke(0, files.Count);
        foreach (string f in files)
        {
            try
            {
                Dictionary<string, string>? meta = null;
                if (o.UseSidecar) meta = UcConverter.ReadSidecar(f);
                if (meta == null && o.UseApi &&
                    NetEaseMeta.TryGetSongId(Path.GetFileName(f), out long id) &&
                    apiMap.TryGetValue(id, out var m))
                    meta = m;
                if (meta != null) sum.MetaCount++;

                var res = UcConverter.Convert(f, o.EffectiveOutDir, meta, o.Rename, o.Conflict,o.EffectiveOutDir1);
                if (res.Status == UcConvertStatus.Skipped)
                {
                    sum.Skipped++;
                }
                else if (res.Status == UcConvertStatus.Converted)
                {
                    sum.Ok++;
                    if (res.Marked) sum.Marked++;
                    sum.LastOutput = res.OutputPath;
                    if (res.TagError != null)
                    {
                        sum.TagFail++;
                        if (sum.FirstTagErr == null) sum.FirstTagErr = res.TagError;
                    }
                }
                else sum.Errors.Add(Path.GetFileName(f) + ": 格式无法识别");
            }
            catch (IOException)
            {
                sum.Errors.Add(Path.GetFileName(f) + ": 文件被占用或无权限，已跳过");
            }
            catch (Exception ex)
            {
                sum.Errors.Add(Path.GetFileName(f) + ": " + ex.Message);
            }

            sum.Done++;
            progress?.Invoke(sum.Done, sum.Files);
        }

        sum.ApiHit = apiMap.Count;
        return sum;
    }
}
