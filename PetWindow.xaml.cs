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
using System.Net.WebSockets;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using holey.Dialog;
using ShockwaveFlashObjects;
using YiKits;
using Point = System.Windows.Point;

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
        _zoom = YiKit.IniGetDouble("zoompet", 1);
        if (Width != w * _zoom)
        {
            this.Width = w * _zoom;
        }

        if (Height != h * _zoom)
        {
            this.Height = h * _zoom;
        }

        // YiKit.Print(Left);
        // YiKit.Print(Width);
        // YiKit.Print(Top);
        // YiKit.GetAllCate();
        try
        {
            _sleep = (int)(Convert.ToSingle(YiKit.IniGet("sleep", "ini", def: "1")) * 1000);
        }
        catch (Exception e)
        {
            _sleep = 1000;
        }

        if (_sleep <= 1)
        {
            _sleep = 1000;
        }

        r = SystemParameters.WorkArea;

        l = YiKit.IniGetDouble("petleft", r.Width - Width - 10);
        t = YiKit.IniGetDouble("pettop", r.Bottom - Height - 10);

        IsVpn = YiKit.IniGetBool("isvpn", true);

        if (this.Left != l)
        {
            Left = l;
        }

        if (this.Top != t)
        {
            Top = t;
        }

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
            {
                YiKit.IniWrite("petleft", this.Left.ToString(), "ini");
            }

            if (this.Top != t)
            {
                YiKit.IniWrite("pettop", this.Top.ToString(), "ini");
            }
        };


        YiKit.SetForegroundWindow(YiKit.GetWindowHandle(this));
        
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

    private void LoadPet()
    {
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal;
        Thread.CurrentThread.Priority = ThreadPriority.Lowest;
        YiKit.RunThread(_ =>
        {
            long lastUp = 0;
            long lastDown = 0;
            bool isnowork = false;

            while (IsAlive)
            {
                var tap = IsVpn ? YiKit.GetInterface(ip: "172.27.") : YiKit.GetInterface(vir: false);

                if (tap == null || tap.OperationalStatus != OperationalStatus.Up)
                {
                    IsVpn = !IsVpn;
                    Thread.Sleep(_sleep);
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
                        if (nowDown == lastDown && nowUp == lastUp)
                        {
                            if (isnowork) break;
                            tapas += "速\n";
                            tapas += "上" + (nowUp == lastUp ? "：" : "↑: ") + YiKit.SumByte(nowUp) + "\n";
                            tapas += "下" + (nowDown == lastDown ? "：" : "↓: ") + YiKit.SumByte(nowDown) + "\n";

                            isnowork = true;
                        }
                        else
                        {
                            isnowork = false;
                            tapas += "速" + (nowUp == lastUp ? "  " : "⇡") +
                                     (((nowDown - lastDown) >= (nowUp - lastUp)) ? "⇣" : "⇡")
                                     + (nowDown == lastDown ? "  " : "⇣") + " "
                                     + YiKit.SumByte((nowDown + nowUp - lastDown - lastUp) * (1000f / _sleep)) + "\n";

                            tapas += "上" + (nowUp == lastUp ? "：" : "↑: ") + YiKit.SumByte(nowUp) + "\n";
                            tapas += "下" + (nowDown == lastDown ? "：" : "↓: ") + YiKit.SumByte(nowDown) + "\n";
                        }


                        // tapas += "ip:"+ipaddr.Address + "\n";
                        // tapas += "MAC:"+tap.GetPhysicalAddress() + "\n";
                        YiKit.RunOnUIThread(() =>
                        {
                            if (!tapas.Equals(MainStr.Content.ToString()))
                            {
                                MainStr.Content = tapas;
                                StatStrRect.ToolTip = ipaddr.Address;
                                lastDown = nowDown;
                                lastUp = nowUp;
                            }
                        });
                        break;
                    }
                }


                Thread.Sleep(_sleep);
            }
        });
        YiKit.RunThread(_ =>
        {
            int cpuse = 0;
            int memuse = 0;
            while (IsAlive)
            {
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
                YiKit.CpuTem((b) =>
                {
                    YiKit.RunOnUIThread(() =>
                    {
                        if (!ReferenceEquals(b + "℃", CpuStr.Text))
                        {
                            CpuStr.Text = b + "℃";
                            if (b is float bf)
                            {
                                if (CpuStr.Foreground is LinearGradientBrush)
                                {
                                    (CpuStr.Foreground as LinearGradientBrush)!.StartPoint =
                                        new Point(0, (100 - bf) / 2);
                                }
                            }
                        }
                    });
                });
                YiKit.GpuTem((b) =>
                {
                    YiKit.RunOnUIThread(() =>
                    {
                        if (!ReferenceEquals(b, StatStr.Text))
                        {
                            if ((float)b > 80)
                            {
                                if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                {
                                    linearGradientBrush.GradientStops[0].Color = Colors.Red;
                                    linearGradientBrush.GradientStops[1].Color = Colors.Red;
                                }
                            }
                            else if ((float)b > 70)
                            {
                                if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                {
                                    linearGradientBrush.GradientStops[0].Color = Colors.Firebrick;
                                    linearGradientBrush.GradientStops[1].Color = Colors.Red;
                                }
                            }
                            else if ((float)b > 60)
                            {
                                if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                {
                                    linearGradientBrush.GradientStops[0].Color = Colors.Fuchsia;
                                    linearGradientBrush.GradientStops[1].Color = Colors.Firebrick;
                                }
                            }
                            else if ((float)b > 50)
                            {
                                if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                {
                                    linearGradientBrush.GradientStops[0].Color = Colors.BlueViolet;
                                    linearGradientBrush.GradientStops[1].Color = Colors.Firebrick;
                                }
                            }
                            else if ((float)b > 40)
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
                                    linearGradientBrush.GradientStops[1].Color = Colors.Blue;
                                }
                            }

                            StatStr.Text = b + "";
                        }
                    });
                });
                // YiKit.Print(YiKit.MemUse());

                #endregion

                Thread.Sleep(_sleep);
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
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
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
        IsVpn = !IsVpn;
        YiKit.IniWrite("isvpn", IsVpn.ToString());
        YiKit.ShowToast(IsVpn ? "切换专网" : "显示公网", l: (int)((int)Left + Width / 2), t: (int)((int)Top - Height / 2));
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
        YiKit.StartProcessSync("taskmgr");
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
                YiKit.ShowInputDialogDefValue("请输入相同的匹配码或者相同的名称,系统会自动记忆", YiKit.IniGet("clbname", "ini", def: ""))?[0];
            if (string.IsNullOrEmpty(clbname)) //如果没有输入或者输入为空
            {
                return;
            }

            if (YiWscClient != null && YiWscClient.State != WebSocketState.Open)
                CloseWb();
            StartWb();

            YiKit.IniWrite("clbname", clbname, "ini");
            YiKit.ClipboardMonitor(this, OnClipboardUpdated);
            // ((sender as MenuItem)!).Header = "已开启剪切板监听";
        }

        YiKit.IniWrite("clipboard", value: _clipboard + "", area: "ini");
    }

    //读取cpu温度

    private static string lastClipboardText = "";

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

            YiWscClient.Send("ait::"   + text);
            Thread.Sleep(100);
            lastClipboardText = text;
            if (YiWscClient.Send("cpy:" + clbname + ":" + text) == false)
            {
                YiWscClient.Open();
                YiWscClient.Send("cpy:" + clbname + ":" + text);
            }

         

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
    }

    private void YiWscClientOnClose(object sender, EventArgs e)
    {
        YiKit.RunOnUIThread(() =>
        {
            ClipboardMenuItem.Header = "剪切板已关闭";
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
                Thread th = new Thread(delegate() { YiKit.SetClipboardText(data.Remove(0, 4)); });
                th.TrySetApartmentState(ApartmentState.STA);
                th.Start();
                th.Join();
            } else if (data.StartsWith("ait:"))
            {
                /*YiKit.RunThread((delegate(object state)
                {
                    YiKit.SetClipboardText(data.Remove(0,4));
                }));*/
                YiKit.Print(data);
                YiKit.RunOnUIThread(() =>
                {
                    YiKit.ShowToast(data.Substring(4).Replace(':', ' '));

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
            ClipboardMenuItem.Header = "已开启剪切板";
            YiKit.ShowToast("已开启剪切板共享");
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
        string? s = YiKit.ShowInputDialogDefValue("监控刷新时间(秒)", YiKit.IniGet("sleep", "ini", def: "1"))?[0];
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
        string? s = YiKit.ShowInputDialogDefValue("设置放大或缩小倍数", YiKit.IniGet("zoompet", "ini", def: "1"))?[0];
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
}