using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration.Install;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Resources;
using System.ServiceProcess;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using holey.Services;
using holey.Vdd;
using YiKits;

namespace holey
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        //检查系统是否安装了.NET Framework 4.6.1
        private static bool CheckNetFramework()
        {
            using (var ndpKey = Microsoft.Win32.RegistryKey
                       .OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32)
                       .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\"))
            {
                if (ndpKey != null && ndpKey.GetValue("Release") != null)
                {
                    YiKit.Print(ndpKey.GetValue("Release"));
                    // return (int)ndpKey.GetValue("Release") >= 394802;//4.6.2
                    // return (int)ndpKey.GetValue("Release") >= 394254;//4.6.1
                    return (int)ndpKey.GetValue("Release") >= 393295;
                }
                else
                {
                    return false;
                }
            }
        }

        //下载
        private static void WebClientDownloadNetFramework()
        {
            System.Net.WebClient webClient = new System.Net.WebClient();
            webClient.Credentials = CredentialCache.DefaultNetworkCredentials;

            webClient.DownloadFile(
                "https://download.microsoft.com/download/E/4/1/E4173890-A24A-4936-9FC9-AF930FE3FA40/NDP461-KB3102436-x86-x64-AllOS-ENU.exe",
                "dotNetFx461_Full_setup.exe");
        }

        //使用http下载dotNetFx461_Full_setup.exe


        private static void DownloadNetFramework()
        {
            Process process = new Process();
            process.StartInfo.FileName = "wget.exe";
            process.StartInfo.Arguments =
                "https://download.microsoft.com/download/3/5/9/35980F81-60F4-4DE3-88FC-8F962B97253B/NDP461-KB3102438-Web.exe";
            // "https://download.microsoft.com/download/E/4/1/E4173890-A24A-4936-9FC9-AF930FE3FA40/NDP461-KB3102436-x86-x64-AllOS-ENU.exe";
            process.StartInfo.UseShellExecute = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.WaitForExit();
        }

        //windows下载dotNetFx461_Full_setup.exe
        private static void RenameNetFramework()
        {
            Process process = new Process();
            process.StartInfo.FileName = "ren.exe";
            process.StartInfo.Arguments = "NDP461-KB3102436-x86-x64-AllOS-ENU.exe dotNetFx461_Full_setup.exe";
            process.StartInfo.UseShellExecute = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.WaitForExit();
        }

        //后台安装.NET Framework 4.6.1
        private static void InstallNetFramework(string dotnetfx461FullSetupExe)
        {
            Process process = new Process();
            process.StartInfo.FileName = dotnetfx461FullSetupExe;
            process.StartInfo.Arguments = "/q /norestart";
            process.StartInfo.UseShellExecute = true;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            process.WaitForExit();
        }

        //获取系统缓存路径
        public static string GetCachePath()
        {
            return System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "holey");
        }


        private void App_OnStartup(object sender, StartupEventArgs e)
        {
            var args = e.Args;
            if (args.Length >= 1 && args[0] == "-cmd")
            {
                // 特殊模式：仅执行 schtasks 命令（由 UAC 提权后调用）
                if (args.Length==2)
                    YiKit.RunCmd(args[1]);
                else if (args.Length==3)
                    YiKit.RunCommand(args[1],args[2]);
                return;
            }
            
            if (YiKit.IniGetInt("eyi", 0) == 1)
            {
                YiKit.RunThread((state =>
                {
                    try
                    {
                        ServiceController mSc = new ServiceController(holey.MainWindow.Svname);
                        if (mSc.Status == ServiceControllerStatus.Stopped ||
                            mSc.Status == ServiceControllerStatus.StopPending)
                        {
                            holey.MainWindow.StarteYi();
                        }
                        else if (mSc.Status == ServiceControllerStatus.Running ||
                                 mSc.Status == ServiceControllerStatus.StartPending)
                        {
                            YiKit.Print(mSc.Status);
                        }
                    }
                    catch (Exception exception)
                    {
                        YiKit.ShowToastError("网络启动失败！");
                    }
                }));
            }


            if (!CheckNetFramework())
            {
                YiKit.ShowToastError(
                    "未检测到程序必须组件，【NDP461-KB3102436-x86-x64-AllOS-ENU】正在下载。。。\nUntested program must be installed, and the [NDP461-KB3102436-x86-x64-AllOS-ENU] file is being downloaded...",
                    time: 120);
                YiKit.RunThread((state =>
                {
                    string fn = "dotNetFx461_Full_setup.exe";
                    YiKit.FileDownLoad(
                        "https://download.microsoft.com/download/E/4/1/E4173890-A24A-4936-9FC9-AF930FE3FA40/NDP461-KB3102436-x86-x64-AllOS-ENU.exe",
                        fn, GetCachePath());

                    YiKit.RunOnUIThread(() =>
                    {
                        YiKit.ShowToastError(
                            "【NDP461-KB3102436-x86-x64-AllOS-ENU】下载完成！正在安装。。。请稍后。。。\n[NDP461-KB3102436-x86-x64-AllOS-ENU] Download is complete!  is being installed...",
                            time: 120);
                    });
                    InstallNetFramework(GetCachePath() + "\\" + fn);

                    YiKit.RunOnUIThread(() =>
                    {
                        YiKit.ShowToastWaring(
                            "恭喜您。程序依赖下载并安装完成！\nCongratulations.The program depends on download and installation to complete!");
                    });
                }));
            }

            YiKit.ShowToastError("检查程序更新，当前程序版本：" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version,
                time: 2);
            string page = YiKit.IniGet("autostartpage", def: null);
            if (page != null && page.Length > 1)
            {
                new MainWindow(page).Show();
            }
            else if (YiKit.IniGetInt("boot", 0) == 0)
            {
                new MainWindow().Show();
                YiKit.IniWrite("boot", "1");
            }
            else if (e.Args.Length == 0)
            {
                new PetWindow().Show();
            }
            else
                foreach (var t in e.Args)
                {
                    if (t.Equals("main"))
                    {
                        new MainWindow().Show();
                        break;
                    }
                    else if (t.Equals("ProgressSuspend"))
                    {
                        PetWindow pet = new PetWindow();
                        pet.Show();
                        pet.Suspend();
                    }
                    else if (t.Equals("ProgressResume"))
                    {
                        PetWindow pet = new PetWindow();
                        pet.Show();
                        pet.Resume();
                    }
                }

            if (YiKit.IniGetInt("eyi", 0) == 1)
            {
                //启动服务
                string serviceName = "EsYi";
                ServiceController mSc = null; //建立服务对象
                ServiceController[] services = ServiceController.GetServices();
                foreach (ServiceController sc in services)
                {
                    if (sc.ServiceName.ToLower() == serviceName.ToLower())
                    {
                        mSc = sc;
                    }
                }

                if (mSc == null)
                {
                    /*using (AssemblyInstaller installer = new AssemblyInstaller())
                    {
                        installer.UseNewContext = true;
                        installer.Path = Assembly.GetExecutingAssembly().Location;

                        IDictionary savedState = new Hashtable();
                        installer.Install(savedState);
                        installer.Commit(savedState);
                    }*/
                }
                else
                {
                    if (mSc.Status == ServiceControllerStatus.Stopped ||
                        mSc.Status == ServiceControllerStatus.StopPending)
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
            }

            int i = 0;
        }
        //vdd

        #region vdd


        protected override void OnStartup(StartupEventArgs e)
        {
            // Disable GPU to prevent flickering when adding display
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
            base.OnStartup(e);
            var silent = e.Args.Contains("-silent");
            /*var window = new VddMainWindow();
            if (!silent)
            {
                window.Show();
            }*/

        }



        static T LoadBaml<T>(Stream stream)
        {
            ParserContext pc = new ParserContext();
            MethodInfo loadBamlMethod = typeof(XamlReader).GetMethod("LoadBaml",
                BindingFlags.NonPublic | BindingFlags.Static);
            return (T)loadBamlMethod.Invoke(null, new object[] { stream, pc, null, false });
        }

        #endregion

        public static string? GetTranslation(string tMsgDriverIsDisabled, object tMsgMustRestartPc=null)
        {
            
            return tMsgDriverIsDisabled;
        }

    }
}