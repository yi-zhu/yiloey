using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using holey.Kits;

namespace holey.Dialog;

/// <summary>
/// 批量命名：读取文件夹（可选递归）内媒体文件的 ID3 标签/文件名，按命名格式预览并批量重命名。
/// 默认格式：{歌手}-{歌曲}-{流派}.{后缀}
/// </summary>
public partial class BatchRenameDialog : Window
{
    private const int MaxFiles = 2000;

    private static readonly Regex TokenRegex = new Regex("[{\uFF5B]([^\uFF5D}]+)[}\uFF5D]",
        RegexOptions.Compiled);

    // 中英文令牌统一为小写规范名
    private static readonly Dictionary<string, string> TokenMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "歌手", "artist" }, { "歌曲", "title" }, { "流派", "genre" }, { "专辑", "album" },
        { "原文件名", "name" }, { "文件名", "name" }, { "后缀", "ext" }, { "扩展名", "ext" },
        { "序号", "n" }, { "目录", "folder" },
        { "artist", "artist" }, { "title", "title" }, { "genre", "genre" }, { "album", "album" },
        { "name", "name" }, { "stem", "name" }, { "ext", "ext" }, { "n", "n" }, { "index", "n" },
        { "folder", "folder" }
    };

    public class RenameItem : INotifyPropertyChanged
    {
        public string Path { get; set; } = "";
        public string Folder { get; set; } = "";
        public string FileName { get; set; } = "";
        public string Ext { get; set; } = "";

        private string artist = "";
        private string title = "";
        private string genre = "";
        private string album = "";
        private string newName = "";

        public string Artist
        {
            get => artist;
            set { artist = value; Changed(nameof(Artist)); }
        }

        public string Title
        {
            get => title;
            set { title = value; Changed(nameof(Title)); }
        }

        public string Genre
        {
            get => genre;
            set { genre = value; Changed(nameof(Genre)); }
        }

        public string Album
        {
            get => album;
            set { album = value; Changed(nameof(Album)); }
        }

        public string NewName
        {
            get => newName;
            set { newName = value; Changed(nameof(NewName)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Changed(string p) => PropertyChanged?.Invoke(this, new(p));
    }

    private readonly ObservableCollection<RenameItem> items = new();
    private bool suppressRefresh;
    private int scanGen;
    private volatile int scanDone;
    private volatile int scanTotal;

    // 属性变化后延迟合并刷新，避免绑定回写造成重入
    private DispatcherTimer refreshTimer;

    // 扫描进度经同一 UI 消息循环轮询报告，避免 BeginInvoke 与 await 续体乱序覆盖最终状态
    private DispatcherTimer scanProgressTimer;

    public BatchRenameDialog()
    {
        InitializeComponent();
        Grid.ItemsSource = items;
        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        refreshTimer.Tick += (_, _) => { refreshTimer.Stop(); RefreshPreviews(); };
        scanProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        scanProgressTimer.Tick += (_, _) =>
        {
            if (scanDone > 0 && scanDone < scanTotal)
                StatusText.Text = $"正在读取标签 {scanDone}/{scanTotal}";
        };
    }

    private void QueueRefresh()
    {
        if (suppressRefresh || refreshTimer == null) return;
        refreshTimer.Stop();
        refreshTimer.Start();
    }

    private void Browse_OnClick(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog();
        if (Directory.Exists(FolderBox.Text)) dlg.SelectedPath = FolderBox.Text;
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            FolderBox.Text = dlg.SelectedPath;
    }

    private void Preset_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Header: string h })
        {
            int sp = h.IndexOf("  （");
            if (sp > 0) h = h.Substring(0, sp);
            FormatBox.Text = h;
        }
    }

    private async void Scan_OnClick(object sender, RoutedEventArgs e)
    {
        string folder = FolderBox.Text.Trim().Trim('"');
        if (!Directory.Exists(folder))
        {
            StatusText.Text = "文件夹不存在";
            return;
        }

        string ext = ExtBox.Text.Trim().TrimStart('.');
        if (string.IsNullOrEmpty(ext)) ext = "mp3";
        bool sub = SubDirCheck.IsChecked == true;

        ScanBtn.IsEnabled = false;
        ApplyBtn.IsEnabled = false;
        suppressRefresh = true;
        scanDone = 0;
        scanTotal = 0;
        scanProgressTimer.Start();
        items.Clear();
        StatusText.Text = "正在枚举文件...";

        int skipped = 0;
        try
        {
            var result = await Task.Run(() =>
            {
                List<string> files;
                try
                {
                    files = Directory.EnumerateFiles(folder, "*." + ext,
                            sub ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                        .Take(MaxFiles + 1).ToList();
                }
                catch (Exception ex)
                {
                    return (new List<RenameItem>(), 0, ex.Message);
                }

                bool limited = files.Count > MaxFiles;
                if (limited) files = files.Take(MaxFiles).ToList();
                scanTotal = files.Count;

                var list = new List<RenameItem>(files.Count);
                int fail = 0;
                int done = 0;
                foreach (string file in files)
                {
                    try
                    {
                        var info = Mp3TagReader.Read(file);
                        string extOf = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                        var item = new RenameItem
                        {
                            Path = file,
                            Folder = Path.GetDirectoryName(file) ?? "",
                            FileName = Path.GetFileName(file),
                            Ext = extOf,
                            Artist = info.Artist,
                            Title = info.Title,
                            Genre = info.Genre,
                            Album = info.Album
                        };
                        item.PropertyChanged += (_, _) => QueueRefresh();
                        list.Add(item);
                    }
                    catch
                    {
                        // 文件被占用/无权限时跳过，不中断整体读取
                        fail++;
                    }

                    done++;
                    scanDone = done;
                }

                return (list, fail, limited ? $"已达上限 {MaxFiles}，请分批处理" : null);
            });

            if (result.Item3 != null && result.Item1.Count == 0)
            {
                StatusText.Text = "读取失败: " + result.Item3;
                return;
            }

            skipped = result.Item2;
            suppressRefresh = false;
            scanTotal = int.MaxValue; // 关闭进度回写，防止与最终状态竞争
            foreach (var it in result.Item1) items.Add(it);
            RefreshPreviews();
            ApplyBtn.IsEnabled = items.Count > 0;
            StatusText.Text = $"共 {items.Count} 个文件"
                              + (skipped > 0 ? $"，跳过 {skipped} 个（被占用或无权限）" : "")
                              + (result.Item3 != null ? $"（{result.Item3}）" : "");
            return;
        }
        catch (Exception ex)
        {
            suppressRefresh = false;
            StatusText.Text = "读取异常: " + ex.Message;
        }
        finally
        {
            scanProgressTimer.Stop();
            suppressRefresh = false;
            ScanBtn.IsEnabled = true;
        }
    }

    private void FormatBox_OnTextChanged(object sender, TextChangedEventArgs e) => QueueRefresh();

    private void RefreshPreviews()
    {
        // XAML 解析期间 TextChanged 可能先于其余字段就绪触发；批量读取中不做逐项刷新
        if (suppressRefresh || items == null || string.IsNullOrEmpty(FormatBox?.Text)) return;
        int start = ParseInt(StartBox?.Text, 1);
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            string name = BuildName(it, i, start);
            if (string.IsNullOrEmpty(name)) name = it.FileName;

            // 同一目录内重名追加序号
            string key = it.Folder + "\\" + name;
            if (used.TryGetValue(key, out int c))
            {
                used[key] = c + 1;
                string stem = Path.GetFileNameWithoutExtension(name);
                name = $"{stem}({c + 1}).{it.Ext}";
            }
            else used[key] = 0;

            it.NewName = name;
        }
    }

    private string BuildName(RenameItem it, int index, int start)
    {
        string format = FormatBox.Text ?? "";
        var sb = new StringBuilder();
        int last = 0;
        foreach (Match m in TokenRegex.Matches(format))
        {
            sb.Append(format, last, m.Index - last);
            string token = m.Groups[1].Value.Trim();
            int width = 0;
            int colon = token.IndexOf(':');
            if (colon > 0)
            {
                if (!int.TryParse(token.Substring(colon + 1), out width)) width = 0;
                token = token.Substring(0, colon).Trim();
            }

            if (TokenMap.TryGetValue(token, out string norm))
            {
                string value;
                switch (norm)
                {
                    case "artist": value = Sanitize(it.Artist); break;
                    case "title": value = Sanitize(it.Title); break;
                    case "genre": value = Sanitize(it.Genre); break;
                    case "album": value = Sanitize(it.Album); break;
                    case "name": value = Sanitize(Path.GetFileNameWithoutExtension(it.FileName)); break;
                    case "ext": value = it.Ext; break;
                    case "folder": value = Sanitize(Path.GetFileName(it.Folder)); break;
                    default: value = (start + index).ToString(); break;
                }

                if (width > 1 && int.TryParse(value, out int num)) value = num.ToString("D" + width);
                else if (width > 1) value = value.PadLeft(width, '0');
                sb.Append(value);
            }
            else sb.Append(m.Value); // 未知令牌原样保留
            last = m.Index + m.Length;
        }

        sb.Append(format, last, format.Length - last);
        string name = sb.ToString().Trim();
        // 字段为空时清理残留的连接符：-A-.mp3 -> A.mp3，A--B -> A-B
        name = Regex.Replace(name, @"^[-_\s]+", "");
        name = Regex.Replace(name, @"[-_\s]+(?=\.|$)", "");
        name = Regex.Replace(name, @"-{2,}", "-");
        if (string.IsNullOrEmpty(name)) return "";
        if (!name.Contains('.') && !string.IsNullOrEmpty(it.Ext)) name += "." + it.Ext;
        return name;
    }

    private static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        foreach (char c in s)
            sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        return sb.ToString().Trim().TrimEnd('.');
    }

    private static int ParseInt(string s, int def)
        => int.TryParse(s?.Trim(), out int v) ? v : def;

    private async void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        if (items.Count == 0) return;
        if (MessageBox.Show($"确认按当前格式重命名 {items.Count} 个文件？", "批量命名",
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        ScanBtn.IsEnabled = false;
        ApplyBtn.IsEnabled = false;
        StatusText.Text = "正在重命名...";
        // UI 线程快照，后台线程不得触碰绑定中的 ObservableCollection
        var snapshot = items.ToList();
        int gen = ++scanGen;
        try
        {
            var report = await Task.Run(() =>
            {
                int ok = 0, same = 0, fail = 0;
                var fails = new List<string>();
                var moved = new List<(RenameItem Item, string Name, string Path)>();
                foreach (var it in snapshot)
                {
                    try
                    {
                        string target = Path.Combine(it.Folder, it.NewName);
                        if (string.Equals(Normalize(it.Path), Normalize(target), StringComparison.OrdinalIgnoreCase))
                        {
                            same++;
                            continue;
                        }

                        if (File.Exists(target) || Directory.Exists(target))
                        {
                            fail++;
                            fails.Add(it.NewName + " 已存在");
                            continue;
                        }

                        File.Move(it.Path, target);
                        moved.Add((it, it.NewName, target));
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        fail++;
                        fails.Add(it.FileName + ": " + ex.Message);
                    }
                }

                return (ok, same, fail, fails, moved);
            });

            foreach (var m in report.moved)
            {
                m.Item.Path = m.Path;
                m.Item.FileName = m.Name;
            }

            RefreshPreviews();
            StatusText.Text = $"重命名完成 成功:{report.ok} 跳过:{report.same} 失败:{report.fail}"
                              + (report.fails.Count > 0 ? "\n" + string.Join("\n", report.fails.Take(5)) : "");
        }
        catch (Exception ex)
        {
            StatusText.Text = "重命名异常: " + ex.Message;
        }
        finally
        {
            if (gen == scanGen)
            {
                ScanBtn.IsEnabled = true;
                ApplyBtn.IsEnabled = items.Count > 0;
            }
        }
    }

    private static string Normalize(string p)
    {
        try { return Path.GetFullPath(p); } catch { return p; }
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
