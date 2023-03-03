using System.Diagnostics;
using System.Windows;

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

        //使用http下载dotNetFx461_Full_setup.exe


        private static void DownloadNetFramework()
        {
            Process process = new Process();
            process.StartInfo.FileName = "wget.exe";
            process.StartInfo.Arguments =
                "https://download.microsoft.com/download/E/4/1/E4173890-A24A-4936-9FC9-AF930FE3FA40/NDP461-KB3102436-x86-x64-AllOS-ENU.exe";
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
        private static void InstallNetFramework()
        {
            Process process = new Process();
            process.StartInfo.FileName = "dotNetFx461_Full_setup.exe";
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
            if (!CheckNetFramework())
            {
                
                
                YiKit.RunThread((state =>
                {
                    YiKit.FileDownLoad(
                        "https://download.microsoft.com/download/E/4/1/E4173890-A24A-4936-9FC9-AF930FE3FA40/NDP461-KB3102436-x86-x64-AllOS-ENU.exe",
                        "dotNetFx461_Full_setup.exe", GetCachePath());
                }));
                YiKit.RunOnUIThread(() => { ; });
                
            }

            if (e.Args.Length == 0)
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
        }
    }
}