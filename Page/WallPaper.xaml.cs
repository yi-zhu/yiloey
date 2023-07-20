using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YiKits;
using Color = System.Windows.Media.Color;

namespace holey.Page;

public partial class WallPaper : Window
{
    public static WallPaper? me;
    public List<Uri> _uris = new();
    public Dictionary<Uri, RenderTargetBitmap> _uriBitmaps = new();
    public string _source = "";
    public double _volume = 0;
    public double _pictime = 0;

    public double Pictime
    {
        get => _pictime;
        set
        {
            _pictime = value;
            YiKit.IniWrite("Pictime", value + "", "WallPaper");
        }
    }

    public string Source
    {
        get => _source;
        set
        {
            _source = value;
            YiKit.IniWrite("Source", value, "WallPaper");
        }
    }

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            YiKit.IniWrite("Volume", value + "", "WallPaper");
        }
    }

    public WallPaper()
    {
        InitializeComponent();
        me = this;
    }

    protected override void OnClosed(EventArgs e)
    {
        YiKit.RegisterWindowMessage(YiKit.GetWindowHandle(this), false);
        me = null;
        base.OnClosed(e);
    }


    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Init();

        //注册windows全局消息广播
        YiKit.RegisterWindowMessage(YiKit.GetWindowHandle(this));
        HwndSource hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        if (hwndSource != null)
        {
            hwndSource.AddHook(new HwndSourceHook(WndProc));
        }

        string file = YiKit.IniGet("Source", "WallPaper");
        BMedia.Stretch = System.Windows.Media.Stretch.UniformToFill;
        BMedia.CacheMode = new BitmapCache(1);

        if (string.IsNullOrEmpty(file))
        {
            ShowFile();
        }
        else
        {
            _pictime = YiKit.IniGetInt("Pictime", 0, "WallPaper");
            BMedia.Volume = _volume = YiKit.IniGetInt("Volume", 0, "WallPaper");
            if (YiKit.IsDir(file))
            {
                PlayDir(file);
            }
            else
            {
                _uris.Add(new Uri(file));
                BMedia.Source = _uris[0];

                PlayMidea();
            }
        }


        BMedia.MediaFailed += (((sender, args) =>
        {
            if (_uris.Count == 1)
            {
                YiKit.ShowToastError(_uris[0].LocalPath + "格式不支持！");
            }
            else
                NextPlay();
        }));

        BMedia.MediaEnded += ((sender, args) =>
        {
            NextPlay();

            /*
            if (BMedia.HasVideo || BMedia.HasAudio)
            {
                // BMedia.Stop();
                // BMedia.Play();
            }*/
        });
    }

    int i = 0;

    private void NextPlay()
    {
        try
        {

            if (i >= _uris.Count - 1)
            {
                i = 0;
            }
            else
            {
                i = i + 1;
            }

 

            /*if (BMedia.HasVideo || BMedia.HasAudio)
        {
        }*/
            if (!BMedia.NaturalDuration.HasTimeSpan)
            {
                YiKit.RunThread((state =>
                {
                    Thread.Sleep((int) (_pictime * 60000));
                    Dispatcher.Invoke(() =>
                    {
                        BMedia.Stop();
                        if (BMedia.Source != _uris[i])
                        {
                            BMedia.Source = _uris[i];
                        }

                        PlayMidea();
                    });
                }));
            }
            else if (BMedia.Source != _uris[i])
            {
                BMedia.Stop();
                BMedia.Source = _uris[i];
                PlayMidea();
            }
            else
            {
                BMedia.Stop();
                PlayMidea();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    public void ShowFile()
    {
        List<string> strs = YiKit.ShowInputDialog("桌面背景配置：支持图片、视频。如果不想改变 点【取消】",
            "选择文件位置（点击选择）", "或选择文件夹位置（点击选择）",
            "音量(0最小-1最大)默认0", "图片轮换时间（分钟）（只对文件夹图片生效！）默认0.5");
        if (null == strs || strs.Count != 4)
        {
            return;
        }

        if (!string.IsNullOrEmpty(strs[2]))

            try
            {
                _volume = double.Parse(string.IsNullOrEmpty(strs[2]) ? "0" : strs[2]);
                if (_volume > 100)
                {
                    YiKit.ShowToastError("音量只能输入  最小0-1最大 或0-100  之间的数字，如0.5");
                    Volume = 1;
                }
                else if (_volume > 1)
                {
                    Volume = _volume / 100;
                }
                else if (_volume < 0)
                {
                    Volume = 0;
                }
                else
                    Volume = _volume;

                BMedia.Volume = Volume;
            }
            catch (Exception e)
            {
                YiKit.ShowToastError("音量只能输入  最小0-1最大 或0-100  之间的数字，如0.5");
            }

        if (!string.IsNullOrEmpty(strs[3]))
            try
            {
                _pictime = double.Parse(string.IsNullOrEmpty(strs[3]) ? "0" : strs[3]);
                if (_pictime < 0)
                {
                    YiKit.ShowToastError("图片轮询不能小于1");
                    Pictime = 0;
                }
                else
                    Pictime = _pictime;
            }
            catch (Exception e)
            {
                YiKit.ShowToastError("图片轮询时间(分钟)只能>0");
            }

        if (!string.IsNullOrEmpty(strs[0]))
        {
            _uris.Clear();
            _uris.Add(new Uri(strs[0]));
            BMedia.Source = new Uri(strs[0]);
            Source = strs[0];
            PlayMidea();
        }

        if (!string.IsNullOrEmpty(strs[1]))
        {
            Source = strs[1];
            PlayDir(strs[1]);
        }
    }

    public void PlayDir(string dirpath)
    {
        string[] dir = Directory.GetFiles(dirpath, "*.*", SearchOption.AllDirectories);
        _uris.Clear();
        foreach (string s in dir)
        {
            if (s.EndsWith(".mp4") || s.EndsWith(".png") || s.EndsWith(".jpg") || s.EndsWith(".bmp") ||
                s.EndsWith(".avi") || s.EndsWith(".wmv") || s.EndsWith(".mkv") || s.EndsWith(".flv") ||
                s.EndsWith(".rmvb") || s.EndsWith(".rm") || s.EndsWith(".mov") || s.EndsWith(".3gp") ||
                s.EndsWith(".mp3") || s.EndsWith(".jfif") ||
                s.EndsWith(".mpeg") || s.EndsWith(".mpg") || s.EndsWith(".mpe") || s.EndsWith(".ts") ||
                s.EndsWith(".vob") || s.EndsWith(".dat") || s.EndsWith(".asf") || s.EndsWith(".wmvhd") ||
                s.EndsWith(".dvr-ms") || s.EndsWith(".webm") || s.EndsWith(".m4v") || s.EndsWith(".m2ts") ||
                s.EndsWith(".mts") || s.EndsWith(".tp") || s.EndsWith(".tpr") || s.EndsWith(".divx") ||
                s.EndsWith(".xvid") || s.EndsWith(".m2t") || s.EndsWith(".m2p") || s.EndsWith(".m2v") ||
                s.EndsWith(".mp4v") || s.EndsWith(".qt") || s.EndsWith(".swf") || s.EndsWith(".avchd"))
            {
                _uris.Add(new Uri(s));
            }
        }

        if (_uris.Count > 0)
        {
            BMedia.Source = _uris[0];
            PlayMidea();
        }
    }

    private void PlayMidea()
    {
        try
        {
            BMedia.Play();
            if (!_uriBitmaps.ContainsKey(BMedia.Source) || _uriBitmaps[BMedia.Source] == null)
            {
                RenderTargetBitmap bmp = new((int) SystemParameters.PrimaryScreenWidth,
                    (int) SystemParameters.PrimaryScreenHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(BMedia);
                _uriBitmaps[BMedia.Source] = bmp;
            }

            BImage.Source = _uriBitmaps[BMedia.Source];
            
        }
        catch
        {
            NextPlay();
            // YiKit.ShowToast("播放失败,请检查文件是否被支持：mp4,mp3,avi,png,jpg...");
        }
    }

    /// <summary>
    /// 暂停播放
    /// </summary>
    /// <param name="pause">默认暂停</param>
    protected void PausePlay(bool pause = true)
    {
        if (pause)
        {
            BMedia.Pause();
        }
        else
        {
            BMedia.Play();
        }
    }


    protected virtual IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case YiKit.WM_SIZEING:

                if (wParam.ToInt32() == (int) YiKit.ABNotify.ABN_FULLSCREENAPP)
                {
                    if (lParam == (IntPtr) 1)
                    {
                        PausePlay();
                    }
                    else
                    {
                        PausePlay(false);
                    }
                }
                //你的代码

                break;
        }

        return IntPtr.Zero;
    }

    //窗口显示回调


    #region 初始化设置

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