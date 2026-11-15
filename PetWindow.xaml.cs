using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Xsl;
using holey.Dialog;
using holey.Kits;
using holey.KitWin;
using holey.Page;
using holey.Vdd;
using holey.Vdd.Vdd;
using holey.WebView;
using YiKits;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using Color = System.Drawing.Color;
using Control = System.Windows.Forms.Control;
using Image = System.Drawing.Image;
using InputDialog = YiKits.Dialog.InputDialog;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Point = System.Windows.Point;
using ProgressBar = System.Windows.Controls.ProgressBar;
using Size = System.Drawing.Size;

namespace holey;

// 被监控进程类
public class MonitoredProcess
{
    public string ProcessName { get; set; }
    public string ProcessPath { get; set; }
    public bool IsMonitoring { get; set; }
}

public partial class PetWindow : Window
{
    #region 窗口隐藏

    [DllImport("user32.dll", SetLastError = true)]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EX_STYLE = -20;
    private const int WS_EX_APPWINDOW = 0x00040000, WS_EX_TOOLWINDOW = 0x00000080;

    private int _clipboard = 0, _translate = 0;

    public static PetWindow Me;

    public bool _close = false;
    private double w = 120;
    private double h = 60;
    private double _zoom = 1;
    private double l = 0;
    private double t = 0;

    /// <summary>
    ///  此方法为隐藏Alt+Tab；隐藏窗口在toolbar 请设置ShowInTaskbar
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    void FormLoaded(object sender, RoutedEventArgs args)
    {
        //Variable to hold the handle for the form
        var hwnd = new WindowInteropHelper(this).Handle;
        //Performing some magic to hide the form from Alt+Tab
        // YiKit.SetWindowLong(hwnd, YiKit.GwlExstyle, YiKit.GwlExstyle | YiKit.WsExTransparent);
        SetWindowLong(hwnd, GWL_EX_STYLE,
            (GetWindowLong(hwnd, GWL_EX_STYLE) | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        //鼠标穿透
        // SetWindowLong(hwnd, GWL_EX_STYLE,0x20);
        // SetWindowLong(hwnd, GWL_EX_STYLE, (GetWindowLong(hwnd, GWL_EX_STYLE) | 0x00000020));
    }

    #endregion

    private Rect r;

    public PetWindow()
    {
        InitializeComponent();
        Me = this;
        _zoom = YiKit.IniGetDouble("zoompet", 1.5);
        if (Width != w * _zoom)
        {
            this.Width = w * _zoom;
        }

        if (Height != h * _zoom)
        {
            this.Height = h * _zoom;
        }

        try
        {
            _sleep = (int)(Convert.ToSingle(YiKit.IniGet("sleep", "ini", def: "1")) * 1000);
        }
        catch (Exception e)
        {
            _sleep = 1000;
        }

        if (_sleep <= 1) _sleep = 1000;

        r = SystemParameters.WorkArea;

        l = YiKit.IniGetDouble("petleft", r.Width - Width - 10);
        t = YiKit.IniGetDouble("pettop", r.Bottom - Height - 10);

        if (l >= r.Width || l < -10) l = r.Width - Width - 10;
        if (t >= r.Height || t < -10) t = r.Bottom - Height - 10;

        IsVpn = YiKit.IniGetBool("isvpn", true);

        if (this.Left != l)
            Left = l;

        if (this.Top != t)
            Top = t;

        //屏蔽贴边放大
        this.ResizeMode = ResizeMode.NoResize;

        //屏蔽贴边
        PreviewMouseLeftButtonDown += (sender, args) =>
        {
            // this.ResizeMode = ResizeMode.NoResize; 
            l = this.Left;
            t = this.Top;
        };

        PreviewMouseLeftButtonUp += (sender, args) =>
        {
            // this.ResizeMode = ResizeMode.CanResize;
            if (this.Left != l)
                YiKit.IniWrite("petleft", this.Left.ToString(), "ini");

            if (this.Top != t)
                YiKit.IniWrite("pettop", this.Top.ToString(), "ini");
        };


        YiKit.SetForegroundWindow(YiKit.GetWindowHandle(this));

        //按键监听
        YiKit.KeyDownEvent += (((sender, e) =>
        {
            if (e.KeyValue == (int)Keys.Q && (Control.ModifierKeys & Keys.Shift) == Keys.Shift &&
                (Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                isTranslateing = true;
            }
            else if (e.KeyValue == (int)Keys.Q && (int)Control.ModifierKeys == (int)Keys.Control)
            {
                isTranslateing = true;

                // YiKit.Print("按下了Ctrl+Q:");
            }
            else if (e.KeyValue == (int)Keys.Q)
            {
                isTranslateing = false;

                // YiKit.Print("取消Q:");
            }
        })); //按键监听
        YiKit.KeyUpEvent += (((sender, e) =>
        {
            if (e.KeyValue == (int)Keys.Q)
            {
                // isTranslateing = true;
                if (isTranslateing)
                {
                    // YiKit.Print("松开了Q");
                    string text = YiKit.GetScreenText();
                    if (String.IsNullOrEmpty(text))
                    {
                        YiKit.SendKeys("^c");
                    }
                    else
                    {
                        if (isTranslateing)
                        {
                            if (TranslateDialog.Me == null)
                            {
                                new TranslateDialog();
                            }

                            text = text.Trim();
                            traslateText = text;
                            if (YiKit.IsEn(text))
                            {
                                YiWscClient.Send("ait:e2z:" + text);

                                // TranslateDialog.Me.Activate();
                            }
                            else
                                YiWscClient.Send("ait:z2e:" + text);

                            TranslateDialog.Me.ZhTx.Text = text;
                            TranslateDialog.Me.EnTx.Text = "... ...";
                            TranslateDialog.Me.show();

                            isTranslateing = false;
                            return;
                        }
                    }

                    // Thread.Sleep(100);


                    /*
                    if (TranslateDialog.Me==null||TranslateDialog.Me.ZhTx.Text != traslateText)
                    {
                        YiKit.Print("ESC");
                        YiKit.SendKeys("{ESC}");
                        YiKit.SendKeys("^c");
                    }*/
                }
            }
        }));
        YiKit.SetWindowLong(YiKit.GetWindowHandle(this), YiKit.GwlExstyle,
            YiKit.GwlExstyle | YiKit.WsExTransparent);
        YiKit.SetWindowListen(callback: (state =>
        {
            if (state == (object)YiKit.ScMaximize)
            {
                //全屏
                this.Topmost = false;
            }
            else if (state == (object)YiKit.ScRestore)
            {
                this.Topmost = false;
                this.Topmost = true;
            }
        }));
        LoadPet();
        if (YiKit.IniGetInt("wallpaper", 0) == 1)
        {
            WallPaperMenuItem.IsChecked = true;
            new WallPaper();
            WallPaper.me.Show();
            WallPaper.me.Activate();
        }


        if (YiKit.IniGet("centzoom", def: "0").Equals("1"))
            CentZomm_OnItemClick(CentZoom, null);
        if (YiKit.IniGet("crosshair", def: "0").Equals("1"))
            Crosshair_OnItemClick(Crosshair, null);
        //VDD自动启动
        VDD = YiKit.IniGetBool("vdd", def: false);
        VddItem.IsChecked = VDD;
        if (VDD) ProgramVdd.StartVdd(["-a"], true);

        AutoStartMenuItem.IsChecked = YiKit.IniGetBool("autostart", false);

        RestoreUcAutoWatch();

        // 从配置文件加载被监控进程
        LoadMonitoredProcesses();

        // 三方菜单：加载用户自定义项
        LoadThirdPartyCustomMenus();
    }

    // 检查被监控进程
    private void CheckMonitoredProcesses()
    {
        foreach (var monitoredProcess in _monitoredProcesses)
        {
            if (monitoredProcess.IsMonitoring)
            {
                bool isRunning = IsProcessRunning(monitoredProcess.ProcessName);
                if (!isRunning)
                {
                    // 进程已消失，重启进程
                    RestartProcess(monitoredProcess);
                }
            }
        }
    }

    // 检查进程是否运行
    private bool IsProcessRunning(string processName)
    {
        try
        {
            // 移除.exe后缀（如果有）
            string processNameWithoutExe = processName;
            if (processNameWithoutExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                processNameWithoutExe = processNameWithoutExe.Substring(0, processNameWithoutExe.Length - 4);
            }

            Process[] processes = Process.GetProcessesByName(processNameWithoutExe);
            return processes.Length > 0;
        }
        catch (Exception ex)
        {
            YiKit.Print("检查进程失败: " + ex.Message);
            return false;
        }
    }

    // 重启进程
    private void RestartProcess(MonitoredProcess monitoredProcess)
    {
        try
        {
            if (File.Exists(monitoredProcess.ProcessPath))
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = monitoredProcess.ProcessPath,
                    WorkingDirectory = Path.GetDirectoryName(monitoredProcess.ProcessPath)
                };
                Process.Start(startInfo);
                YiKit.Print("已重启进程: " + monitoredProcess.ProcessName);
                YiKit.ShowToast("已重启进程: " + monitoredProcess.ProcessName, l: (int)(Left + Width / 2),
                    t: (int)(Top + 30));
            }
            else
            {
                YiKit.Print("进程路径不存在: " + monitoredProcess.ProcessPath);
            }
        }
        catch (Exception ex)
        {
            YiKit.Print("重启进程失败: " + ex.Message);
        }
    }


    // 添加被监控进程
    private void AddMonitoredProcess()
    {
        try
        {
            // 打开文件选择对话框，让用户选择要监控的进程
            var openFileDialog = new OpenFileDialog
            {
                Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
                Title = "选择要监控的进程"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string processPath = openFileDialog.FileName;
                string processName = Path.GetFileName(processPath);

                // 检查是否已经在监控列表中
                if (!_monitoredProcesses.Any(p =>
                        p.ProcessPath.Equals(processPath, StringComparison.OrdinalIgnoreCase)))
                {
                    var monitoredProcess = new MonitoredProcess
                    {
                        ProcessName = processName,
                        ProcessPath = processPath,
                        IsMonitoring = true
                    };

                    _monitoredProcesses.Add(monitoredProcess);
                    YiKit.Print("已添加被监控进程: " + processName);
                    YiKit.ShowToast("已添加被监控进程: " + processName, l: (int)(Left + Width / 2), t: (int)(Top + 30));

                    // 保存到配置文件
                    SaveMonitoredProcesses();
                }
                else
                {
                    YiKit.ShowToast("该进程已在监控列表中", l: (int)(Left + Width / 2), t: (int)(Top + 30));
                }
            }
        }
        catch (Exception ex)
        {
            YiKit.Print("添加被监控进程失败: " + ex.Message);
        }
    }

    // 显示被监控进程列表
    private void ShowMonitoredProcesses()
    {
        try
        {
            if (_monitoredProcesses.Count == 0)
            {
                YiKit.ShowToast("当前没有被监控的进程", l: (int)(Left + Width / 2), t: (int)(Top + 30));
                return;
            }

            string message = "被监控进程列表:\n";
            foreach (var process in _monitoredProcesses)
            {
                message += $"{process.ProcessName} - {(process.IsMonitoring ? "监控中" : "已暂停")}\n";
            }

            YiKit.ShowToast(message, 0, 5, l: (int)(Left + Width / 2), t: (int)(Top + 30));
        }
        catch (Exception ex)
        {
            YiKit.Print("显示被监控进程失败: " + ex.Message);
        }
    }

    // 杀死进程
    private void KillProcess_OnClick(object sender, RoutedEventArgs e)
    {
        var ret = YiKit.ShowInputDialog("杀死匹配的进程", "进程名:");


        if (ret == null)
        {
            return;
        }

        string num = YiKit.ProgressEnd(ret[0]);
        YiKit.ShowToast("杀死了" + num + "个" + ret[0] + "进程");
    }

    // 添加监控进程菜单项点击事件
    private void AddMonitoredProcess_OnClick(object sender, RoutedEventArgs e)
    {
        AddMonitoredProcess();
    }

    // 显示监控进程菜单项点击事件
    private void ShowMonitoredProcess_OnClick(object sender, RoutedEventArgs e)
    {
        ShowMonitoredProcesses();
    }

    // 保存被监控进程到配置文件
    private void SaveMonitoredProcesses()
    {
        try
        {
            // 清空旧配置
            YiKit.IniWrite("monitored_processes_count", "0", "process_monitor");

            // 保存进程列表
            for (int i = 0; i < _monitoredProcesses.Count; i++)
            {
                var process = _monitoredProcesses[i];
                YiKit.IniWrite($"process_{i}_name", process.ProcessName, "process_monitor");
                YiKit.IniWrite($"process_{i}_path", process.ProcessPath, "process_monitor");
                YiKit.IniWrite($"process_{i}_monitoring", process.IsMonitoring.ToString(), "process_monitor");
            }

            // 保存进程数量
            YiKit.IniWrite("monitored_processes_count", _monitoredProcesses.Count.ToString(), "process_monitor");
        }
        catch (Exception ex)
        {
            YiKit.Print("保存被监控进程失败: " + ex.Message);
        }
    }

    // 加载被监控进程从配置文件
    private void LoadMonitoredProcesses()
    {
        try
        {
            _monitoredProcesses.Clear();

            // 读取进程数量
            int count = YiKit.IniGetInt("monitored_processes_count", 0, "process_monitor");

            // 读取进程列表
            for (int i = 0; i < count; i++)
            {
                string name = YiKit.IniGet($"process_{i}_name", "", "process_monitor");
                string path = YiKit.IniGet($"process_{i}_path", "", "process_monitor");
                bool isMonitoring = YiKit.IniGetBool($"process_{i}_monitoring", true, "process_monitor");

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
                {
                    _monitoredProcesses.Add(new MonitoredProcess
                    {
                        ProcessName = name,
                        ProcessPath = path,
                        IsMonitoring = isMonitoring
                    });
                }
            }
        }
        catch (Exception ex)
        {
            YiKit.Print("加载被监控进程失败: " + ex.Message);
        }
    }

    // 开启选中进程监控菜单项点击事件
    private void StartMonitoredProcess_OnClick(object sender, RoutedEventArgs e)
    {
        SelectAndOperateProcess("选择要开启监控的进程", (process) =>
        {
            process.IsMonitoring = true;
            YiKit.Print("已开启进程监控: " + process.ProcessName);
            YiKit.ShowToast("已开启进程监控: " + process.ProcessName, l: (int)(Left + Width / 2), t: (int)(Top + 30));
            SaveMonitoredProcesses();
        });
    }

    // 关闭选中进程监控菜单项点击事件
    private void StopMonitoredProcess_OnClick(object sender, RoutedEventArgs e)
    {
        SelectAndOperateProcess("选择要关闭监控的进程", (process) =>
        {
            process.IsMonitoring = false;
            YiKit.Print("已关闭进程监控: " + process.ProcessName);
            YiKit.ShowToast("已关闭进程监控: " + process.ProcessName, l: (int)(Left + Width / 2), t: (int)(Top + 30));
            SaveMonitoredProcesses();
        });
    }

    // 删除选中进程菜单项点击事件
    private void RemoveMonitoredProcess_OnClick(object sender, RoutedEventArgs e)
    {
        SelectAndOperateProcess("选择要删除的进程", (process) =>
        {
            _monitoredProcesses.Remove(process);
            YiKit.Print("已删除进程: " + process.ProcessName);
            YiKit.ShowToast("已删除进程: " + process.ProcessName, l: (int)(Left + Width / 2), t: (int)(Top + 30));
            SaveMonitoredProcesses();
        });
    }

    // 清空所有进程菜单项点击事件
    private void ClearMonitoredProcesses_OnClick(object sender, RoutedEventArgs e)
    {
        if (_monitoredProcesses.Count == 0)
        {
            YiKit.ShowToast("当前没有被监控的进程", l: (int)(Left + Width / 2), t: (int)(Top + 30));
            return;
        }

        if (MessageBox.Show("确定要清空所有被监控的进程吗？", "确认操作", MessageBoxButton.YesNo, MessageBoxImage.Question) ==
            MessageBoxResult.Yes)
        {
            _monitoredProcesses.Clear();
            YiKit.Print("已清空所有被监控进程");
            YiKit.ShowToast("已清空所有被监控进程", l: (int)(Left + Width / 2), t: (int)(Top + 30));
            SaveMonitoredProcesses();
        }
    }

    // 选择并操作进程的通用方法
    private void SelectAndOperateProcess(string title, Action<MonitoredProcess> operation)
    {
        if (_monitoredProcesses.Count == 0)
        {
            YiKit.ShowToast("当前没有被监控的进程", l: (int)(Left + Width / 2), t: (int)(Top + 30));
            return;
        }

        // 构建进程选择列表
        List<string> processNames = _monitoredProcesses
            .Select(p => $"{p.ProcessName} {(p.IsMonitoring ? "(监控中)" : "(已暂停)")} 是否").ToList();

        List<string> selected = ShowInputDialog(title, processNames.ToArray());
        List<string> result = new List<string>();
        while (selected.Count < processNames.Count)
        {
            selected.Insert(0, "");
        }

        for (int i = 0; i < selected.Count; i++)
        {
            if (selected[i] == "true")
            {
                result.Add(processNames[i]);
            }
        }

        if (result != null && result.Count > 0)
        {
            string selectedName = result[0];
            // 提取进程名称（去掉状态后缀）
            string processName = selectedName.Split(new[] { " (" }, StringSplitOptions.None)[0];

            // 查找对应的进程
            MonitoredProcess process = _monitoredProcesses.FirstOrDefault(p => p.ProcessName == processName);
            if (process != null)
            {
                operation(process);
            }
        }
    }

    public static bool VDD = false;

    protected override void OnSourceInitialized(EventArgs e)
    {
        //无边框
        YiKit.UiAreo(this, new Thickness(-1));

        base.OnSourceInitialized(e);
        _clipboard = YiKit.IniGetInt("clipboard", area: "ini", def: 0);
        _translate = YiKit.IniGetInt("translate", area: "ini", def: 0);
        if (_clipboard == 1 || _translate == 1)
        {
            YiKit.ClipboardMonitor(this, OnClipboardUpdated);
            clbname = YiKit.IniGet("clbname", "ini", def: Environment.UserName);
            lastClipboardText = YiKit.GetClipboardText();
            StartWb();

            if (_translate == 1)
            {
                //开启翻译
                isTranslate = true;
                YiKit.KeyboardMon();
                if (TranslateDialog.Me == null)
                {
                    new TranslateDialog();
                }
            }
        }
    }


    protected override void OnClosed(EventArgs e)
    {
        YiKit.CloseClipboardMonitor(OnClipboardUpdated);
        base.OnClosed(e);
        if (MainWindow.Me == null || !MainWindow.Me.IsVisible)
        {
            Environment.Exit(0);
        }
    }


    private int _sleep = 1000;

    /// <summary>
    /// 默认
    /// </summary>
    private bool IsVpn { get; set; } = true;

    private bool IsAlive { get; set; } = true;
    private NetworkInterface? tap;

    // 进程监听相关
    private List<MonitoredProcess> _monitoredProcesses = new List<MonitoredProcess>();

    public bool _isWallPage = false;

    private void LoadPet()
    {
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        Thread.CurrentThread.Priority = ThreadPriority.Lowest;
        long cot = 0;

        YiKit.RunThread(_ =>
        {
            long lastUp = 0;
            long lastDown = 0;
            bool isnowork = false;
            string tapas = "";
            string tapaw = "";
            string tapasU = "";
            string tapasD = "";
            tap = YiKit.GetInterface(ip: YiKit.IniGet("showip", def: null));

            while (IsAlive)
            {
                cot += 1;
                if (cot % 60 == 1)
                {
                    YiKit.RunOnUIThread(() =>
                    {
                        this.Topmost = false;
                        this.Topmost = true;
                    });
                }

                if (tap == null || tap.OperationalStatus != OperationalStatus.Up)
                {
                    tap = YiKit.GetInterface(sequence: true);
                    // IsVpn = !IsVpn;
                    Thread.Sleep(_sleep);
                    continue;
                }

                IPInterfaceProperties ipInterface = tap.GetIPProperties();
                // string MAC = adapter.GetPhysicalAddress().ToString(); //获取单播地址集
                UnicastIPAddressInformationCollection ipCollection = ipInterface.UnicastAddresses;

                long nowUp;
                long nowDown;
                foreach (UnicastIPAddressInformation ipaddr in ipCollection)
                {
                    if (ipaddr.Address.AddressFamily == AddressFamily.InterNetwork) //判断是否为ipv4
                    {
                        //Console.WriteLine(JsonConvert.SerializeObject(adapter));
                        nowUp = tap.GetIPStatistics().BytesSent;
                        nowDown = tap.GetIPStatistics().BytesReceived;
                        if (lastUp == 0)
                        {
                            lastDown = nowDown;
                            lastUp = nowUp;
                        }

                        /*if (nowUp - lastUp<10)
                        {
                            tapas += "上⇵上↑↓⇣⇡" + YiKit.SumByte((nowUp - lastUp) * (1000 / _sleep)) + "  |  ";
                        }else tapas += "↑ " + YiKit.SumByte((nowUp - lastUp) * (1000 / _sleep)) + "  |  ";*/

                        DateTime currentDateTime = DateTime.Now;

                        // 创建一个中国农历实例
                        ChineseLunisolarCalendar chineseCalendar = new ChineseLunisolarCalendar();

                        // 获取农历月、日
                        int lunarMonth = chineseCalendar.GetMonth(currentDateTime);
                        int lunarDay = chineseCalendar.GetDayOfMonth(currentDateTime);

                        if (nowDown == lastDown && nowUp == lastUp)
                        {
                            // if (isnowork) break;
                            // tapas = "时间:"+YiKit.NowT();
                            tapaw = YiKit.NowW();
                            tapas = string.Format("{0:0}", currentDateTime.Hour) + ":" + YiKit.NowD();
                                   
                            tapasU = string.Format("{0:00}", currentDateTime.Minute) + "" +
                                     (nowUp == lastUp ? "：" : "↑: ") + YiKit.SumByte(nowUp) + "";
                            tapasD = string.Format("{0:00}", currentDateTime.Second) + "" +
                                     (nowDown == lastDown ? "：" : "↓: ") + YiKit.SumByte(nowDown);
                            isnowork = true;
                        }
                        else
                        {
                            isnowork = false;
                            tapas = string.Format("{0:0}", currentDateTime.Hour) + "" + (nowUp == lastUp ? "  " : "⇡") +
                                    (((nowDown - lastDown) >= (nowUp - lastUp)) ? "⇣" : "⇡")
                                    + (nowDown == lastDown ? "  " : "⇣") + ""
                                    + YiKit.SumByte((nowDown + nowUp - lastDown - lastUp) * (1000f / _sleep)) + "";

                            tapasU = string.Format("{0:00}", currentDateTime.Minute) + "" +
                                     (nowUp == lastUp ? "：" : "↑: ") + YiKit.SumByte(nowUp) + "";
                            tapasD = string.Format("{0:00}", currentDateTime.Second) + "" +
                                     (nowDown == lastDown ? "：" : "↓: ") + YiKit.SumByte(nowDown) + "";
                        }


                        // tapas += "ip:"+ipaddr.Address + "\n";
                        // tapas += "MAC:"+tap.GetPhysicalAddress() + "\n";
                        YiKit.RunOnUIThread(() =>
                        {
                            tapas += YiKit.NowW();
                            if (!tapas.Equals(MainStrN.Text))
                            {
                                MainStrN.Text = tapas;
                                lastDown = nowDown;
                                lastUp = nowUp;
                            }
                            // if (!tapaw.Equals(MainStrW.Text))
                            // {
                            //     MainStrW.Text = tapaw;
                            // }

                            if (!ipaddr.Address.Equals(StatStrRect.ToolTip))
                            {
                                StatStrRect.ToolTip = ipaddr.Address;
                            }

                            if (!tapasU.Equals(MainStrU.Text))
                            {
                                MainStrU.Text = tapasU;
                            }

                            if (!tapasD.Equals(MainStrD.Text))
                            {
                                MainStrD.Text = tapasD;
                                lastUp = nowUp;
                            }
                        });
                        break;
                    }
                }


                // 每5秒检查一次被监控进程
                if (cot % 5 == 0)
                {
                    CheckMonitoredProcesses();
                }

                Thread.Sleep(_sleep);
            }
        });
        YiKit.RunThread(_ =>
        {
            int cpuse = 0;
            int memuse = 0;
            int diskuse = 0;
            int gpumem = 0;
            double diskusebyte = 0d;
            double diskusebyteR = 0d;
            double diskusebyteW = 0d;
            double gpumemmbU = 0d;
            double gpumemmbT = 0d;
            string disktx = "";
            string csd = "";
            string sd = "";
            

            while (IsAlive)
            {
                #region CPU使用率

                cpuse = (int)YiKit.CpuUse();

                if (cpuse >= 90)
                {
                    YiKit.RunOnUIThread(() =>
                    {
                        if (WallPaper.me != null)
                        {
                            _isWallPage = true;
                            WallPaper.me?.Close();
                        }
                    });
                }
                else if (cpuse >= 80)
                {
                    YiKit.RunOnUIThread(() => WallPaper.me?.PausePlay());
                }
                else if (cpuse <= 50 && _isWallPage)
                {
                    YiKit.RunOnUIThread(() =>
                    {
                        if (WallPaper.me == null)
                        {
                            new WallPaper();
                            WallPaper.me?.Show();
                        }
                    });
                }
                else
                {
                    YiKit.RunOnUIThread(() => WallPaper.me?.PlayMidea());
                }

                memuse = (int)YiKit.MemUse();
                diskuse = (int)YiKit.DiskUse();
                diskusebyteR = (int)YiKit.DiskUseByte("Read");
                diskusebyteW = (int)YiKit.DiskUseByte("Write");
                disktx = (diskusebyteW == diskusebyteR ? "" : (diskusebyteW > diskusebyteR ? "✎" : "👁")) +
                         YiKit.SumByteDate(diskusebyteW + diskusebyteR);
                gpumemmbU = YiKit.GpuMem();
                gpumemmbT = YiKit.GpuMemTol();
                gpumem = (int)YiKit.GpuMemuse();
                csd = YiKit.NowCSD();
                sd = YiKit.NowSD();
                YiKit.RunOnUIThread(() =>
                {
                    //cpu
                    CpuRect.Width = cpuse;
                    if (!sd.Equals(CpuStrR.Text))
                        CpuStrR.Text = sd;
                    if (!csd.Equals(StrCsd.Text))
                        StrCsd.Text = csd;
                    //gpumem
                    if (!GpuMemStr.Text.Equals(gpumem + ""))
                    {
                        GpuMemBtn.Height = gpumem / 2;
                        GpuMemStr.Text = gpumem + "";
                        GpuMemBtn.ToolTip = gpumemmbU + "MB/" + gpumemmbT + "MB";
                    }


                    //mem
                    if (!MemStr.Text.Equals(memuse + ""))
                    {
                        MemBtn.Height = memuse / 2;
                        MemStr.Text = memuse + "";
                    }

                    //disk
                    if (!DiskStrW.Text.Equals(disktx))
                    {
                        DiskStrW.Text = disktx;
                    }

                    if (DiskBtn.Width != diskuse && diskuse > -1)
                    {
                        DiskBtn.Width = diskuse;
                    }
                });


                YiKit.CpuTem((b) => ApplyTempToUi(CpuTempStr, b, "处理器温度"));
                YiKit.DiskTem((b) => ApplyTempToUi(DiskTempStr, b, "磁盘温度"));
                YiKit.MemTem((b) => ApplyTempToUi(MemTempStr, b, "内存温度"));
                YiKit.GpuTem((b) =>
                {
                    if (b == null)
                    {
                        return;
                    }

                    if (b.ToString() == "0")
                    {
                        return;
                    }

                    YiKit.RunOnUIThread(() =>
                    {
                        if (!ReferenceEquals(b, StatStr.Text))
                        {
                            /*StatStr.Text = b + "";
                            if (b is float bf)
                            {
                                if (StatStrRect.Fill is LinearGradientBrush brush)
                                {
                                    brush.EndPoint = new Point(bf/2.5, 0);
                                }
                            }*/
                            if (b is float gpuTemp)
                            {
                                if (gpuTemp > 90)
                                {
                                    if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                    {
                                        linearGradientBrush.GradientStops[0].Color = Colors.Red;
                                        linearGradientBrush.GradientStops[1].Color = Colors.Red;
                                    }
                                }
                                else if (gpuTemp > 80)
                                {
                                    if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                    {
                                        linearGradientBrush.GradientStops[0].Color = Colors.Firebrick;
                                        linearGradientBrush.GradientStops[1].Color = Colors.Red;
                                    }
                                }
                                else if (gpuTemp > 70)
                                {
                                    if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                    {
                                        linearGradientBrush.GradientStops[0].Color = Colors.Fuchsia;
                                        linearGradientBrush.GradientStops[1].Color = Colors.Firebrick;
                                    }
                                }
                                else if (gpuTemp > 60)
                                {
                                    if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                    {
                                        linearGradientBrush.GradientStops[0].Color = Colors.BlueViolet;
                                        linearGradientBrush.GradientStops[1].Color = Colors.Firebrick;
                                    }
                                }
                                else if (gpuTemp > 50)
                                {
                                    if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                    {
                                        linearGradientBrush.GradientStops[0].Color = Colors.Blue;
                                        linearGradientBrush.GradientStops[1].Color = Colors.Fuchsia;
                                    }
                                }
                                else
                                {
                                    if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                    {
                                        linearGradientBrush.GradientStops[0].Color = Colors.Blue;
                                        linearGradientBrush.GradientStops[1].Color = Colors.MediumBlue;
                                    }
                                }

                                StatStr.Text = b + "";
                            }
                        }
                    });
                });

                // YiKit.Print(YiKit.MemUse());

                #endregion

                Thread.Sleep((int)(_sleep * 1.5));
            }
        });
    }


    //调用英伟达的显卡


    protected override void OnClosing(CancelEventArgs e)
    {
        IsAlive = false;
        base.OnClosing(e);
    }

    private void PetWindow_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        // YiKit.Print(e.ToString());
    }

