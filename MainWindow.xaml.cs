using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Configuration.Install;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Reflection;
using System.ServiceProcess;
using System.ServiceProcess;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using holey.Page;
using Microsoft.Win32;
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
        private WebPage _webPage;
        private YAudioPage _yAudioPage;
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


        /// <summary>
        /// 加载初始页面
        /// </summary>
        /// <param name="pageName"></param>
        public void loadInitPage(string pageName)
        {
            Type pageType = Type.GetType(pageName);
            if (pageType != null)
            {
                System.Windows.Controls.Page page = Activator.CreateInstance(pageType) as System.Windows.Controls.Page;


                MPage.Content = new Frame { Content = page };
            }
        }

        public void loadSize()
        {
            this.Left=YiKit.IniGetDouble("mainleft", 500);
            this.Top=YiKit.IniGetDouble("maintop", 300);
            this.Width=YiKit.IniGetDouble("mainsizew", 750);
            this.Height=YiKit.IniGetDouble("mainsizeh", 450);
            this.LocationChanged += (s, e) =>
            {
                YiKit.IniWrite("mainleft",this.Left.ToString());
                YiKit.IniWrite("maintop",this.Top.ToString());
            };
            this.SizeChanged += (s, e) =>
            {
                YiKit.IniWrite("mainsizew", this.Width.ToString());
                YiKit.IniWrite("mainsizeh",  this.Height.ToString());
                
            };


        }

  

        public MainWindow(string pageName = null)
        {
            
            if (PetWindow.Me == null)
            {
                PetWindow = new PetWindow();
                PetWindow.Show();
                if (pageName == null)
                    Hide();
            }


            InitializeComponent();
            loadSize();
            
            if (pageName != null)
            {
                loadInitPage(pageName);
            }
            else
            {
                _mainPage = new MainPage();
                MPage.Content = new Frame { Content = _mainPage };
            }

            IsAlive = true;
            Me = this;
            // _mainPage = new MainPage();
            // _tapPage = new TapPage();
            // MPage.Content = new Frame { Content = _mainPage };
            if (YiKit.IsX86())
            {
                _vname = "ey";
            }

            StarteYi();

            //初始化系统休眠
            if (YiKit.IniGetBool("wake", false))
            {
                YiKit.ChangePowerWake();
                PowerWakeItem.ToolTip = "不允许系统休眠";
                PowerWakeItem.Header = "👁";
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
            if (!YiKit.IsAdministrator())
            {
                return null;
            }

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
                    if (start == false)
                    {
                        mSc.Stop();
                        YiKit.IniWrite("eyi", "0");
                    }
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

            RegistryKey? adapterRegistry = Registry.LocalMachine.OpenSubKey("SYSTEM")
                ?.OpenSubKey("CurrentControlSet")
                ?.OpenSubKey("Enum")
                ?.OpenSubKey("ROOT")
                ?.OpenSubKey("NET");
            IList<string>? list = adapterRegistry?.GetSubKeyNames()?.ToList();
            bool isTap = false;
            if (list != null)
                foreach (string s in list)
                {
                    //ProviderName
                    RegistryKey? subKeyrs;
                    try
                    {
                        subKeyrs = adapterRegistry?.OpenSubKey(s, true);
                    }
                    catch (Exception e)
                    {
                        continue;
                    }

                    string[]? ns = subKeyrs?.GetValueNames();
                    string[] ks =
                    {
                        "FriendlyName", "Mfg", "DeviceDesc",
                    };

                    if (ns != null)
                        foreach (string k in ks)
                        {
                            if (ns.Contains(k))
                            {
                                if (subKeyrs != null && (subKeyrs.GetValue(k).Equals("YiTap MbE Family Controller") ||
                                                         subKeyrs.GetValue(k).Equals("YiTap")))
                                {
                                    isTap = true;
                                    break;
                                }
                                else if (subKeyrs != null && (subKeyrs.GetValue(k).Equals("tap0901") ||
                                                              subKeyrs.GetValue(k).Equals("TAP-Win32 Adapter V9")))
                                {
                                    // subKeyrs.SetValue(k, "yitap");

                                    if (ns.Contains("FriendlyName"))
                                        subKeyrs.SetValue("FriendlyName", "YiTap MbE Family Controller");
                                    if (ns.Contains("DeviceDesc"))
                                        subKeyrs.SetValue("DeviceDesc", "YiTap");
                                    if (ns.Contains("Mfg"))
                                        subKeyrs.SetValue("Mfg", "Yi");

                                    subKeyrs.Flush();
                                    if (ns.Contains("FriendlyName"))
                                        subKeyrs.SetValue("FriendlyName", "YiTap MbE Family Controller");
                                    // subKeyrs.SetValue("FriendlyName", "YiTap MbE Family Controller");
                                    subKeyrs.Dispose();
                                    isTap = true;
                                    return isTap;
                                }
                            }
                        }
                }

            return isTap;
        }


        public static void StarteYi()
        {
            string path = YiKit.GetAppStartPath() + "drv\\" + (YiKit.IsX86() ? "x86" : "x64") +
                          "\\tapinstall.exe";
            if (!File.Exists(path))
            {
                return;
            }

            if (!YiKit.IsAdministrator())
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

                Environment.Exit(0);
                return;
            }

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
            /*if (!YiKit.IsCanConnectedToInternet())
            {
                return;
            }*/
            YiKit.RunThread((state =>
            {
                // HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Enum\ROOT\NET\0001


                bool isTap = ChangeAdpter();
                YiKit.ShowToastError("请在接下来在弹框中选择“允许”", time: 10);
                if (!isTap)
                {
                    //安装tap
                    path = YiKit.GetAppStartPath() + "drv\\" + (YiKit.IsX86() ? "x86" : "x64") +
                           "\\";
                    YiKit.StartProcessSync(path + "tapinstall.exe install " + path + "OemWin2k.inf tap0901", (obj =>
                    {
                        YiKit.ShowToast(obj.ToString());
                        if (!isTap)
                        {
                            var ret = obj.ToString();
                            if (ret.Contains("failed"))
                            {
                                //安装失败
                                YiKit.ShowToastError("安装失败：" + ret);
                            }
                            else
                            {
                                // YiKit.ShowToast("安装成功");
                                isTap = ChangeAdpter();
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
                            }
                        }
                    }));

                    // return;
                }
                else
                {
                    if (!isTap)
                    {
                        YiKit.ShowToastError("安装失败！请确认是否点击了允许按钮");
                        ShowLoading(false);
                        // return;
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
                }
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
        /// 设置唤醒状态
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void WakeItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (!YiKit.IniGetBool("wake", false))
            {
                YiKit.ChangePowerWake();
                YiKit.IniWrite("wake", "true");
                PowerWakeItem.ToolTip = "不允许系统休眠";
                PowerWakeItem.Header = "👁";
            }
            else
            {
                YiKit.ChangePowerWake(false);
                YiKit.IniWrite("wake", "false");
                PowerWakeItem.ToolTip = "不干涉系统休眠";
                PowerWakeItem.Header = "😴";
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

        private void AuiItem_Click(object sender, RoutedEventArgs e)
        {
            if (_yAudioPage == null)
                _yAudioPage = new YAudioPage();
            MPage.Content = _yAudioPage;
        }
    
        
    }
}