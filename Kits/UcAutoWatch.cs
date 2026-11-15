using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using YiKits;

namespace holey.Kits;

/// <summary>
/// uc 自动监听：FileSystemWatcher 盯住源文件夹，新出现的 .uc 等它写完（长度稳定且能读）后
/// 按 UcOptions 自动转换到输出文件夹。由主窗口 uc转换 菜单左键开关。
/// </summary>
public static class UcAutoWatch
{
    // 单个文件最多陪跑这么久：超过说明它一直在写/一直被独占，放弃并提示
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(15);
    private static readonly object Gate = new object();
    private static readonly HashSet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    // 每个文件最后一次写入事件的时间：监听线程据此判断“静默了多久”
    private static readonly Dictionary<string, long> LastWrite = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
    private static FileSystemWatcher? watcher;
    private static CancellationTokenSource? cts;
    private static ConcurrentQueue<(string Path, long StartTicks)>? queue;

    public static bool IsRunning { get; private set; }

    public static bool Start(UcOptions o)
    {
        Stop();
        if (!Directory.Exists(o.SrcDir)) return false;

        var q = new ConcurrentQueue<(string, long)>();
        var token = new CancellationTokenSource();
        try
        {
            var w = new FileSystemWatcher(o.SrcDir, "*." + o.ExtNorm)
            {
                IncludeSubdirectories = o.SubDirs,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024
            };
            w.Created += (_, e) => Enqueue(o, e.FullPath);
            w.Changed += (_, e) => Enqueue(o, e.FullPath);
            w.Renamed += (_, e) => Enqueue(o, e.FullPath);
            w.Error += (_, e) =>
            {
                if (e.GetException() is InternalBufferOverflowException)
                    Toast(o, "uc监听缓冲区溢出，可能漏掉了个别新文件", 1);
            };

            // 队列/令牌就绪后再开始收事件，避免启动瞬间的事件被丢掉
            queue = q;
            cts = token;
            lock (Gate)
            {
                Known.Clear();
                LastWrite.Clear();
            }
            watcher = w;
            w.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            token.Dispose();
            queue = null;
            cts = null;
            Toast(o, "uc自动监听启动失败: " + ex.Message, 2);
            return false;
        }

        var th = new Thread(() => Loop(o, q, token.Token))
        {
            IsBackground = true,
            Name = "uc-watch"
        };
        th.Start();
        IsRunning = true;
        return true;
    }

    public static void Stop()
    {
        IsRunning = false;
        try { cts?.Cancel(); } catch { }
        try { watcher?.Dispose(); } catch { }
        cts = null;
        watcher = null;
        queue = null;
        lock (Gate)
        {
            Known.Clear();
            LastWrite.Clear();
        }
    }

    private static void Enqueue(UcOptions o, string path)
    {
        try
        {
            // 只有输出目录在源目录内时才需要排除转出的文件；
            // 输出是源的父目录（常见配置）时若仍按前缀判断，会把所有源文件误当输出忽略
            if (UcJob.IsOutInsideSrc(o) && UcJob.IsInDir(path, o.EffectiveOutDir))
                return;
            lock (Gate)
            {
                // 每次写入事件都刷新静默起点；队列只保留一个待处理项
                LastWrite[path] = DateTime.UtcNow.Ticks;
                if (!Known.Add(path)) return;
            }
            queue?.Enqueue((path, DateTime.UtcNow.Ticks));
        }
        catch
        {
            // 监听回调里不能抛异常
        }
    }