    private bool isThrough = false;
    private DispatcherTimer _releaseTimer;

    /// <summary>
    /// 设置窗口是否开启鼠标穿透
    /// </summary>
    /// <param name="enable">是否开启穿透</param>
    /// <param name="autoReleaseAfterMs">开启后多少毫秒自动解除，null 表示不自动解除</param>
    public void SetWindowMouseThrough(bool enable, int? autoReleaseAfterMs = null)
    {
        // 如果状态没有变化，直接返回
        if (isThrough == enable)
            return;
        if (enable)
        {
            ViewboxPet.Opacity = 0.1;
            GpuMemBtn.Opacity = 0.1;
            MemBtn.Opacity = 0.1;
        }

        var helper = new WindowInteropHelper(this);
        IntPtr hwnd = helper.Handle;

        const int GWL_EXSTYLE = -20;
        const uint WS_EX_TRANSPARENT = 0x00000020;

        uint currentStyle;

        if (IntPtr.Size == 8)
        {
            currentStyle = (uint)YiKit.GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        }
        else
        {
            currentStyle = (uint)YiKit.GetWindowLong(hwnd, GWL_EXSTYLE);
        }

        if (enable)
        {
            currentStyle |= WS_EX_TRANSPARENT;
        }
        else
        {
            currentStyle &= ~WS_EX_TRANSPARENT;
        }

        if (IntPtr.Size == 8)
        {
            YiKit.SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(currentStyle));
        }
        else
        {
            YiKit.SetWindowLong(hwnd, GWL_EXSTYLE, (int)currentStyle);
        }

        isThrough = enable;

        // 超时释放逻辑
        if (autoReleaseAfterMs.HasValue && enable)
        {
            if (_releaseTimer == null)
            {
                _releaseTimer = new DispatcherTimer();
                _releaseTimer.Tick += (s, e) =>
                {
                    if (isMouseInside)
                    {
                    }
                    else
                    {
                        _releaseTimer.Stop();
                        SetWindowMouseThrough(false); // 自动关闭穿透
                        if (isMouseInside) return;
                        ViewboxPet.Opacity = 1;
                        MemBtn.Opacity = 1;
                        GpuMemBtn.Opacity = 1;
                    }
                };
            }

            _releaseTimer.Interval = TimeSpan.FromMilliseconds(autoReleaseAfterMs.Value);
            _releaseTimer.Stop(); // 防止重复启动
            _releaseTimer.Start();
        }
        else if (!enable)
        {
            _releaseTimer?.Stop(); // 关闭穿透时停止定时器
        }
    }

    /// <summary>
    /// 显示主窗口
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ShowItem_OnClick(object sender, RoutedEventArgs e)
    {
        OpenWin();
        return;
        if (MainWindow.Me == null || PresentationSource.FromVisual(MainWindow.Me) == null)
        {
            if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                new MainWindow().Show();
            }
            else
            {
                //创建启动对象
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.UseShellExecute = true;
                startInfo.WorkingDirectory = Environment.CurrentDirectory;
                startInfo.FileName = Assembly.GetEntryAssembly()?.Location + " main";
                ;
                //设置启动动作,确保以管理员身份运行
                startInfo.Verb = "runas";


                // Start the new process
                try
                {
                    Process.Start(startInfo);
                }
                catch (Exception) //用户不同意权限关闭app
                {
                    // The user did not allow the application to run as administrator
                    MessageBox.Show("Sorry, but I don't seem to be able to start " +
                                    "this program with administrator rights!");
                }
            }
        }
        else MainWindow.Me?.Show();
        /*if (MainWindow.IsAlive)
        {
            MainWindow.YiWindow?.Show();
        }else  new  MainWindow().Show();*/
    }

    /// <summary>
    /// 右键关闭
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void CloseItem_OnClick(object sender, RoutedEventArgs e)
    {
        /*if (PresentationSource.FromVisual(MainWindow.Me) == null)
        {
            new MainWindow().Show();
        }
        else MainWindow.Me?.Show();*/

        //退出
        Close();
    }

    /// <summary>
    /// 右键退出
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ExitItem_OnClick(object sender, RoutedEventArgs e)
    {
        //退出
        Close();
        Application.Current.Shutdown();
        Environment.Exit(0);
    }

    private void Control_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // IsVpn = !IsVpn;
        // YiKit.IniWrite("isvpn", IsVpn.ToString());
        ;
        tap = YiKit.GetInterface(sequence: true);
        int? index = tap?.GetIPProperties()?.UnicastAddresses.Count - 1;
        if (index == null)
            return;
        IPAddress? add = tap?.GetIPProperties()?.UnicastAddresses[(int)index]?.Address;
        if (add != null)
        {
            try
            {
                YiKit.IniWrite("showip",
                    (add + "").Substring(0, add.ToString().LastIndexOf(".", StringComparison.Ordinal)));
            }
            catch (Exception exception)
            {
                YiKit.Print(exception);
            }
        }

        YiKit.ShowToast(tap?.Name + "\n" + tap?.GetIPProperties()?.UnicastAddresses[(int)index]?.Address,
            l: (int)((int)Left + Width / 2), t: (int)((int)Top + 30));
        // YiKit.ShowToast(IsVpn ? "切换专网" : "显示公网", l: (int) ((int) Left + Width / 2), t: (int) ((int) Top));
    }


    private void PintBtn_OnClick(object sender, RoutedEventArgs e)
    {
        AllowsTransparency = !AllowsTransparency;
        YiKit.Print("dsds");
        //鼠标穿透
        SetWindowLong(new WindowInteropHelper(this).Handle, -20, 0x20);
    }

    private void TaskmgrItem_OnClick(object sender, RoutedEventArgs e)
    {
        YiKit.StartProcessAsync("taskmgr");
    }


    private List<string> ShowInputDialogInWindow(string name, params string[] names)
    {
        InputDialog inputDialog = new InputDialog(name, names);
        List<string> strs = null;

        inputDialog.Result += (sender, _) => { strs = sender as List<string>; };
        Point screenCoordinates = PointToScreen(new Point(0, 0));


        inputDialog.Left = screenCoordinates.X - inputDialog.Width / 2;
        inputDialog.Top = screenCoordinates.Y - inputDialog.Height;
        bool? b = inputDialog.ShowDialog();
        if (b is false)
        {
            return null;
        }

        return strs;
    }

    private List<string> ShowInputDialog(string name, params string[] names)
    {
        InputDialog inputDialog = new InputDialog(name, names);
        List<string> strs = null;

        inputDialog.Result += (sender, _) => { strs = sender as List<string>; };
        inputDialog.Left = r.Width / 2 - inputDialog.Width / 2;
        inputDialog.Top = r.Height / 2 - inputDialog.Height / 2;
        bool? b = inputDialog.ShowDialog();
        if (b is false)
        {
            return null;
        }

        return strs;
    }


    private void PauseItem_OnClick(object sender, RoutedEventArgs e)
    {
        Suspend();
    }


    public void OpenWin()
    {
        WindowsIdentity id = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(id);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            if (MainWindow.Me == null || PresentationSource.FromVisual(MainWindow.Me) == null)
            {
                new MainWindow().Show();
            }
            else

                MainWindow.Me?.Show();
        }
        else
        {
            // 获取当前进程的启动信息
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                FileName = Process.GetCurrentProcess().MainModule.FileName,
                Arguments = "main",
                Verb = "runas" // 这里设置启动动作为“以管理员身份运行”
            };

// 以管理员身份启动当前进程
            try
            {
                Process.Start(startInfo);
            }
            catch (Win32Exception)
            {
                // 如果用户点击“否”或关闭UAC窗口，则会抛出Win32Exception异常
                // 此时应该在代码中处理这种情况
                // 例如给用户提示或者采取其他操作
                return;
            }

            Close();
        }
    }

    private List<string> suspendsList = [];

    public void Suspend()
    {
        WindowsIdentity id = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(id);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
        }
        else
        {
            // 获取当前进程的启动信息
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                FileName = Process.GetCurrentProcess().MainModule.FileName,
                Arguments = "ProgressSuspend",
                Verb = "runas" // 这里设置启动动作为“以管理员身份运行”
            };

// 以管理员身份启动当前进程
            try
            {
                Process.Start(startInfo);
            }
            catch (Win32Exception)
            {
                // 如果用户点击“否”或关闭UAC窗口，则会抛出Win32Exception异常
                // 此时应该在代码中处理这种情况
                // 例如给用户提示或者采取其他操作
                return;
            }

            Close();
        }


        List<string> strs = ShowInputDialog("要暂停的进程名字例：window.exe或window");
        if (strs == null || strs.Count == 0)
        {
            return;
        }

        string str = strs[0];
        if (str.EndsWith(".exe"))
        {
            str = str.Substring(0, str.Length - 4);
        }

