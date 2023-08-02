using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using holey.Page;
using YiKits;
using System.ServiceProcess;
using System.Configuration.Install;
using System.Management;
using Microsoft.Win32;

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
        private WebPage _webPage;
        public static bool IsAlive { get; set; } = true;
        public static MainWindow Me { get; set; }
        public static PetWindow? PetWindow;
        public static MemoWindow? MemoWindow;

        /// <summary>
        /// 默认运行64位程序
        /// </summary>
        private static string _vname = "eyi";

        private static string _svname = "Edge-V";

        /// <summary>
        /// 服务名称
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static string Svname
        {
            get => _svname;
            set => _svname = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// 获取holey v程序名称
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public static string Vname
        {
            get => (YiKit.IsX86() ? "ey" : "eyi");
            set => _vname = value ?? throw new ArgumentNullException(nameof(value));
        }

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


        /// <summary>
        /// 开启eyi
        /// </summary>
        /// <param name="start"></param>
        /// <param name="autochage"></param>
        /// <returns></returns>
        public static ServiceController StartEyi(bool start = true, bool autochage = false)
        {
            ServiceController mSc = null; //建立服务对象
            ServiceController[] services = ServiceController.GetServices();
            foreach (ServiceController sc in services)
            {
                if (sc.DisplayName.ToLower() == Svname.ToLower())
                {
                    mSc = sc;
                }
            }

            if (mSc == null)
            {
                //
               // sc failure vname reset= 0 actions= restart/3000/restart/3000/restart/3000
                YiKit.StartProcessSync(YiKit.GetAppStartPath() + Vname + ".exe --install");

                /*
                AssemblyInstaller installer = new AssemblyInstaller();
                IDictionary mDictionary = new Hashtable();
                installer.UseNewContext = true;
                installer.Path = Assembly.GetExecutingAssembly().Location;
                installer.CommandLine = new string[1] { "eyi" };
                try
                {
                    installer.Install(mDictionary);
                    installer.Commit(mDictionary);
                }
                catch (Exception e)
                {
                    installer.Uninstall(mDictionary);
                    // installer.Commit(null);
                    installer.Install(mDictionary);
                    installer.Commit(mDictionary);
                }

                installer.AfterInstall += (((sender, args) => { YiKit.ShowToast("安装结束"); }));
                installer.Dispose();
                */

                /*
                using (AssemblyInstaller installer = new AssemblyInstaller())
                {
                    installer.UseNewContext = true;
                    installer.Path = Assembly.GetExecutingAssembly().Location;
            
                    IDictionary savedState = new Hashtable();
                    installer.Install(savedState);
                    installer.Commit(savedState);
                }*/
                mSc = new ServiceController(Svname);
            }

            if (autochage)
            {
                if (mSc.Status == ServiceControllerStatus.Stopped || mSc.Status == ServiceControllerStatus.StopPending)
                {
                    mSc.Start();
                    YiKit.IniWrite("eyi", "1");
                }
                else if (mSc.Status == ServiceControllerStatus.Running ||
                         mSc.Status == ServiceControllerStatus.StartPending)
                {
                    mSc.Stop();
                    YiKit.IniWrite("eyi", "0");
                }
            }
            else
            {
                if (start)
                {
                    if (mSc.Status == ServiceControllerStatus.Stopped ||
                        mSc.Status == ServiceControllerStatus.StopPending)
                    {
                        mSc.Start();
                        YiKit.IniWrite("eyi", "1");
                    }
                }
                else
                {
                    if (mSc.Status == ServiceControllerStatus.Running ||
                        mSc.Status == ServiceControllerStatus.StartPending)
                    {
                        mSc.Stop();
                        YiKit.IniWrite("eyi", "0");
                    }
                }
            }


            return mSc;
        }
        //启动其他应用程序


        /// <summary>
        /// 改变adpter信息，并返回成功与否
        /// </summary>
        /// <returns></returns>
        public static bool ChangeAdpter()
        {
            /*RegistryKey adapterRegistry = Registry.LocalMachine.OpenSubKey("SYSTEM")
                ?.OpenSubKey("CurrentControlSet")
                ?.OpenSubKey("Control")
                ?.OpenSubKey("Class")
                ?.OpenSubKey("{4D36E972-E325-11CE-BFC1-08002bE10318}");*/

            RegistryKey adapterRegistry = Registry.LocalMachine.OpenSubKey("SYSTEM")
                ?.OpenSubKey("CurrentControlSet")
                ?.OpenSubKey("Enum")
                ?.OpenSubKey("ROOT")
                ?.OpenSubKey("NET");
            IList<string> list = adapterRegistry.GetSubKeyNames().ToList();
            bool isTap = false;
            foreach (string s in list)
            {
                //ProviderName
                RegistryKey subKeyrs;
                try
                {
                    subKeyrs = adapterRegistry.OpenSubKey(s, true);
                }
                catch (Exception e)
                {
                    continue;
                }

                string[] ns = subKeyrs.GetValueNames();
                string[] ks =
                {
                    "FriendlyName", "Mfg", "DeviceDesc",
                };


                foreach (string k in ks)
                {
                    if (ns.Contains(k))
                    {
                        if (subKeyrs.GetValue(k).Equals("YiTap MbE Family Controller"))
                        {
                            isTap = true;
                            break;
                        }
                        else if (subKeyrs.GetValue(k).Equals("tap0901"))
                        {
                            // subKeyrs.SetValue(k, "yitap");

                            if (ns.Contains("FriendlyName"))
                                subKeyrs.SetValue("FriendlyName", "YiTap MbE Family Controller");
                            if (ns.Contains("DeviceDesc"))
                                subKeyrs.SetValue("DeviceDesc", "YiTap");
                            if (ns.Contains("Mfg"))
                                subKeyrs.SetValue("Mfg", "Yi");


                            isTap = true;
                            break;
                        }
                    }
                }
            }

            return isTap;
        }


        public static void StarteYi()
        {
            ShowLoading();
            /*//获取服务
            // 获取服务列表
            ServiceController[] serviceList = ServiceController.GetServices();
// 按名称排序
            // serviceList = serviceList.OrderBy(m => m.DisplayName).ToArray();
// 遍历服务列表
            foreach (ServiceController service in serviceList)
            {
               
                // 服务信息
            }*/
            int i = 0;
            //执行
            YiKit.IsCanConnectedToInternet();

            YiKit.RunThread((state =>
            {
                // HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\ROOT\NET\0001

                bool isTap = ChangeAdpter();

                if (!isTap)
                {
                    //安装tap
                    string path = YiKit.GetAppStartPath() + "drv\\" + (YiKit.IsX86() ? "x86" : "x64") +
                                  "\\";
                    YiKit.StartProcessAsync(path + "tapinstall.exe install " + path + "OemWin2k.inf tap0901", (obj =>
                    {
                        var ret = obj.ToString();
                        if (ret.Contains("failed"))
                        {
                            //安装失败
                            YiKit.ShowToastError("安装失败：" + ret);
                            return;
                        }
                        else
                        {
                            // YiKit.ShowToast("安装成功");
                            ChangeAdpter();
                        }
                    }));

                    return;
                }


                ServiceController mSc = StartEyi(autochage: true);
                // mSc.WaitForStatus(ServiceControllerStatus.Running); //等待启动
                mSc.Refresh();
                YiKit.RunOnUIThread(() =>
                {
                    YiKit.ShowToast((mSc?.Status == ServiceControllerStatus.Running ||
                                     mSc?.Status == ServiceControllerStatus.StartPending)
                        ? "程序已启动"
                        : "程序已停止");
                    ShowLoading(false);
                });
            }));

            /*using (AssemblyInstaller installer = new AssemblyInstaller())
            {
                installer.UseNewContext = true;
                installer.Path = YiKit.GetAppStartPath()+"/"+_vname +  ".exe -c xiuxingzhe -k yizhu  -r -a dhcp:0.0.0.0 -l yizhu.space:65533";
                installer.Uninstall(null);
            }*/


            /*YiKit.StartProcessSync(
                string.Format("Taskkill /IM {0} /F", "eyi*")
                , e =>
                {


                });*/
        }

        public static void TapInstall()
        {
            YiKit.RunOnUIThread(() => ShowLoading(true));


            string prx = "\"" + YiKit.GetAppPath() + "\\Drivers";


            if (YiKit.IsX86())
            {
                prx += "\\x86\\";
            }
            else
                prx += "\\x64\\";

            YiKit.StartProcessSync("certutil -addstore \"TrustedPublisher\" " + prx + "\\der.cer\"");

            YiKit.StartProcessSync(prx + "tapinstall.exe\" install " + prx + "OemVista.inf\" tap0901",
                end: (state => { YiKit.RunOnUIThread(() => ShowLoading(false)); }));
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
            if (PetWindow.Me == null || !PetWindow.Me.IsVisible)
            {
                Environment.Exit(0);
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
            YiKit.SetAutoStart((Assembly.GetExecutingAssembly().Location), isAuto: _isAuto);
            _isAuto = !_isAuto;
        }

        private void SdItem_Click(object sender, RoutedEventArgs e)
        {
            if (_webPage == null)
                _webPage = new WebPage();
            MPage.Content = _webPage;
        }
    }
}