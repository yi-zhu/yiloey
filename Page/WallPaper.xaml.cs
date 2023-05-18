using System;
using System.Runtime.InteropServices;
using System.Windows;
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
}