using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using YiKits;

namespace holey.Page;

public partial class WallPaper : Window
{
    public static WallPaper? me;

    public WallPaper()
    {
        InitializeComponent();
        me = this;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        me = null;
    }


    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Init();
        string file = YiKit.IniGet("Source", "WallPaper");
        if (string.IsNullOrEmpty(file))
        {
            ShowFile();
        }
        else
        {
            BMedia.Source = new Uri(file);
            BMedia.Volume = YiKit.IniGetInt("Volume", 0, "WallPaper");
            BMedia.Play();
        }


        BMedia.MediaEnded += ((sender, args) =>
        {
            BMedia.Stop();
            BMedia.Play();
        });
    }

    public void ShowFile()
    {
        List<string> strs = YiKit.ShowInputDialog("桌面背景配置：支持图片、视频。如果不想改变 点【取消】", "文件位置（点击选择）", "音量(0最小-1最大)");
        if (null == strs || strs.Count != 2)
        {
            return;
        }

        BMedia.Source = new Uri(strs[0]);
        YiKit.IniWrite("Source", strs[0], "WallPaper");
        YiKit.IniWrite("Volume", strs[1], "WallPaper");
        BMedia.Volume = double.Parse(string.IsNullOrEmpty(strs[1]) ? "0" : strs[1]);
        BMedia.Play();
    }


    //窗口显示回调


    #region MyRegion

    private IntPtr programIntPtr = IntPtr.Zero;
    bool inited { get; set; }

    public void Init()
    {
        try
        {
            if (inited) return;
            // 通过类名查找一个窗口，返回窗口句柄。
            programIntPtr = YiKit.FindWindow("Progman", null);

            // 窗口句柄有效
            if (programIntPtr != IntPtr.Zero)
            {
                IntPtr result = IntPtr.Zero;

                // 向 Program Manager 窗口发送 0x52c 的一个消息，超时设置为0x3e8（1秒）。
                YiKit.SendMessageTimeout(programIntPtr, 0x52c, IntPtr.Zero, IntPtr.Zero, 0, 0x3e8, result);

                // 遍历顶级窗口
                YiKit.EnumWindows((hwnd, lParam) =>
                {
                    // 找到包含 SHELLDLL_DefView 这个窗口句柄的 WorkerW
                    if (YiKit.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    {
                        // 找到当前 WorkerW 窗口的，后一个 WorkerW 窗口。 
                        IntPtr tempHwnd = YiKit.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);

                        // 隐藏这个窗口
                        YiKit.ShowWindow(tempHwnd, 0);
                    }

                    return true;
                }, IntPtr.Zero);
            }

            ;
            // YiKit.SetParent(new System.Windows.Interop.WindowInteropHelper(this).Handle, programIntPtr);
            YiKit.SetParent(YiKit.GetWindowHandle(this), programIntPtr);
            inited = true;
        }
        catch (Exception ex)
        {
        }
    }

    #endregion

    /// <summary>
    ///  此方法为隐藏Alt+Tab；隐藏窗口在toolbar 请设置ShowInTaskbar
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    void FormLoaded(object sender, RoutedEventArgs args)
    {
        //Variable to hold the handle for the form
        var hwnd = new WindowInteropHelper(this).Handle;
        //Performing some magic to hide the form from Alt+Tab
        YiKit.SetWindowLong(hwnd, YiKit.GWL_EX_STYLE,
            (YiKit.GetWindowLong(hwnd, YiKit.GWL_EX_STYLE) | YiKit.WS_EX_TOOLWINDOW) & ~YiKit.WS_EX_APPWINDOW);
        //鼠标穿透
        // SetWindowLong(hwnd, GWL_EX_STYLE,0x20);
        // SetWindowLong(hwnd, GWL_EX_STYLE, (GetWindowLong(hwnd, GWL_EX_STYLE) | 0x00000020));
        Height = YiKit.GetScreenHeight();
        Width = YiKit.GetScreenWidth();
        Left = 0;
        Top = 0;
    }
}