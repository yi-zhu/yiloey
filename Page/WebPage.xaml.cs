using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;

using Microsoft.Win32;
using YiKits;
using Control = System.Windows.Forms.Control;

namespace holey.Page;

public partial class WebPage : System.Windows.Controls.Page
{
    public WebPage()
    {
        InitializeComponent();
        string wbv2v = null;
        try
        {
             // wbv2v = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (Exception e)
        {
           YiKit.ShowToastError("加载webview2异常！");
        }
        if (String.IsNullOrEmpty(wbv2v))
        {
            YiKit.RunThread((state =>
            {
                YiKit.FileDownLoad(
                    "https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/03e33965-9ac7-458c-b6bc-c48177736a31/MicrosoftEdgeWebview2Setup.exe",
                    "webview2.exe", YiKit.GetCachePath());
                YiKit.InstallFile(YiKit.GetCachePath()+"\\webview2.exe"," /silent /install");
            }));

        }
        
    
        SetWebBrowserEmulationMode();
        AddBrower();
    }


    private void AddBrower()
    {
        // WebView2 webBrowser = new WebView2();
        // webBrowser.NavigationCompleted += (((sender, args) => { YiKit.Print(args); }));
        WindowsFormsHost host = new WindowsFormsHost();
        host.HorizontalAlignment = HorizontalAlignment.Stretch;
        host.Background = Brushes.Transparent;
        // host.Child = webBrowser;
        ;
        MyGrid.Children.Add(host);
        // webBrowser.CoreWebView2.Navigate("https://yizhu.space:1443/sd");

        Grid.SetRow(host, 0);
        Grid.SetColumn(host, 0);
        Grid.SetColumnSpan(host, 1);
        Grid.SetRowSpan(host, 1);
    }

    private void SetWebBrowserEmulationMode()
    {
        const string featureControlRegistryKey =
            @"HKEY_CURRENT_USER\Software\Microsoft\Internet Explorer\Main\FeatureControl\";
        const string browserEmulationKey = @"FEATURE_BROWSER_EMULATION";
        string appName = AppDomain.CurrentDomain.FriendlyName;

        // 检查注册表中是否已经设置了该项
        object value = Registry.GetValue(featureControlRegistryKey, browserEmulationKey, null);
        if (value != null && value.Equals(11001))
        {
            return; // 已经设置了，无需再次设置
        }

        // 设置注册表项
        Registry.SetValue(featureControlRegistryKey, browserEmulationKey, 11001, RegistryValueKind.DWord);
        Registry.SetValue(featureControlRegistryKey + appName, browserEmulationKey, 11001, RegistryValueKind.DWord);
    }
}