// 通过进程名查找并挂起目标进程
        Process[] targetProcesses = Process.GetProcessesByName(str);
        if (targetProcesses.Length == 0)
        {
            targetProcesses = Process.GetProcessesByName(str + "64");
        }

        bool isSuspend = false;
        foreach (Process p in targetProcesses)
        {
            if (suspendsList.Contains(p.ProcessName))
            {
                YiKit.ProgressResume(p);
                suspendsList.Remove(p.ProcessName);
            }
            else
            {
                isSuspend = true;
                YiKit.ProgressSuspend(p);
                suspendsList.Add(p.ProcessName);
            }
        }

        if (isSuspend)
        {
            YiKit.ShowToast("已挂起" + targetProcesses.Length + "个" + str + "进程！");
        }
        else
            YiKit.ShowToast("已恢复" + targetProcesses.Length + "个" + str + "进程！");
    }

    public void Resume()
    {
        WindowsIdentity id = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new WindowsPrincipal(id);
        if (principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
        }
        else
        {
            // 获取当前进程的启动信息
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                FileName = Process.GetCurrentProcess().MainModule.FileName,
                Arguments = "ProgressResume",
                Verb = "runas" // 这里设置启动动作为“以管理员身份运行”
            };

// 以管理员身份启动当前进程
            try
            {
                Process.Start(startInfo);
            }
            catch (Win32Exception)
            {
                // 如果用户点击“否”或关闭UAC窗口，则会抛出Win32Exception异常
                // 此时应该在代码中处理这种情况
                // 例如给用户提示或者采取其他操作
                return;
            }

            Close();
        }


        List<string> strs = ShowInputDialog("要恢复暂停的进程名字例：leigod.exe或leigod");
        if (strs == null || strs.Count == 0)
        {
            return;
        }

        string str = strs[0];
        if (str.EndsWith(".exe"))
        {
            str = str.Substring(0, str.Length - 4);
        }

// 通过进程名查找并挂起目标进程
        Process[] targetProcesses = Process.GetProcessesByName(str);
        foreach (Process p in targetProcesses)
        {
            YiKit.ProgressResume(p);
        }
    }

    private void ResumItem_OnClick(object sender, RoutedEventArgs e)
    {
        Resume();
    }


    private string? clbname = "";

    private void UnlockClipboardItemOnClick(object sender, RoutedEventArgs e)
    {
        // YiKit.StartProcessSync("adb -d shell appops set space.yizhu.njapp SYSTEM_ALERT_WINDOW allow;adb -d shell pm grant space.yizhu.njapp android.permission.READ_LOGS;adb shell am force-stop space.yizhu.njapp;");
    }

    private void ClipboardItemOnClick(object sender, RoutedEventArgs e)
    {
        if (_clipboard == 1)
        {
            _clipboard = 0;
            ((sender as MenuItem)!).Header = "已关闭剪切板监听";
            ((sender as MenuItem)!).IsChecked = false;
            YiKit.CloseClipboardMonitor(OnClipboardUpdated);
            YiKit.IniWrite("clipboard", area: "ini", value: 0 + "");
            if (YiWscClient != null && YiWscClient.State == WebSocketState.Open)
            {
                CloseWb();
            }
        }
        else
        {
            _clipboard = 1;
            clbname =
                YiKit.ShowInputDialog("同步剪贴板",
                    "请输入相同的匹配码或者相同的名称,系统会自动记忆:" + YiKit.IniGet("clbname", "ini", def: ""))?[0] + "";
            if (string.IsNullOrEmpty(clbname)) //如果没有输入或者输入为空
            {
                return;
            }

            if (YiWscClient != null && YiWscClient.State != WebSocketState.Open)
                CloseWb();
            StartWb();

            YiKit.IniWrite("clbname", clbname, "ini");
            YiKit.ClipboardMonitor(this, OnClipboardUpdated);
            lastClipboardText = YiKit.GetClipboardText();
            // ((sender as MenuItem)!).Header = "已开启剪切板监听";
        }

        YiKit.IniWrite("clipboard", value: _clipboard + "", area: "ini");
    }

    // 新建一个剪切板窗口，窗口实时读取剪切板并记录不同历史记录，并具有自动写入到文件的选项
    private void ClipboardItemOnRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // 防止触发左键点击事件
        var clipboardWindow = new ClipboardHistoryWindow();
        clipboardWindow.Show();
    }
    /// <summary>把温度写到对应 TextBlock（磁盘/内存/处理器）。</summary>
    private void ApplyTempToUi(System.Windows.Controls.TextBlock target, object b, string tip)
    {
        if (target == null || b == null) return;
        if (b.ToString() == "0") return;

        YiKit.RunOnUIThread(() =>
        {
            string tempText;
            double? num = null;
            try
            {
                num = Convert.ToDouble(b);
                tempText = Math.Round(num.Value) + "℃";
            }
            catch
            {
                tempText = b.ToString().Split(',')[0];
            }

            if (tempText == target.Text) return;
            target.Text = tempText;
            string src = YiKit.LastTempSource;
            target.ToolTip = string.IsNullOrEmpty(src)
                ? tip + ": " + tempText
                : tip + ": " + tempText + "（" + src + "）";
            if (num.HasValue)
            {
                if (num.Value > 80)
                    target.Foreground = new SolidColorBrush(Colors.Red);
                else if (num.Value > 60)
                    target.Foreground = new SolidColorBrush(Colors.Yellow);
                else
                    target.Foreground = new SolidColorBrush(Colors.White);
            }
        });
    }

    private static string lastClipboardText = "";

    private string traslateText = "-1";
    private string _cpyText = "";

    private void OnClipboardUpdated(object sender, EventArgs e)
    {
        if (_clipboard == 0) //剪贴板已关闭
        {
            return;
        }

        if (Clipboard.ContainsText())
        {
            string text = "";
            try
            {
                text = Clipboard.GetText();
                if (!isTranslateing)
                    if (lastClipboardText.Equals(text))
                        return;
            }
            catch (Exception)
            {
                Thread.Sleep(10);
                YiKit.Print("error:--------------------------------------------");
                try
                {
                    text = Clipboard.GetText();
                    if (!isTranslateing)
                        if (lastClipboardText.Equals(text))
                            return;
                }
                catch (Exception)
                {
                    YiKit.Print("error2:");
                }
            }

            if (_cpyText == text)
            {
                return;
            }

            YiKit.Print("剪贴板更新:" + text);
            // YiWscClient.Send("ait::"   + text);
            /*if (text == lastClipboardText)
            {
                return;
            }*/

            if (isTranslateing)
            {
                YiKit.SetClipboardText(lastClipboardText);
                if (TranslateDialog.Me == null)
                {
                    new TranslateDialog();
                }

                text = text.Trim();
                traslateText = text;
                string ty = "e2z";
                if (YiKit.IsEn(text))
                {
                    // YiWscClient.Send("ait::" + YiKit.RemoveSpecialCharacters(text));
                    ty = "e2z";
                }
                else
                    // YiKit.ShowToastWaring("不是英文");
                    ty = "z2e";

                if (YiWscClient.Send("ait:" + ty + ":" + text) == false)
                {
                    YiWscClient.Open((state => { YiWscClient.Send("ait:" + ty + ":" + text); }));
                }

                TranslateDialog.Me.ZhTx.Text = text;
                TranslateDialog.Me.EnTx.Text = "... ...";
                TranslateDialog.Me.show();
                // TranslateDialog.Me.Activate();
                isTranslateing = false;
                return;
            }
            else if (text == lastClipboardText || text.Length > 65535)
            {
                return;
            }
            else if (YiWscClient!.Send("cpy:" + clbname + ":" + text) == false)
            {
                YiWscClient.Open((state => YiWscClient.Send("cpy:" + clbname + ":" + text)));
                ;
            }

            Thread.Sleep(100);
            lastClipboardText = text;


            // YiKit.Print(text);
        }
        // string[] fs = data.GetFormats();
        // tb.Text += $"\n数据格式：{string.Join("、", fs)}";
    }

    #region websocket

    public static YiWebSocketClient? YiWscClient = null;

    public void CloseWb()
    {
        YiWscClient?.Close();
    }

    private string[] yiwsUrl = ["ws://27.221.101.137:10011/", "ws://10.10.107.23:10011/", "ws://172.27.1.2:10011/"];
    private int yiwsUrlIndex = 0;

    public void StartWb()
    {
        if (YiWscClient != null)
        {
            YiWscClient.Reconnect();
            return;
        }

        YiKit.RunThread((state =>
        {
            YiWscClient = new YiWebSocketClient(yiwsUrl, clbname);
            // YiWscClient = new YiWebSocketClient("ws://172.27.1.3:10011/", clbname);
            YiWscClient.OnOpen += YiWscClientOnOpen;
            YiWscClient.OnMessage += YiWscClientOnMessage;
            YiWscClient.OnClose += YiWscClientOnClose;
            YiWscClient.OnError += YiWscClientOnError;
            YiWscClient.Open();
        }));
    }

    private void YiWscClientOnError(object sender, Exception ex)
    {
        YiKit.RunOnUIThread(() =>
        {
            ClipboardMenuItem.Header = "剪切板异常";
            ClipboardMenuItem.IsChecked = false;
            yiwsUrlIndex = new Random().Next(0, yiwsUrl.Length - 1);
            ClipboardMenuItem.ToolTip = ex.Message;
        });
    }

    private void YiWscClientOnClose(object sender, EventArgs e)
    {
        YiKit.RunOnUIThread(() =>
        {
            ClipboardMenuItem.Header = "剪切板已关闭";
            ClipboardMenuItem.IsChecked = false;
            YiKit.ShowToastWaring("剪切板共享已关闭");
        });
    }

    private void YiWscClientOnMessage(object sender, string data)
    {
        //处理的消息错误将会忽略
        try
        {
            //需要优化
            //处理消息
            if (data.StartsWith("cpy:"))
            {
                /*YiKit.RunThread((delegate(object state)
                {
                    YiKit.SetClipboardText(data.Remove(0,4));
                }));*/
                _cpyText = data.Remove(0, 4);
                Thread th = new Thread(delegate() { YiKit.SetClipboardText(data.Remove(0, 4)); });
                th.TrySetApartmentState(ApartmentState.STA);
                th.Start();
                th.Join();
            }
            else if (data.StartsWith("ait:"))
            {
                /*YiKit.RunThread((delegate(object state)
                {
                    YiKit.SetClipboardText(data.Remove(0,4));
                }));*/

                YiKit.Print(data);
                YiKit.RunOnUIThread(() =>
                {
                    data = data.Substring(4);
                    if (data.IndexOf(":") > -1)
                    {
                        data = data.Substring(data.IndexOf(":") + 1);
                    }

                    if (TranslateDialog.Me != null)
                    {
                        TranslateDialog.Me.EnTx.Text = data.Replace(':', ' ');
                    }
                    else
                        YiKit.ShowToast(data.Replace(':', ' '));

                    // YiKit.ShowToast("已开启剪切板共享");
                });
            }
        }
        catch (Exception)
        {
            // ignored
        }
    }


    private void YiWscClientOnOpen(object sender, EventArgs e)
    {
        YiWscClient.Send("ini:" + clbname);
        YiKit.RunOnUIThread(() =>
        {
            string msg = "已开启";
            if (_clipboard == 1)
            {
                ClipboardMenuItem.Header = "已开启剪切板";
                ClipboardMenuItem.IsChecked = true;
                msg += "剪切板共享 ";
            }

            if (_translate == 1)
            {
                TranslateMenuItem.Header = "已开启翻译";
                TranslateMenuItem.IsChecked = true;
                msg += " 实时翻译翻译";
            }

            YiKit.ShowToast(msg);
        });
    }

    #endregion

    private List<string> hiddenList = new List<string>();

    private void HideItem_OnClick(object sender, RoutedEventArgs e)
    {
        List<string> strs = ShowInputDialog("要隐藏的进程名字例：window.exe或window");
        if (strs == null || strs.Count == 0)
        {
            return;
        }

        string str = strs[0];
        if (str.EndsWith(".exe"))
        {
            str = str.Substring(0, str.Length - 4);
        }

// 通过进程名查找并挂起目标进程
        Process[] targetProcesses = Process.GetProcessesByName(str);
        if (targetProcesses.Length == 0)
        {
            targetProcesses = Process.GetProcessesByName(str + "64");
        }

        bool isHidden = false;
        foreach (Process p in targetProcesses)
        {
            if (hiddenList.Contains(p.ProcessName))
            {
                YiKit.ShowWindow(p);
                hiddenList.Remove(p.ProcessName);
            }
            else
            {
                hiddenList.Add(p.ProcessName);
                YiKit.ShowWindow(p, false);
                isHidden = true;
            }
        }

        if (isHidden)
        {
            YiKit.ShowToast("已隐藏" + targetProcesses.Length + "个" + str + "窗口！");
        }
        else
            YiKit.ShowToast("已恢复" + targetProcesses.Length + "个" + str + "窗口！");
    }

    private void SleepItem_OnClick(object sender, RoutedEventArgs e)
    {
        var ret = YiKit.ShowInputDialog($"监控刷新时间",
            "监控刷新时间:" + (YiKit.IniGet("sleep", "ini", def: "1")));
        if (ret == null) return;
        string? s = ret?[0];
        if (string.IsNullOrEmpty(s))
        {
            return;
        }

        try
        {
            if (Convert.ToSingle(s) <= 0f)
            {
                YiKit.ShowToastWaring("时间不能小于0, 设置失败!");
                return;
            }

            _sleep = (int)(Convert.ToSingle(s) * 1000);
        }
        catch (Exception exception)
        {
            YiKit.ShowToastError("时间只可为数字, 设置失败!");
            return;
        }


        YiKit.IniWrite("sleep", s, "ini");
    }

    private void ZoomItem_OnClick(object sender, RoutedEventArgs e)
    {
        var ret = YiKit.ShowInputDialog($"设置放大或缩小倍数",
            "设置放大或缩小倍数:" + YiKit.IniGet("zoompet", "ini", def: "1"));
        if (ret == null) return;
        string? s = ret?[0];
        
        if (string.IsNullOrEmpty(s))
        {
            return;
        }

        try
        {
            if (Convert.ToSingle(s) <= 0f)
            {
                YiKit.ShowToastWaring("倍数不能小于0, 设置失败!");
                return;
            }

            _zoom = (Convert.ToSingle(s));
        }
        catch (Exception exception)
        {
            YiKit.ShowToastError("时间只可为数字, 设置失败!");
            return;
        }


        YiKit.IniWrite("zoompet", s, "ini");
        this.Width = w * _zoom;
        this.Height = h * _zoom;
    }

    private void RuxingItem_OnClick(object sender, RoutedEventArgs e)
    {
    }


    bool isTranslateing = false;
    bool isTranslate = false;

    private void TranslateItemOnClick(object sender, RoutedEventArgs e)
    {
        if (isTranslate)
        {
            YiKit.KeyboardMonStop();
            isTranslate = false;
            YiKit.IniWrite("translate", "0", "ini");
            ((sender as MenuItem)!).Header = "翻译已关闭";
            ((sender as MenuItem)!).IsChecked = false;
        }
        else
        {
            isTranslate = true;
            YiKit.KeyboardMon();
            YiKit.ClipboardMonitor(this, OnClipboardUpdated);
            lastClipboardText = YiKit.GetClipboardText();
            if (TranslateDialog.Me == null)
            {
                new TranslateDialog();
            }

            YiKit.IniWrite("translate", "1", "ini");
            ((sender as MenuItem)!).Header = "已开启翻译";
            ((sender as MenuItem)!).IsChecked = true;
        }
    }

    private void TranslateShowItemOnClick(object sender, RoutedEventArgs e)
    {
        if (TranslateDialog.Me == null)
        {
            TranslateDialog.Me = new TranslateDialog();
            TranslateDialog.Me.Show();
        }
        else
        {
            TranslateDialog.Me.Show();
            TranslateDialog.Me.Activate();
        }
    }
