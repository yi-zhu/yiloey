using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using holey.Page;
using YiKits;

namespace holey
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainPage _mainPage;
        private TapPage _tapPage;
        private SetPage _setPage;
        public static bool IsAlive { get; set; } = true;
        public static MainWindow Me { get; set; }
        public static PetWindow? PetWindow;
        public static MemoWindow? MemoWindow;

        /// <summary>
        /// 默认运行64位程序
        /// </summary>
        private static string _vname = "eyi";

        public MainWindow()
        {
            if (PetWindow.Me == null)
            {
                PetWindow = new PetWindow();
                PetWindow.Show();
                Hide();
            }

            InitializeComponent();
            IsAlive = true;
            Me = this;
            _mainPage = new MainPage();
            _tapPage = new TapPage();
            MPage.Content = new Frame { Content = _mainPage };
            if (YiKit.IsX86())
            {
                _vname = "ey";
            }
        }


        //启动其他应用程序

        public static void StarteYi()
        {
            ShowLoading();
            NetworkInterface tap = null;
            YiKit.StartProcessAsync(_vname +
                                    " -c xiuxingzhe -k yizhu -r -a  dhcp:0.0.0.0 -s 255.240.0.0 -b  -i 20 -b -l yizhu.space:65533 "
                , a =>
                {
                    YiKit.Print(a);
                    string? msg = a as string;

                    if (msg.Contains("Open device "))
                    {
                        string name = msg.Substring(msg.IndexOf("{"), msg.IndexOf("}") + 1 - msg.IndexOf("{"));
                        tap = YiKit.GetInterface(name);
                        YiKit.RunOnUIThread(() => ShowLoading(false));
                    }
                    else if (msg.Contains("Rx REGISTER_SUPER_ACK"))
                    {
                        YiKit.Print("up:" + tap?.GetIPProperties().UnicastAddresses[0].Address + "down:" +
                                    tap?.GetIPv4Statistics().BytesReceived);
                    }
                    else if (msg.Contains("Re-checking dynamic IP address"))
                    {
                        YiKit.StartProcessSync("route delete 0.0.0.0 mask 0.0.0.0 172.27.0.1");
                    }
                    else if (msg.Contains("No Windows tap device found"))
                    {
                        //执行失败,没有网卡，或者已经运行
                        Thread.Sleep(300);
                        //执行判断运行网卡，
                        Process[] processes = Process.GetProcesses();
                        foreach (Process process in processes)
                        {
                            if (process.ProcessName.Equals(_vname))
                            {
                                //已在运行

                                YiKit.Print(process.ProcessName + "程序已运行");
                                YiKit.RunOnUIThread(() => ShowLoading(false));
                                return;
                            }
                        }

                       

                        YiKit.RunOnUIThread(() => ShowLoading(false));
                        //安装tap
                        YiKit.RunThread((state =>
                        {
                            TapInstall();
                            StarteYi();
                        }));
                    }
                });

            /*YiKit.StartProcessSync(
                string.Format("Taskkill /IM {0} /F", "eyi*")
                , e =>
                {


                });*/
        }

        public static void TapInstall()
        {
            YiKit.RunOnUIThread(() => ShowLoading(true));

            
            string prx = "\""+YiKit.GetAppPath()+"\\Drivers";

            
            YiKit.StartProcessSync("certutil -addstore \"TrustedPublisher\" "+prx+"\\opv.cer\"");
            if (YiKit.IsX86())
            {
                prx += "\\x86\\";
            }
            else
                prx += "\\x64\\";

            YiKit.StartProcessSync(prx+"tapinstall.exe\" install "+prx+"OemVista.inf\" tap0901",end:(state =>
            {
                YiKit.RunOnUIThread(() => ShowLoading(false));

            }));
        }


        public static void ShowLoading(bool show = true)
        {
            if (show)
            {
                if (!Me.LodingGrid.IsVisible)
                {
                    Me.LodingGrid.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (Me.LodingGrid.IsVisible)
                {
                    Me.LodingGrid.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Info_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("版本" + ApplicationHoley.Version + " \n版权所有@yizhu \n日期：2022年10月28日");
        }

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("您的版本" + ApplicationHoley.Version + "已是最新");
        }

        private void TapItem_Click(object sender, RoutedEventArgs e)
        {
            MPage.Content = _tapPage;
        }

        private void IndexItem_Click(object sender, RoutedEventArgs e)
        {
            MPage.Content = _mainPage;
        }

        protected override void OnClosed(EventArgs e)
        {
            IsAlive = false;
            /*
            //销毁progress进程。
            if (YiKit.Processes.Count>0)
            {
                foreach (Process process in YiKit.Processes)
                {
                    process?.Close();
                }
           
           
            }
            var collections = Application.Current.Windows;

            foreach (Window window in collections)
            {
                if (window != this)
                    window.Close();
            }*/
            base.OnClosed(e);
            if (PetWindow.Me==null||!PetWindow.Me.IsVisible)
            {
                Environment.Exit (0); 
            }

        }


        private void TopItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (Topmost)
            {
                Topmost = false;
                TopMenuItem.Header = "📌";
            }
            else
            {
                Topmost = true;
                TopMenuItem.Header = "🔝";
            }
        }


        /// <summary>
        /// 打开右下角宠物窗口
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PetItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (PetWindow == null)
            {
                PetWindow = new PetWindow();
            }

            if (PresentationSource.FromVisual(PetWindow) == null)
            {
                PetWindow = new PetWindow();
                PetWindow.Show();
            }
            else
            {
                if (PetWindow.Visibility == Visibility.Visible) PetWindow.Hide();
                else PetWindow.Show();
            }
        }

        /// <summary>
        /// 打开右上角备忘录
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void MemoItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (MemoWindow == null)
            {
                MemoWindow = new MemoWindow();
            }

            if (PresentationSource.FromVisual(MemoWindow) == null)
            {
                MemoWindow = new MemoWindow();
                MemoWindow.Show();
            }
            else
            {
                if (MemoWindow.Visibility == Visibility.Visible) MemoWindow.Hide();
                else MemoWindow.Show();
            }
        }


        /// <summary>
        /// 设置页面
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SetItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (_setPage == null)
            {
                _setPage = new SetPage();
            }

            MPage.Content = _setPage;
        }


        bool _isAuto = true;

        private void AutoItem_OnClick(object sender, RoutedEventArgs e)
        {
            YiKit.SetAutoStart(isAuto: _isAuto);
            _isAuto = !_isAuto;
        }
    }
}