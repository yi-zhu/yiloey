using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using holey.Kits;

namespace holey.Dialog;

/// <summary>
/// uc 转换：解密网易云 .uc 缓存（XOR-0xA3）到输出文件夹；
/// 标签来源优先同名 .json 边车，其次按文件名歌曲ID联网查网易云 api/song/detail；
/// 可选按"歌手 - 标题"重命名输出；被占用/失败的文件跳过并计数
/// </summary>
public partial class UcConvertDialog : Window
{
    private volatile int progDone;
    private volatile int progTotal;
    private System.Windows.Threading.DispatcherTimer progTimer;

    public UcConvertDialog()
    {
        InitializeComponent();
        // 进度经 UI 消息循环轮询，避免 BeginInvoke 与 await 续体乱序覆盖最终状态
        progTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        progTimer.Tick += (_, _) =>
        {
            if (progDone > 0 && progDone < progTotal)
                StatusText.Text = $"转换中 {progDone}/{progTotal}";
        };

        var o = UcOptions.Load();
        SrcBox.Text = o.SrcDir;
        OutBox.Text = o.OutDir;
        OutBox1.Text = o.OutDir1;
        ExtBox.Text = o.ExtNorm;
        SubDirCheck.IsChecked = o.SubDirs;
        MetaCheck.IsChecked = o.UseSidecar;
        ApiCheck.IsChecked = o.UseApi;
        RenameCheck.IsChecked = o.Rename;
        ConflictBox.SelectedIndex = (int)o.Conflict; // 下拉项顺序与 UcConflict 一致
        ToastCheck.IsChecked = o.ShowToast;
        ToastPosBox.SelectedIndex = (int)o.ToastPos;
        ScanBox.Text = o.ScanSeconds.ToString("0.##");
        ClickCheck.IsChecked = o.ClickAfter;
        ClickXBox.Text = o.ClickX >= 0 ? o.ClickX.ToString() : "";
        ClickYBox.Text = o.ClickY >= 0 ? o.ClickY.ToString() : "";
        KeysCheck.IsChecked = o.KeysAfter;
        KeysBox.Text = o.Keys;
    }

    private void BrowseSrc_OnClick(object sender, RoutedEventArgs e) => PickFolder(SrcBox);

    private void BrowseOut_OnClick(object sender, RoutedEventArgs e) => PickFolder(OutBox);
    private void BrowseOut1_OnClick(object sender, RoutedEventArgs e) => PickFolder(OutBox1);

    private static void PickFolder(TextBox target)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog();
        if (Directory.Exists(target.Text)) dlg.SelectedPath = target.Text;
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            target.Text = dlg.SelectedPath;
    }

    private UcOptions ReadOptions(string src) => new UcOptions
    {
        SrcDir = src,
        OutDir = OutBox.Text.Trim().Trim('"'),
        OutDir1 = OutBox1.Text.Trim().Trim('"'),
        Ext = ExtBox.Text.Trim(),
        SubDirs = SubDirCheck.IsChecked == true,
        UseSidecar = MetaCheck.IsChecked == true,
        UseApi = ApiCheck.IsChecked == true,
        Rename = RenameCheck.IsChecked == true,
        Conflict = (UcConflict)ConflictBox.SelectedIndex,
        ShowToast = ToastCheck.IsChecked == true,
        ToastPos = (UcToastPos)Math.Max(0, ToastPosBox.SelectedIndex),
        ScanSeconds = double.TryParse(ScanBox.Text.Trim(), out double scan)
            ? Math.Min(120, Math.Max(0.5, scan)) : 2,
        ClickAfter = ClickCheck.IsChecked == true,
        ClickX = int.TryParse(ClickXBox.Text.Trim(), out int cx) ? cx : -1,
        ClickY = int.TryParse(ClickYBox.Text.Trim(), out int cy) ? cy : -1,
        KeysAfter = KeysCheck.IsChecked == true,
        Keys = KeysBox.Text.Trim()
    };

    /// <summary>关闭窗口即记住设置，自动监听左键开启时直接复用</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        string src = SrcBox.Text.Trim().Trim('"');
        if (src.Length > 0) ReadOptions(src).Save();
        base.OnClosing(e);
    }

    private async void Run_OnClick(object sender, RoutedEventArgs e)
    {
        string src = SrcBox.Text.Trim().Trim('"');
        if (!Directory.Exists(src))
        {
            StatusText.Text = "源文件夹不存在";
            return;
        }
        if (!double.TryParse(ScanBox.Text.Trim(), out double scan) || scan < 0.1 )
        {
            StatusText.Text = "扫描时间建议为 0.5 ~ 120 秒";
            return;
        }
        if (ClickCheck.IsChecked == true &&
            (!int.TryParse(ClickXBox.Text.Trim(), out int cx) ||
             !int.TryParse(ClickYBox.Text.Trim(), out int cy) || cx < 0 || cy < 0))
        {
            StatusText.Text = "请填写有效的点击坐标（可点“取当前位置”后把鼠标移到目标）";
            return;
        }

        var opt = ReadOptions(src);
        opt.Save();

        RunBtn.IsEnabled = false;
        progDone = 0;
        progTotal = 0;
        progTimer.Start();
        StatusText.Text = "正在枚举文件...";
        try
        {
            var (sum, enumErr) = await Task.Run(() =>
            {
                var s = UcJob.RunBatch(opt,
                    (done, total) => { progTotal = total; progDone = done; },
                    msg => StatusTextDispatch(() => StatusText.Text = msg),
                    out string err);
                return (s, err);
            });

            progTotal = int.MaxValue; // 关闭进度回写，防止与最终状态竞争
            progTimer.Stop();
            if (enumErr != null)
            {
                StatusText.Text = enumErr;
                return;
            }
            if (sum.Files == 0)
            {
                StatusText.Text = $"未找到 *.{opt.ExtNorm} 文件";
                return;
            }

            StatusText.Text = sum.Head() + $"\n输出: {opt.EffectiveOutDir}\n" + sum.Note(opt)
                              + (sum.Errors.Count > 0
                                  ? "\n" + string.Join("\n", sum.Errors.Take(5))
                                  : "");
        }
        catch (Exception ex)
        {
            StatusText.Text = "转换异常: " + ex.Message;
        }
        finally
        {
            progTimer.Stop();
            RunBtn.IsEnabled = true;
        }
    }

    private void StatusTextDispatch(Action set)
        => Dispatcher.BeginInvoke(set, System.Windows.Threading.DispatcherPriority.Background);

    /// <summary>倒计时 3 秒后读取鼠标屏幕坐标，期间用户可把鼠标移到目标位置</summary>
    private async void PickPos_OnClick(object sender, RoutedEventArgs e)
    {
        PickPosBtn.IsEnabled = false;
        for (int i = 3; i > 0; i--)
        {
            PickPosBtn.Content = $"{i} 秒...";
            await Task.Delay(1000);
        }
        var p = System.Windows.Forms.Cursor.Position;
        ClickXBox.Text = p.X.ToString();
        ClickYBox.Text = p.Y.ToString();
        ClickCheck.IsChecked = true;
        PickPosBtn.Content = "取当前位置";
        PickPosBtn.IsEnabled = true;
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