//获取是否有全屏应用

    private void WallPaperItem_OnClick(object sender, RoutedEventArgs e)
    {
        MouseButtonEventArgs mouseArgs = e as MouseButtonEventArgs;
        if (mouseArgs != null)
        {
            if (mouseArgs.RightButton == MouseButtonState.Pressed)
            {
                WallPaper.me.ShowFile();
                WallPaper.me.Show();
                WallPaper.me.Activate();
                return;
            }
            else if (mouseArgs.LeftButton == MouseButtonState.Pressed)
            {
                ;
            }
        }

        if (WallPaper.me == null)
        {
            new WallPaper();
            WallPaper.me.Show();
            WallPaper.me.Activate();
            YiKit.IniWrite("wallpaper", "1");
            (sender as MenuItem).IsChecked = true;
            _isWallPage = true;
            if (!string.IsNullOrEmpty(YiKit.IniGet("Source", "WallPaper")))
            {
                // WallPaper.me.ShowFile();
            }
            // WallPaper.me.ShowFile();
        }
        else
        {
            _isWallPage = false;
            YiKit.IniWrite("wallpaper", "0");
            (sender as MenuItem).IsChecked = false;
            WallPaper.me.Close();
            WallPaper.me = null;
        }
    }


    private void MemoMenuItem_OnClickItemOnClick(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(YiKit.GetAppStartPath() + "Tools/poolmon.exe"))
        {
            return;
        }

        //内存分析
        YiKit.StartProcessSync(YiKit.GetAppStartPath() + "Tools/poolmon.exe  -b", end: (state =>
        {
            var ret = YiKit.ShowInputDialog("系统后台执行，执行完后弹窗", "tag", " Bytes代表TAG占用总内存，frees代表已经释放的内存()为实时的更新值",
                "需要根据自己的经验判断到底是哪个tag，条目出现灰底代表内存有变动 ", "经常变动且frees一直没增加的有极大可能内存泄露，查询需要等待1分钟");

            if (ret == null)
            {
                YiKit.ProgressEnd("poolmon.exe");
                return;
            }

            YiKit.ShowToastWaring("请耐心等待，系统将会分析出当前TAG下的所有程序\n右键可以复制内容到剪贴板\n自己根据系统指出的路径判断是否是自己安装的程序，卸载即可", time: 60);
            // findstr /m /l /s NpCD c:/ *.sys
            var o = YiKit.RunCmd("findstr /m /l /s " +
                                 ret[0] +
                                 " c:/ *.sys");

            YiKit.ProgressEnd("poolmon.exe");
            YiKit.ShowToastWaring(o as string, time: 60);
        }));
    }

    private void ChangeSizeMenuItem_OnClickItemOnClick(object sender, RoutedEventArgs e)
    {
        List<string> strs = YiKit.ShowInputDialog("设置其他窗口的大小", "进程名称", "宽", "高", "x", "y", "是否置顶");
        if (null == strs || strs.Count < 5)
        {
            return;
        }

        try
        {
            if (Convert.ToSingle(strs[1]) <= 0f)
            {
                YiKit.ShowToastWaring("宽不能小于0, 设置失败!");
                return;
            }

            string str = strs[0];
            if (str.EndsWith(".exe"))
            {
                str = str.Substring(0, str.Length - 4);
            }

// 通过进程名查找并挂起目标进程
            Process[] targetProcesses = Process.GetProcessesByName(str);
            if (targetProcesses.Length == 0)
            {
                str += "64";

                targetProcesses = Process.GetProcessesByName(str);
            }

            YiKit.IniWrite("changeSize", str + "," + strs[1] + "," + strs[2]);
            bool isHidden = false;
            foreach (Process p in targetProcesses)
            {
                p.Refresh();
                IntPtr hwnd = p.MainWindowHandle;
                if (hwnd.ToInt32() != 0)
                {
                    YiKit.SetWindowPos(hwnd, string.IsNullOrEmpty(strs[5]) ? IntPtr.Zero : YiKit.WindowPosTOPMOST,
                        Convert.ToInt32(strs[3]), Convert.ToInt32(strs[4]),
                        Convert.ToInt32(strs[1]), Convert.ToInt32(strs[2]), 0x0040);
                }

                // YiKit.SetWindowPos(p.MainWindowHandle, 0, 0, 0, Convert.ToInt32(strs[1]), Convert.ToInt32(strs[2]), 0x0040);
            }
        }
        catch (Exception exception)
        {
            YiKit.ShowToastError("时间只可为数字, 设置失败!");
            return;
        }
    }

    private void UIElement_OnPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (WallPaper.me == null)
        {
            new WallPaper();
        }

        WallPaper.me.ShowFile();
    }

    string subKey = "计算机\\HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Power\\PowerSettings";

    private void PowerItem_OnClick(object sender, RoutedEventArgs e)
    {
        string[] sks = YiKit.RegeditLmGetSubKey(subKey);
        if (sks == null)
        {
            return;
        }

        foreach (string sk in sks)
        {
            string[] pks = YiKit.RegeditLmGetSubKey(subKey + "\\" + sk);
            foreach (string pk in pks)
            {
                YiKit.RegeditLmEdit(subKey + "\\" + sk + "\\" + pk, "Attributes", 2);
            }
        }
    }

    /// <summary>
    /// 中心放大
    /// </summary>
    private CentZoom _centZoom;

    private void CentZomm_OnItemClick(object sender, RoutedEventArgs e)
    {
        if (IsRightClick(e))
        {
            if (YiKit.IniGetDouble("放大倍数", area: InputDialog.are, def: -1) == -1)
            {
                YiKit.IniWrite("放大倍数", "2.0", area: InputDialog.are);
                YiKit.IniWrite("放大透明度", "0.8", area: InputDialog.are);
                YiKit.IniWrite("放大刷新率", "30", area: InputDialog.are);
                YiKit.IniWrite("放大左偏移", "0", area: InputDialog.are);
                YiKit.IniWrite("放大上偏移", "0", area: InputDialog.are);
                YiKit.IniWrite("放大显示大小", "100", area: InputDialog.are);
                YiKit.IniWrite("放大显示左偏移", "0", area: InputDialog.are);
                YiKit.IniWrite("放大显示上偏移", "0", area: InputDialog.are);
            }

            YiKit.ShowInputDialog("放大显示设置（不要将放大显示和放大区域重叠）", "放大倍数", "放大透明度", "放大刷新率", "放大左偏移", "放大上偏移", "放大显示大小",
                "放大显示左偏移", "放大显示上偏移");
            _centZoom?.changeSetStyle();
        }
        else

        {
            var isc = OnItemCheckedProgress(sender as MenuItem);
            _centZoom ??= new CentZoom();
            if (isc) _centZoom.Show();
            else _centZoom.Hide();
        }
    }

    /// <summary>
    /// 中心准星
    /// </summary>
    private Crosshair _crosshair;

    private void Crosshair_OnItemClick(object sender, RoutedEventArgs e)
    {
        if (IsRightClick(e))
        {
            if (YiKit.IniGetDouble("准星上偏移", area: InputDialog.are, def: -1) == -1)
            {
                YiKit.IniWrite("准星上偏移", "0", area: InputDialog.are);
                YiKit.IniWrite("准星左偏移", "0", area: InputDialog.are);
                YiKit.IniWrite("准星大小", "2", area: InputDialog.are);
                YiKit.IniWrite("准星颜色", "0xffff", area: InputDialog.are);
            }

            YiKit.ShowInputDialog("设置准星坐标偏移和大小", "准星上偏移", "准星左偏移", "准星大小", "准星颜色");
            _crosshair?.changeStyle();
        }
        else
        {
            bool isc = OnItemCheckedProgress(sender as MenuItem);
            _crosshair ??= new Crosshair();
            if (isc) _crosshair.Show();
            else _crosshair.Hide();
        }
    }

    /// <summary>
    /// 处理MeunItem显示样式,并返回当前MenuItem是否被选中/激活
    /// </summary>
    /// <param name="mi">menuItem</param>
    /// <param name="name">保存到ini文件的key</param>
    /// <returns>当前MenuItem是否被选中/激活</returns>
    private bool OnItemCheckedProgress(MenuItem? mi, string? name = null)
    {
        if (null == mi) return false;
        mi.IsChecked = !mi.IsChecked;
        name ??= mi.Name as string;
        name ??= mi.Header as string;
        if (null != name) YiKit.IniWrite(name.ToLower(), mi.IsChecked ? "1" : "0");
        return mi.IsChecked;
    }

    /// <summary>
    /// 查询对应 Header 的 MenuItem，不区分大小写
    /// </summary>
    /// <param name="parent">可以是 Grid、Menu、MenuItem 等任意支持子元素的容器</param>
    /// <param name="targetHeader">要查找的菜单项标题</param>
    /// <returns>匹配的 MenuItem 对象，未找到返回 null</returns>
    public MenuItem FindMenuItem(DependencyObject parent, string targetHeader)
    {
        if (parent == null)
            return null;

        // 如果当前节点本身就是 MenuItem 并且匹配，则直接返回
        if (parent is MenuItem menuItem)
        {
            if (string.Equals(menuItem.Header?.ToString(), targetHeader, StringComparison.CurrentCultureIgnoreCase))
            {
                return menuItem;
            }
        }

        // 判断是否为 ItemsControl 类型（如 Menu、MenuItem）
        if (parent is ItemsControl itemsControl)
        {
            foreach (object item in itemsControl.Items)
            {
                if (item is MenuItem childItem)
                {
                    var result = FindMenuItem(childItem, targetHeader);
                    if (result != null)
                        return result;
                }
            }
        }
        else
        {
            // 遍历视觉树查找所有子元素（适用于 Grid、StackPanel 等非 ItemsControl 容器）
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (var i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                var result = FindMenuItem(child, targetHeader);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    /// <summary>
    /// 判断是否右键点击
    /// </summary>
    /// <param name="e"></param>
    /// <returns>true是右键，false是左键或判断失败</returns>
    public bool IsRightClick(RoutedEventArgs e)
    {
        if (e is not MouseButtonEventArgs mouseArgs) return false;
        return mouseArgs.RightButton == MouseButtonState.Pressed;
    }

    private void IcoItem_OnClick(object sender, RoutedEventArgs e)
    {
        List<string> strs = YiKit.ShowInputDialog(
            "图像转换为ICO图标，文件和文件夹二选一，同时选，取文件夹",
            "选择源文件位置",
            "选择源文件夹位置",
            "ico尺寸:256,128,64,48,32,16");

        if (strs == null || strs.Count < 1)
        {
            return;
        }

        YiKit.RunThread((state) =>
        {
            int[] sizes = { 256, 128, 64, 48, 32, 16 };
            string path = strs[0];
            string pathDir = strs[1];
            string icoSizeStr = strs[2];

            // 解析尺寸参数
            if (!string.IsNullOrWhiteSpace(icoSizeStr))
            {
                try
                {
                    icoSizeStr = icoSizeStr.Replace("，", ",");
                    sizes = Array.ConvertAll(icoSizeStr.Split(','), int.Parse);
                }
                catch (Exception ex)
                {
                    YiKit.ShowToastError("尺寸填写失败，将使用默认值：256,128,64,48,32,16");
                    Console.WriteLine("尺寸解析失败：" + ex.Message);
                }
            }

            List<string> imageFiles = new List<string>();

            // 判断是文件还是文件夹
            if (!string.IsNullOrEmpty(pathDir) && Directory.Exists(pathDir))
            {
                // 获取文件夹中所有支持的图像文件
                var files = Directory.GetFiles(pathDir);
                foreach (var file in files)
                {
                    if (IsImageFile(file))
                        imageFiles.Add(file);
                }
            }
            else if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                if (IsImageFile(path))
                    imageFiles.Add(path);
            }

            if (imageFiles.Count == 0)
            {
                YiKit.ShowToastError("未找到有效的图像文件！");
                return;
            }

            // 开始转换每个图像
            foreach (var pngPath in imageFiles)
            {
                try
                {
                    using (Bitmap original = new Bitmap(pngPath))
                    {
                        string icoPath = Path.ChangeExtension(pngPath, ".ico");

                        using (FileStream fs = new FileStream(icoPath, FileMode.Create))
                        {
                            BinaryWriter writer = new BinaryWriter(fs);

                            // 写入 ICO 文件头
                            writer.Write((short)0); // Reserved
                            writer.Write((short)1); // Type: icon (.ICO)
                            writer.Write((short)sizes.Length); // Number of images

                            long[] directoryEntriesPos = new long[sizes.Length];
                            int imageIndex = 0;

                            // 预留目录条目位置
                            foreach (int size in sizes)
                            {
                                directoryEntriesPos[imageIndex++] = fs.Position;
                                writer.Write(new byte[16]); // 占位符
                            }

                            // 逐个写入图标数据
                            imageIndex = 0;
                            foreach (int size in sizes)
                            {
                                long imageDataOffset = fs.Position;
                                using (Bitmap resized = new Bitmap(size, size))
                                using (Graphics g = Graphics.FromImage(resized))
                                {
                                    //g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                    //g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                                    // g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                                    g.DrawImage(original, 0, 0, size, size);

                                    using (MemoryStream pngStream = new MemoryStream())
                                    {
                                        resized.Save(pngStream, ImageFormat.Png);
                                        byte[] pngData = pngStream.ToArray();

                                        // 回到目录条目位置并写入信息
                                        fs.Seek(directoryEntriesPos[imageIndex++], SeekOrigin.Begin);
                                        writer.Write((byte)size); // Width
                                        writer.Write((byte)size); // Height
                                        writer.Write((byte)0); // Color count (>=256)
                                        writer.Write((byte)0); // Reserved
                                        writer.Write((short)1); // Planes
                                        writer.Write((short)32); // Bits per pixel
                                        writer.Write(pngData.Length); // Size of image data
                                        writer.Write((int)imageDataOffset); // Offset to image data

                                        // 返回末尾继续写图像数据
                                        fs.Seek(imageDataOffset, SeekOrigin.Begin);
                                        writer.Write(pngData);
                                    }
                                }
                            }
                        }

                        // 主线程更新UI
                        YiKit.ShowToast($"✅ 已生成 ICO：{icoPath}");
                    }
                }
                catch (Exception ex)
                {
                    YiKit.ShowToastError($"❌ 转换失败：{Path.GetFileName(pngPath)}\n错误：{ex.Message}");
                }
            }

            YiKit.ShowToast($"🎉 共转换 {imageFiles.Count} 个文件");
        });
    }

    public static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tiff"
    };

    public static bool IsImageFile(string filePath)
    {
        string ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }

    private void AdbItem_OnClick(object sender, RoutedEventArgs e)
    {
        List<string> strs = YiKit.ShowInputDialog(
            "apk（电视）安装器（需开启adb调试）",
            "电视IP地址",
            "选择apk应用文件位置"
        );
        if (strs == null || strs.Count < 1)
        {
            return;
        }

        String ip = strs[0];
        String apk = strs[1];

        YiKit.RunThread((state =>
        {
            var ret = "";
            try
            {
                ret = YiKit.RunCmd("adb.exe connect " + ip);
            }
            catch (Exception exception)
            {
                YiKit.ShowToastError("运行失败，系统不具备安装条件");
            }

            if (ret.Contains(" connected"))
            {
                YiKit.ShowToastOther("请观察电视是否弹出允许或确定对话框，请允许或确定！", time: 30);
                YiKit.RunCmd("adb install" + apk);
            }
            else
            {
                YiKit.ShowToastError("请在电视设置->用户（或安全选项卡）->打开允许调试或adb调试！");
            }
        }));
    }

    private void OgvItem_OnClick(object sender, RoutedEventArgs e)
    {
        //ffmpeg -i input.ogv -vf fps=30 -c:v png -pix_fmt rgba output_%04d.png


        string type = "媒体文件";
        if ((sender as MenuItem).Header.ToString().Contains("Gif")) type = "gif";
        List<string> strs = YiKit.ShowInputDialog(
            type + "转png联系表",
            "媒体类型（gif忽略此项）:ogv",
            "文件位置（可为空）：",
            "文件夹位置（和文件二选一）：",
            "列数(自动计算请填0):0",
            "目标宽度等比缩放:256",
            "帧率:24",
            "删除黑色背景强度:black:0.005",
            "是否删除缓存图片："
        );
        if (strs == null || strs.Count < 1)
        {
            YiKit.ShowToastError("参数错误");
            return;
        }

        if ((sender as MenuItem).Header.ToString().Contains("Gif")) type = "gif";
        else type = strs[0];

        string ogvFilePath = strs[1].Trim();
        int columns = int.TryParse(strs[3], out var col) ? col : 0;
        int targetWidth = int.TryParse(strs[4], out var width) ? width : 256;
        int fps = int.TryParse(strs[5], out var f) ? f : 30;
        string color = "black";

        double delDark = 0.005;
        if (strs[6].Contains(":"))
        {
            string[] colors = strs[6].Split(':');
            delDark = double.TryParse(colors[1], out var d) ? d : 0.005;
            color = colors[0];
        }
        else
            delDark = double.TryParse(strs[6], out var d) ? d : 0.005;

        string ogvFolder = strs[2].Trim();
        bool isDel = false;
        bool.TryParse(strs[7], out isDel);

        YiKit.RunThread(async s =>
        {
            try
            {
                List<string> ogvFiles = new List<string>();

                // 如果提供了文件夹，则优先使用文件夹中的 .ogv 文件
                if (!string.IsNullOrEmpty(ogvFolder) && Directory.Exists(ogvFolder))
                {
                    ogvFiles.AddRange(Directory.GetFiles(ogvFolder, "*." + type, SearchOption.TopDirectoryOnly));
                }

                // 如果提供了单个文件，则加入列表
                if (!string.IsNullOrEmpty(ogvFilePath) && File.Exists(ogvFilePath))
                {
                    ogvFiles.Add(ogvFilePath);
                }

                if (ogvFiles.Count == 0)
                {
                    // 如果没有找到任何 .fla 文件，则查找 .zip 文件并尝试提取其中的 .fla

                    var zipFiles = Directory.GetFiles(ogvFolder, "*.zip", SearchOption.TopDirectoryOnly);

                    foreach (var zipFile in zipFiles)
                    {
                        // 确保临时目录干净
                        // if (Directory.Exists(extractTempDir)) Directory.Delete(extractTempDir, true);
                        // Directory.CreateDirectory(extractTempDir);

                        // 提取 ZIP 中的 .type 文件
                        using (var archive = ZipFile.OpenRead(zipFile))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                if (entry.FullName.EndsWith("." + type, StringComparison.OrdinalIgnoreCase))
                                {
                                    string uniqueName = $"{Path.GetFileNameWithoutExtension(zipFile)}.{type}";
                                    string destPath = Path.Combine(ogvFolder, uniqueName);
                                    entry.ExtractToFile(destPath);
                                    ogvFiles.Add(destPath);
                                }
                            }
                        }
                    }
                }

                if (ogvFiles.Count == 0)
                {
                    YiKit.ShowToastError($"未找到有效的{type}文件");
                    return;
                }

                foreach (var ogvFile in ogvFiles)
                {
                    string fileDir = Path.GetDirectoryName(ogvFile);
                    string outputDir = Path.Combine(
                        fileDir,
                        Path.GetFileNameWithoutExtension(ogvFile)
                    );

                    string contactSheetPath = Path.Combine(fileDir, Path.GetFileNameWithoutExtension(ogvFile) + ".png");

                    Directory.CreateDirectory(outputDir);

                    // 创建输出目录
                    // if (Directory.Exists(framesDir)) Directory.Delete(framesDir, true);
                    // Directory.CreateDirectory(framesDir);

                    // 使用 FFmpeg 提取帧（RGBA 透明通道）
                    //ffmpeg -i he1.ogv   -vf "scale={targetWidth}:-1,format=rgba,colorkey=black:0.005:0.1,fps={fps}" "D:\soft\Godot\projects\jie-(4.4)\anim\ogv\he1\frame_%04d.png"


                    string ffmpegArgs = null;
                    if (type.Equals("gif"))
                    {
                        ffmpegArgs =
                            $"-i \"{ogvFile}\" -vf \"scale={targetWidth}:-1,format=rgba,colorkey={color}:{delDark}:0.1\" \"{Path.Combine(outputDir, "frame_%04d.png")}\"";
                    }
                    else
                    {
                        ffmpegArgs =
                            $"-i \"{ogvFile}\" -vf \"scale={targetWidth}:-1,format=rgba,colorkey={color}:{delDark}:0.1,fps={fps}\"  \"{Path.Combine(outputDir, "frame_%04d.png")}\"";
                    }

                    if (ffmpegArgs == null)
                        return;

                    YiKit.RunCmd($"ffmpeg.exe {ffmpegArgs}");

                    if (!Directory.Exists(outputDir) || Directory.GetFiles(outputDir, "*.png").Length == 0)
                    {
                        YiKit.ShowToastError($"{Path.GetFileName(ogvFile)} 导出帧失败");
                        continue;
                    }

                    // 计算列数（如果用户设置为0则自动计算）
                    if (columns <= 0)
                    {
                        columns = (int)Math.Ceiling(Math.Sqrt(Directory.GetFiles(outputDir, "*.png").Length));
                    }

                    // 合成联系表

                    // 调用 pngPacker
                    pngPacker(outputDir, contactSheetPath, targetWidth, columns);

                    if (isDel)
                    {
                        YiKit.TryDeleteDir(outputDir);
                    }

                    YiKit.ShowToast($"文件{contactSheetPath}处理完成！");
                }

                YiKit.ShowToast($"全部 .{type} 处理完成！");
            }
            catch (Exception ex)
            {
                YiKit.ShowToastError("发生异常：" + ex.Message);
            }
        });
    }

    private void FlaItem_OnClick(object sender, RoutedEventArgs e)
    {
        List<string> strs = YiKit.ShowInputDialog(
            "fla转png联系表",
            "fla文件位置（可为空）：",
            "fla文件夹位置（和文件二选一）：",
            "列数(自动计算请填0)：",
            "目标宽度(等比缩放)：",
            "Animate.exe文件位置："
        );


        if (strs != null && strs.Count > 3)
        {
            YiKit.RunThread((s) =>
            {
                string flaFile = strs[0]; // 单个 .fla 文件（可为空）
                string folderPath = strs[1]; // fla文件夹位置（可能为空）
                int columns = 0;
                if (strs[2] != null && !string.IsNullOrEmpty(strs[2]))
                {
                    try
                    {
                        columns = int.Parse(strs[2]);
                    }
                    catch (Exception)
                    {
                        columns = -1;
                    }
                }

                int targetWidth = int.Parse(strs[3]);
                string animateExe = strs[4];

                // 准备要处理的 .fla 文件列表
                List<string> flaFilesToProcess = new List<string>();

                // 如果有单个 .fla 文件路径且存在，直接加入处理列表
                if (!string.IsNullOrWhiteSpace(flaFile) && File.Exists(flaFile))
                {
                    flaFilesToProcess.Add(flaFile);
                }

                // 如果指定了文件夹路径，尝试从该目录中查找 .fla 文件
                if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
                {
                    // 获取文件夹内所有 .fla 文件
                    var flasInFolder = Directory.GetFiles(folderPath, "*.fla", SearchOption.TopDirectoryOnly);
                    flaFilesToProcess.AddRange(flasInFolder);


                    // 如果没有找到任何 .fla 文件，则查找 .zip 文件并尝试提取其中的 .fla
                    if (flaFilesToProcess.Count == 0)
                    {
                        var zipFiles = Directory.GetFiles(folderPath, "*.zip", SearchOption.TopDirectoryOnly);

                        foreach (var zipFile in zipFiles)
                        {
                            string extractTempDir = Path.Combine(folderPath, Path.GetFileNameWithoutExtension(zipFile));

                            // 确保临时目录干净
                            // if (Directory.Exists(extractTempDir)) Directory.Delete(extractTempDir, true);
                            // Directory.CreateDirectory(extractTempDir);

                            // 提取 ZIP 中的 .fla 文件
                            using (var archive = ZipFile.OpenRead(zipFile))
                            {
                                foreach (var entry in archive.Entries)
                                {
                                    if (entry.FullName.EndsWith(".fla", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string uniqueName = $"{Path.GetFileNameWithoutExtension(zipFile)}.fla";
                                        string destPath = Path.Combine(folderPath, uniqueName);
                                        entry.ExtractToFile(destPath);
                                        flaFilesToProcess.Add(destPath);
                                    }
                                }
                            }
                        }
                    }
                }

                // 如果最终找到了需要处理的 .fla 文件，则启动异步任务处理每个文件
                if (flaFilesToProcess.Count > 0)
                {
                    YiKit.RunThread(async (s) =>
                    {
                        foreach (var file in flaFilesToProcess)
                        {
                            string outputDir = Path.Combine(Path.GetDirectoryName(file),
                                Path.GetFileNameWithoutExtension(file));
                            try
                            {
                                await Fla2PngSheet(file, outputDir, columns, targetWidth, animateExe);
                                YiKit.ShowToast($"成功生成联系表：" + file);
                            }
                            catch (Exception ex)
                            {
                                YiKit.ShowToastError($"处理 {file} 失败：{ex.Message}");
                            }
                        }
                    });
                }
                else
                {
                    YiKit.ShowToastError("未找到任何 .fla 文件或包含 .fla 的 ZIP 文件。");
                }
            });
        }
    }

    /// <summary>
    /// 导出 FLA 的所有帧并生成联系表（单方法封装版）
    /// </summary>
    /// <param name="flaFilePath">FLA 文件路径</param>
    /// <param name="outputDir">输出目录</param>
    /// <param name="columns">联系表每行的列数</param>
    /// <param name="targetFrameWidth">每张图像缩放后的宽度（保持比例）</param>
    /// <param name="animatePath">Adobe Animate 的安装路径</param>
    /// <param name="jsflTemplatePath">JSFL 脚本模板路径</param>
    /// <returns>生成的联系表路径</returns>
    public static async Task<string> Fla2PngSheet(
        string flaFilePath,
        string outputDir,
        int columns = 0,
        int targetFrameWidth = 256,
        string animatePath = @"C:\Program Files\Adobe\Adobe Animate 2025\Animate.exe"
    )
    {
        // 检查输入参数
        if (!File.Exists(flaFilePath))
        {
            YiKit.ShowToastError("fla文件不存在");
            return null;
        }

        if (outputDir == null || string.IsNullOrEmpty(outputDir))
        {
            outputDir = Path.GetDirectoryName(flaFilePath) + "/" +
                        YiKit.ChineseToHash(Path.GetFileNameWithoutExtension(flaFilePath));
        }

        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);


        string framesDir = Path.Combine(outputDir, "frames");
        string resizedDir = Path.Combine(outputDir, "resized_frames");
        string contactSheetPath = Path.Combine(Path.GetDirectoryName(flaFilePath),
            Path.GetFileNameWithoutExtension(flaFilePath) + ".png");


        if (!File.Exists(animatePath))
            throw new FileNotFoundException("找不到 Adobe Animate 程序", animatePath);

        // 清理旧数据
        if (Directory.Exists(framesDir)) Directory.Delete(framesDir, true);
        if (Directory.Exists(resizedDir)) Directory.Delete(resizedDir, true);
        Directory.CreateDirectory(framesDir);

        // 替换 JSFL 输出路径
        // 内置 JSFL 脚本模板（字符串形式）
        const string JsflTemplate = @"
    fl.outputPanel.clear();
    var doc = fl.getDocumentDOM();
    if (!doc) {
        alert('无法打开文档');
        exit();
    }
    var timeline = doc.getTimeline();
    var totalFrames = timeline.frameCount;
    var outputFolder = ""REPLACE_OUTPUT_FOLDER"";
    if (!FLfile.exists(outputFolder)) {
        FLfile.createFolder(outputFolder);
    }
    for (var i = 0; i < totalFrames; i++) {
        timeline.currentFrame = i;
        doc.selectAll();
        var filePath = ""file:///"" +outputFolder + ""frame_"" + padLeft(i + 1, 4, '0') + "".png"";
        doc.exportPNG(filePath, true, true);
    }
    doc.exportPNG(""file:///"" +outputFolder + ""end.png"", true, true);

    // ✅ 添加完成标志：写入一个 done.flag 文件
    // ✅ 正确版本
    //FLfile.createFolder(outputFolder+""done.flag"");
    //var flagFile = outputFolder + ""done.flag"";
    //FLfile.write(flagFile, ""Export completed at "" + new Date());

    function padLeft(str, length, padChar) {
        str = String(str);
        while (str.length < length) {
            str = padChar + str;
        }
        return str;
    }";

        // 替换输出路径
        string jsflCode = JsflTemplate.Replace("REPLACE_OUTPUT_FOLDER", framesDir.Replace("\\", "/") + "/");
        string tempJsfl = Path.Combine(outputDir, "temp_export.jsfl");
        File.WriteAllText(tempJsfl, jsflCode);

        // 启动 Adobe Animate
        Process.Start(animatePath, $"\"{flaFilePath}\" \"{tempJsfl}\"");

        string flagFile = Path.Combine(framesDir, "end.png");

        int timeoutSeconds = 180;
        bool completed = false;

        for (int i = 0; i < timeoutSeconds; i++)
        {
            if (File.Exists(flagFile))
            {
                completed = true;
                await Task.Delay(100); // 等待1秒
                YiKit.TryDeleteFile(flagFile);
                break;
            }

            await Task.Delay(1000); // 等待1秒
        }

        if (completed)
        {
            Console.WriteLine("JSFL 脚本执行完成！");
        }
        else
        {
            Console.WriteLine("等待超时，JSFL 可能未完成。");
        }

        pngPacker(framesDir, contactSheetPath, targetFrameWidth, columns);

        /*
        // 缩放图像尺寸
        Directory.CreateDirectory(resizedDir);
        foreach (string file in Directory.GetFiles(framesDir, "*.png"))
        {
            string filename = Path.GetFileName(file);
            string output = Path.Combine(resizedDir, filename);

            using (Image img = Image.FromFile(file))
            {
                double ratio = (double)targetFrameWidth / img.Width;
                int newHeight = (int)(img.Height * ratio);
                using (Image resized = new Bitmap(img, new Size(targetFrameWidth, newHeight)))
                using (Graphics g = Graphics.FromImage(resized))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(resized, 0, 0);
                    resized.Save(output, ImageFormat.Png);
                }
            }
        }

        // 合成联系表
        var images = Directory.GetFiles(resizedDir, "*.png")
            .Select(f => Image.FromFile(f))
            .ToList();

        if (images.Count == 0)
            throw new Exception("未找到 PNG 图像文件");

        int thumbWidth = images[0].Width;
        int thumbHeight = images[0].Height;
        int rows = (int)Math.Ceiling((double)images.Count / columns);

        Bitmap sheet = new Bitmap(columns * thumbWidth, rows * thumbHeight);
        using (Graphics g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.Transparent);
            int x = 0, y = 0;
            foreach (var img in images)
            {
                g.DrawImage(img, new Rectangle(x, y, thumbWidth, thumbHeight));
                x += thumbWidth;
                if (x >= sheet.Width)
                {
                    x = 0;
                    y += thumbHeight;
                }
            }
        }

        sheet.Save(contactSheetPath, ImageFormat.Png);*/
        YiKit.ShowToast("转换完成：" + contactSheetPath);
        // sheet.Save(contactSheetPath, ImageFormat.Png);

// ✅ 删除中间目录
        if (Directory.Exists(outputDir)) YiKit.TryDeleteDir(outputDir);
        return contactSheetPath;
    }


    public static void pngPacker(string framesDir, string contactSheetPath, int targetFrameWidth = 256, int columns = 0,
        int targetFrameHeight = 256, int rows = 0)
    {
        // 直接从原始 PNG 帧图像创建联系表，不生成缩放后的临时文件


        int thumbWidth = targetFrameWidth; // 每个缩略图宽度，如果是0则不改变或自动等比缩放
        int thumbHeight = targetFrameHeight; //如果是0 稍后根据第一张图像比例计算高度

        var images = new List<Bitmap>();
        List<int> actualHeights = new List<int>(); // 可选：记录每个图像的实际高度（非统一）

// 读取所有 PNG 帧，并在内存中缩放
        foreach (string file in Directory.GetFiles(framesDir, "*.png"))
        {
            using (Image original = Image.FromFile(file))
            {
                double ratio = 1;
                int newh;
                int neww;
                if (targetFrameWidth > 0 && targetFrameHeight > 0)
                {
                    newh = targetFrameHeight;
                    neww = targetFrameWidth;
                }

                else if (targetFrameHeight > 0)
                {
                    ratio = (double)targetFrameHeight / original.Height;
                    newh = (int)(original.Height * ratio);
                    neww = (int)original.Width;
                }


                else if (targetFrameWidth > 0)
                {
                    ratio = (double)targetFrameWidth / original.Width;
                    neww = (int)(original.Width * ratio);
                    newh = (int)(original.Height);
                }
                else
                {
                    newh = original.Height;
                    neww = original.Width;
                }

                if (newh > thumbHeight)
                    thumbHeight = newh; // 所有缩略图统一高度（也可动态处理）
                if (neww > thumbWidth)
                    thumbWidth = neww; // 所有缩略图统一高度（也可动态处理）

                var resized = new Bitmap(original, new Size(neww, newh));
                images.Add(resized); // 添加到列表中
            }
        }

        if (images.Count == 0)
            YiKit.Print("未找到 PNG 图像文件");
        if (columns < 1 && rows < 1)
        {
            columns = (int)Math.Ceiling(Math.Sqrt(images.Count)); // 自动计算行列
            rows = (int)Math.Ceiling((double)images.Count / columns);
        }
        else if (columns < 1)
        {
            columns = (int)Math.Ceiling((double)images.Count / rows);
        }
        else if (rows < 1)
        {
            rows = (int)Math.Ceiling((double)images.Count / columns);
        }

// 创建联系表画布
        Bitmap sheet = new Bitmap(columns * thumbWidth, rows * thumbHeight);
        using (Graphics g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.Transparent); // 如果背景透明可用 PNG 格式保存
            int x = 0, y = 0;

            foreach (var img in images)
            {
                g.DrawImage(img, new Rectangle(x, y, img.Width, img.Height));
                x += thumbWidth;

                if (x >= sheet.Width)
                {
                    x = 0;
                    y += thumbHeight;
                }
            }
        }

// 保存联系表
        sheet.Save(contactSheetPath, ImageFormat.Png);

// 清理资源
        foreach (var img in images)
        {
            img.Dispose();
        }
    }

    /// <summary>
    /// 从 ZIP 文件中提取所有 .fla 文件到目标目录
    /// </summary>
    /// <param name="zipFilePath">ZIP 文件路径</param>
    /// <param name="outputDirectory">要保存 .fla 文件的目录</param>
    /// <returns>提取出的 .fla 文件路径列表</returns>
    public static string[] ExtractOnlyFlaFiles(string zipFilePath, string outputDirectory)
    {
        if (!File.Exists(zipFilePath))
        {
            YiKit.ShowToastWaring("找不到 ZIP 文件");
            return null;
        }

        if (!Directory.Exists(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var extractedFiles = new List<string>();

        using (var archive = ZipFile.OpenRead(zipFilePath))
        {
            foreach (var entry in archive.Entries)
            {
                // 只处理 .fla 文件
                if (entry.FullName.EndsWith(".fla", StringComparison.OrdinalIgnoreCase))
                {
                    string fileName = Path.GetFileName(entry.FullName);
                    string destFileName = GetUniqueFileName(outputDirectory, fileName);

                    string destinationPath = Path.Combine(outputDirectory, destFileName);

                    // 解压文件
                    entry.ExtractToFile(destinationPath);
                    extractedFiles.Add(destinationPath);
                }
            }
        }

        return extractedFiles.ToArray();
    }

    /// <summary>
    /// 如果文件已存在，则自动加上数字后缀以避免冲突
    /// </summary>
    private static string GetUniqueFileName(string directory, string originalName)
    {
        string fileName = Path.GetFileNameWithoutExtension(originalName);
        string extension = Path.GetExtension(originalName);
        string path = Path.Combine(directory, originalName);

        int count = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{fileName}({count}){extension}");

            count++;
        }

        return Path.GetFileName(path);
    }

    private void MainStr_OnMouseEnter(object sender, MouseEventArgs e)
    {
        MainStr.Opacity = 0.1;
    }

    private void MainStr_OnMouseLeave(object sender, MouseEventArgs e)
    {
        MainStr.Opacity = 1;
    }

    private void VddItem_OnClick(object sender, RoutedEventArgs e)
    {
        var item = sender as MenuItem;
        if (item != null)
        {
            item.IsChecked = !item.IsChecked;
            YiKit.IniWrite("vdd", item.IsChecked.ToString());
            if (item.IsChecked)
            {
                if (Core.GetDisplays().Count == 0)
                {
                    ProgramVdd.StartVdd(["-a"], true);
                }
            }
        }
    }

    private void VddItem_OnRClick(object sender, RoutedEventArgs e)
    {
        // Program.StartVdd([" -cli"]);
        VddMainWindow vdd = new VddMainWindow();
        vdd.Show();
        // CLI.Execute([" -cli"]);
        // new Tray();
    }

    private void ViewboxPet_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private bool isMoveing = false;

    private void Tag_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            isMoveing = true;
            DragMove();
        }
        else if (isMoveing == true) isMoveing = false;
    }

    bool isMouseInside = false;

    private void ViewboxPet_OnMouseLeave(object sender, MouseEventArgs e)
    {
        isMouseInside = false;
    }


    private void MenuItemGrid_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        isMouseInside = true;
        if (isMoveing)
            return;
        SetWindowMouseThrough(true, 5000);
    }


    private void From_OnClick(object sender, RoutedEventArgs e)
    {
        if (RowAndCol.me == null)
            new RowAndCol();
        RowAndCol.me.Show();
    }

    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_MONITORPOWER = 0xF170;

    private void CloseMonItem_OnClick(object sender, RoutedEventArgs e)
    {
        List<string> strs = YiKit.ShowInputDialog("定时关闭显示器", "指定时间", "等待时间");
        if (null == strs || strs.Count < 2)
        {
            return;
        }

        string zdtime = strs[0];
        string ddtime = strs[1];

        // -1：关闭显示器
        // 0：打开显示器
        // 1：低功耗模式
        // 2：完全关闭显示器
        Task.Delay(1000).ContinueWith(_ =>
        {
            // 1. 先声明：我们正在使用显示器（防止系统干扰）
            YiKit.SetThreadExecutionState(YiKit.EXECUTION_STATE.ES_DISPLAY_REQUIRED |
                                          YiKit.EXECUTION_STATE.ES_CONTINUOUS);
            YiKit.ShowToast("sdsds");
            // 2. 发送关闭显示器命令
            // YiKit.SendMessage((IntPtr)0xFFFF, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)(-1));
            YiKit.SendMessage(IntPtr.Zero, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)(2));

            // 3. 释放状态（可选，但推荐）
            YiKit.SetThreadExecutionState(YiKit.EXECUTION_STATE.ES_CONTINUOUS);
        });
    }

    private async void CalcLteFrom_OnClick(object sender, RoutedEventArgs e)
    {
        List<string> strs = YiKit.ShowInputDialog("LTECDR计算", "选择文件位置", "选择文件夹位置", "分隔符(\t)");
        if (null == strs || strs.Count < 2)
        {
            return;
        }

        string group =
            "车管所(51024989)堵截组(51024972)高架路大队(51025906)高架路大队备用组(51025908)高速备用组(51024984)高速支队(51024983)高新备用组(51024986)高新大队(51024985)槐荫备用组(51024993)槐荫大队(51024992)济阳大队(51024982)交警108组(51024968)警卫组(51024967)警务保障组(51024969)救援备用组(51024987)救援中心(51024977)历城备用组(51025910)历城大队(51025909)历下备用组(51024974)历下大队(51024973)南山备用组(51024971)南山大队(51024970)平阴大队(51024976)起步区大队(51025905)起步区大队备用组(51025907)商河大队(51024975)市中备用组(51024979)市中大队(51024978)天桥备用组(51024991)天桥大队(51024990)章丘大队(51024981)长清大队(51024980)支队备用组1(51024994)支队备用组10(51025904)支队备用组2(51024995)支队备用组3(51024996)支队备用组4(51024997)支队备用组5(51024998)支队备用组6(51024999)支队备用组7(51025901)支队备用组8(51025902)支队备用组9(51025903)直属大队(51024988)";

        var gs = group.Split(')');
        Dictionary<string, string> group_str = new Dictionary<string, string>();
        foreach (var item in gs)
        {
            if (item.Split('(').Length > 1)
            {
                string id = item.Split('(')[1];
                string name = item.Split('(')[0];
                group_str[id] = name;
            }
        }

        string filePath = strs[0];
        string dirPath = strs[1];
        string delimiter = YiKit.ParseEscapeSequences(strs[2]);

        YiKit.ShowToast("开始计算，请稍候...");

        try
        {
            // ✅ 关键：使用扩展类型接收结果，不要丢弃 Caller 数据
            var resultExtended = await Task.Run(() => AnalyzeByCallee_Extended(dirPath, filePath, group_str));

            if (resultExtended != null)
            {
                if (string.IsNullOrWhiteSpace(dirPath))
                    throw new ArgumentException("输出文件路径不能为空", nameof(dirPath));

                string directory = Path.GetDirectoryName(dirPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 主输出：组级统计
                string summaryFile = Path.Combine(dirPath, $"{YiKit.NowD()}.csv");
                using (var writer = new StreamWriter(summaryFile, false, Encoding.UTF8))
                {
                    writer.WriteLine($"组号{delimiter}组名{delimiter}次数{delimiter}总计秒");
                    foreach (var kvp in resultExtended.OrderBy(r => r.Key))
                    {
                        var res = kvp.Value;
                        writer.WriteLine(
                            $"{kvp.Key}{delimiter}{(group_str.ContainsKey(kvp.Key) ? group_str[kvp.Key] : "")}{delimiter}{res.Count}{delimiter}{res.TotalDurationSeconds}");
                    }
                }

                var num = 0;
                // 详细输出：每个 CALLER 的数据 ✅ 现在能访问 CallerCount 和 CallerDuration
                string detailFile = Path.Combine(dirPath, $"{YiKit.NowD()}_detail.csv");
                using (var writer = new StreamWriter(detailFile, false, Encoding.UTF8))
                {
                    writer.WriteLine($"组号{delimiter}组名{delimiter}CALLER{delimiter}呼叫次数{delimiter}累计时长(秒)");
                    foreach (var kvp in resultExtended.OrderBy(r => r.Key))
                    {
                        var extended = kvp.Value; // 直接就是 CallDurationResultExtended
                        foreach (var caller in extended.CallerCount.Keys)
                        {
                            num++;
                            writer.WriteLine(
                                $"{kvp.Key}{delimiter}{(group_str.ContainsKey(kvp.Key) ? group_str[kvp.Key] : "")}" +
                                $"{delimiter}{caller}{delimiter}{extended.CallerCount[caller]}" +
                                $"{delimiter}{extended.CallerDuration[caller]}");
                        }
                    }
                }

                // 详细输出：每个 CALLER 的数据 ✅ 现在能访问 CallerCount 和 CallerDuration
                string detailSumFile = Path.Combine(dirPath, $"{YiKit.NowD()}_sum_detail.csv");
                using (var writer = new StreamWriter(detailSumFile, false, Encoding.UTF8))
                {
                    Dictionary<string, CallDurationResult> res = new Dictionary<string, CallDurationResult>();
                    writer.WriteLine($"号码{delimiter}呼叫次数{delimiter}累计时长(秒)");
                    foreach (var kvp in resultExtended.OrderBy(r => r.Key))
                    {
                        var extended = kvp.Value; // 直接就是 CallDurationResultExtended
                        foreach (var caller in extended.CallerCount.Keys)
                        {
                            if (res.ContainsKey(caller))
                            {
                                res[caller].Count += extended.CallerCount[caller];
                                res[caller].TotalDurationSeconds += extended.CallerDuration[caller];
                            }
                            else
                                res[caller] = new CallDurationResult()
                                {
                                    Count = extended.CallerCount[caller],
                                    TotalDurationSeconds = extended.CallerDuration[caller]
                                };
                        }
                    }

                    foreach (var kvp in res)
                    {
                        writer.WriteLine(
                            $"{kvp.Key}{delimiter}{kvp.Value.Count}{delimiter}{kvp.Value.TotalDurationSeconds}");
                    }
                }

                MessageBox.Show($"计算完成！计算{lte_all_count}次。\n已生成：\n{summaryFile}\n{detailFile}\n{detailSumFile}", "完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"计算出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static long lte_all_count = 0;

// 返回扩展类型，保留 Caller 细节
    private static Dictionary<string, CallDurationResultExtended> AnalyzeByCallee_Extended(
        string folderPath, string fire = "", Dictionary<string, string> group_str = null,
        string[] fileExtensions = null, bool showToast = true)
    {
        fileExtensions = fileExtensions ?? new[] { ".txt", ".csv" };

        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"文件夹不存在: {folderPath}");

        var files = new List<string>();
        if (Directory.Exists(folderPath))
        {
            foreach (var file in Directory.GetFiles(folderPath))
            {
                string ext = Path.GetExtension(file).ToLower();
                if (fileExtensions.Contains(ext))
                {
                    files.Add(file);
                }
            }
        }

        if (!string.IsNullOrEmpty(fire))
            files.Add(fire);

        if (files.Count == 0)
            return new Dictionary<string, CallDurationResultExtended>();

        var allRecords = new ConcurrentBag<CallRecord>();
        var validCallees = new HashSet<string>();
        if (group_str != null)
        {
            foreach (var key in group_str.Keys)
            {
                validCallees.Add(key);
            }
        }

        foreach (var file in files)
        {
            try
            {
                if (!file.Contains("CDR_")) continue;
                var records = ProcessFile(file);
                if (showToast)
                    YiKit.ShowToast("【" + Path.GetFileName(file) + "】处理完成: " + records.Count);
                foreach (var record in records)
                {
                    allRecords.Add(record);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"处理文件失败: {file} - {ex.Message}");
            }
        }

        lte_all_count = allRecords.Count;
        var sessionGroups = allRecords
            .GroupBy(r => new { r.FileName, r.SessionId })
            .Select(g =>
            {
                var sorted = g.OrderBy(r => r.StartTime).ToList();
                var last = sorted.Last();
                return new
                {
                    Callee = sorted[0].Callee,
                    CallerFirstJoin = g.GroupBy(x => x.Caller).ToDictionary(x => x.Key, x => x.Min(y => y.StartTime)),
                    EndTime = last.EndTime,
                    SessionId = g.Key.SessionId
                };
            })
            .Where(x => x.EndTime.HasValue)
            .ToList();

        var result = new Dictionary<string, CallDurationResultExtended>();

        foreach (var session in sessionGroups)
        {
            string callee = session.Callee;
            if (validCallees.Count > 0 && !validCallees.Contains(callee)) continue;

            if (!result.ContainsKey(callee))
                result[callee] = new CallDurationResultExtended();

            var res = result[callee];

            foreach (var callerEntry in session.CallerFirstJoin)
            {
                string caller = callerEntry.Key;
                DateTime joinTime = callerEntry.Value;
                long duration = (long)(session.EndTime.Value - joinTime).TotalSeconds + 1;

                res.TotalDurationSeconds += duration;
                res.Count++;

                if (res.CallerCount.ContainsKey(caller))
                    res.CallerCount[caller]++;
                else
                    res.CallerCount[caller] = 1;

                if (res.CallerDuration.ContainsKey(caller))
                    res.CallerDuration[caller] += duration;
                else
                    res.CallerDuration[caller] = duration;
            }
        }

        return result; // ✅ 直接返回扩展类型
    }

    public static Dictionary<string, CallDurationResult> AnalyzeByCallee(
        string folderPath, string fire = "", Dictionary<string, string> group_str = null,
        string[] fileExtensions = null, bool showToast = true)
    {
        fileExtensions = fileExtensions ?? new[] { ".txt", ".csv" };

        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"文件夹不存在: {folderPath}");

        var files = new List<string>();
        if (Directory.Exists(folderPath))
        {
            foreach (var file in Directory.GetFiles(folderPath))
            {
                string ext = Path.GetExtension(file).ToLower();
                if (fileExtensions.Contains(ext))
                {
                    files.Add(file);
                }
            }
        }

        if (!string.IsNullOrEmpty(fire))
            files.Add(fire);

        if (files.Count == 0)
            return new Dictionary<string, CallDurationResult>();

        var allRecords = new ConcurrentBag<CallRecord>();
        var validCallees = new HashSet<string>();
        if (group_str != null)
        {
            foreach (var key in group_str.Keys)
            {
                validCallees.Add(key);
            }
        }

        foreach (var file in files)
        {
            try
            {
                if (!file.Contains("CDR_")) continue;
                var records = ProcessFile(file);
                if (showToast)
                    YiKit.ShowToast("【" + Path.GetFileName(file) + "】处理完成: " + records.Count);
                foreach (var record in records)
                {
                    allRecords.Add(record);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"处理文件失败: {file} - {ex.Message}");
            }
        }

        // 按 (文件名, SessionId) 唯一分组
        var sessionGroups = allRecords
            .GroupBy(r => new { r.FileName, r.SessionId })
            .Select(g =>
            {
                var sorted = g.OrderBy(r => r.StartTime).ToList();
                var last = sorted.Last();
                return new
                {
                    Callee = sorted[0].Callee,
                    CallerFirstJoin = g.GroupBy(x => x.Caller).ToDictionary(x => x.Key, x => x.Min(y => y.StartTime)),
                    EndTime = last.EndTime,
                    SessionId = g.Key.SessionId
                };
            })
            .Where(x => x.EndTime.HasValue)
            .ToList();

        var result = new Dictionary<string, CallDurationResultExtended>();

        foreach (var session in sessionGroups)
        {
            string callee = session.Callee;
            if (validCallees.Count > 0 && !validCallees.Contains(callee)) continue;

            if (!result.ContainsKey(callee))
                result[callee] = new CallDurationResultExtended();

            var res = result[callee];

            foreach (var callerEntry in session.CallerFirstJoin)
            {
                string caller = callerEntry.Key;
                DateTime joinTime = callerEntry.Value;
                long duration = (long)(session.EndTime.Value - joinTime).TotalSeconds + 1;

                res.TotalDurationSeconds += duration;
                res.Count++;

                // 手动实现 GetValueOrDefault 兼容
                if (res.CallerCount.ContainsKey(caller))
                    res.CallerCount[caller]++;
                else
                    res.CallerCount[caller] = 1;

                if (res.CallerDuration.ContainsKey(caller))
                    res.CallerDuration[caller] += duration;
                else
                    res.CallerDuration[caller] = duration;
            }
        }

        // 转为标准结果类型
        var finalResult = new Dictionary<string, CallDurationResult>();
        foreach (var kvp in result)
        {
            finalResult[kvp.Key] = new CallDurationResult
            {
                TotalDurationSeconds = kvp.Value.TotalDurationSeconds,
                Count = kvp.Value.Count,
                AverageDurationSeconds =
                    kvp.Value.Count > 0 ? (int)(kvp.Value.TotalDurationSeconds / kvp.Value.Count) : 0
            };
        }

        return finalResult;
    }

    private static List<CallRecord> ProcessFile(string filePath)
    {
        var records = new List<CallRecord>();
        string[] lines;

        try
        {
            lines = File.ReadAllLines(filePath);
        }
        catch (Exception e)
        {
            YiKit.Print(e.Message);
            return records;
        }

        if (lines.Length == 0) return records;

        char delimiter = lines[0].Contains('\t') ? '\t' :
            lines[0].Contains(',') ? ',' : '\t';

        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i]?.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var fields = line.Split(delimiter);
            if (fields.Length < 10) continue;

            try
            {
                records.Add(new CallRecord
                {
                    FileName = Path.GetFileNameWithoutExtension(filePath),
                    SvcType = fields[0],
                    SessionId = int.Parse(fields[1]),
                    Caller = fields[2],
                    Callee = fields[3],
                    StartTime = ParseDateTime(fields[4]),
                    EndTime = string.IsNullOrWhiteSpace(fields[5]) ? (DateTime?)null : ParseDateTime(fields[5]),
                    Result = fields[6],
                    Cause = fields[7],
                    CallingMsisdn = fields[8],
                    CalledMsisdn = fields[9]
                });
            }
            catch
            {
                continue;
            }
        }

        return records;
    }

    private static DateTime ParseDateTime(string input)
    {
        input = input.Trim();
        string[] formats =
        {
            "yyyy/M/d H:mm", "yyyy/M/d H:mm:ss",
            "yyyy/M/d HH:mm", "yyyy/M/d HH:mm:ss",
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"
        };

        return DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.None,
            out DateTime dt)
            ? dt
            : throw new FormatException($"无法解析日期: {input}");
    }

    // 通话记录模型
    private class CallRecord
    {
        public string FileName { get; set; }
        public string SvcType { get; set; }
        public int SessionId { get; set; }
        public string Caller { get; set; }
        public string Callee { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Result { get; set; }
        public string Cause { get; set; }
        public string CallingMsisdn { get; set; }
        public string CalledMsisdn { get; set; }
    }

    // 统计结果
    public class CallDurationResult
    {
        public long TotalDurationSeconds { get; set; }
        public int Count { get; set; }
        public int AverageDurationSeconds { get; set; }
    }

    // 扩展结果（支持 Caller 细节）
    public class CallDurationResultExtended : CallDurationResult
    {
        public Dictionary<string, int> CallerCount { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, long> CallerDuration { get; set; } = new Dictionary<string, long>();
    }

    /// <summary>
    /// 从结构相同的 en/zh XML 字典中，提取 英文文本 -> 中文文本 映射
    /// 只提取 <t> 标签内的文本内容，结果为扁平字典
    /// </summary>
    void ExtractTranslation(
        Dictionary<string, object> enDict,
        Dictionary<string, object> zhDict,
        Dictionary<string, string> enZhdict,
        HashSet<object> visited = null)
    {
        // 防止循环引用
        visited ??= new HashSet<object>();
        if (!visited.Add(enDict)) return;
        if (!visited.Add(zhDict))
        {
            visited.Remove(enDict);
            return;
        }

        foreach (var kvp in enDict)
        {
            string key = kvp.Key;
            object enValue = kvp.Value;
            object zhValue = null;

            // 确保中文字典有相同 key
            if (!zhDict.TryGetValue(key, out zhValue))
                continue;

            // --- 特殊处理: <t> 标签 ---
            if (key == "t")
            {
                // 情况1: 单个 <t> 节点（字典）
                if (enValue is Dictionary<string, object> enT &&
                    zhValue is Dictionary<string, object> zhT)
                {
                    if (enT.TryGetValue("#text", out var enText) &&
                        zhT.TryGetValue("#text", out var zhText) &&
                        enText is string enStr &&
                        zhText is string zhStr &&
                        !string.IsNullOrEmpty(enStr))
                    {
                        // 直接添加映射（英文 → 中文）
                        if (!enZhdict.ContainsKey(enStr))
                        {
                            if (enStr.Length > 1)
                                enZhdict[enStr] = zhStr;
                        }
                    }
                }
                // 情况2: 多个 <t>（列表）
                else if (enValue is IList enList && zhValue is IList zhList)
                {
                    int count = Math.Min(enList.Count, zhList.Count);
                    for (int i = 0; i < count; i++)
                    {
                        if (enList[i] is Dictionary<string, object> enItem &&
                            zhList[i] is Dictionary<string, object> zhItem)
                        {
                            if (enItem.TryGetValue("#text", out var enText) &&
                                zhItem.TryGetValue("#text", out var zhText) &&
                                enText is string enStr &&
                                zhText is string zhStr &&
                                !string.IsNullOrEmpty(enStr))
                            {
                                if (!enZhdict.ContainsKey(enStr))
                                {
                                    if (enStr.Length > 1)
                                        enZhdict[enStr] = zhStr;
                                }
                            }
                        }
                    }
                }
            }
            // --- 递归处理嵌套结构 ---
            else if (enValue is Dictionary<string, object> enSubDict &&
                     zhValue is Dictionary<string, object> zhSubDict)
            {
                ExtractTranslation(enSubDict, zhSubDict, enZhdict, visited);
            }
            else if (enValue is IList enList && zhValue is IList zhList)
            {
                int count = Math.Min(enList.Count, zhList.Count);
                for (int i = 0; i < count; i++)
                {
                    if (enList[i] is Dictionary<string, object> enItem &&
                        zhList[i] is Dictionary<string, object> zhItem)
                    {
                        ExtractTranslation(enItem, zhItem, enZhdict, visited);
                    }
                }
            }
            // 其他字符串值（如 page 的 title）也可提取（可选）
            // else if (enValue is string enStr && zhValue is string zhStr)
            // {
            //     if (!string.IsNullOrEmpty(enStr) && !enZhdict.ContainsKey(enStr))
            //         enZhdict[enStr] = zhStr;
            // }
        }
    }

    private void X4TranItem_OnClick(object sender, RoutedEventArgs e)
    {
        List<string> strs = YiKit.ShowInputDialog("X4翻译",
            "选择英文原文文件位置", "选择中文文文件位置", "选择待翻译的文件夹位置", "文件类型(配合文件夹):xml", "选择待翻译的文件位置");

        if (strs == null || strs.Count < 1) return;
        string file1 = strs[0];
        string file2 = strs[1];
        string dir1 = strs[2];
        string type1 = strs[3];
        string file3 = strs[4];
        List<string> files = new List<string>();

        YiKit.RunThread((state =>
        {
            // 如果提供了文件夹，则优先使用文件夹中的 .ogv 文件
            if (!string.IsNullOrEmpty(dir1) && Directory.Exists(dir1))
            {
                files.AddRange(Directory.GetFiles(dir1, "*." + type1, SearchOption.TopDirectoryOnly));
            }

            // 如果提供了单个文件，则加入列表
            if (!string.IsNullOrEmpty(file3) && File.Exists(file3))
            {
                files.Add(file3);
            }

            var endict = YiKit.xmlToDict(file1);
            var zhdict = YiKit.xmlToDict(file2);
            Dictionary<string, string> enZhdict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // 递归遍历英文字典，同步查找中文字典中的对应值
            ExtractTranslation(endict, zhdict, enZhdict);

            var sorted = enZhdict.OrderByDescending(kv => kv.Key.Length);
            int num = files.Count;
            foreach (string file in files)
            {
                YiKit.RunThread(s =>
                {
                    string tempFile = file + ".tmp";
                    try
                    {
                        YiKit.ShowToast($"开始处理：{file}");
                        // === 第一步：读取原始编码和原始字节流信息 ===
                        byte[] originalBytes;
                        Encoding detectedEncoding;

                        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read))
                        {
                            originalBytes = new byte[fs.Length];
                            fs.Read(originalBytes, 0, originalBytes.Length);
                        }

                        using (var reader = new StreamReader(file, true)) // true 表示自动检测编码
                        {
                            detectedEncoding = reader.CurrentEncoding;
                        }

                        // === 第二步：转换为字符串并检测换行符 ===
                        string content = detectedEncoding.GetString(originalBytes);

                        string originalNewLine = "\r\n"; // 默认
                        if (content.Contains("\r\n")) originalNewLine = "\r\n";
                        else if (content.Contains("\n")) originalNewLine = "\n";
                        else if (content.Contains("\r")) originalNewLine = "\r";

                        // === 第三步：预构建替换对（使用传统类或数组）===


                        var replacements = new List<ReplaceItem>();

                        foreach (var kv in sorted)
                        {
                            string key = kv.Key;
                            string value = kv.Value;

                            // 添加三种模式
                            replacements.Add(new ReplaceItem("\"" + key + "\"", "\"" + value + "\""));
                            replacements.Add(new ReplaceItem("\"" + key, "\"" + value));
                            replacements.Add(new ReplaceItem(key + "\"", value + "\""));
                        }

                        // 按 OldText 长度降序排序，避免短 key 干扰长 key（如 "OK" 和 "OK_Button"）
                        replacements.Sort((a, b) => b.OldText.Length.CompareTo(a.OldText.Length));

                        // === 第四步：使用 StringBuilder 批量处理 ===
                        // 预分配容量，减少内存重分配
                        var sb = new StringBuilder(content, content.Length + content.Length / 10);

                        foreach (var item in replacements)
                        {
                            // 可选：提前判断是否包含，避免无效 Replace 扫描
                            if (content.Contains(item.OldText))
                            {
                                sb.Replace(item.OldText, item.NewText);
                            }
                        }

                        string modifiedContent = sb.ToString();

                        // === 第五步：还原原始换行符 ===
                        modifiedContent = modifiedContent.Replace("\r\n", "\n").Replace('\r', '\n');
                        modifiedContent = modifiedContent.Replace("\n", originalNewLine);

                        // === 第六步：写回文件（保留原始编码）===
                        File.WriteAllText(tempFile, modifiedContent, detectedEncoding);

                        // === 第七步：原子替换 ===
                        File.Delete(file);
                        File.Move(tempFile, file);

                        num--;
                        YiKit.ShowToast($"{file}处理完成,剩余{num}个文件");
                    }
                    catch (Exception ex)
                    {
                        if (File.Exists(tempFile)) File.Delete(tempFile);
                        YiKit.ShowToastError($"{file} 错误信息：{ex.Message}");
                    }
                });
            }
        }));
    }

    // 定义一个简单类来存储替换项
    class ReplaceItem
    {
        public string OldText;
        public string NewText;

        public ReplaceItem(string oldText, string newText)
        {
            OldText = oldText;
            NewText = newText;
        }
    }

    private void WebViewItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (YiWebView.me == null) new YiWebView();
        YiWebView.me.Show();
    }

    private void PortCheck_OnClick(object sender, RoutedEventArgs e)
    {
        OpenPortTools(occupyTab: false);
    }

    private void OpenPortTools(bool occupyTab, string? port = null)
    {
        if (PortCheckDialog.Me == null)
        {
            var dlg = new PortCheckDialog { Owner = this };
            dlg.Show();
        }
        else
        {
            if (!PortCheckDialog.Me.IsVisible)
                PortCheckDialog.Me.Show();
            PortCheckDialog.Me.Activate();
        }

        if (occupyTab)
            PortCheckDialog.Me?.ShowOccupyTab(port);
        else
            PortCheckDialog.Me?.ShowScanTab();
    }

    private void AutoStart_OnClickItemOnClick(object sender, RoutedEventArgs e)
    {
        var item = sender as MenuItem;
        if (item != null)
        {
            YiKit.SetAutoStart(Assembly.GetExecutingAssembly().Location, "StartHoley", !item.IsChecked);
            // YiKit.CreateTaskIgnoreBattery("StartHoley", Assembly.GetExecutingAssembly().Location);
            YiKit.SetAutoStartOnBoot(Assembly.GetExecutingAssembly().Location, "StartHoley", !item.IsChecked, true);
            item.IsChecked = !item.IsChecked;
            YiKit.IniWrite("autostart", item.IsChecked.ToString());
        }
    }

    private void GetSys_OnClick(object sender, RoutedEventArgs e)
    {
        List<string> strs = YiKit.ShowInputDialog("获得system",
            "选择要被提权的程序文件位置");

        if (strs == null || strs.Count < 1) return;
        string exepath = strs[0];
        YiKit.GetSystem(exepath);
    }

    private void MouKbo_OnClick(object sender, RoutedEventArgs e)
    {
        if (MouseAndKeyboard.me == null)
            MouseAndKeyboard.me = new MouseAndKeyboard();
        MouseAndKeyboard.me.Show();
    }

    private void PicItem_OnClick(object sender, RoutedEventArgs e)
    {
        //ffmpeg -i input.ogv -vf fps=30 -c:v png -pix_fmt rgba output_%04d.png


        string type = "png";
        List<string> strs = YiKit.ShowInputDialog(
            "pic合并成联系表",
            "图片格式:png",
            "文件位置（可为空）：",
            "文件夹位置（和文件二选一）：",
            "列数(合并为几列):1",
            "行数(自动计算填0):0",
            "原文件宽度缩放为(保持不变填0):0",
            "原文件高度缩放为(保持不变填0):0",
            "删除黑色背景强度:0x000000:0.005",
            "是否删除缓存文件:true"
        );
        if (strs == null || strs.Count < 1)
        {
            YiKit.ShowToastError("参数错误");
            return;
        }

        type = strs[0].Replace(".", "");
        string filePath = strs[1].Trim();
        int columns = int.TryParse(strs[3], out var col) ? col : 0;
        int rows = int.TryParse(strs[4], out var row) ? row : 0;
        int targetWidth = int.TryParse(strs[5], out var width) ? width : 0;
        int targetHight = int.TryParse(strs[6], out var h) ? h : 0;
        string color = "black";
        var isDel = bool.TryParse(strs[8], out var del) ? del : false;

        double delDark = 0.005;
        if (strs[7].Contains(":"))
        {
            string[] colors = strs[7].Split(':');
            delDark = double.TryParse(colors[1], out var d) ? d : 0.005;
            color = colors[0];
        }
        else
            delDark = double.TryParse(strs[7], out var d) ? d : 0.005;

        string folder = strs[2].Trim();

        YiKit.RunThread(async s =>
        {
            try
            {
                List<string> files = new List<string>();

                // 如果提供了文件夹，则优先使用文件夹中的 .ogv 文件
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    files.AddRange(Directory.GetFiles(folder, "*." + type, SearchOption.TopDirectoryOnly));
                }

                // 如果提供了单个文件，则加入列表
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    if (!files.Contains(filePath))
                        files.Add(filePath);
                }

                if (files.Count == 0)
                {
                    // 如果没有找到任何 .fla 文件，则查找 .zip 文件并尝试提取其中的 .fla

                    var zipFiles = Directory.GetFiles(folder, "*.zip", SearchOption.TopDirectoryOnly);

                    foreach (var zipFile in zipFiles)
                    {
                        // 确保临时目录干净
                        // if (Directory.Exists(extractTempDir)) Directory.Delete(extractTempDir, true);
                        // Directory.CreateDirectory(extractTempDir);

                        // 提取 ZIP 中的 .type 文件
                        using (var archive = ZipFile.OpenRead(zipFile))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                if (entry.FullName.EndsWith("." + type, StringComparison.OrdinalIgnoreCase))
                                {
                                    string uniqueName = $"{Path.GetFileNameWithoutExtension(zipFile)}.{type}";
                                    string destPath = Path.Combine(folder, uniqueName);
                                    entry.ExtractToFile(destPath);
                                    files.Add(destPath);
                                }
                            }
                        }
                    }
                }

                if (files.Count == 0)
                {
                    YiKit.ShowToastError($"未找到有效的{type}文件");
                    return;
                }

                string outputDir = null;
                string contactSheetPath = null;

                foreach (var path in files)
                {
                    string fileDir = Path.GetDirectoryName(path);
                    if (outputDir == null)
                        outputDir = Path.Combine(
                            fileDir,
                            "sheet" + YiKit.NowTicks()
                        );
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    if (contactSheetPath == null)
                        contactSheetPath = Path.Combine(fileDir,
                            Path.GetFileName(fileDir) + "_" + Path.GetRandomFileName() + ".png");
                    Directory.CreateDirectory(outputDir);
                    // 创建输出目录
                    // if (Directory.Exists(framesDir)) Directory.Delete(framesDir, true);
                    // Directory.CreateDirectory(framesDir);

                    // 使用 FFmpeg 提取帧（RGBA 透明通道）
                    //ffmpeg -i he1.ogv   -vf "scale={targetWidth}:-1,format=rgba,colorkey=black:0.005:0.1,fps={fps}" "D:\soft\Godot\projects\jie-(4.4)\anim\ogv\he1\frame_%04d.png"
                    string ffmpegArgs = null;


                    ffmpegArgs =
                        $"-i \"{path}\" -vf \"scale={targetWidth}:{targetHight},format=rgba,colorkey={color}:{delDark}:0.1\"  \"{Path.Combine(outputDir, fileName + ".png")}\"";


                    if (ffmpegArgs == null)
                        return;

                    YiKit.RunCmd($"ffmpeg.exe {ffmpegArgs}");

                    if (!Directory.Exists(outputDir) || Directory.GetFiles(outputDir, "*.png").Length == 0)
                    {
                        YiKit.ShowToastError($"{Path.GetFileName(path)} 导出帧失败");
                        continue;
                    }
                }
                // 计算列数（如果用户设置为0则自动计算）

                // 合成联系表

                // 调用 pngPacker
                pngPacker(outputDir, contactSheetPath, targetWidth, columns, targetHight, rows);

                if (isDel)
                {
                    YiKit.TryDeleteDir(outputDir);
                }

                YiKit.ShowToast($"文件{contactSheetPath}处理完成！");
                YiKit.ShowToast($"全部 {type} 处理完成！");
            }
            catch (Exception ex)
            {
                YiKit.ShowToastError("发生异常：" + ex.Message);
            }
        });
    }

    private void SplitContactSheet_OnClick(object sender, RoutedEventArgs e)
    {
        List<string> inputs = YiKit.ShowInputDialog(
            "拆分联系表",
            "联系表文件位置（.png）:",
            "【可选】输出文件夹位置（留空则自动创建子文件夹）:",
            "列数（横向数量）:0",
            "行数（纵向数量，可填0自动计算）:0",
            "【可选】子图宽度（留空则自动计算）:",
            "【可选】子图高度（留空则自动计算）:"
        );

        if (inputs == null || inputs.Count < 4)
        {
            YiKit.ShowToastError("参数不足");
            return;
        }

        string sheetPath = inputs[0].Trim();
        string customOutputFolder = inputs[1].Trim();
        int columns = int.TryParse(inputs[2], out var col) ? col : 0;
        int rows = int.TryParse(inputs[3], out var row) ? row : 0;
        bool hasWidth = int.TryParse(inputs[4].Trim(), out int tileWidth);
        bool hasHeight = int.TryParse(inputs[5].Trim(), out int tileHeight);

        if (string.IsNullOrEmpty(sheetPath) || !File.Exists(sheetPath))
        {
            YiKit.ShowToastError("联系表文件不存在");
            return;
        }

        if (columns <= 0)
        {
            YiKit.ShowToastError("列数必须大于0");
            return;
        }

        // 获取联系表基本信息
        string sheetDir = Path.GetDirectoryName(sheetPath);
        string sheetBaseName = Path.GetFileNameWithoutExtension(sheetPath); // 如 "my_sheet"

        // 确定输出文件夹
        string outputFolder;
        if (!string.IsNullOrWhiteSpace(customOutputFolder))
        {
            outputFolder = customOutputFolder;
            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);
        }
        else
        {
            // 默认：在联系表同目录下创建以文件名命名的文件夹
            outputFolder = Path.Combine(sheetDir, sheetBaseName);
            Directory.CreateDirectory(outputFolder); // 如果存在则无操作
        }

        // 加载图像获取尺寸
        using var fullImage = new Bitmap(sheetPath);
        int imageWidth = fullImage.Width;
        int imageHeight = fullImage.Height;

        // === 自动推导 tileWidth / tileHeight / rows ===
        if (!hasWidth && !hasHeight)
        {
            if (imageWidth % columns != 0)
            {
                YiKit.ShowToastError($"图像宽度 {imageWidth} 无法被列数 {columns} 整除！");
                return;
            }

            tileWidth = imageWidth / columns;

            if (rows <= 0)
            {
                YiKit.ShowToastError("请提供行数或子图高度，否则无法确定垂直分割。");
                return;
            }

            if (imageHeight % rows != 0)
            {
                YiKit.ShowToastError($"图像高度 {imageHeight} 无法被行数 {rows} 整除！");
                return;
            }

            tileHeight = imageHeight / rows;
        }
        else if (!hasWidth)
        {
            if (rows <= 0)
            {
                if (imageHeight % tileHeight != 0)
                {
                    YiKit.ShowToastError($"图像高度 {imageHeight} 无法被子图高度 {tileHeight} 整除！");
                    return;
                }

                rows = imageHeight / tileHeight;
            }
            else
            {
                if (imageHeight != rows * tileHeight)
                {
                    YiKit.ShowToastError($"图像高度 {imageHeight} ≠ 行数×高度 ({rows}×{tileHeight})");
                    return;
                }
            }

            if (imageWidth % columns != 0)
            {
                YiKit.ShowToastError($"图像宽度 {imageWidth} 无法被列数 {columns} 整除！");
                return;
            }

            tileWidth = imageWidth / columns;
        }
        else if (!hasHeight)
        {
            if (imageWidth != columns * tileWidth)
            {
                YiKit.ShowToastError($"图像宽度 {imageWidth} ≠ 列数×宽度 ({columns}×{tileWidth})");
                return;
            }

            if (rows <= 0)
            {
                YiKit.ShowToastError("请提供行数或子图高度，否则无法确定垂直分割。");
                return;
            }

            if (imageHeight % rows != 0)
            {
                YiKit.ShowToastError($"图像高度 {imageHeight} 无法被行数 {rows} 整除！");
                return;
            }

            tileHeight = imageHeight / rows;
        }
        else
        {
            if (imageWidth != columns * tileWidth || imageHeight != rows * tileHeight)
            {
                YiKit.ShowToastError(
                    $"图像尺寸 ({imageWidth}x{imageHeight}) 与网格 ({columns}x{rows} @ {tileWidth}x{tileHeight}) 不匹配！");
                return;
            }
        }

        if (rows <= 0)
        {
            YiKit.ShowToastError("无法确定有效行数。");
            return;
        }

        // === 执行拆分 ===
        YiKit.RunThread(async _ =>
        {
            try
            {
                using var fullImage = new Bitmap(sheetPath);
                int total = 0;

                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < columns; c++)
                    {
                        Rectangle rect = new Rectangle(c * tileWidth, r * tileHeight, tileWidth, tileHeight);
                        using var tile = fullImage.Clone(rect, fullImage.PixelFormat);

                        // 文件名格式：my_sheet_0000.png
                        string fileName = $"{sheetBaseName}_{total:D4}.png";
                        string outputPath = Path.Combine(outputFolder, fileName);

                        tile.Save(outputPath, ImageFormat.Png);
                        total++;
                    }
                }

                YiKit.ShowToast($"成功拆分为 {total} 张 PNG" + "保存至：{outputFolder}");
            }
            catch (Exception ex)
            {
                YiKit.ShowToastError("拆分失败：" + ex.Message);
            }
        });
    }


    private void RealsePort_OnClick(object sender, RoutedEventArgs e)
    {
        OpenPortTools(occupyTab: true);
    }

    /// <summary>
    /// 安装离线 OCR 语言包到 ocr/tessdata（安装时下载一次，识别全程离线）
    /// </summary>
    private async void OcrModule_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (OcrHelper.IsReady())
            {
                YiKit.ShowToast(OcrHelper.StatusText());
                return;
            }

            YiKit.ShowToastWaring("正在安装离线 OCR 语言包，请稍候...", time: 30);
            await OcrHelper.EnsureInstalledAsync(msg => YiKit.ShowToast(msg));
            YiKit.ShowToast(OcrHelper.IsReady()
                ? "离线 OCR 安装完成，可在鼠标键盘脚本中使用"
                : "OCR 安装未完成，请检查网络后重试");
        }
        catch (Exception ex)
        {
            YiKit.ShowToastError("OCR 安装失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 其它三方应用，Tag下载链接 ，Header三方程序名称，
    /// 先监测本地有没有如没有从TAG下载，下载时显示进度条
    /// 下载后解压（解压时显示进度条）到当前目录的Header文件夹
    /// 解压完成后，启动Header.exe三方程序
    /// </summary>
    private void OtherProgress_OnClick(object sender, RoutedEventArgs e)
    {
        if (!(sender is MenuItem menuItem)) return;

        string appName = menuItem.Header?.ToString();
        string downloadUrl = menuItem.Tag?.ToString();

        if (string.IsNullOrEmpty(appName) || string.IsNullOrEmpty(downloadUrl))
        {
            YiKit.ShowToastError("菜单项配置错误：缺少程序名称或下载链接");
            return;
        }

        string appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, appName);
        string exePath = Path.Combine(appDir, appName + ".exe");
        // 读取已保存的启动参数
        string savedArgs = YiKit.IniGet(appName + "_args", "ini", def: "");
  
        // 读取已保存的实际exe相对路径（处理压缩包内有根文件夹的情况）
        string savedExeRelativePath = YiKit.IniGet(appName + "_exe", "ini", def: "");

        // 优先使用已保存的exe路径
        if (!string.IsNullOrWhiteSpace(savedExeRelativePath))
        {
            string savedFullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, savedExeRelativePath);
            if (File.Exists(savedFullPath))
            {
                exePath = savedFullPath;
            }
        }

        // 检查固定路径是否存在
        if (!File.Exists(exePath))
        {
            // 目录存在时才递归查找
            if (Directory.Exists(appDir))
            {
                // 递归查找整个appDir中的exe
                string foundExe = FindExe(appDir, appName);
                if (!string.IsNullOrWhiteSpace(foundExe))
                {
                    exePath = foundExe;
                    // 保存相对路径，便于目录移动后仍能正常使用
                    string relativePath = GetRelativePath(AppDomain.CurrentDomain.BaseDirectory, foundExe);
                    YiKit.IniWrite(appName + "_exe", relativePath, "ini");
                }
            }
        }

        // 已存在则直接启动
        if (File.Exists(exePath))
        {
            try
            {
                
                if(savedArgs.Contains(appName+".exe")) savedArgs=savedArgs.Replace(appName + ".exe", exePath);
                if (savedArgs.Contains("||"))
                {
                    string[] argList;
                    argList = savedArgs.Split('|');
                    foreach (var arg in argList)
                    {
                        var psi = new ProcessStartInfo(exePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exePath) };
                       
                        
                        if (!string.IsNullOrWhiteSpace(arg))
                        {
                            psi.Arguments = arg.Replace(exePath, "");
                            Process.Start(psi);
                        }

                    
                    }
                }
                else
                {
                    var psi1 = new ProcessStartInfo(exePath) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exePath) };
                    if (!string.IsNullOrWhiteSpace(savedArgs))
                        psi1.Arguments = savedArgs;
                
                    Process.Start(psi1);
                    YiKit.ShowToast($"启动 {appName}" + (string.IsNullOrWhiteSpace(savedArgs) ? "" : " " + savedArgs));
                }


          
            }
            catch (Exception ex)
            {
                YiKit.ShowToastError($"启动失败: {ex.Message}");
            }

            return;
        }

        // 创建进度窗口
        var progressWindow = new Window
        {
            Title = appName + " - 下载进度",
            Width = 400,
            Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.SingleBorderWindow
        };

        var panel = new StackPanel { Margin = new Thickness(15) };
        var statusText = new TextBlock { Text = "准备下载...", Margin = new Thickness(0, 0, 0, 8) };
        var progressBar = new ProgressBar { Height = 20, Minimum = 0, Maximum = 100, Value = 0 };
        panel.Children.Add(statusText);
        panel.Children.Add(progressBar);
        progressWindow.Content = panel;

        progressWindow.Loaded += async (s, args) =>
        {
            string zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, appName + ".zip");
            try
            {
                // 下载
                statusText.Text = $"下载 {appName} ...";
                using (var client = new WebClient())
                {
                    client.Proxy = null;
                    var progress = new Progress<(int percent, long received, long total)>(p =>
                    {
                        progressBar.Value = p.percent;
                        if (p.total > 0)
                            statusText.Text = $"下载中... {p.percent}% ({FormatSize(p.received)}/{FormatSize(p.total)})";
                        else
                            statusText.Text = $"下载中... {p.percent}%";
                    });

                    client.DownloadProgressChanged += (o, ev) =>
                        ((IProgress<(int, long, long)>)progress).Report((ev.ProgressPercentage, ev.BytesReceived,
                            ev.TotalBytesToReceive));

                    await client.DownloadFileTaskAsync(downloadUrl, zipPath);
                }

                progressBar.Value = 100;
                statusText.Text = "下载完成，正在解压...";
                await Task.Delay(300);

                // 解压
                if (Directory.Exists(appDir))
                    Directory.Delete(appDir, true);
                Directory.CreateDirectory(appDir);

                statusText.Text = "正在解压...";
                await Task.Run(() =>
                {
                    using (var archive = ZipFile.OpenRead(zipPath))
                    {
                        int total = archive.Entries.Count;
                        int done = 0;
                        foreach (var entry in archive.Entries)
                        {
                            string fullPath = Path.Combine(appDir, entry.FullName);
                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                Directory.CreateDirectory(fullPath);
                            }
                            else
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                                entry.ExtractToFile(fullPath, true);
                            }

                            done++;
                            int pct = (int)(done * 100.0 / total);
                            Dispatcher.Invoke(() =>
                            {
                                progressBar.Value = pct;
                                statusText.Text = $"解压中... {pct}% ({done}/{total})";
                            });
                        }
                    }
                });

                // 删除zip
                try
                {
                    File.Delete(zipPath);
                }
                catch
                {
                }

                // 查找实际exe（解压后可能存在子目录或不同exe名）
                string actualExe = FindExe(appDir, appName);
                if (actualExe == null)
                {
                    statusText.Text = "解压完成，未找到可执行文件";
                    YiKit.ShowToastError($"解压完成但未找到 {appName}.exe，请手动查看 {appDir}");
                    return;
                }

                // 保存相对路径，下次启动时直接使用，便于目录移动后仍能正常使用
                string relativePath = GetRelativePath(AppDomain.CurrentDomain.BaseDirectory, actualExe);
                YiKit.IniWrite(appName + "_exe", relativePath, "ini");

                statusText.Text = "解压完成，启动程序...";
                await Task.Delay(300);

                progressWindow.Close();

                var psi = new ProcessStartInfo(actualExe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(actualExe)
                };
                if (!string.IsNullOrWhiteSpace(savedArgs))
                    psi.Arguments = savedArgs;
                Process.Start(psi);
                YiKit.ShowToast($"已启动 {appName}" + (string.IsNullOrWhiteSpace(savedArgs) ? "" : " " + savedArgs));
            }
            catch (Exception ex)
            {
                statusText.Text = "失败: " + ex.Message;
                YiKit.ShowToastError($"操作失败: {ex.Message}");
            }
        };

        progressWindow.Show();
    }

