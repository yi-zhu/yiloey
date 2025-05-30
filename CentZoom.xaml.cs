using System;
using System.Drawing;
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
    public CentZoom()
    {
        InitializeComponent();
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
        _captureWidth = (int)(canvasMagnifier.Width / MagnificationFactor);
        _captureHeight = (int)(canvasMagnifier.Height / MagnificationFactor);
        screenRect = Screen.PrimaryScreen.Bounds;
        this.Left = screenRect.Width - canvasMagnifier.Width;
        this.Top = 0;
        hwnd = new WindowInteropHelper(this).Handle;
        YiKit.SetWindowPos(hwnd, YiKit.WindowPosTOPMOST, 0, 0, 0, 0, 0x0002 | 0x0001);
        EnableMousePassThrough();
        changeSetStyle();
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
    private double xsOp = 0.8;

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
        canvasMagnifier.Width = zxSize;
        canvasMagnifier.Height = zxSize;
        canvasMagnifier.Opacity = xsOp;
        this.Left = screenRect.Width - canvasMagnifier.Width+ xsLeft;
        this.Top = 0+xsTop;
        Width = Height = zxSize;
        // 计算圆形的中心点
        double centerX = zxSize / 2.0;
        double centerY = zxSize / 2.0;

        // 半径取宽高中较小的一半，确保是正圆
        double radius = Math.Min(zxSize, zxSize) / 2.0;

        // 更新裁剪区域
        ellipseClip.Center = new Point(centerX, centerY);
        ellipseClip.RadiusX = radius;
        ellipseClip.RadiusY = radius;
        return false;
    }


    private void UpdateMagnifier()
    {
        while (true)
        {
            /*var b=new ImageBrush(bitmapSource)
            {
                Stretch = Stretch.Uniform,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top
            };*/
            Bitmap screen = CaptureScreen();


            YiKit.RunOnUIThread(() =>
            {
                this.Topmost = false;
                this.Topmost = true;
                // canvasMagnifier.Visibility=Visibility.Visible;
                var bitmapSource = ConvertToBitmapSource(screen);
                var b = new ImageBrush(bitmapSource);
                canvasMagnifier.Background = b;
            });


            Task.Delay((int)(1000 / fps)).Wait();
            // YiKit.RunOnUIThread(()=>canvasMagnifier.Visibility=Visibility.Collapsed);
        }
    }

    private Rectangle screenRect;

    private Bitmap CaptureScreen()
    {
        // 获取主屏幕的工作区（排除任务栏等）

        // 计算屏幕中心点
        int centerX = (screenRect.Left + screenRect.Right) / 2;
        int centerY = (screenRect.Top + screenRect.Bottom) / 2;
        // canvasMagnifier.Visibility = Visibility.Collapsed;
        // 确定截图区域，防止超出屏幕边界
        Rectangle captureRect = new Rectangle(
            Math.Max(centerX - _captureWidth / 2, screenRect.Left), // 防止左边超出
            Math.Max(centerY - _captureHeight / 2, screenRect.Top), // 防止上边超出
            Math.Min(_captureWidth, screenRect.Right - (centerX - _captureWidth / 2)), // 防止右边超出
            Math.Min(_captureHeight, screenRect.Bottom - (centerY - _captureHeight / 2)) // 防止下边超出
        );


        using (Bitmap fullScreenshot = new Bitmap(captureRect.Width, captureRect.Height))
        {
            using (Graphics g = Graphics.FromImage(fullScreenshot))
            {
                // 注意：这里需要将截图区域转换为相对于屏幕左上角的坐标系
                g.CopyFromScreen(captureRect.Location, new System.Drawing.Point(0, 0), captureRect.Size);
            }
            // YiKit.RunOnUIThread(()=>canvasMagnifier.Visibility = Visibility.Visible);

            return new Bitmap(fullScreenshot);
        }
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
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
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
                dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            }

            var target = new RenderTargetBitmap(
                (int)(source.PixelWidth * MagnificationFactor),
                (int)(source.PixelHeight * MagnificationFactor),
                source.DpiX,
                source.DpiY,
                PixelFormats.Pbgra32);

            target.Render(visual);
            // Console.WriteLine($"生成图像：{target.PixelWidth}x{target.PixelHeight}");
            return target;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }


    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    public static Point GetMousePosition()
    {
        POINT point = new POINT();
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
        var hwnd = new WindowInteropHelper(this).Handle;
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
}