using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tesseract;
using Color = System.Windows.Media.Color;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace holey.Kits;

/// <summary>
/// 离线 OCR（Tesseract + 本地 tessdata）。识别全程不联网。
/// 区域：full/空=全屏；x,y,w,h=矩形。
/// </summary>
public static class OcrHelper
{
    public const string ModuleDirName = "ocr";

    public class OcrWordBox
    {
        public string Text { get; set; } = "";
        public Rectangle Bounds { get; set; }
    }

    public class OcrResult
    {
        public string Text { get; set; } = "";
        public List<OcrWordBox> Words { get; set; } = new();
        public string Engine { get; set; } = "";
    }

    public static string ModuleDir =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModuleDirName);

    public static string TessdataDir => Path.Combine(ModuleDir, "tessdata");

    private static readonly string[] RequiredLangFiles =
    {
        "chi_sim.traineddata",
        "eng.traineddata"
    };

    public static bool IsReady()
    {
        if (!Directory.Exists(TessdataDir)) return false;
        foreach (var f in RequiredLangFiles)
        {
            string p = Path.Combine(TessdataDir, f);
            if (!File.Exists(p) || new FileInfo(p).Length < 1000) return false;
        }

        return true;
    }

    public static string StatusText()
    {
        if (IsReady())
            return "离线 OCR 就绪 (Tesseract chi_sim+eng) → " + TessdataDir;
        return "离线 OCR 未就绪：请先在「三方 → ocr」安装语言包";
    }

    public static Rectangle ParseRegion(string? region)
    {
        region = (region ?? "").Trim();
        if (string.IsNullOrEmpty(region) ||
            region.Equals("full", StringComparison.OrdinalIgnoreCase) ||
            region.Equals("fullscreen", StringComparison.OrdinalIgnoreCase) ||
            region.Equals("全屏", StringComparison.OrdinalIgnoreCase))
        {
            int x = (int)System.Windows.SystemParameters.VirtualScreenLeft;
            int y = (int)System.Windows.SystemParameters.VirtualScreenTop;
            int w = (int)System.Windows.SystemParameters.VirtualScreenWidth;
            int h = (int)System.Windows.SystemParameters.VirtualScreenHeight;
            return new Rectangle(x, y, w, h);
        }

        var parts = region.Split(new[] { ',', '，', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 &&
            int.TryParse(parts[0], out int rx) &&
            int.TryParse(parts[1], out int ry) &&
            int.TryParse(parts[2], out int rw) &&
            int.TryParse(parts[3], out int rh) &&
            rw > 0 && rh > 0)
            return new Rectangle(rx, ry, rw, rh);

        throw new ArgumentException("OCR 区域格式应为 full 或 x,y,w,h");
    }

    public static Bitmap CaptureRegion(Rectangle rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new ArgumentException("截图区域无效");

        var bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, rect.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public static OcrResult Recognize(string? region = "full")
    {
        var rect = ParseRegion(region);
        using var bmp = CaptureRegion(rect);
        return RecognizeBitmap(bmp, rect.Location);
    }

    public static Task<OcrResult> RecognizeAsync(string? region = "full") =>
        Task.Run(() => Recognize(region));

    public static OcrResult RecognizeBitmap(Bitmap bmp, Point offset)
    {
        if (!IsReady())
            throw new InvalidOperationException(StatusText());

        string tmp = Path.Combine(Path.GetTempPath(), "holey_ocr_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            bmp.Save(tmp, ImageFormat.Png);
            using var engine = new TesseractEngine(TessdataDir, "chi_sim+eng", EngineMode.Default);
            using var pix = Pix.LoadFromFile(tmp);
            using var page = engine.Process(pix);

            var result = new OcrResult
            {
                Text = (page.GetText() ?? "").Trim(),
                Engine = "tesseract-offline"
            };

            using (var iter = page.GetIterator())
            {
                iter.Begin();
                do
                {
                    string word = iter.GetText(PageIteratorLevel.Word);
                    if (string.IsNullOrWhiteSpace(word)) continue;
                    if (!iter.TryGetBoundingBox(PageIteratorLevel.Word, out var box)) continue;
                    result.Words.Add(new OcrWordBox
                    {
                        Text = word.Trim(),
                        Bounds = new Rectangle(
                            offset.X + box.X1,
                            offset.Y + box.Y1,
                            Math.Max(1, box.X2 - box.X1),
                            Math.Max(1, box.Y2 - box.Y1))
                    });
                } while (iter.Next(PageIteratorLevel.Word));
            }

            return result;
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    public static bool TryFindText(OcrResult result, string keyword, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (string.IsNullOrWhiteSpace(keyword) || result == null) return false;
        keyword = keyword.Trim();

        foreach (var w in result.Words)
        {
            if (string.IsNullOrEmpty(w.Text)) continue;
            if (string.Equals(w.Text, keyword, StringComparison.OrdinalIgnoreCase) ||
                w.Text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                bounds = w.Bounds;
                return true;
            }
        }

        for (int i = 0; i < result.Words.Count; i++)
        {
            string acc = "";
            var union = Rectangle.Empty;
            for (int j = i; j < result.Words.Count && j < i + 16; j++)
            {
                acc += result.Words[j].Text;
                union = union.IsEmpty
                    ? result.Words[j].Bounds
                    : Rectangle.Union(union, result.Words[j].Bounds);
                if (acc.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bounds = union;
                    return true;
                }
            }
        }

        return false;
    }

    public static bool TextContains(OcrResult? result, string keyword)
    {
        if (result?.Text == null || string.IsNullOrWhiteSpace(keyword)) return false;
        return result.Text.IndexOf(keyword.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 安装/补齐离线语言包（仅安装时联网；识别时不联网）。
    /// </summary>
    public static async Task EnsureInstalledAsync(Action<string>? log = null)
    {
        Directory.CreateDirectory(TessdataDir);
        // tessdata_fast 体积小，适合离线打包
        string baseUrl = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/";

        foreach (var f in RequiredLangFiles)
        {
            string path = Path.Combine(TessdataDir, f);
            if (File.Exists(path) && new FileInfo(path).Length > 1000)
            {
                log?.Invoke("已存在 " + f);
                continue;
            }

            string url = baseUrl + f;
            log?.Invoke("下载离线语言包 " + f + " ...");
            string tmp = path + ".tmp";
            try
            {
                using (var wc = new WebClient())
                {
                    await wc.DownloadFileTaskAsync(new Uri(url), tmp);
                }

                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                log?.Invoke("完成 " + f + " (" + new FileInfo(path).Length / 1024 + " KB)");
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
                log?.Invoke("失败 " + f + ": " + ex.Message);
                throw;
            }
        }

        // 写标记文件
        File.WriteAllText(Path.Combine(ModuleDir, "READY.txt"),
            "offline tesseract tessdata ready\r\n" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Encoding.UTF8);
    }

    /// <summary>
    /// 全屏拖拽框选区域，返回 x,y,w,h；取消返回 null。
    /// </summary>
    public static string? PickRegionString()
    {
        var win = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
            Topmost = true,
            ShowInTaskbar = false,
            WindowState = WindowState.Maximized,
            Cursor = Cursors.Cross,
            Title = "框选 OCR 区域（拖拽，Esc 取消）"
        };

        var canvas = new Canvas { Background = System.Windows.Media.Brushes.Transparent };
        var rectVisual = new System.Windows.Shapes.Rectangle
        {
            Stroke = System.Windows.Media.Brushes.Lime,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(40, 0, 255, 0)),
            Visibility = Visibility.Collapsed
        };
        canvas.Children.Add(rectVisual);
        win.Content = canvas;

        System.Windows.Point? start = null;
        string? result = null;

        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                result = null;
                win.Close();
            }
        };

        canvas.MouseLeftButtonDown += (_, e) =>
        {
            start = e.GetPosition(canvas);
            Canvas.SetLeft(rectVisual, start.Value.X);
            Canvas.SetTop(rectVisual, start.Value.Y);
            rectVisual.Width = 0;
            rectVisual.Height = 0;
            rectVisual.Visibility = Visibility.Visible;
            canvas.CaptureMouse();
        };

        canvas.MouseMove += (_, e) =>
        {
            if (start == null || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(canvas);
            double x = Math.Min(start.Value.X, p.X);
            double y = Math.Min(start.Value.Y, p.Y);
            double w = Math.Abs(p.X - start.Value.X);
            double h = Math.Abs(p.Y - start.Value.Y);
            Canvas.SetLeft(rectVisual, x);
            Canvas.SetTop(rectVisual, y);
            rectVisual.Width = w;
            rectVisual.Height = h;
        };

        canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (start == null) return;
            canvas.ReleaseMouseCapture();
            var p = e.GetPosition(canvas);
            int x = (int)Math.Min(start.Value.X, p.X);
            int y = (int)Math.Min(start.Value.Y, p.Y);
            int w = (int)Math.Abs(p.X - start.Value.X);
            int h = (int)Math.Abs(p.Y - start.Value.Y);
            // 转为屏幕坐标
            var tl = canvas.PointToScreen(new System.Windows.Point(x, y));
            if (w < 3 || h < 3)
                result = null;
            else
                result = $"{(int)tl.X},{(int)tl.Y},{w},{h}";
            win.Close();
        };

        win.ShowDialog();
        return result;
    }
}