    private static void Loop(UcOptions o, ConcurrentQueue<(string Path, long StartTicks)> q, CancellationToken ct)
    {
        int scanMs = (int)Math.Round(Math.Max(0.5, o.ScanSeconds) * 1000);
        while (!ct.IsCancellationRequested)
        {
            if (!q.TryDequeue(out var item))
            {
                Thread.Sleep(150);
                continue;
            }

            string path = item.Path;
            try
            {
                if (!File.Exists(path))
                {
                    Forget(path);
                    continue;
                }

                long quietMs;
                lock (Gate)
                {
                    long last = LastWrite.TryGetValue(path, out long t) ? t : item.StartTicks;
                    quietMs = (DateTime.UtcNow.Ticks - last) / TimeSpan.TicksPerMillisecond;
                }

                if (quietMs < scanMs)
                {
                    if (IsOverdue(item, ct))
                    {
                        Forget(path);
                        Toast(o, "uc自动转换放弃（文件一直在写入或无法读取）: " + Path.GetFileName(path), 1);
                        continue;
                    }
                    // 还在写：等静默期走完；期间 Changed 事件会刷新 LastWrite
                    Thread.Sleep((int)Math.Min(scanMs - quietMs, 300));
                    q.Enqueue((path, item.StartTicks));
                    continue;
                }

                if (!IsReadableComplete(path))
                {
                    if (IsOverdue(item, ct))
                    {
                        Forget(path);
                        Toast(o, "uc自动转换放弃（文件一直在写入或无法读取）: " + Path.GetFileName(path), 1);
                    }
                    else
                    {
                        Thread.Sleep(300);
                        q.Enqueue((path, item.StartTicks));
                    }
                    continue;
                }

                if (ct.IsCancellationRequested) continue; // 等待期间被关闭

                var sum = UcJob.RunFiles(new[] { path }, o, null, null);
                Forget(path);
                if (sum.Ok > 0)
                {
                    string name = Path.GetFileName(sum.LastOutput ?? path);
                    Toast(o, $"uc自动转换完成: {name}");
                    RunPostActions(o);
                }
                else if (sum.Skipped > 0)
                {
                    // 目标已存在（跳过策略）：安静处理，避免反复弹提示
                    RunPostActions(o);
                }
                else
                {
                    string why = sum.Errors.FirstOrDefault() ?? "格式无法识别";
                    Toast(o, "uc自动转换失败: " + why, 1);
                }
            }
            catch (Exception ex)
            {
                Forget(path);
                Toast(o, "uc自动转换异常: " + ex.Message, 2);
            }
        }
    }

    private static bool IsOverdue((string Path, long StartTicks) item, CancellationToken ct) =>
        DateTime.UtcNow.Ticks - item.StartTicks >= GiveUpAfter.Ticks || ct.IsCancellationRequested;

    /// <summary>静默期已过：再确认能共享读取且长度一致就认为写完（不阻塞等独占句柄）</summary>
    private static bool IsReadableComplete(string path)
    {
        try
        {
            long len = new FileInfo(path).Length;
            if (len <= 0) return false;
            using var fs = UcConverter.OpenReadShared(path);
            return fs.Length == len;
        }
        catch
        {
            return false;
        }
    }

    private static void Forget(string path)
    {
        lock (Gate)
        {
            Known.Remove(path);
            LastWrite.Remove(path);
        }
    }

    /// <summary>按设置弹出提示；位置为工作区坐标（Toast 以中心点定位）</summary>
    private static void Toast(UcOptions o, string msg, int type = 0)
    {
        if (!o.ShowToast) return;
        TryToast(() =>
        {
            var (l, t) = ToastPoint(o.ToastPos);
            switch (type)
            {
                case 2: YiKit.ShowToastError(msg, l: l, t: t); break;
                case 1: YiKit.ShowToastWaring(msg, l: l, t: t); break;
                default: YiKit.ShowToast(msg, l: l, t: t); break;
            }
        });
    }

    private static (int L, int T) ToastPoint(UcToastPos pos)
    {
        var wa = SystemParameters.WorkArea;
        int cx = (int)(wa.Left + wa.Width / 2);
        int cy = (int)(wa.Top + wa.Height / 2);
        int mx = 280, my = 90;
        return pos switch
        {
            UcToastPos.TopLeft => ((int)wa.Left + mx, (int)wa.Top + my),
            UcToastPos.TopRight => ((int)wa.Right - mx, (int)wa.Top + my),
            UcToastPos.BottomLeft => ((int)wa.Left + mx, (int)wa.Bottom - my),
            UcToastPos.BottomRight => ((int)wa.Right - mx, (int)wa.Bottom - my),
            _ => (cx, cy),
        };
    }

    /// <summary>转换完成后的可选动作：先点击目标位置切换焦点，再发送按键</summary>
    private static void RunPostActions(UcOptions o)
    {
        if (!o.ClickAfter && !o.KeysAfter) return;
        var app = Application.Current;
        if (app == null) return;
        try
        {
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (o.ClickAfter && o.ClickX >= 0 && o.ClickY >= 0)
                        YiKit.SimulateMouseClick(o.ClickX, o.ClickY);
                    if (o.KeysAfter && !string.IsNullOrWhiteSpace(o.Keys))
                    {
                        if (o.ClickAfter) Thread.Sleep(150); // 等点击把焦点切到目标窗口
                        YiKit.SendKeys(o.Keys.Trim());
                    }
                }
                catch
                {
                    // 目标窗口已关闭等情况下不影响转换
                }
            }));
        }
        catch
        {
            // Dispatcher 已关闭
        }
    }

    /// <summary>退出/关闭时 Dispatcher 可能已不可用，吐司失败不影响转换</summary>
    private static void TryToast(Action a)
    {
        try { a(); } catch { }
    }
}
