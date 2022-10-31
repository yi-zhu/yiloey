using System;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace holey;

public class YiKit
{
    public static void RunThread(WaitCallback callBack, object? state = null)
    {
        if (callBack == null)
            return;

        ThreadPool.QueueUserWorkItem(callBack, state);
    }

    public static void RunOnUIThread(Delegate callBack)
    {
        if (callBack == null)
            return;
        Application.Current.Dispatcher.BeginInvoke(callBack);
    }

    public static void Print(string msg)
    {
        Console.WriteLine(msg);
    }

    public static void Print(object? msg)
    {
        if (msg != null)
        {
            Console.WriteLine(msg);
        }
    }

    public static void Print(Exception msg)
    {
        Console.WriteLine(msg);
    }

    public static void showAni(Control element)
    {
        DoubleAnimation doubleAnimation = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(2)));
        Storyboard storyboard = new Storyboard();
        storyboard.Children.Add(doubleAnimation); //添加仅故事板
        Storyboard.SetTarget(doubleAnimation, element); //设置目标
        Storyboard.SetTargetProperty(doubleAnimation, new PropertyPath(Control.OpacityProperty)); //设置目标依赖项属性  
        storyboard.Begin(element, HandoffBehavior.SnapshotAndReplace); //开始动画
    }

    public static void StartProcessSync(string? cmd, WaitCallback? end = null, Delegate? start = null)
    {
        if (cmd == null)
        {
            return;
        }

        if (!cmd.Contains(" "))
        {
            return;
        }

        string exe = cmd.Split(' ')[0];
        string arg = cmd.Substring(exe.Length);
        start?.DynamicInvoke();
        Process process = new Process(); //创建进程对象    
        ProcessStartInfo startInfo = new ProcessStartInfo(exe, arg); // 括号里是(程序名,参数)
        process.StartInfo = startInfo;
        process.StartInfo.UseShellExecute = false; //是否使用操作系统的shell启动
        startInfo.RedirectStandardInput = true; //接受来自调用程序的输入     
        startInfo.RedirectStandardOutput = true; //由调用程序获取输出信息
        startInfo.RedirectStandardError = true; //重定向标准错误输出
        //startInfo.CreateNoWindow = true;             //不显示调用程序的窗口 

        process.Start();
        RunThread(s =>
        {
            string output = process.StandardOutput.ReadToEnd();
            if (output == null || output.Length < 3)
            {
                process.StandardError.ReadToEnd();
            }

            end?.DynamicInvoke(output);
        });
    }

    public static void StartProcessAsync(string? cmd, WaitCallback? end = null, Delegate? start = null)
    {
        if (cmd == null)
        {
            return;
        }

        if (!cmd.Contains(" "))
        {
            return;
        }

        string exe = cmd.Split(' ')[0];
        string arg = cmd.Substring(exe.Length);
        start?.DynamicInvoke();
        Process process = new Process(); //创建进程对象    
        ProcessStartInfo startInfo = new ProcessStartInfo(exe, arg); // 括号里是(程序名,参数)
        process.StartInfo = startInfo;
        process.StartInfo.UseShellExecute = false; //是否使用操作系统的shell启动
        startInfo.RedirectStandardInput = true; //接受来自调用程序的输入     
        startInfo.RedirectStandardOutput = true; //由调用程序获取输出信息
        startInfo.RedirectStandardError = true; //重定向标准错误输出
        //startInfo.CreateNoWindow = true;             //不显示调用程序的窗口 
        process.OutputDataReceived += (s, d) =>
        {
            if (d.Data != null)
            {
                end?.DynamicInvoke(d.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="name"></param>
    public static NetworkInterface GetInterface(string? name = null,string? mac = null )
    {
        NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
        foreach (NetworkInterface adapter in nics)
        {
            //判断是否为以太网卡
            //Wireless80211         无线网卡    Ppp     宽带连接
            //Ethernet              以太网卡
            //if (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            //{
            //获取以太网卡网络接口信息
            IPInterfaceProperties ip = adapter.GetIPProperties();
            // string MAC = adapter.GetPhysicalAddress().ToString(); //获取单播地址集
            UnicastIPAddressInformationCollection ipCollection = ip.UnicastAddresses;
            /*string fCardType = "未知网卡";
            string fRegistryKey =
                "SYSTEM\\CurrentControlSet\\Control\\Network\\{4D36E972-E325-11CE-BFC1-08002BE10318}\\" + adapter.Id +
                "\\Connection";
            RegistryKey rk = Registry.LocalMachine.OpenSubKey(fRegistryKey, false);
            if (rk != null)
            {
                string fPnpInstanceID = rk.GetValue("PnpInstanceID", "").ToString();
                string Characteristics = rk.GetValue("Characteristics", "").ToString();
                int fMediaSubType = Convert.ToInt32(rk.GetValue("MediaSubType", 0));
                if (fPnpInstanceID.Length > 3 &&
                    (fPnpInstanceID.Substring(0, 3) == "PCI" || fPnpInstanceID.Substring(0, 3) == "USB"))
                {
                    fCardType = "物理网卡";
                }
                else
                {
                    fCardType = "虚拟网卡";
                }
            }*/
            if (name != null)
            {
                if (adapter.Id.Equals(name))
                {
                    return adapter;
                }
            }
            else if (mac != null)
            {
                if (adapter.GetPhysicalAddress().ToString().Equals(mac))
                {
                    return adapter;
                }
            }
            else
            {
                return adapter;
            }
            foreach (UnicastIPAddressInformation ipadd in ipCollection)
            {

               
                //InterNetwork    IPV4地址      InterNetworkV6        IPV6地址
                //Max            MAX 位址
                if (ipadd.Address.AddressFamily == AddressFamily.InterNetwork) //判断是否为ipv4
                {
                    //Console.WriteLine(JsonConvert.SerializeObject(adapter));
                    Console.WriteLine($"描述(Description):{adapter.Description}");
                    Console.WriteLine($"名称(Name):{adapter.Name}");
                    // Console.WriteLine($"网卡类型:{fCardType}");
                    Console.WriteLine($"网络操作类型(OperationalStatus):{adapter.OperationalStatus}");
                    Console.WriteLine($"网络接口类型(NetworkInterfaceType):{adapter.NetworkInterfaceType}");
                    Console.WriteLine($"MAC:{adapter.GetPhysicalAddress()}");
                    Console.WriteLine($"Address:{ipadd.Address}");
                }
            }

        }

        return null;
    }

}