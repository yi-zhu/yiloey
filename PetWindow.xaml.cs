using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Net;
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
using System.Windows.Forms;
using System.Windows.Media.Effects;
using holey.Dialog;
using holey.Page;
using YiKits;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using Control = System.Windows.Forms.Control;
using InputDialog = YiKits.Dialog.InputDialog;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
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

        if (l >= r.Width)
        {
            l = r.Width - Width - 10;
        }

        if (t >= r.Height)
        {
            t = r.Bottom - Height - 10;
        }

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


            ;
        };


        YiKit.SetForegroundWindow(YiKit.GetWindowHandle(this));

        //按键监听
        YiKit.KeyDownEvent += (((sender, e) =>
        {
            if (e.KeyValue == (int)Keys.Q && (Control.ModifierKeys & Keys.Shift) == Keys.Shift &&
                (Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                isTranslateing = true;

                // YiKit.Print("按下了Ctrl+alt+Q:");
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
            new WallPaper();
            WallPaper.me.Show();
            WallPaper.me.Activate();
        }


        if (YiKit.IniGet("centzoom", def: "0").Equals("1"))
            CentZomm_OnItemClick(CentZoom, null);
        if (YiKit.IniGet("crosshair", def: "0").Equals("1"))
            Crosshair_OnItemClick(Crosshair, null);
    }


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
            clbname = YiKit.IniGet("clbname", "ini", def: System.Environment.UserName);
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
                            tapas = string.Format("{0:0}", currentDateTime.Hour) + ":" + YiKit.NowD() + "." +
                                    YiKit.NowW();
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
                            if (!tapas.Equals(MainStrN.Text))
                            {
                                MainStrN.Text = tapas;
                                lastDown = nowDown;
                                lastUp = nowUp;
                            }

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

                YiKit.RunOnUIThread(() =>
                {
                    //cpu
                    CpuRect.Width = cpuse;
                    if (!YiKit.NowSD().Equals(CpuStrR.Text))
                        CpuStrR.Text = YiKit.NowSD();
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


                YiKit.CpuTem((b) =>
                {
                    if (null == b)
                    {
                        return;
                    }

                    if (b.ToString() == "0")
                    {
                        return;
                    }

                    YiKit.RunOnUIThread(() =>
                    {
                        if (!ReferenceEquals(b + "℃", CpuStr.Text))
                        {
                            CpuStr.Text = b + "℃";
                            if (b is float bf)
                            {
                                if (CpuStr.Foreground is LinearGradientBrush brush)
                                {
                                    brush.StartPoint =
                                        new Point(0, (125 - bf) / 2);
                                }
                            }
                        }
                    });
                });
                YiKit.GpuTem((b) =>
                {
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
                            if ((float)b > 90)
                            {
                                if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                {
                                    linearGradientBrush.GradientStops[0].Color = Colors.Red;
                                    linearGradientBrush.GradientStops[1].Color = Colors.Red;
                                }
                            }
                            else if ((float)b > 80)
                            {
                                if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                {
                                    linearGradientBrush.GradientStops[0].Color = Colors.Firebrick;
                                    linearGradientBrush.GradientStops[1].Color = Colors.Red;
                                }
                            }
                            else if ((float)b > 70)
                            {
                                if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                {
                                    linearGradientBrush.GradientStops[0].Color = Colors.Fuchsia;
                                    linearGradientBrush.GradientStops[1].Color = Colors.Firebrick;
                                }
                            }
                            else if ((float)b > 60)
                            {
                                if (StatStrRect.Fill is LinearGradientBrush linearGradientBrush)
                                {
                                    linearGradientBrush.GradientStops[0].Color = Colors.BlueViolet;
                                    linearGradientBrush.GradientStops[1].Color = Colors.Firebrick;
                                }
                            }
                            else if ((float)b > 50)
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
        YiKits.Dialog.InputDialog inputDialog = new InputDialog(name, names);
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
            lastClipboardText = YiKit.GetClipboardText();
            // ((sender as MenuItem)!).Header = "已开启剪切板监听";
        }

        YiKit.IniWrite("clipboard", value: _clipboard + "", area: "ini");
    }

    //读取cpu温度

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
            else if (text == lastClipboardText)
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

    public void StartWb()
    {
        if (YiWscClient != null)
        {
            return;
        }

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
                msg += "剪切板共享 ";
            }

            if (_translate == 1)
            {
                TranslateMenuItem.Header = "已开启翻译";
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
            var ret="";
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
                YiKit.ShowToastOther("请观察电视是否弹出允许或确定对话框，请允许或确定！",time:30);
                YiKit.RunCmd("adb install" + apk);
            }
            else
            {
                YiKit.ShowToastError("请在电视设置->用户（或安全选项卡）->打开允许调试或adb调试！");
            }
        }));
    }
}