// 格式化文件大小
    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
        if (bytes < 1024 * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("F1") + " MB";
        return (bytes / (1024.0 * 1024 * 1024)).ToString("F2") + " GB";
    }

// 递归查找可执行文件
    private static string FindExe(string dir, string appName)
    {
        // 优先找与appName同名的exe
        string target = appName + ".exe";
        var files = Directory.GetFiles(dir, target, SearchOption.AllDirectories);
        if (files.Length > 0) return files[0];

        // 退而找任意exe
        var allExes = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories);
        return allExes.Length > 0 ? allExes[0] : null;
    }

    // .NET Framework 4.6.2 不支持 Path.GetRelativePath，实现自定义版本
    private static string GetRelativePath(string basePath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
            return targetPath;

        basePath = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar);
        targetPath = Path.GetFullPath(targetPath);

        // 如果已经以basePath开头，直接返回相对路径
        if (targetPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            string relative = targetPath.Substring(basePath.Length);
            if (relative.StartsWith(Path.DirectorySeparatorChar.ToString()))
                relative = relative.Substring(1);
            return relative;
        }

        // 查找公共父目录
        string[] baseParts = basePath.Split(Path.DirectorySeparatorChar);
        string[] targetParts = targetPath.Split(Path.DirectorySeparatorChar);

        int commonParts = 0;
        while (commonParts < baseParts.Length && commonParts < targetParts.Length &&
               string.Equals(baseParts[commonParts], targetParts[commonParts], StringComparison.OrdinalIgnoreCase))
        {
            commonParts++;
        }

        // 构建相对路径
        StringBuilder sb = new StringBuilder();
        for (int i = commonParts; i < baseParts.Length; i++)
        {
            sb.Append("..").Append(Path.DirectorySeparatorChar);
        }
        for (int i = commonParts; i < targetParts.Length; i++)
        {
            if (i > commonParts)
                sb.Append(Path.DirectorySeparatorChar);
            sb.Append(targetParts[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 其它三方应用，右键添加启动参数，输入后自动保存，下次左键启动时自动添加启动参数。
    /// 自定义项还可改下载链接，或填写「删」删除该项。
    /// </summary>
    private void OtherProgressRight_OnClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (!(sender is MenuItem menuItem)) return;

        string appName = menuItem.Header?.ToString();
        if (string.IsNullOrEmpty(appName)) return;

        bool isCustom = string.Equals(Convert.ToString(menuItem.Uid), "custom", StringComparison.OrdinalIgnoreCase);

        string savedArgs = YiKit.IniGet(appName + "_args", "ini", def: "");
        string savedUrl = menuItem.Tag?.ToString() ?? "";

        if (isCustom)
        {
            var ret = YiKit.ShowInputDialog($"管理 {appName}",
                $"{appName}启动参数:" + savedArgs,
                "下载链接:" + savedUrl,
                "删除此项(填删):");
            if (ret == null) return;

            string args = ret.Count > 0 ? (ret[0] ?? "") : "";
            string url = ret.Count > 1 ? (ret[1] ?? "") : savedUrl;
            string del = ret.Count > 2 ? (ret[2] ?? "").Trim() : "";

            if (del == "删" || del.Equals("delete", StringComparison.OrdinalIgnoreCase) || del == "1")
            {
                RemoveThirdPartyCustom(appName);
                YiKit.ShowToast($"已删除三方项: {appName}");
                return;
            }

            YiKit.IniWrite(appName + "_args", args, "ini");
            if (!string.IsNullOrWhiteSpace(url))
            {
                menuItem.Tag = url.Trim();
                UpdateThirdPartyCustomUrl(appName, url.Trim());
            }

            YiKit.ShowToast($"{appName} 已保存");
            return;
        }

        var retArgs = YiKit.ShowInputDialog($"设置 {appName} 启动参数",
            $"{appName}启动参数:" + (string.IsNullOrEmpty(savedArgs) ? "" : savedArgs));
        if (retArgs == null) return;

        string onlyArgs = retArgs.Count > 0 ? (retArgs[0] ?? "") : "";
        YiKit.IniWrite(appName + "_args", onlyArgs, "ini");
        YiKit.ShowToast($"{appName} 启动参数已保存", l: (int)(Left + Width / 2), t: (int)(Top + 30));
    }

    private const string ThirdPartyIniArea = "third_party";

    private void ThirdPartyMenu_OnRightClick(object sender, MouseButtonEventArgs e)
    {
        // 只在点到「三方」标题本身时添加，点子项不拦截
        if (!ReferenceEquals(e.OriginalSource, sender) &&
            !(e.OriginalSource is System.Windows.Controls.TextBlock tb &&
              sender is MenuItem mi && tb.Text == Convert.ToString(mi.Header)))
        {
            // 子菜单项的右键交给各自处理
            var src = e.OriginalSource as DependencyObject;
            while (src != null && src is not MenuItem)
                src = VisualTreeHelper.GetParent(src);
            if (src is MenuItem clicked && !ReferenceEquals(clicked, ThirdPartyMenu))
                return;
        }

        e.Handled = true;
        AddThirdPartyCustomInteractive();
    }

    private void ThirdPartyAdd_OnClick(object sender, RoutedEventArgs e) =>
        AddThirdPartyCustomInteractive();

    private void AddThirdPartyCustomInteractive()
    {
        var ret = YiKit.ShowInputDialog("添加三方工具",
            "名称(文件夹/程序名):",
            "下载链接(zip):");
        if (ret == null || ret.Count < 2) return;

        string name = (ret[0] ?? "").Trim();
        string url = (ret[1] ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
        {
            YiKit.ShowToastError("名称和下载链接都不能为空");
            return;
        }

        // 非法路径字符
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        if (IsBuiltinThirdPartyName(name) || FindThirdPartyCustomIndex(name) >= 0)
        {
            YiKit.ShowToastError("已存在同名三方项: " + name);
            return;
        }

        AppendThirdPartyCustom(name, url);
        EnsureThirdPartyCustomMenuItem(name, url);
        YiKit.ShowToast($"已添加三方: {name}");
    }

    private static bool IsBuiltinThirdPartyName(string name)
    {
        string[] builtins = { "NMTC", "ocr", "scrcpy", "adb", "procexp", "添加…" };
        return builtins.Any(b => string.Equals(b, name, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadThirdPartyCustomMenus()
    {
        if (ThirdPartyMenu == null) return;

        // 清掉旧的自定义项（Uid=custom）
        for (int i = ThirdPartyMenu.Items.Count - 1; i >= 0; i--)
        {
            if (ThirdPartyMenu.Items[i] is MenuItem mi &&
                string.Equals(Convert.ToString(mi.Uid), "custom", StringComparison.OrdinalIgnoreCase))
                ThirdPartyMenu.Items.RemoveAt(i);
        }

        int count = YiKit.IniGetInt("count", 0, ThirdPartyIniArea);
        bool any = false;
        for (int i = 0; i < count; i++)
        {
            string name = YiKit.IniGet($"item_{i}_name", ThirdPartyIniArea, def: "");
            string url = YiKit.IniGet($"item_{i}_url", ThirdPartyIniArea, def: "");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;
            EnsureThirdPartyCustomMenuItem(name.Trim(), url.Trim());
            any = true;
        }

        if (ThirdPartyCustomSep != null)
            ThirdPartyCustomSep.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EnsureThirdPartyCustomMenuItem(string name, string url)
    {
        // 已存在则更新 Tag
        foreach (var obj in ThirdPartyMenu.Items)
        {
            if (obj is MenuItem exist &&
                string.Equals(Convert.ToString(exist.Header), name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Convert.ToString(exist.Uid), "custom", StringComparison.OrdinalIgnoreCase))
            {
                exist.Tag = url;
                return;
            }
        }

        var item = new MenuItem
        {
            Header = name,
            Tag = url,
            Uid = "custom",
            ToolTip = "左键：下载/启动；右键：参数/链接/删除"
        };
        item.Click += OtherProgress_OnClick;
        item.PreviewMouseRightButtonDown += OtherProgressRight_OnClick;

        // 插在「添加…」之前、分隔符之后
        int insertAt = ThirdPartyMenu.Items.Count;
        for (int i = 0; i < ThirdPartyMenu.Items.Count; i++)
        {
            if (ThirdPartyMenu.Items[i] is MenuItem mi &&
                string.Equals(Convert.ToString(mi.Header), "添加…", StringComparison.Ordinal))
            {
                insertAt = i;
                break;
            }
        }

        ThirdPartyMenu.Items.Insert(insertAt, item);
        if (ThirdPartyCustomSep != null)
            ThirdPartyCustomSep.Visibility = Visibility.Visible;
    }

    private void AppendThirdPartyCustom(string name, string url)
    {
        int count = YiKit.IniGetInt("count", 0, ThirdPartyIniArea);
        YiKit.IniWrite($"item_{count}_name", name, ThirdPartyIniArea);
        YiKit.IniWrite($"item_{count}_url", url, ThirdPartyIniArea);
        YiKit.IniWrite("count", (count + 1).ToString(), ThirdPartyIniArea);
    }

    private int FindThirdPartyCustomIndex(string name)
    {
        int count = YiKit.IniGetInt("count", 0, ThirdPartyIniArea);
        for (int i = 0; i < count; i++)
        {
            string n = YiKit.IniGet($"item_{i}_name", ThirdPartyIniArea, def: "");
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void UpdateThirdPartyCustomUrl(string name, string url)
    {
        int idx = FindThirdPartyCustomIndex(name);
        if (idx < 0) return;
        YiKit.IniWrite($"item_{idx}_url", url, ThirdPartyIniArea);
    }

    private void RemoveThirdPartyCustom(string name)
    {
        int count = YiKit.IniGetInt("count", 0, ThirdPartyIniArea);
        var list = new List<(string name, string url)>();
        for (int i = 0; i < count; i++)
        {
            string n = YiKit.IniGet($"item_{i}_name", ThirdPartyIniArea, def: "");
            string u = YiKit.IniGet($"item_{i}_url", ThirdPartyIniArea, def: "");
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                continue;
            list.Add((n, u));
        }

        for (int i = 0; i < list.Count; i++)
        {
            YiKit.IniWrite($"item_{i}_name", list[i].name, ThirdPartyIniArea);
            YiKit.IniWrite($"item_{i}_url", list[i].url, ThirdPartyIniArea);
        }

        // 清掉尾部旧键
        for (int i = list.Count; i < count; i++)
        {
            YiKit.IniWrite($"item_{i}_name", "", ThirdPartyIniArea);
            YiKit.IniWrite($"item_{i}_url", "", ThirdPartyIniArea);
        }

        YiKit.IniWrite("count", list.Count.ToString(), ThirdPartyIniArea);
        YiKit.IniWrite(name + "_args", "", "ini");
        YiKit.IniWrite(name + "_exe", "", "ini");
        LoadThirdPartyCustomMenus();
    }

/// <summary>
/// 批量合并MP3：选择tmp文件夹后，自动按文件名排序合并每个子文件夹内的mp3，输出到tmp/out
/// </summary>
private void MergeMp3Item_OnClick(object sender, RoutedEventArgs e)
{
    string type = "媒体文件";
    List<string> strs = YiKit.ShowInputDialog(
         "mp3合并",
        "媒体类型:mp3",
        "文件夹位置："
      
    );
    if (strs == null || strs.Count < 1)
    {
        YiKit.ShowToastError("参数错误");
        return;
    }

 

    string tmpDir = strs[1];
    string pox = strs[0];
    string outDir = Path.Combine(tmpDir, "out");

    YiKit.RunThread(_ =>
    {
        try
        {
            if (!Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            var subDirs = Directory.GetDirectories(tmpDir)
                .Where(d => !d.Equals(outDir, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (subDirs.Count == 0)
            {
                YiKit.ShowToastWaring("未找到任何子文件夹");
                return;
            }

            int successCount = 0;
            int failCount = 0;

            foreach (var dir in subDirs)
            {
                string folderName = Path.GetFileName(dir);
                string outputFile = Path.Combine(outDir, folderName +"."+ pox);

                // 按文件名自然排序获取所有 mp3
                var mp3Files = Directory.GetFiles(dir, "*."+pox)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (mp3Files.Count == 0)
                {
                    YiKit.Print($"[SKIP] {folderName}: 无MP3文件");
                    continue;
                }

                // 生成 ffmpeg concat 列表文件
                string listFile = Path.Combine(Path.GetTempPath(), $"ffmpeg_concat_{Guid.NewGuid():N}.txt");
                try
                {
                    var sb = new StringBuilder();
                    foreach (var file in mp3Files)
                    {
                        // concat demuxer 要求单引号包裹路径，并转义内部单引号
                        string escaped = file.Replace("'", "'\\''");
                        sb.AppendLine($"file '{escaped}'");
                    }
                    var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                    File.WriteAllText(listFile, sb.ToString(), utf8NoBom);

                    // 调用 ffmpeg 无损合并（不使用 -safe 0 以兼容旧版本）
                    string args = $"-y -f concat -safe 0  -i \"{listFile}\" -c copy \"{outputFile}\" -loglevel warning";
                    YiKit.RunCmd($"ffmpeg.exe {args}");

                    if (File.Exists(outputFile) && new FileInfo(outputFile).Length > 0)
                    {
                        successCount++;
                        YiKit.ShowToast($"[OK] {folderName} ({mp3Files.Count}个文件)");
                    }
                    else
                    {
                        failCount++;
                        YiKit.ShowToastError($"[FAIL] {folderName} 合并失败");
                    }
                }
                finally
                {
                    // 清理临时列表文件
                    try { File.Delete(listFile); } catch { }
                }
            }

            YiKit.ShowToast($"全部完成！成功:{successCount} 失败:{failCount}\n输出: {outDir}");
        }
        catch (Exception ex)
        {
            YiKit.ShowToastError("MP3合并异常: " + ex.Message);
        }
    });
}

/// <summary>
/// 批量命名：读取文件夹内媒体文件的ID3标签/文件名，按格式（默认 歌手-歌曲-流派.后缀）预览并重命名
/// </summary>
private void BatchRenameItem_OnClick(object sender, RoutedEventArgs e)
{
    var dlg = new BatchRenameDialog { Owner = this };
    dlg.Show();
}

/// <summary>
/// uc转换：左键开关自动监听（新文件按上次窗口设置自动转换），右键打开设置窗口。
/// 源文件夹为空时自动弹窗口让用户先配置。
/// </summary>
private bool ucWatchAfterConfig;

private void UcConvertItem_OnClick(object sender, RoutedEventArgs e)
{
    if (UcAutoWatch.IsRunning)
    {
        UcAutoWatch.Stop();
        UcConvertItem.IsChecked = false;
        YiKit.IniWrite("auto", "false", "uc");
        YiKit.ShowToast("已关闭 uc 自动监听");
        return;
    }

    var opt = UcOptions.Load();
    if (!Directory.Exists(opt.SrcDir))
    {
        ucWatchAfterConfig = true;
        OpenUcConvertDialog();
        YiKit.ShowToastWaring("请先设置源文件夹，关闭窗口后自动开始监听");
        return;
    }
    if (!UcAutoWatch.Start(opt))
    {
        YiKit.ShowToastError("uc自动监听启动失败: " + opt.SrcDir);
        return;
    }
    UcConvertItem.IsChecked = true;
    YiKit.IniWrite("auto", "true", "uc");
    YiKit.ShowToast("已开启 uc 自动监听: " + opt.SrcDir + "（已有文件请右键打开窗口手动转换）");
}

private void UcConvertItem_OnRClick(object sender, MouseButtonEventArgs e)
{
    e.Handled = true;
    OpenUcConvertDialog();
}

private void OpenUcConvertDialog()
{
    var dlg = new UcConvertDialog { Owner = this };
    bool startAfter = ucWatchAfterConfig;
    ucWatchAfterConfig = false;
    dlg.Closed += (_, _) =>
    {
        // 窗口关闭时设置已保存：需要开始，或正在监听时按新设置重启
        if (!startAfter && !UcAutoWatch.IsRunning) return;

        var opt = UcOptions.Load();
        if (Directory.Exists(opt.SrcDir) && UcAutoWatch.Start(opt))
        {
            UcConvertItem.IsChecked = true;
            YiKit.IniWrite("auto", "true", "uc");
            if (startAfter) YiKit.ShowToast("已开启 uc 自动监听: " + opt.SrcDir);
        }
        else if (startAfter)
        {
            YiKit.ShowToastWaring("没有有效的源文件夹，自动监听未开启");
        }
        else
        {
            UcAutoWatch.Stop();
            UcConvertItem.IsChecked = false;
            YiKit.IniWrite("auto", "false", "uc");
            YiKit.ShowToastWaring("源文件夹已失效，uc自动监听已停止");
        }
    };
    dlg.Show();
}

/// <summary>程序启动时恢复自动监听（与 ini 里的开关保持一致）</summary>
private void RestoreUcAutoWatch()
{
    if (!YiKit.IniGetBool("auto", false, "uc")) return;
    var opt = UcOptions.Load();
    if (Directory.Exists(opt.SrcDir) && UcAutoWatch.Start(opt))
        UcConvertItem.IsChecked = true;
    else
        YiKit.IniWrite("auto", "false", "uc");
}

/// <summary>兜底：下载（如缺失）并启动第三方 NMTC_V1.5 转换工具</summary>
private void NmtcItem_OnClick(object sender, RoutedEventArgs e)
{
    YiKit.RunThread(_ => UcConverter.EnsureAndLaunchNmtc());
}
}