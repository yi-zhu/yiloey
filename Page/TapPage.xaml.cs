using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace holey.Page;

public partial class TapPage : System.Windows.Controls.Page
{
    public TapPage()
    {
        InitializeComponent();
    }

    private void InstallTap_OnClick(object sender, RoutedEventArgs e)
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
        ProcessStartInfo startInfo = new ProcessStartInfo("driver/yitap.exe", " install  "+driverDir+"yltap.inf yltap"); // 括号里是(程序名,参数)
        process.StartInfo = startInfo;
        process.StartInfo.UseShellExecute = false; //是否使用操作系统的shell启动
        startInfo.RedirectStandardInput = true; //接受来自调用程序的输入     
        startInfo.RedirectStandardOutput = true; //由调用程序获取输出信息
        startInfo.RedirectStandardError = true; //重定向标准错误输出
        //startInfo.CreateNoWindow = true;             //不显示调用程序的窗口 
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        Console.WriteLine(output);
    }

    private void InstallService_OnClick(object sender, RoutedEventArgs e)
    {
        
    }

    private void UninstallTap_OnClick(object sender, RoutedEventArgs e)
    {
        
    }

    private void UnInstallService_OnClick(object sender, RoutedEventArgs e)
    {
        
    }
}