using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YiKits;
using YiKits.Dialog;
using Application = System.Windows.Application;
using Brushes = System.Drawing.Brushes;
using Color = System.Drawing.Color;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;

namespace holey;

public partial class Crosshair : Window
{
    public Crosshair()
    {
        InitializeComponent();
    }



    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
       
        EnableMousePassThrough();
        Rectangle screenRect = Screen.PrimaryScreen.Bounds;
        Left = (screenRect.Width-this.Width) / 2;
        Top = (screenRect.Height-this.Height )/ 2;
     
    }

    /// <summary>
    /// 准星上偏移
    /// </summary>
    private double zxTop = 0;
    /// <summary>
    /// 准星左偏移
    /// </summary>
    private double zxLeft = 0;
    /// <summary>
    /// 准星大小
    /// </summary>
    private double zxSize = 0;
    /// <summary>
    /// 准星颜色
    /// </summary>
    private int zxColor = 0xfff;

    public bool changeStyle()
    {
        zxTop=YiKit.IniGetDouble("准星上偏移", area:InputDialog.are, def: 0);
        zxLeft=YiKit.IniGetDouble("准星左偏移", area:InputDialog.are, def: 0);
        zxSize=YiKit.IniGetDouble("准星大小", area:InputDialog.are, def: 2);
        zxColor=YiKit.IniGetInt("准星颜色", area:InputDialog.are, def: 0xfff);
        // zxl.Stroke= YiKit.IntToBrush(zxColor);
        // zxl2.Stroke= YiKit.IntToBrush(zxColor);

        this.Left += zxLeft;
        this.Top += zxTop;
        zxl2.X1 = zxSize / -2;
        zxl2.X2 = zxSize / 2;
        zxl.Y1 = zxSize / -2;
        zxl.Y2 = zxSize / 2;
        // Viewbox.Height = Viewbox.Width = zxSize;
        return false;

    }

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);


    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }


    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowExTransparent(hwnd);
        changeStyle();
    }

    private void EnableMousePassThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        YiKit.SetWindowLong(hwnd,YiKit.GwlExstyle,
            (YiKit.GetWindowLong(hwnd, YiKit.GwlExstyle) | YiKit.WsExToolWindow) & ~YiKit.WsExAppWindow);
        // SetWindowExTransparent(hwnd);
    }

    [DllImport("user32", SetLastError = true)]
    static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32", SetLastError = true)]
    static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;

    private void SetWindowExTransparent(IntPtr hwnd)
    {
        int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        DragMove();
    }
}