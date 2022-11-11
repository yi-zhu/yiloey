using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Xml;
using Microsoft.Win32;

namespace holey;

public class YiKit
{
    public static List<Process> YiThreadList = new List<Process>();

    public static void RunThread(WaitCallback? callBack, object? state = null)
    {
        if (callBack == null)
            return;

        ThreadPool.QueueUserWorkItem(callBack, state);
    }

    public static void RunOnUIThread(Delegate? callBack)
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
        Storyboard.SetTargetProperty(doubleAnimation, new PropertyPath(UIElement.OpacityProperty)); //设置目标依赖项属性  
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
        YiThreadList.Add(process);
        ProcessStartInfo startInfo = new ProcessStartInfo(exe, arg); // 括号里是(程序名,参数)
        process.StartInfo = startInfo;
        process.StartInfo.UseShellExecute = false; //是否使用操作系统的shell启动
        startInfo.RedirectStandardInput = true; //接受来自调用程序的输入     
        startInfo.RedirectStandardOutput = true; //由调用程序获取输出信息
        startInfo.RedirectStandardError = true; //重定向标准错误输出
        //startInfo.CreateNoWindow = true;             //不显示调用程序的窗口 

        process.Start();
        RunThread(_ =>
        {
            YiThreadList.Remove(process);
            string output = process.StandardOutput.ReadToEnd();
            if (output.Length < 3)
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
        YiThreadList.Add(process);
        ProcessStartInfo startInfo = new ProcessStartInfo(exe, arg); // 括号里是(程序名,参数)
        process.StartInfo = startInfo;
        process.StartInfo.UseShellExecute = false; //是否使用操作系统的shell启动
        startInfo.RedirectStandardInput = true; //接受来自调用程序的输入     
        startInfo.RedirectStandardOutput = true; //由调用程序获取输出信息
        startInfo.RedirectStandardError = true; //重定向标准错误输出
        //startInfo.CreateNoWindow = true;             //不显示调用程序的窗口 

        process.OutputDataReceived += (_, d) =>
        {
            if (d.Data != null)
            {
                end?.DynamicInvoke(d.Data);
            }
        };


        process.Start();
        process.BeginOutputReadLine();
        process.Exited += ((_, _) => { YiThreadList.Remove(process); });
    }


    //单位换算
    /// <summary>
    /// 单位自动换算升级，
    /// </summary>
    /// <param name="num">byte</param>
    /// <returns>自动单位的字符串</returns>
    public static string SumByte(double num)
    {
        if (num < 0)
        {
            return "None";
        }

        string[] sizes = { "Bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB", "DB", "NB" };
        int order = 0;
        while (num >= 1024 && order + 1 < sizes.Length)
        {
            order++;
            num = num / 1024;
        }


        return String.Format(order == 0 ? "{0:0} {1}" : "{0:0.000} {1}", num, sizes[order]);
    }


    /// <summary>
    /// 
    /// </summary>
    /// <param name="name"></param>
    /// <param name="ip"></param>
    /// <param name="vir"></param>
    /// <param name="mac"></param>
    // [Obsolete("此方法无法获取某些网卡的IPV4地址，请使用",false)]
    public static NetworkInterface? GetInterface(string? name = null, string? mac = null, string? ip = null,
        bool vir = true)
    {
        NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
        IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
        // string[] category = new string[] { };
        // if (!vir) category = new PerformanceCounterCategory("Network Interface").GetInstanceNames();//这个方法获得的名字有问题

        foreach (NetworkInterface adapter in nics)
        {
            if (!vir)
            {
                //判断是否物理网卡

                int fCardType = 0;
                string fRegistryKey =
                    "SYSTEM\\CurrentControlSet\\Control\\Network\\{4D36E972-E325-11CE-BFC1-08002BE10318}\\" +
                    adapter.Id +
                    "\\Connection";
                RegistryKey? rk = Registry.LocalMachine.OpenSubKey(fRegistryKey, false);
                if (rk != null)
                {
                    string? fPnpInstanceId = rk.GetValue("PnpInstanceID", "")?.ToString();
                    string? characteristics = rk.GetValue("Characteristics", "")?.ToString();
                    int fMediaSubType = Convert.ToInt32(rk.GetValue("MediaSubType", 0));
                    if (fPnpInstanceId is { Length: > 3 } &&
                        (fPnpInstanceId.Substring(0, 3) == "PCI" || fPnpInstanceId.Substring(0, 3) == "USB"))
                    {
                        fCardType = 1;
                    }
                    else
                    {
                        fCardType = 0;
                    }
                }

                if (fCardType == 1 && adapter.OperationalStatus == OperationalStatus.Up)
                {
                    return adapter;
                }

                continue;
            }


            // Only display informatin for interfaces that support IPv4.
            if (adapter.Supports(NetworkInterfaceComponent.IPv4) == false)
            {
                continue;
            }

            //判断是否为以太网卡
            //Wireless80211         无线网卡    Ppp     宽带连接
            //Ethernet              以太网卡
            //if (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            //{
            //获取以太网卡网络接口信息
            IPInterfaceProperties ipInterface = adapter.GetIPProperties();
            // string MAC = adapter.GetPhysicalAddress().ToString(); //获取单播地址集
            UnicastIPAddressInformationCollection ipCollection = ipInterface.UnicastAddresses;

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
            else if (ip != null)
            {
                foreach (UnicastIPAddressInformation ipadd in ipCollection)
                {
                    //InterNetwork    IPV4地址      InterNetworkV6        IPV6地址
                    //Max            MAX 位址
                    if (ipadd.Address.AddressFamily == AddressFamily.InterNetwork) //判断是否为ipv4
                    {
                        //Console.WriteLine(JsonConvert.SerializeObject(adapter));

                        if (ipadd.Address.ToString().StartsWith(ip))
                        {
                            /*Console.WriteLine($"描述(Description):{adapter.Description}");
                            Console.WriteLine($"名称(Name):{adapter.Name}");
                            // Console.WriteLine($"网卡类型:{fCardType}");
                            Console.WriteLine($"网络操作类型(OperationalStatus):{adapter.OperationalStatus}");
                            Console.WriteLine($"网络接口类型(NetworkInterfaceType):{adapter.NetworkInterfaceType}");
                            Console.WriteLine($"MAC:{adapter.GetPhysicalAddress()}");
                            Console.WriteLine($"Address:{ipadd.Address}");*/
                            return adapter;
                        }
                    }
                }
            }

            /*foreach (UnicastIPAddressInformation ipadd in ipCollection)
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
            }*/
        }

        return null;
    }


    private static PerformanceCounter? Cpuner;
    private static PerformanceCounter? Memner;

    public static float CpuUse()
    {
        if (Cpuner == null)
        {
            Cpuner = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        }

        return Cpuner.NextValue();
    }

    public static float MemUse()
    {
        if (Memner == null)
        {
            Memner = new PerformanceCounter("Memory", "% Committed Bytes In Use");
        }

        return Memner.NextValue();
    }

    #region 毛玻璃

    [StructLayout(LayoutKind.Sequential)]
    public struct MARGINS
    {
        public MARGINS(Thickness t)
        {
            Left = (int)t.Left;
            Right = (int)t.Right;
            Top = (int)t.Top;
            Bottom = (int)t.Bottom;
        }

        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll", PreserveSig = false)]
    static extern void DwmExtendFrameIntoClientArea(
        IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll", PreserveSig = false)]
    static extern bool DwmIsCompositionEnabled();


    /// <summary>
    /// 毛玻璃效果,添加到OnSourceInitialized方法。
    /// </summary>
    /// <param name="window"></param>
    /// <param name="margin"></param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public static bool Areo(Window window, Thickness margin)
    {
        if (!DwmIsCompositionEnabled())
            return false;

        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException(
                "请添加到OnSourceInitialized.");

        // Set the background to transparent from both the WPF and Win32 perspectives
        window.Background = Brushes.Transparent;
        HwndSource.FromHwnd(hwnd).CompositionTarget.BackgroundColor = Colors.Transparent;

        MARGINS margins = new MARGINS(margin);
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        return true;
    }

    #endregion


    private void CreateBorderColorAnim()
    {
        ColorAnimationUsingKeyFrames colorKeyFrms = new ColorAnimationUsingKeyFrames();
        colorKeyFrms.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(245, 104, 5),
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.5d))));
        colorKeyFrms.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(236, 247, 8),
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1d))));
        colorKeyFrms.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(10, 124, 238),
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1.5d))));
        colorKeyFrms.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(69, 205, 199),
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2d))));
        colorKeyFrms.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(8, 83, 158),
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.5d))));
        colorKeyFrms.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(234, 112, 112),
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3d))));
        colorKeyFrms.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(6, 247, 203),
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3.5d))));
        colorKeyFrms.KeyFrames.Add(new EasingColorKeyFrame(Color.FromRgb(78, 201, 250),
            KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4d))));

        colorKeyFrms.RepeatBehavior = RepeatBehavior.Forever;
        // BeginAnimation(NeonColorProperty, colorKeyFrms);
    }

    
    /// <summary>
    /// 保存到文件
    /// </summary>
    /// <param name="fileName"></param>
    /// <param name="data"></param>
    /// <returns></returns>
    public static bool SaveToFile(string fileName, byte[] data)
    {
        ;
        var file = new FileStream(fileName, FileMode.OpenOrCreate);
        if (file.Length==data.Length) {
            YiKit.Print("文件大小：" + file.Length + "数据大小" + data.Length);
            data = null;
            return true;
        }
        file.Write(data, 0, data.Length);
        file.Close();
        data = null;
        YiKit.Print("文件大小：" + file.Length+"创建成功");

        return true;
    }

    #region XML读写

    /// <summary>
    /// 
    /// </summary>
    /// <param name="val"></param>
    /// <param name="name"></param>
    /// <param name="path"></param>
    public static void XmlWrite(string val, string name="yi", string? path="")
    {
        try
        {
            XmlTextWriter writer;
            try
            {
                path ??= "";

                if (path.Length>1&&!path.EndsWith("\\"))
                {
                    path += "\\";
                }

                writer = new XmlTextWriter(
                    path + name.Substring(0, name.LastIndexOf(".", StringComparison.Ordinal)) + ".xml",
                    Encoding.UTF8);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return;
            }

            val = val.Replace("{", "");
            val = val.Replace("}", "");
            val = Regex.Replace(val.Trim(), "\\[", "");
            val = Regex.Replace(val.Trim(), "\\\\_", "-");
            val = Regex.Replace(val.Trim(), "\\]", "");
            val = Regex.Replace(val.Trim(), "\\\"", "");
            string[] vals = val.Split(',');
            writer.Formatting = Formatting.Indented;
            writer.WriteStartDocument();
            writer.WriteStartElement("root");

            foreach (string str in vals)
            {
                try
                {
                    if (str.Split(':').Length > 2)
                    {
                        writer.WriteElementString(str.Split(':')[0], str.Replace(str.Split(':')[0] + ":", ""));
                    }
                    else
                        writer.WriteElementString(str.Split(':')[0], str.Split(':')[1]);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    writer.Close();
                }
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
            writer.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }


    public static string XmlRead(string key, string path="yi")
    {
        string ret = "";

        try
        {
            // Open the file to be read
            XmlTextReader reader = new XmlTextReader(path + ".xml");

            // Clear output label
            // Continue to read each element and text until the file is done
            while (reader.Read())
            {
                // If the currently read item is text then print it to screen,
                // otherwise the loop repeats getting the next piece of information
                if (reader.Name == key)
                {
                    reader.Read();
                    ret = reader.Value;
                    break;
                }
            }

            // When done reading the file close it
            reader.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return ret.Replace("\r\n", "");
    }


    public static string GetConStr(string key, string def = null)
    {
        string val = ConfigurationManager.AppSettings.Get(key);
        if (null == val)
        {
            return def;
        }

        return val;
    }

    public static int GetConInt(string key, int def = 0)
    {
        string val = ConfigurationManager.AppSettings.Get(key);
        if (null == val)
        {
            return def;
        }

        return Int32.Parse(val);
    }

    #endregion
}