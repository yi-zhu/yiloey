using System;
using System.Diagnostics;
using System.Drawing;
using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YiKits;
using YiKits.Dialog;
using Point = System.Windows.Point;

namespace holey;

public partial class CentZoom : Window
{
    public static CentZoom me;

    public CentZoom()
    {
        InitializeComponent();
        me = this;
        _hookID = SetHook(_proc);
    }

    /// <summary>
    /// 放大倍数
    /// </summary>
    private double MagnificationFactor = 2;

    private int _captureWidth = 100;
    private int _captureHeight = 100;

    private double fps = 30;
    private IntPtr hwnd = IntPtr.Zero;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        screenRect = Screen.PrimaryScreen.Bounds;
        this.Left = screenRect.Width - canvasMagnifier.Width;
        this.Top = 0;
        hwnd = new WindowInteropHelper(this).Handle;
        YiKit.SetWindowLong(hwnd,YiKit.GwlExstyle,
            (YiKit.GetWindowLong(hwnd, YiKit.GwlExstyle) | YiKit.WsExToolWindow) & ~YiKit.WsExAppWindow);
        YiKit.SetWindowPos(hwnd, YiKit.WindowPosTOPMOST, 0, 0, 0, 0, 0x0002 | 0x0001);
        // EnableMousePassThrough();
        changeSetStyle();
        // _targetBitmap.Freeze(); // 可以让 BitmapSource 跨线程共享
        Task.Run(() => UpdateMagnifier());
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        YiKit.SetWindowPos(hwnd, YiKit.WindowPosTOPMOST, 0, 0, 0, 0, 0x0002 | 0x0001);
    }

    /// <summary>
    /// 上偏移
    /// </summary>
    private double zxTop = 0;

    /// <summary>
    /// 左偏移
    /// </summary>
    private double zxLeft = 0;

    /// <summary>
    /// 大小
    /// </summary>
    private double zxSize = 100;

    /// <summary>
    /// 显示左偏移
    /// </summary>
    private double xsLeft = 0;

    /// <summary>
    /// 显示上偏移
    /// </summary>
    private double xsTop = 0;

    /// <summary>
    /// 显示透明度
    /// </summary>
    public double xsOp = 0.8;

    public bool changeSetStyle()
    {
        MagnificationFactor = YiKit.IniGetDouble("放大倍数", 2, area: InputDialog.are);
        fps = YiKit.IniGetDouble("放大刷新率", 30, area: InputDialog.are);
        xsOp = YiKit.IniGetDouble("放大透明度", 0.8, area: InputDialog.are);
        zxLeft = YiKit.IniGetDouble("放大左偏移", 0, area: InputDialog.are);
        zxTop = YiKit.IniGetDouble("放大上偏移", 0, area: InputDialog.are);
        zxSize = YiKit.IniGetDouble("放大显示大小", 100, area: InputDialog.are);
        xsLeft = YiKit.IniGetDouble("放大显示左偏移", 0, area: InputDialog.are);
        xsTop = YiKit.IniGetDouble("放大显示上偏移", 0, area: InputDialog.are);

        // 设置 Canvas 的宽高
        canvasMagnifier.Width = zxSize * 3;
        canvasMagnifier.Height = zxSize;
        canvasMagnifier.Opacity = xsOp;
        this.Left = screenRect.Width - canvasMagnifier.Width + xsLeft;
        this.Top = 0 + xsTop;
        Width = canvasMagnifier.Width;
        Height = canvasMagnifier.Height;
        _captureWidth = (int)(canvasMagnifier.Width / MagnificationFactor);
        _captureHeight = (int)(canvasMagnifier.Height / MagnificationFactor);
        // 计算圆形的中心点
        // double centerX = zxSize / 2.0;
        // double centerY = zxSize / 2.0;

        // 半径取宽高中较小的一半，确保是正圆
        // double radius = Math.Min(zxSize, zxSize) / 2.0;

        // 更新裁剪区域
        // ellipseClip.Center = new Point(centerX, centerY);
        // ellipseClip.RadiusX = radius*4;
        // ellipseClip.RadiusY = radius;
        // 获取主屏幕的工作区（排除任务栏等）
        // 计算屏幕中心点
        centerX = (int)((screenRect.Left + screenRect.Right) / 2 + zxLeft);
        centerY = (int)((screenRect.Top + screenRect.Bottom) / 2 + zxTop);
        // canvasMagnifier.Visibility = Visibility.Collapsed;
        // 确定截图区域，防止超出屏幕边界
        captureRect = new Rectangle(
            Math.Max(centerX - _captureWidth / 2, screenRect.Left), // 防止左边超出
            Math.Max(centerY - _captureHeight / 2, screenRect.Top), // 防止上边超出
            Math.Min(_captureWidth, screenRect.Right - (centerX - _captureWidth / 2)), // 防止右边超出
            Math.Min(_captureHeight, screenRect.Bottom - (centerY - _captureHeight / 2)) // 防止下边超出
        );
        fullScreenshot = new Bitmap(captureRect.Width, captureRect.Height);
        g = Graphics.FromImage(fullScreenshot);

        return false;
    }

    private long lastTime = 0;
    private Bitmap cas = null;

    private int _delay = 30;
    private long num = 0;
    private Graphics g;

    private void UpdateMagnifier()
    {
        IntPtr hBitmap = default;
        while (true)
        {
            if (FpsTb.Visibility == Visibility.Visible)
            {
                try
                {
                    if (num == 0)

                        lastTime = DateTime.Now.Ticks;


                    // 注意：这里需要将截图区域转换为相对于屏幕左上角的坐标系
                    g.CopyFromScreen(captureRect.Location, new System.Drawing.Point(0, 0), captureRect.Size);

                    hBitmap = ((Bitmap)fullScreenshot).GetHbitmap();

                    bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    // 放大图像
                    var scale = new ScaleTransform(MagnificationFactor, MagnificationFactor);
                    var visual = new DrawingVisual();
                    using (DrawingContext dc = visual.RenderOpen())
                    {
                        dc.PushTransform(scale);
                        dc.DrawImage(bitmapSource, new Rect(0, 0, bitmapSource.PixelWidth, bitmapSource.PixelHeight));
                    }

                    bitmapSource.Freeze();

                    num++;
                    if (DateTime.Now.Ticks - lastTime >= 10000000)
                    {
                        YiKit.RunOnUIThread(() =>
                        {
                            FpsTb.Text = num.ToString();
                            num = 0;
                        });
                        YiKit.Print("实际fps:" + num + "延迟:" + _delay);

                        if (_delay > 1)
                            if (num < fps)
                                _delay = (int)(_delay / (fps / num));
                            else if (fps < num)
                                _delay += 1;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
                finally
                {
                    DeleteObject(hBitmap); // 必须手动释放 GDI 资源
                }

                if (bitmapSource != null)
                {
                    YiKit.RunOnUIThread(() =>
                    {
                        this.Topmost = false;
                        this.Topmost = true;
                        canvasMagnifier.Background = new ImageBrush(bitmapSource);
                    });
                }
            }

            Task.Delay(_delay).Wait();
        }
    }

    private Rectangle screenRect;
    private Bitmap fullScreenshot;
    private Rectangle captureRect;
    private int centerX;
    private int centerY;
    private RenderTargetBitmap _targetBitmap = null;
    private BitmapSource bitmapSource = null;

    private Bitmap CaptureScreen()
    {
        // 获取主屏幕的工作区（排除任务栏等）

        // 计算屏幕中心点
        centerX = (int)((screenRect.Left + screenRect.Right) / 2 + zxLeft);
        centerY = (int)((screenRect.Top + screenRect.Bottom) / 2 + zxTop);
        // canvasMagnifier.Visibility = Visibility.Collapsed;
        // 确定截图区域，防止超出屏幕边界
        captureRect = new Rectangle(
            Math.Max(centerX - _captureWidth / 2, screenRect.Left), // 防止左边超出
            Math.Max(centerY - _captureHeight / 2, screenRect.Top), // 防止上边超出
            Math.Min(_captureWidth, screenRect.Right - (centerX - _captureWidth / 2)), // 防止右边超出
            Math.Min(_captureHeight, screenRect.Bottom - (centerY - _captureHeight / 2)) // 防止下边超出
        );


        fullScreenshot ??= new Bitmap(captureRect.Width, captureRect.Height);
        using Graphics g = Graphics.FromImage(fullScreenshot);
        // 注意：这里需要将截图区域转换为相对于屏幕左上角的坐标系
        g.CopyFromScreen(captureRect.Location, new System.Drawing.Point(0, 0), captureRect.Size);
        return fullScreenshot;
    }

    [DllImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    private BitmapSource ConvertToBitmapSource(Bitmap bitmap)
    {
        if (bitmap == null)
            return null;

        IntPtr hBitmap = ((Bitmap)bitmap).GetHbitmap();

        try
        {
            bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            // 放大图像
            var scale = new ScaleTransform(MagnificationFactor, MagnificationFactor);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(scale);
                dc.DrawImage(bitmapSource, new Rect(0, 0, bitmapSource.PixelWidth, bitmapSource.PixelHeight));
            }

            bitmapSource.Freeze();
            return bitmapSource;
            /*
            _targetBitmap = new RenderTargetBitmap(
                (int)(source.PixelWidth * MagnificationFactor),
                (int)(source.PixelHeight * MagnificationFactor),
                source.DpiX,
                source.DpiY,
                PixelFormats.Pbgra32);


            _targetBitmap.Render(visual);
            _targetBitmap.Freeze();

            // Console.WriteLine($"生成图像：{target.PixelWidth}x{target.PixelHeight}");
            return _targetBitmap;*/
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }


    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(ref Crosshair.POINT lpPoint);


    public static Point GetMousePosition()
    {
        Crosshair.POINT point = new Crosshair.POINT();
        GetCursorPos(ref point);
        return new Point(point.X, point.Y);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowExTransparent(hwnd);
    }

    private void EnableMousePassThrough()
    {
        SetWindowExTransparent(hwnd);
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

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        UnhookWindowsHookEx(_hookID);
    }

    #region 全局监听右键

    private const int WH_MOUSE_LL = 14;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public Crosshair.POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private LowLevelMouseProc _proc = HookCallback;
    private static IntPtr _hookID = IntPtr.Zero;


    private IntPtr SetHook(LowLevelMouseProc proc)
    {
        using (var curAssembly = Process.GetCurrentProcess().MainModule)
        {
            return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curAssembly.ModuleName), 0);
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            // 检查鼠标消息类型
            if (wParam.ToInt32() == WM_RBUTTONDOWN)
            {
                OnRightButtonDown();
            }
            else if (wParam.ToInt32() == WM_RBUTTONUP)
            {
                OnRightButtonUp();
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    // 自定义事件回调
    private static void OnRightButtonDown()
    {
        // 触发你的逻辑，比如：
        me.canvasMagnifier.Opacity = me.xsOp;
        me.FpsTb.Opacity = me.xsOp;
        me.Topmost = false;
        me.Topmost = true;
        me.FpsTb.Visibility = Visibility.Visible;
    }

    private static void OnRightButtonUp()
    {
        // 右键释放后的逻辑
        me.canvasMagnifier.Opacity = 0;
        me.FpsTb.Visibility = Visibility.Hidden;
        me.Topmost = false;
    }

    #endregion
}