using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Windows.Controls;
using holey.Dialog;
using YiKits;

namespace holey;

public partial class PetWindow : Window
{
    #region 窗口隐藏

    [DllImport("user32.dll", SetLastError = true)]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EX_STYLE = -20;
    private const int WS_EX_APPWINDOW = 0x00040000, WS_EX_TOOLWINDOW = 0x00000080;

    private int _clipboard = 0;

    public static PetWindow Me;

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
        r = SystemParameters.WorkArea;
        Left = r.Width - Width - 10;
        Top = r.Bottom - Height - 10;
        // YiKit.Print(Left);
        // YiKit.Print(Width);
        // YiKit.Print(Top);
        // YiKit.GetAllCate();
        LoadPet();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        //无边框
        YiKit.Areo(this, new Thickness(-1));

        base.OnSourceInitialized(e);
        _clipboard = YiKit.IniGetInt("clipboard", area: "ini", def: 0);
        if (_clipboard == 1)
        {
            //开启剪切板监听
            YiKit.ClipboardMonitor(this, OnClipboardUpdated);
            clbname = YiKit.IniGet("clbname", "ini", def: "");
            StartWb();
        }
    }


    protected override void OnClosed(EventArgs e)
    {
        YiKit.CloseClipboardMonitor(OnClipboardUpdated);
    }


    private int _sleep = 900;

    /// <summary>
    /// 默认
    /// </summary>
    private bool IsVpn { get; set; } = true;

    private bool IsAlive { get; set; } = true;

    private void LoadPet()
    {
        YiKit.RunThread(_ =>
        {
            long lastUp = 0;
            long lastDown = 0;
            int cpuse = 0;
            int memuse = 0;
            while (IsAlive)
            {
                var tap = IsVpn ? YiKit.GetInterface(ip: "172.27.") : YiKit.GetInterface(vir: false);

                if (tap == null || tap.OperationalStatus != OperationalStatus.Up)
                {
                    IsVpn = !IsVpn;
                    Thread.Sleep(_sleep / 2);
                    continue;
                }

                IPInterfaceProperties ipInterface = tap.GetIPProperties();
                // string MAC = adapter.GetPhysicalAddress().ToString(); //获取单播地址集
                UnicastIPAddressInformationCollection ipCollection = ipInterface.UnicastAddresses;
                string tapas = "";
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
                        tapas += "速：" + (nowUp == lastUp ? "  " : "⇡")
                                      + (nowDown == lastDown ? "  " : "⇣") + " "
                                      + YiKit.SumByte((nowDown + nowUp - lastDown - lastUp) * (1000 / _sleep)) + "\n";

                        tapas += "上" + (nowUp == lastUp ? "：" : "↑: ") + YiKit.SumByte(nowUp) + "\n";
                        tapas += "下" + (nowDown == lastDown ? "：" : "↓: ") + YiKit.SumByte(nowDown) + "\n";
                        // tapas += "ip:"+ipaddr.Address + "\n";
                        // tapas += "MAC:"+tap.GetPhysicalAddress() + "\n";
                        YiKit.RunOnUIThread(() =>
                        {
                            if (!tapas.Equals(MainStr.Content.ToString()))
                            {
                                MainStr.Content = tapas;

                                CpuRectEnd.ToolTip = ipaddr.Address;
                            }

                            lastDown = nowDown;
                            lastUp = nowUp;
                        });
                        break;
                    }
                }

                #region CPU使用率

                cpuse = (int)YiKit.CpuUse();
                memuse = (int)YiKit.MemUse();
                YiKit.RunOnUIThread(() => CpuRect.Width = cpuse);
                YiKit.RunOnUIThread(() =>
                {
                    if (!MemStr.Text.Equals(memuse + ""))
                    {
                        MemBtn.Height = memuse / 2;
                        MemStr.Text = memuse + "";
                    }
                });
                YiKit.CpuTem((b) => { YiKit.RunOnUIThread(() => { CpuStr.Text = b + "℃"; }); });
                YiKit.GpuTem((b) => { YiKit.RunOnUIThread(() => { StatStr.Text = b + ""; }); });
                // YiKit.Print(YiKit.MemUse());

                #endregion

                Thread.Sleep(_sleep);
            }
        });
    }


    protected override void OnClosing(CancelEventArgs e)
    {
        IsAlive = false;

        base.OnClosing(e);
    }

    private void PetWindow_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            this.DragMove();
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
        if (PresentationSource.FromVisual(MainWindow.Me) == null)
        {
            new MainWindow().Show();
        }
        else MainWindow.Me?.Show();

        //退出
        this.Close();
    }

    /// <summary>
    /// 右键退出
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ExitItem_OnClick(object sender, RoutedEventArgs e)
    {
        //退出
        this.Close();
        Application.Current.Shutdown();
    }

    private void Control_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        IsVpn = !IsVpn;
    }

    private void PintBtn_OnClick(object sender, RoutedEventArgs e)
    {
        this.AllowsTransparency = !AllowsTransparency;
        YiKit.Print("dsds");
        //鼠标穿透
        SetWindowLong(new WindowInteropHelper(this).Handle, -20, 0x20);
    }

    private void CpuRectEnd_OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        IsVpn = !IsVpn;
    }

    private void TaskmgrItem_OnClick(object sender, RoutedEventArgs e)
    {
        YiKit.StartProcessSync("taskmgr");
    }


    private List<string> ShowInputDialogInWindow(string name, params string[] names)
    {
        InputDialog inputDialog = new InputDialog(name, names);
        List<string> strs = null;

        inputDialog.Result += (sender, _) => { strs = sender as List<string>; };
        Point screenCoordinates = this.PointToScreen(new Point(0, 0));


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
                FileName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName,
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

            this.Close();
        }
    }

    private List<string> suspendsList = new List<string>();

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
                FileName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName,
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

            this.Close();
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
                FileName = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName,
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

            this.Close();
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


    private string clbname = "";

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
                YiKit.ShowInputDialogDefValue("请输入相同的匹配码或者相同的名称,系统会自动记忆", YiKit.IniGet("clbname", "ini", def: ""))[0];
            if (YiWscClient != null && YiWscClient.State != WebSocketState.Open)
                CloseWb();
            StartWb();

            YiKit.IniWrite("clbname", clbname, "ini");


            YiKit.ClipboardMonitor(this, OnClipboardUpdated);
            ((sender as MenuItem)!).Header = "已开启剪切板监听";
        }

        {
        }
        YiKit.IniWrite("clipboard", value: _clipboard + "", area: "ini");
    }

    //读取cpu温度

    private static string lastClipboardText = "";

    private void OnClipboardUpdated(object sender, EventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            string text = "";
            try
            {
                text = Clipboard.GetText();
                if (lastClipboardText.Equals(text)) return;
            }
            catch (Exception)
            {
                Thread.Sleep(10);
                YiKit.Print("error:--------------------------------------------");
                try
                {
                    text = Clipboard.GetText();
                    if (lastClipboardText.Equals(text)) return;
                }
                catch (Exception)
                {
                    YiKit.Print("error2:");
                }
            }

            lastClipboardText = text;
            if (YiWscClient.Send("cpy:" + clbname + ":" + text) == false)
            {
                YiWscClient.Open();
                YiWscClient.Send("cpy:" + clbname + ":" + text);
            }

            ;

            YiKit.Print(text);
        }
        // string[] fs = data.GetFormats();
        // tb.Text += $"\n数据格式：{string.Join("、", fs)}";
    }

    #region websocket

    public YiWebSocketClient YiWscClient;

    public void CloseWb()
    {
        YiWscClient?.Close();
    }

    public void StartWb()
    {
        YiKit.RunThread((state =>
        {
            YiWscClient = new YiWebSocketClient("ws://27.221.101.137:10011/", clbname);
            YiWscClient.OnOpen += YiWscClientOnOpen;
            YiWscClient.OnMessage += YiWscClientOnMessage;
            YiWscClient.OnClose += YiWscClientOnClose;
            YiWscClient.OnError += YiWscClientOnError;
            YiWscClient.Open();
        }));
    }

    private void YiWscClientOnError(object sender, Exception ex)
    {
    }

    private void YiWscClientOnClose(object sender, EventArgs e)
    {
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
                Thread th = new Thread(delegate() { YiKit.SetClipboardText(data.Remove(0, 4)); });
                th.TrySetApartmentState(ApartmentState.STA);
                th.Start();
                th.Join();
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
}