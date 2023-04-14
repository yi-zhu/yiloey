using System;
using System.Diagnostics;
using System.Windows;
using YiKits;

namespace holey.Page;

public partial class TapPage : System.Windows.Controls.Page
{

    private string _infName = "yltap.inf";
    private string _tapName = "yltap";
    
    public TapPage()
    {
        InitializeComponent();
    }

    private void InstallTap_OnClick(object sender, RoutedEventArgs e)
    {
        MainWindow.ShowLoading();
        try
        {
            string driverDir = "driver/";
            if (Environment.Is64BitProcess)
            {
                driverDir += "x64/";
            }
            else
            {
                driverDir += "x86/";
            }

            if (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor < 2)
            {
                //win7
                driverDir += "win7/";
            }
            else
            {
                //win10
                driverDir += "win10/";
            }

            Process process = new Process(); //创建进程对象    
            ProcessStartInfo startInfo =
                new ProcessStartInfo("Binaries/yitap.exe",
                    " install  " + driverDir + _tapName+".inf "+_tapName); // 括号里是(程序名,参数)
            process.StartInfo = startInfo;
            process.StartInfo.UseShellExecute = false; //是否使用操作系统的shell启动
            startInfo.RedirectStandardInput = true; //接受来自调用程序的输入     
            startInfo.RedirectStandardOutput = true; //由调用程序获取输出信息
            startInfo.RedirectStandardError = true; //重定向标准错误输出
            //startInfo.CreateNoWindow = true;             //不显示调用程序的窗口 
            process.Start();
            YiKit.RunThread(_ =>
            {
                string output = process.StandardOutput.ReadToEnd();
                Console.WriteLine(output);
                YiKit.RunOnUIThread(() => MainWindow.ShowLoading(false));
            });
        }
        catch (Exception exception)
        {
            YiKit.Print(exception);
            MainWindow.ShowLoading(false);
        }
    }

    private void InstallService_OnClick(object sender, RoutedEventArgs e)
    {
    
        YiKit.StartProcessSync("sc create Edge-V binpath= \"eyi.exe\" start= auto displayname= \"Edge\"",(
            ret =>
            {
                if (ret==null)
                {
                    return;
                }
                if (ret.ToString().Contains("成功") )
                {
                    YiKit.StartProcessAsync("sc description Edge-V \"使用 IPv4 转换技术(ISATAP、端口代理和 Teredo)和 IP-HTTPS 提供隧道连接。如果停止该服务，则计算机将不具备这些技术提供的增强连接优势。\"  ");
                    YiKit.ShowToast("创建成功::"+ret.ToString());
                }
                else
                {
                    YiKit.ShowToastError("创建失败::"+ret.ToString());
                }
            }));
    }

    private void UninstallTap_OnClick(object sender, RoutedEventArgs e)
    {
        MainWindow.ShowLoading();
        try
        {
            string driverDir = "driver/";
            if (Environment.Is64BitProcess)
            {
                driverDir += "x64/";
            }
            else
            {
                driverDir += "x86/";
            }

            if (Environment.OSVersion.Version.Major == 6 && Environment.OSVersion.Version.Minor < 2)
            {
                //win7
                driverDir += "win7/";
            }
            else
            {
                //win10
                driverDir += "win10/";
            }

            Process process = new Process(); //创建进程对象    
            ProcessStartInfo startInfo =
                new ProcessStartInfo("Binaries/yitap.exe",
                    " remove  " + driverDir + _tapName+".inf "+_tapName); // 括号里是(程序名,参数)
            process.StartInfo = startInfo;
            process.StartInfo.UseShellExecute = false; //是否使用操作系统的shell启动
            startInfo.RedirectStandardInput = true; //接受来自调用程序的输入     
            startInfo.RedirectStandardOutput = true; //由调用程序获取输出信息
            startInfo.RedirectStandardError = true; //重定向标准错误输出
            //startInfo.CreateNoWindow = true;             //不显示调用程序的窗口 
            process.Start();
            YiKit.RunThread(_ =>
            {
                string output = process.StandardOutput.ReadToEnd();
                Console.WriteLine(output);
                YiKit.RunOnUIThread(() => MainWindow.ShowLoading(false));
            });
        }
        catch (Exception exception)
        {
            YiKit.Print(exception);
            MainWindow.ShowLoading(false);
        }
        
    }

    private void UnInstallService_OnClick(object sender, RoutedEventArgs e)
    {
    }
}