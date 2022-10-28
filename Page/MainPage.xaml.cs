using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace holey.Page;

public partial class MainPage : System.Windows.Controls.Page
{
    public MainPage()
    {
        InitializeComponent();
    }

    private void MainBtn_OnClick(object sender, RoutedEventArgs e)
    {
        Process process = new Process();//创建进程对象    
        ProcessStartInfo startInfo = new ProcessStartInfo("eyi","-h"); // 括号里是(程序名,参数)
        process.StartInfo = startInfo;
        process.StartInfo.UseShellExecute = false;    //是否使用操作系统的shell启动
        startInfo.RedirectStandardInput = true;      //接受来自调用程序的输入     
        startInfo.RedirectStandardOutput = true;     //由调用程序获取输出信息
        startInfo.RedirectStandardError = true;      //重定向标准错误输出
        //startInfo.CreateNoWindow = true;             //不显示调用程序的窗口 
        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        Console.WriteLine(output);
    }
}