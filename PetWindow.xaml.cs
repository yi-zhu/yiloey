using System;
using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

    public PetWindow()
    {
        InitializeComponent();
        Me = this;
        var r = SystemParameters.WorkArea;
        Left = r.Right * 0.93 - ActualWidth;
        Top = r.Bottom * 0.9 - ActualHeight;
        YiKit.Print(Left);
        YiKit.Print(Top);
        LoadPet();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        YiKit.Areo(this, new Thickness(-1));
        base.OnSourceInitialized(e);
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

                        tapas += "上"+ (nowUp == lastUp ? "：" : "↑: ") + YiKit.SumByte(nowUp) + "\n";
                        tapas += "下" + (nowDown == lastDown ? "：" : "↓: ") + YiKit.SumByte(nowDown)  + "\n";
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
                        MemBtn.Height = memuse/2;
                        MemStr.Text = memuse +""; 
                    }
                     
                });
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
        if (PresentationSource.FromVisual(MainWindow.Me) == null)
        {
            new MainWindow().Show();
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
}