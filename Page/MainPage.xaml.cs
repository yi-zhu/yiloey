using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YiKits;

// ReSharper disable FieldCanBeMadeReadOnly.Local

namespace holey.Page;

public partial class MainPage
{
    public MainPage()
    {
        InitializeComponent();
        LoadTap();
      
    }

    private NetworkInterface? tap = null;
    private int _sleep = 1000;
    private void LoadTap()
    {
         tap = IsVpn ? YiKit.GetInterface(vir: true):YiKit.GetInterface(ip: YiKit.IniGet("showip",def:null))  ;

        YiKit.RunThread(_ =>
        {
            long lastUp = 0;
            long lastDown = 0;
            while (MainWindow.IsAlive)
            {
                if (MainWindow.Me is { IsVisible: false })
                {
                    Thread.Sleep(_sleep);
                    continue;
                }
                if (tap==null||tap.OperationalStatus != OperationalStatus.Up)
                {  YiKit.RunOnUIThread(() =>
                    {
                    if (MainStr.Content.ToString()!.Contains("已连接"))
                    {
                        YiKit.RunOnUIThread(() =>
                        {
                            MainStr.Content = "断开连接："+DateTime.Now.ToShortTimeString();
                            // MainBtn.Background = MainBtnRed.Background;
                        });
                    }
                    else  if (MainStr.Content.ToString()!.Contains("未连接"))
                    {
                        YiKit.RunOnUIThread(() => { MainStr.Content = "未连接"; });
                    }});
                    Thread.Sleep(_sleep/2);

                    continue;
                }
               
                IPInterfaceProperties ipInterface = tap.GetIPProperties();
                // string MAC = adapter.GetPhysicalAddress().ToString(); //获取单播地址集
                UnicastIPAddressInformationCollection ipCollection = ipInterface.UnicastAddresses;
                string tapas = "";
                long nowUp;
                long nowDown;
                tapas += "连接信息\n";
                tapas += "已连接\n";
                foreach (UnicastIPAddressInformation ipaddr in ipCollection)
                {
                    if (ipaddr.Address.AddressFamily == AddressFamily.InterNetwork) //判断是否为ipv4
                    {
                        //Console.WriteLine(JsonConvert.SerializeObject(adapter));
                        nowUp = tap.GetIPStatistics().BytesSent;
                        nowDown = tap.GetIPStatistics().BytesReceived;
                       
                        tapas += "丢包："+YiKit.SumByte(tap.GetIPStatistics().OutgoingPacketsWithErrors+tap.GetIPStatistics().IncomingPacketsWithErrors)+ "\n";
                        tapas += "广播："+YiKit.SumByte(tap.GetIPStatistics().NonUnicastPacketsSent+tap.GetIPStatistics().NonUnicastPacketsReceived)+ "\n";

                        tapas += "上传："+YiKit.SumByte(nowUp) + "\n";
                        tapas += "下载："+YiKit.SumByte(nowDown) + "\n";
                        // tapas += "ip:"+ipaddr.Address + "\n";
                        // tapas += "MAC:"+tap.GetPhysicalAddress() + "\n";
                        if (nowUp>0)
                        {
                            YiKit.RunOnUIThread(()=>MainStr.Content = "🔗已连接");
                            YiKit.RunOnUIThread(() =>
                            {
                                MainBtn.Background = MainBtnBlue.Background;

                            });
                        }
                        if (lastUp==0)
                        {
                            lastDown = nowDown;
                            lastUp = nowUp;
                        }
                        YiKit.RunOnUIThread(() =>
                        {
                            if (!tapas.Equals(NowAdpter.Text))
                            {
                                NowAdpter.Text = tapas;
                            }
                            UpSpeed.Text = "发送速度：" +YiKit.SumByte((nowUp - lastUp)*(1000/_sleep));
                            DownSpeed.Text = "接收速度：" +YiKit.SumByte((nowDown - lastDown)*(1000/_sleep));
                            // UpSum.Text = "已发送：" +YiKit.SumByte(nowUp);
                            // DownSum.Text = "已接收：" +YiKit.SumByte(nowDown);
                            if (!IP.Text.Contains(ipaddr.Address.ToString()))
                            {
                                IP.Text = "IP：" + ipaddr.Address;
                                MAC.Text = "MAC："+string.Format("{0:##.##.##.##:##:##}",tap.GetPhysicalAddress());
                            }
                            lastDown = nowDown;
                            lastUp = nowUp;
                        });
                        break;
                    }
                }

             
                Thread.Sleep(_sleep);

            }
            
        });
        
    }
    


    private void MainBtn_OnClick(object sender, RoutedEventArgs e)
    {
       MainWindow.StarteYi();
       
    }

    public bool IsVpn { get; private set; } = true;

    private void VTextBlock_OnClick(object sender, RoutedEventArgs e)
    {
        IsVpn = true;
        tap = YiKit.GetInterface(vir: true);
        VTextBlock.Foreground = Brushes.Blue;
        PTextBlock.Foreground = Brushes.Brown;
    }

    private void PTextBlock_OnClick(object sender, RoutedEventArgs e)
    {
        IsVpn = false;
        tap = YiKit.GetInterface(vir: false);
        PTextBlock.Foreground = Brushes.Blue;
        VTextBlock.Foreground = Brushes.Brown;

    }
}