using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Shapes;
using NAudio.CoreAudioApi;
using NAudio.Utils;
using NAudio.Wave;
using YiKits;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace holey.Page;

public partial class YAudioPage : System.Windows.Controls.Page, INotifyPropertyChanged
{
    private string area = "catalog";

    private YAudioPage me;

    private Dictionary<string, string> ids = new Dictionary<string, string>();

    public YAudioPage()
    {
        me = this;
        string path = YiKit.IniGet("path", area);
        pjGid = YiKit.IniGet("pjgid", area);
        capShow = YiKit.IniGetBool("capShow", true, area);
        errorAuto = YiKit.IniGetBool("errorAuto", true, area);
        hlnum = Convert.ToInt64(YiKit.IniGet("hlnum", area, null, "0"));

        bjsc = long.Parse(YiKit.IniGet("bjsc", area, null, "5000"));
        xtb = int.Parse(YiKit.IniGet("xtb", area, null, "1560"));
        ytb = int.Parse(YiKit.IniGet("ytb", area, null, "226"));

        InitializeComponent();

        DataContext = this;
        CapShow.IsChecked = capShow;
        ErrorAutoCk.IsChecked = errorAuto;
        if (hlnum == null)
            hlnum = 100;
        if (bjsc == null)
            hlnum = 5000;
        HlTextBox.Text = hlnum + "";
        BjTimeTextBox.Text = bjsc / 1000 + "";

        if (pjGid == null)
            pjGid = "30000000940";
        JkTextBox.Text = pjGid;
        if (path != null)
        {
            SetDirBtn.Content = path;

            logPath = path + "\\log";
        }

        ;
        LogListBox.ItemsSource = hllogs;
        LogeListBox.ItemsSource = hlelogs;
        // 如果你想每次添加新消息后都滚动到最底部，可以订阅CollectionChanged事件
        hllogs.CollectionChanged += (s, e) => ScrollToEnd();
        hlelogs.CollectionChanged += (s, e) => ScrolleToEnd();

        // 启用UI虚拟化
        LogListBox.SetValue(VirtualizingStackPanel.IsVirtualizingProperty, true);
        LogListBox.SetValue(VirtualizingStackPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);

        // 订阅CollectionChanged事件
        hllogs.CollectionChanged += Messages_CollectionChanged;
        /*YiKit.RunThread((e) =>
        {
            while (true)
            {
                Thread.Sleep(1000);
                YiKit.RunOnUIThread(() => { hllogs.Add(new Random().NextDouble().ToString()+"-+++++++++++++++++++++++++++++++++++++++++++++++"+new Random().NextDouble().ToString());});
            }
        });*/

        if (waveformPolyline == null)
        {
            waveformPolyline = new Polyline
            {
                Stroke = Brushes.Red,
                StrokeThickness = 3
            };
            waveformCanvas.Children.Add(waveformPolyline);
        }

        if (this.GetType().ToString().Equals(YiKit.IniGet("autostartpage")))
        {
            AutoStart.IsChecked = true;
            //自启开始监听

            StartBtn_OnClick(null, null);
        }
    }

    private bool shouldScrollToBottom = true;

    private async void Messages_CollectionChanged(object sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // 如果不需要滚动到底部，则不执行滚动动作
        if (!shouldScrollToBottom) return;
        // 延迟滚动以允许UI完成更新
        await Task.Delay(10); // 适当调整延迟时间
        // 使用Dispatcher确保滚动发生在UI线程上
        this.Dispatcher.Invoke(() => ScrollToEnd());
    }

    // 滚动到ListBox的最后一个元素
    private void ScrollToEnd()
    {
        if (LogListBox.Items.Count > 0 && !mouseInLogList)
            LogListBox.ScrollIntoView(LogListBox.Items[LogListBox.Items.Count - 1]);
    }

    private void ScrolleToEnd()
    {
        if (LogeListBox.Items.Count > 0 && !mouseInLogeList)
            LogeListBox.ScrollIntoView(LogeListBox.Items[LogeListBox.Items.Count - 1]);
    }

    public enum RecordType
    {
        loudspeaker = 0, // 扬声器
        microphone = 1 //麦克风
    }

    //录制的类型
    RecordType t = RecordType.loudspeaker;

    //录制麦克风的声音
    WaveInEvent waveIn = null; //new WaveInEvent();

    //录制扬声器的声音
    WasapiLoopbackCapture capture = null; //new WasapiLoopbackCapture();

    //生成音频文件的对象
    WaveFileWriter writer = null;
    string audioFile = "";


    //结束录制
    public void StopRecordAudio()
    {
        if (t == RecordType.microphone)
            waveIn.StopRecording();
        else
            capture.StopRecording();
    }


    static float CalculateRms(byte[] buffer, int bytesRecorded)
    {
        int samples = bytesRecorded / 2; // 因为每个样本是16位（2字节）
        float sum = 0f;
        for (int i = 0; i < samples; i++)
        {
            short sample = BitConverter.ToInt16(buffer, i * 2);
            sum += sample * sample;
        }

        return (float)Math.Sqrt(sum / samples);
    }

    static byte[] GetWaveformData(byte[] buffer, int bytesRecorded)
    {
        // 简化处理，只取每个样本的第一个字节作为波形数据
        int samples = bytesRecorded / 2;
        byte[] waveform = new byte[samples];
        for (int i = 0; i < samples; i++)
        {
            waveform[i] = buffer[i * 2];
        }

        return waveform;
    }

    public bool watched = false;
    private Polyline waveformPolyline;

    public bool isReadingNames = false;

    public void readNames(object? sender=null, RoutedEventArgs? routedEventArgs=null)
    {
        if (isReadingNames) return;
        YiKit.RunThread(s =>
        {
            
            isReadingNames = true;
            char spx = ';';
            string filep = null;

            if (File.Exists("ids.txt")) filep = "ids.txt";
            else if (File.Exists("id.txt")) filep = "id.txt";
            else if (File.Exists("ids")) filep = "ids";
            else if (File.Exists("id")) filep = "ids";

            if (filep != null)

            {
                try
                {
                    ids.Clear();
                    var count = 0;
                    var count_all = 0;
                    foreach (string line in File.ReadLines(filep))
                    {
                        count_all++;
                        /*if (spx == ';')
                        {
                            if (line.Contains('\t')) spx = '\t';
                            if (line.Contains(' ')) spx = ' ';
                            else if (line.Contains(',')) spx = ',';
                            else if (line.Contains('，')) spx = '，';
                            else if (line.Contains('\t')) spx = '\t';
                            else if (line.Contains('-')) spx = '-';
                            else if (line.Contains(';')) spx = ';';
                            else if (line.Contains('；')) spx = '；';
                        }*/

                        var _ls = line.Split(spx);
                        if (_ls.Length > 1)
                        {
                            ids[_ls[0]] = _ls[1];
                            count++;
                        }

                        
                        // ids.Add(line.Split(spx)[0], line.Split(spx)[1]);
                        // Console.WriteLine(line);
                    }
                    YiKit.ShowToast($"{filep}读取{count_all}条，加载{count}条，失败{count_all-count}条");
                }
                catch (Exception e)
                {
                    isReadingNames = false;
                    YiKit.ShowToastWaring($"{filep}加载失败"+e.Message);
                }

                
            }

            isReadingNames = false;
        });
    }

    public bool startCap(bool start = true)
    {
        if (!start)
        {
            try
            {
                watched = false;
                capture?.StopRecording();
                _lastlen = 0;
                oldlogc = "";
                (StartBtn).Content = "开始监听";
                StartBtn.Background = Brushes.Blue;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
                (StartBtn).Content = "error1";
                return false;
            }

            return true;
        }


        try
        {
            
            if (t == RecordType.microphone)
            {
                waveIn = new WaveInEvent();
                // writer = new WaveFileWriter(audioFile, waveIn.WaveFormat);
                //开始录音，写数据
                waveIn.DataAvailable += (s, a) => { writer.Write(a.Buffer, 0, a.BytesRecorded); };
                //结束录音
                waveIn.RecordingStopped += (s, a) =>
                {
                    writer.Dispose();
                    // writer = null;
                    waveIn.Dispose();
                };
                waveIn.StartRecording();
            }
            else
            {
                if (null != capture)
                {
                    capture.Dispose();
                    Thread.Sleep(100);
                    capture = null;
                }

                capture = new WasapiLoopbackCapture();
                long capNum = 0;
                // writer = new WaveFileWriter(audioFile, capture.WaveFormat);
                capture.DataAvailable += (s, e) =>
                {
                    try
                    {
                        // writer.Write(a.Buffer, 0, e.BytesRecorded);
                        float[] samples = Enumerable // 提取数据中的采样
                            .Range(0, e.BytesRecorded / 4) // 除以四是因为, 缓冲区内每 4 个字节构成一个浮点数, 一个浮点数是一个采样
                            .Select(i => BitConverter.ToSingle(e.Buffer, i * 4)) // 转换为 float
                            .ToArray(); // 转换为数组
                        // 将字节转换为浮点数样本以进行处理
                        // var sampleCount = e.BytesRecorded / 4; // 假设是32位浮点格式
                        // float[] samples = new float[sampleCount];
                        Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
                        if (samples.Length > 1)
                            nowaudio = samples.Max() - samples.Min();
                        else nowaudio = 0;
                        if (capShow)
                            // 在这里实现波形绘制逻辑
                            YiKit.RunOnUIThread(() =>
                            {
                                capNum++;
                                if (capNum % 3 == 1)
                                {
                                    waveformPolyline.Points.Clear();
                                }

                                if (capNum > int.MaxValue)
                                {
                                    capNum = 0;
                                }

                                for (int i = 0; i < samples.Length && i < waveformCanvas.ActualWidth; i++)
                                {
                                    double y = (samples[i]) * waveformCanvas.ActualHeight /
                                               1; // 将-1到1范围内的样本映射到画布高度
                                    waveformPolyline.Points.Add(new Point(i, y));
                                }
                            });
                    }
                    catch (Exception exception)
                    {
                        YiKit.RunOnUIThread(() =>
                        {
                            (StartBtn).Content = exception.ToString();
                            YiKit.ShowToast(exception.ToString());
                        });

                        // Console.WriteLine(exception);
                        // throw;
                    }

                    // nowaudio = allSamples.Max();

                    // 获取采样后, 在这里进行详细处理
                    YiKit.RunOnUIThread(() => { StartBtn.Content = "" + nowaudio; });
                };
                //结束录音
                capture.RecordingStopped += (s, a) =>
                {
                    // writer.Dispose();
                    // writer = null;
                    // capture?.Dispose();
                    (StartBtn).Content = "监听结束~~";
                    // capture = null;

                    if (watched)
                    {
                        watched = false;
                        (StartBtn).Content = "监听被结束，已重启监听中";
                        Thread.Sleep(delay + 50);
                        startCap();
                    }
                };
                capture.StartRecording();
            }
        }
        catch (Exception ex)
        {
            // watched = false;
            // (StartBtn).Content = "开始监听,error2";
            YiKit.RunOnUIThread(() => { YiKit.ShowToastError("" + ex.Message); });
            watched = false;
            Thread.Sleep(delay + 50);
            startCap();
        }

        readNames();
        watched = true;
        startListingLogDir1S();
        playErrorThrea();

        StartBtn.Background = Brushes.Red;
        StartBtn.Content = "监听中";
        return true;
    }

    private bool startBtnBool = true;

    private void StartBtn_OnClick(object sender, RoutedEventArgs e)
    {
        if (startBtnBool)
        {
            startCap(true);
            startBtnBool = !startBtnBool;
        }
        else
        {
            startCap(false);
            startBtnBool = !startBtnBool;
        }
    }

    private float nowaudio = 0;

    private void EndBtn_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            watched = false;
            capture?.StopRecording();
            _lastlen = 0;
            oldlogc = "";
            (StartBtn).Content = "开始监听";
            StartBtn.Background = Brushes.Blue;
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
        }
    }

    private void SICk_OnChecked(object sender, RoutedEventArgs e)
    {
        t = RecordType.microphone;
        SICk.Content = "麦克风";
    }

    private void SICk_OnUnchecked(object sender, RoutedEventArgs e)
    {
        t = RecordType.loudspeaker;
        SICk.Content = "扬声器";
    }


    string logPath = "C:\\Program Files (x86)\\Caltta\\PDTDAS\\log";

    string logFile = "";

    string nowlogc = "";
    string oldlogc = "";

    public string readLastFile()
    {
        if (nowlogc != null)
            oldlogc = nowlogc.Clone().ToString();
        if (logFile == null)
        {
            return null;
        }

        logFile = getNewFile(logPath);
        try
        {
            nowlogc = ReadFileInReverseUtf8(logFile, "gb2312");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        if (nowlogc == null)
        {
            return null;
        }

        /*YiKit.RunOnUIThread(() =>
        {
            LogTextBlock.Text = nowlogc;
            scrollViewer.ScrollToEnd();
        });*/
        return nowlogc;
    }


    public Dictionary<string, string> gcLogLine = new Dictionary<string, string>();

    /// <summary>
    /// 声音比率
    /// </summary>
    private double soundRatio = 0;

    /// <summary>
    /// 被监控的群组是否再呼叫
    /// </summary>
    private bool faqi = false;

    /// <summary>
    /// 日志正在讲话
    /// </summary>
    private bool faqisp = false;

    /// <summary>
    /// 持续时间内轮询次数
    /// </summary>
    private int jknum = 0;

    /// <summary>
    /// 持续时间内轮询次数有效
    /// </summary>
    private int jknumyx = 0;

    /// <summary>
    /// 多少有声音的
    /// </summary>
    private int jknumsound = 0;

    /// <summary>
    /// 多少有声音的有效
    /// </summary>
    private int jknumsoundyx = 0;

    /// <summary>
    /// 判定为背景音次数
    /// </summary>
    private int jknumsoundbj = 0;

    /// <summary>
    /// 连续无声音
    /// </summary>
    private int lxwsy = 0;

    /// <summary>
    /// 背景音临界值
    /// </summary>
    private double beijingyin = 0.1;

    /// <summary>
    /// 无声音临界值
    /// </summary>
    private double wushengyin = 0.03;

    private long pttstart = 0;

    /// <summary>
    /// 轮询单次延迟时间
    /// </summary>
    public int delay = 200;

    ObservableCollection<string> hllogs = new ObservableCollection<string>();
    ObservableCollection<string> hlelogs = new ObservableCollection<string>();
    private string logeFilePath = YiKit.GetAppStartPath() + "\\idswatch\\监控派接异常通话.csv";
    private string logFilePath = YiKit.GetAppStartPath() + "\\idswatch\\" + YiKit.NowD() + "监控派接.csv";

    private bool ismove = false;

    public List<string> tishi = new List<string>()
    {
        "派接异常，强制占用信道，无声音",
        "误触，有背景音，无人声",
        "讲话声音短",
    };

    /// <summary>
    /// 声音计数归零
    /// </summary>
    public void jkSoundZero()
    {
        jknum = 0;
        jknumyx = 0;
        soundRatio = 0;
        jknumsound = 0;
        jknumsoundyx = 0;
        jknumsoundbj = 0;
    }

    uint timeoutMilliseconds = 5000; // 设置超时时间为5秒

    // 定义一个简单的消息，如 WM_NULL，用于检测响应
    private const uint WM_NULL = 0x000D;

    private string pname = "济南公安融合通信";

    // private string pname = "everything";
    IntPtr hwnd = IntPtr.Zero;
    private short numr = 0;
    private short norNum = 0;

    private long numRes;
    private long numNoRes;

    public long NumRes
    {
        get { return numRes; }
        set
        {
            if (numRes != value)
            {
                numRes = value;
                // YiKit.RunOnUIThread(() => { NumResTB.Text = value+""; });
                OnPropertyChanged("NumRes");
            }
        }
    }

    public long NumNoRes
    {
        get { return numNoRes; }
        set
        {
            if (numNoRes != value)
            {
                numNoRes = value;
                norNum++;
                // YiKit.RunOnUIThread(() => { NumNoResTB.Text = value+""; });
                OnPropertyChanged("NumNoRes");
            }
        }
    }

    public void restartProgress()
    {
        return;
        try
        {
            numr++;
            if (hwnd == IntPtr.Zero)
            {
                hwnd = YiKit.FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, pname);
                Thread.Sleep(500);
            }

            if (hwnd != IntPtr.Zero)
            {
                // YiKit.IsHungAppWindow(hwnd);
                if (YiKit.IsHungAppWindow(hwnd))
                {
                    NumRes++;
                    NumNoRes++;
                    if (norNum > 2)
                    {
                        norNum = 0;
                        // 进程无响应
                        YiKit.RunOnUIThread(() =>
                        {
                            YiKit.ShowToastError("进程无响应!!" + "-------" + numr + "--" + num + "-----------------");
                        });
                        YiKit.ProgressEnd(pname);
                        hwnd = IntPtr.Zero;
                        Thread.Sleep(500);
                    }
                    else return;
                }
                else
                {
                    NumRes++;
                    // YiKit.RunOnUIThread(() => { YiKit.ShowToastWaring("读取次数"+numr+"--"+num); });
                    return;
                }
            }

            //930,700
            //执行重启命令
            string lujing = YiKit.IniGet("path", area) + "\\济南公安融合通信.exe";
            //930,700
            YiKit.StartProcessAsync(lujing);
            Thread.Sleep(1000);
            // 1110,766

            while ((hwnd = YiKit.FindWindowEx(IntPtr.Zero, IntPtr.Zero, null, pname)) == IntPtr.Zero)
            {
                Thread.Sleep(1000);
            }

            Thread.Sleep(500);

            YiKit.MoveWindow(hwnd, 0, 0, 1110, 766, true);
            YiKit.SetForegroundWindow(hwnd);
            Thread.Sleep(500);
            YiKit.SimulateMouseClick(630, 580);
            hwnd = IntPtr.Zero;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        // 630,580
    }

    public short errorExNum = 0;

    public void playErrorThrea()
    {
        YiKit.RunThread(s =>
        {
            while (watched)
            {
                if (errorExNum < 2 && ((lxwsy > (bjsc / delay)) || jknumsoundbj > (bjsc / delay)))
                {
                    if (!ismove)
                    {
                        ismove = true;

                        YiKit.RunThread(s1 =>
                        {
                            var sn = 1;
                            while (ismove)
                            {
                                Console.Beep();
                                Thread.Sleep(100 / sn);
                                sn += 1;
                            }
                        });

                        // 40，550，
                        //200,950
                        // YiKit.ProgressMouseClick("济南公安融合通信", xtb, ytb);
                        var h = YiKit.GetWindowPtr("济南公安融合通信");

                        // 设置焦点到目标窗口
                        YiKit.SetForegroundWindow(h);
                        Thread.Sleep(500);
                        YiKit.SimulateMouseClick(40, 550);
                        Thread.Sleep(500);
                        YiKit.SimulateMouseClick(200, 950);
                        Thread.Sleep(500);
                        YiKit.SimulateMouseClick(xtb, ytb);
                        errorExNum++;
                        ismove = false;
                    }

                    // 播放系统报警音
                    // SystemSounds.Beep.Play();

                    // 最终恢复原始背景色
                }
                else if (errorExNum >= 2)
                {
                    jknumsoundbj = 0;
                    lxwsy = 0;
                    faqi = false;
                    errorExNum = 0;
                    ismove = false;
                }
                else Thread.Sleep(1000);
            }
        });
    }

    string errorMsg = "";
    private short num = 0;

    private List<string> lastErrors = new List<string>();
    string lastError = "";
    private List<string> lastErrorFails = new List<string>();

    public void startListingLogDir1S()
    {
        YiKit.RunThread(state =>
        {
            StreamWriter txt = new StreamWriter(logFilePath, true, Encoding.UTF8);
            txt.Flush();
            string log = "";
            string line = "";
            string gid = "", cid = "", uid = "", sm = "", sn = "";

            while (watched)
            {
                if (num > 32)
                {
                    restartProgress();

                    num = 0;

                    //判断日期
                    if (!logFilePath.Contains(YiKit.NowD()))
                    {
                        try
                        {
                            txt.Close();
                            logFilePath = YiKit.GetAppStartPath() + "\\idswatch\\" + YiKit.NowD() + "监控派接.csv";
                            txt = new StreamWriter(logFilePath, true, Encoding.UTF8);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                    }
                }

                num++;
                if (faqi)
                {
                    jknum += 1;
                    jknumsound += (nowaudio < wushengyin ? 0 : 1);
                    if ((nowaudio < beijingyin ? 0 : 1) == 0)
                    {
                        jknumsoundbj += 1;
                    }
                    else if (jknumsoundbj != 0 && (jknumsoundbj <= (bjsc / delay)))
                    {
                        jknumsoundbj = 0;
                    }

                    if (faqisp)
                    {
                        jknumyx += 1;
                        jknumsoundyx += (nowaudio < wushengyin ? 0 : 1);
                    }

                    if (nowaudio == 0)
                    {
                        lxwsy += 1;
                        if (lxwsy > bjsc / delay)
                        {
                            // 播放系统报警音
                            // SystemSounds.Beep.Play();
                            YiKit.RunOnUIThread(() =>
                            {
                                me.Background = (me.Background == Brushes.Transparent)
                                    ? Brushes.Red
                                    : Brushes.Transparent;
                            });
                            // 最终恢复原始背景色
                        }
                    }
                    else
                    {
                        if (lxwsy != 0)
                        {
                            if (lxwsy <= (bjsc / delay))
                                lxwsy = 0;
                            YiKit.RunOnUIThread(() => { me.Background = Brushes.Transparent; });
                        }
                    }
                }
                else
                {
                    if (lxwsy != 0)
                    {
                        if (lxwsy <= (bjsc / delay))
                            lxwsy = 0;
                        YiKit.RunOnUIThread(() =>
                        {
                            if (me.Background != Brushes.Transparent)
                                me.Background = Brushes.Transparent;
                        });
                    }
                }

                log = readLastFile();
                if (log == null)
                {
                    Thread.Sleep(delay);
                    continue;
                }

                if (ids.Count==0)
                {
                    readNames();
                }
                List<string> list =
                    new List<string>(log.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None));
                foreach (string s in list)
                {
                    line = "";
                    try
                    {
                        if (s.Contains("qConnectCB start gid"))
                        {
                            //开始，截取组ID和Cid，和起呼ID
                            gid = YiKit.SubStr(s, "gid:", " ");
                            cid = YiKit.SubStr(s, "channelid", " ");
                            uid = YiKit.SubStr(s, "uid:");
                            sn = ids.ContainsKey(uid) ? ids[uid] : "";
                            if (gcDic.ContainsKey(cid))
                            {
                                gcDic[cid] = gid;
                            }
                            else
                                gcDic.Add(cid, gid);

                            if (!gcLogLine.ContainsKey(cid))
                                gcLogLine.Add(cid, gid + "-" + uid + sn + "," + YiKit.NowD());
                            else
                                gcLogLine[cid] = gid + "-" + uid + sn + "," + YiKit.NowD();
                            if (gcDic.ContainsKey(cid) && gcDic[cid].Equals(pjGid))
                            {
                                faqi = true;
                                if (lxwsy != 0) lxwsy = 0;
                                showTxt(gcLogLine[cid]);
                            }
                        }
                        else if (s.Contains("SendFloorAlert, alertType: 1"))
                        {
                            //讲话
                            cid = YiKit.SubStr(s, "channleid: ", " ");
                            sm = YiKit.SubStr(s, "speekNum: ", " ");

                            sn = ids.ContainsKey(sm)
                                ? ids[sm]
                                : (s.Contains("speakName:") ? YiKit.SubStr(s, "speakName:") : "");

                            if (gcLogLine.ContainsKey(cid))
                                gcLogLine[cid] += "🔊" + sn + "(" + sm + "):" + YiKit.NowT();
                            else gcLogLine.Add(cid, "🔊" + sn + "(" + sm + "):" + YiKit.NowT());
                            if (gcDic.ContainsKey(cid) && gcDic[cid].Equals(pjGid))
                            {
                                if (lxwsy != 0) lxwsy = 0;
                                if (jknumsoundbj != 0) jknumsoundbj = 0;
                                YiKit.RunOnUIThread(() =>
                                {
                                    if (me.Background != Brushes.Transparent)
                                        me.Background = Brushes.Transparent;
                                });
                                faqisp = true;
                                showTxt(gcLogLine[cid]);
                            }
                        }
                        else if (s.Contains("SendFloorAlert, alertType: 0"))
                        {
                            //释放
                            cid = YiKit.SubStr(s, "channleid: ", " ");
                            if (gcLogLine.ContainsKey(cid))
                                gcLogLine[cid] += "-" + YiKit.NowT();
                            else gcLogLine.Add(cid, "-" + YiKit.NowT());
                            if (gcDic.ContainsKey(cid) && gcDic[cid].Equals(pjGid))
                            {
                                faqisp = false;
                                showTxt(gcLogLine[cid]);
                            }
                        }
                        // else if (s.Contains("Change the call channelid:[" ))
                        else if (s.Contains("to be released"))
                        {
                            //结束
                            cid = YiKit.SubStr(s, "channelid: ", " ");
                            if (gcLogLine.ContainsKey(cid))
                                gcLogLine[cid] += " 释放" + YiKit.NowT();
                            else gcLogLine.Add(cid, " 释放" + YiKit.NowT());

                            if (gcDic.ContainsKey(cid) && gcDic[cid].Equals(pjGid))
                            {
                                soundRatio = Math.Round((jknumsound / (jknum * 1f)) * 100.0f, 2);
                                if (jknumyx == 0)
                                {
                                    gcLogLine[cid] +=
                                        "♦音频：" + soundRatio + "%,持续：" + Math.Round(jknum / (1000f / delay), 3) +
                                        "♦有效音频：0" + "%,有效持续0";
                                }
                                else
                                    gcLogLine[cid] +=
                                        "♦音频：" + soundRatio + "%,持续：" + Math.Round(jknum / (1000f / delay), 3) +
                                        "♦有效音频：" +
                                        Math.Round((jknumsoundyx / (jknumyx * 1f)) * 100.0f, 2) +
                                        "%,⏳有效持续" + Math.Round(jknumyx / 5f, 3);

                                if (hllogs.Count > hlnum)
                                {
                                    YiKit.RunOnUIThread(() => { hllogs.RemoveAt(0); });
                                }

                                if (hlelogs.Count > hlnum)
                                {
                                    YiKit.RunOnUIThread(() => { hlelogs.RemoveAt(0); });
                                }

                                YiKit.RunOnUIThread(() => { hllogs.Add(gcLogLine[cid]); });
                                if (errorAuto)
                                {
                                    if (lxwsy > bjsc / delay)
                                    {
                                        errorMsg = gcLogLine[cid] + ",信道占用，无背景音，无人声\r\n";
                                    }
                                    else if (jknumsoundbj > bjsc / delay)
                                    {
                                        errorMsg = gcLogLine[cid] + ",信道占用，有背景音，无人声\r\n";
                                    }
                                    /*else if (jknumyx == 0 ||
                                             Math.Round((jknumsoundyx / (jknumyx * 1f)) * 100.0f, 2) <= 30)
                                    {
                                        errorMsg = gcLogLine[cid] + ",讲话声音短\r\n";
                                    }*/

                                    YiKit.RunOnUIThread(() => { hlelogs.Add(errorMsg); });

                                    errorMsg = errorMsg.Replace("♦", ",").Replace("🔊", "").Replace("⏳", "");
                                    try
                                    {
                                        if (lastErrorFails.Count > 0)
                                        {
                                            YiKit.FileWriteAppend(string.Join("", lastErrorFails),
                                                YiKit.GetAppStartPath() + "\\idswatch\\" + YiKit.NowD() +
                                                "监控派接异常通话.csv");
                                            lastErrorFails.Clear();
                                        }

                                        if (!lastError.Equals(errorMsg))
                                        {
                                            YiKit.FileWriteAppend(errorMsg,
                                                YiKit.GetAppStartPath() + "\\idswatch\\" + YiKit.NowD() +
                                                "监控派接异常通话.csv");
                                            lastError = errorMsg + "";
                                        }


                                        // lastErrors.Add(errorMsg.Replace("♦", ",").Replace("🔊", "").Replace("⏳", ""));
                                    }
                                    catch (Exception e)
                                    {
                                        lastErrorFails.Add(errorMsg);
                                        YiKit.ShowToastError("异常文件写入失败");
                                    }
                                }

                                faqi = false;
                                if (lxwsy != 0) lxwsy = 0;
                                YiKit.RunOnUIThread(() =>
                                {
                                    if (me.Background != Brushes.Transparent)
                                        me.Background = Brushes.Transparent;
                                });
                                jkSoundZero();

                                showTxt(gcLogLine[cid], false);
                                txt.WriteLine(gcLogLine[cid].Replace("♦", ",").Replace("🔊", "").Replace("⏳", ""));
                                txt.Flush();
                                if (lxwsy != 0)
                                {
                                    lxwsy = 0;
                                    YiKit.RunOnUIThread(() =>
                                    {
                                        if (me.Background != Brushes.Transparent)
                                            me.Background = Brushes.Transparent;
                                    });
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        YiKit.ShowToastError(e.Message);
                    }
                }

                Thread.Sleep(delay);
            }

            txt.Close();
        });
    }


    private void showTxt(string txt, bool realTime = true)
    {
        if (realTime)
        {
            YiKit.RunOnUIThread(() => { LogRtTextBlock.Text = txt; });
        }
        else
        {
            // string result = String.Join(Environment.NewLine, hllogs.Select(n => n.ToString()).ToArray());
            YiKit.RunOnUIThread(() =>
            {
                LogRtTextBlock.Text = txt;
                // LogTextBlock.Text = result;
                // scrollViewer.ScrollToEnd();
            });
        }
    }

    private Dictionary<string, string> gcDic = new Dictionary<string, string>();


    /// <summary>
    ///  这个方法受系统监控影响，改成1秒一次
    /// </summary>
    public void startListingLogDir()
    {
        readLastFile();
        // 设置 FileSystemWatcher 来监控文件创建事件
        FileSystemWatcher watcher = new FileSystemWatcher(logPath);
        watcher.Path = @logPath;

        // 只监控文件创建事件
        watcher.NotifyFilter = NotifyFilters.LastWrite;
        watcher.Filter = "*.*"; // 监控所有类型的文件

        // 监视子目录中的更改
        // watcher.IncludeSubdirectories = true;

        // 设置更大的内部缓冲区以防溢出
        watcher.InternalBufferSize = 65536;
        // 注册 Created 事件处理程序
        watcher.Created += ((sender, args) => { logFile = args.FullPath; });
        watcher.Changed += (((sender, args) =>
        {
            logFile = args.FullPath;
            readLastFile();
        }));

        // 启用事件通知
        watcher.EnableRaisingEvents = true;
    }

    private long _lastlen = 0;

    public string ReadFileInReverseUtf8(string filePath, string coding = "utf-8")
    {
        //读文件大小以判断大小变化

        long bufferSize = 3096;

        // 每次读取1KB的数据块
        StringBuilder contentBuilder = new StringBuilder();
        byte[] leftoverBytes = Array.Empty<byte>(); // 用于保存未完成的多字节字符

        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            // var decoder = Encoding.UTF8.GetDecoder();
            var decoder = Encoding.GetEncoding(coding).GetDecoder();


            while (fs.Length == 0)
            {
                Thread.Sleep(100);
            }

            long position = fs.Length;
            bufferSize = (position - _lastlen);

            if (_lastlen==0)
            {
                _lastlen = fs.Length;
                return null;
            }else if (bufferSize > 1024 * 1024)
            {
                bufferSize = 64*1024;
            }
            else if (bufferSize == 0)
            {
                return null;
            }

            _lastlen = fs.Length;
            byte[] buffer = new byte[bufferSize];
            bool runone = true;
            while (position > 0 && runone)
            {
                runone = false;
                long bytesToRead = Math.Min(bufferSize, position);
                position -= bytesToRead;

                fs.Seek(position, SeekOrigin.Begin);
                int bytesRead = fs.Read(buffer, 0, (int)bytesToRead);

                // 将当前块与剩余的字节合并
                if (leftoverBytes.Length > 0)
                {
                    byte[] combined = new byte[bytesRead + leftoverBytes.Length];
                    Array.Copy(leftoverBytes, 0, combined, 0, leftoverBytes.Length);
                    Array.Copy(buffer, 0, combined, leftoverBytes.Length, bytesRead);
                    buffer = combined;
                    bytesRead += leftoverBytes.Length;
                }

                // 确定所需的字符数组大小
                int charBufferSize = decoder.GetCharCount(buffer, 0, bytesRead);
                char[] chars = new char[charBufferSize];

                // 解码字节为字符并添加到结果中（不反转）
                int charsDecoded = decoder.GetChars(buffer, 0, bytesRead, chars, 0);
                contentBuilder.Append(chars, 0, charsDecoded);

                // 确定是否有未解码的字节（即一个多字节字符的一部分）
                leftoverBytes = GetIncompleteUtf8Bytes(buffer, bytesRead);
            }

            // 处理最后一批可能不完整的字符
            if (leftoverBytes.Length > 0)
            {
                decoder.Reset(); // 重置解码器状态
                int finalCharBufferSize = decoder.GetCharCount(leftoverBytes, 0, leftoverBytes.Length);
                char[] finalChars = new char[finalCharBufferSize];
                int finalCharsDecoded = decoder.GetChars(leftoverBytes, 0, leftoverBytes.Length, finalChars, 0);
                contentBuilder.Append(finalChars, 0, finalCharsDecoded);
            }
        }

        // 在所有数据块处理完毕后，对整个字符串进行一次反转
        // string strs = new string(contentBuilder.ToString().ToCharArray());
        /*oldlogc = nowlogc.Clone().ToString();

        List<string> list = new List<string>(strs.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None));

        strs = "";

        list.Reverse();
        foreach (string s in list)
        {
            strs += s+"\r\n";
        }

        nowlogc = strs;*/
// 自动滚动到内容的末尾

        return contentBuilder.ToString();
    }

    private static byte[] GetIncompleteUtf8Bytes(byte[] buffer, int length)
    {
        // UTF-8 字符长度范围：1-4字节
        // 查找最后一个完整字符的起始位置
        for (int i = length - 1; i >= 0; i--)
        {
            // 检查是否是UTF-8起始字节
            if ((buffer[i] & 0xC0) != 0x80)
            {
                // 返回可能不完整的字节
                byte[] incompleteBytes = new byte[length - i - 1];
                Array.Copy(buffer, i + 1, incompleteBytes, 0, length - i - 1);
                return incompleteBytes;
            }
        }

        // 如果所有字节都是延续字节，则返回整个缓冲区的最后部分
        byte[] allContinuationBytes = new byte[length];
        Array.Copy(buffer, buffer.Length - length, allContinuationBytes, 0, length);
        return allContinuationBytes;
    }


    private string ReadFileInReverse()
    {
        StringBuilder reversedContent = new StringBuilder();
        using (FileStream fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (StreamReader sr = new StreamReader(fs, Encoding.UTF8, true, 1024, true))
        {
            string content = sr.ReadToEnd();
            for (int i = content.Length - 1; i >= 0; i--)
            {
                reversedContent.Append(content[i]);
            }
        }

        return reversedContent.ToString();
    }

    private string getNewFile(string dir)
    {
        try
        {
            // 获取指定文件夹的所有文件
            DirectoryInfo directoryInfo = new DirectoryInfo(dir);
            FileInfo[] files = directoryInfo.GetFiles();

            if (files.Length == 0)
            {
                Console.WriteLine("没文件");
                return null;
            }

            // 根据文件的创建时间排序，并获取最新的文件
            FileInfo latestFile = files.OrderByDescending(file => file.CreationTime).FirstOrDefault();
            return latestFile.FullName;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"发生错误: {ex.Message}");
            return null;
        }
    }

    private void SetDirBtn_OnClick(object sender, RoutedEventArgs e)
    {
        using (FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog())
        {
            // 设置初始目录（可选）
            // folderBrowserDialog.SelectedPath = @"C:\Initial\Directory";

            // 显示对话框并检查用户是否点击了“确定”
            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                // 更新文本框内容为所选文件夹路径
                (sender as Button).Content = folderBrowserDialog.SelectedPath;
                YiKit.IniWrite("path", folderBrowserDialog.SelectedPath, area);
                logPath = folderBrowserDialog.SelectedPath + "\\log";
                logFile = getNewFile(logPath);
            }
        }
    }

    private string pjGid = "";

    private void JkTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        pjGid = JkTextBox.Text;
        YiKit.IniWrite("pjgid", pjGid, area);
    }

    private long hlnum = 10;

    private void HlTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        hlnum = long.Parse(HlTextBox.Text);
        YiKit.IniWrite("hlnum", hlnum + "", area);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        // PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// log条目点击
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void LogListBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private ListBoxItem selectLogItem;

    private void ListBoxItem_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        selectLogItem = sender as ListBoxItem;
        if (selectLogItem != null)
        {
            // 将ContextMenu附加到当前选中的ListBoxItem上
            ContextMenu contextMenu = LogListBox.FindResource("ItemContextMenu") as ContextMenu;
            contextMenu.PlacementTarget = selectLogItem;
            contextMenu.IsOpen = true;

            // 阻止事件冒泡到父级控件
            e.Handled = true;
        }
    }

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        MenuItem menuItem = sender as MenuItem;

        if (menuItem != null)
        {
            if (menuItem.Header.Equals("PDT占用信道"))
            {
                selectLogItem.Content += ",PDT占用信道\r\n";
            }
            else if (menuItem.Header.Equals("LTE占用信道"))
            {
                selectLogItem.Content += ",LTE占用信道\r\n";
            }
            else if (menuItem.Header.Equals("PTD与LTE占用信道"))
            {
                selectLogItem.Content += ",PTD与LTE占用信道\r\n";
            }
            else if (menuItem.Header.Equals("无声音"))
            {
                selectLogItem.Content += ",无声音\r\n";
            }
            else if (menuItem.Header.Equals("PDT声音卡顿"))
            {
                selectLogItem.Content += ",PDT声音卡顿\r\n";
            }
            else if (menuItem.Header.Equals("LTE声音卡顿"))
            {
                selectLogItem.Content += ",LTE声音卡顿\r\n";
            }
            else if (menuItem.Header.Equals("LTE声音卡顿"))
            {
                selectLogItem.Content += ",声音卡顿\r\n";
            }
            else if (menuItem.Header.Equals("乱呼叫"))
            {
                selectLogItem.Content += ",乱呼叫\r\n";
            }
            else if (menuItem.Header.Equals("PDT没起来"))
            {
                selectLogItem.Content += ",PDT没起来\r\n";
            }
            else if (menuItem.Header.Equals("LTE未起来"))
            {
                selectLogItem.Content += ",LTE未起来\r\n";
            }
            else if (menuItem.Header.Equals("违规占用信道"))
            {
                selectLogItem.Content += ",违规占用信道\r\n";
            }
            else if (menuItem.Header.Equals("其他"))
            {
                List<string> strs = YiKit.ShowInputDialog("输入异常详情", new[] { "发现的问题" });
                if (strs != null && strs.Count == 1)
                {
                    selectLogItem.Content += "," + (strs[0].Length == 0 ? "其他" : strs[0]) + "\r\n";
                }
            }
            
            YiKit.RunOnUIThread(() =>
            {
                if (!hlelogs.Contains(selectLogItem.Content.ToString()))
                    hlelogs.Add(selectLogItem.Content.ToString());
            });
            try
            {
                YiKit.FileWriteAppend(selectLogItem.Content.ToString().Replace("♦", ",").Replace("🔊", ""),
                    YiKit.GetAppStartPath() + "\\idswatch\\" + YiKit.NowD() + "监控派接异常通话.csv");
            }
            catch (Exception exception)
            {
                YiKit.ShowToastError(exception.Message);
            }
        }
    }

    private bool mouseInLogList = false;
    private bool mouseInLogeList = false;

    private void LogListBox_OnMouseEnter(object sender, MouseEventArgs e)
    {
        mouseInLogList = true;
    }

    private void LogListBox_OnMouseLeave(object sender, MouseEventArgs e)
    {
        mouseInLogList = false;
    }

    private void LogeListBox_OnMouseEnter(object sender, MouseEventArgs e)
    {
        mouseInLogeList = true;
    }

    private void LogeListBox_OnMouseLeave(object sender, MouseEventArgs e)
    {
        mouseInLogeList = false;
    }

    private bool capShow = true;

    private void CapShow_OnChecked(object sender, RoutedEventArgs e)
    {
        capShow = true;
        YiKit.IniWrite("capShow", capShow + "", area);
    }

    private void CapShow_OnUnchecked(object sender, RoutedEventArgs e)
    {
        capShow = false;
        YiKit.IniWrite("capShow", capShow + "", area);
    }

    private bool errorAuto = true;

    private void ErrorAutoCk_OnChecked(object sender, RoutedEventArgs e)
    {
        errorAuto = true;
        YiKit.IniWrite("errorAuto", errorAuto + "", area);
    }

    private void ErrorAutoCk_OnUnchecked(object sender, RoutedEventArgs e)
    {
        errorAuto = false;
        YiKit.IniWrite("errorAuto", errorAuto + "", area);
    }

    /// <summary>
    /// 触发报警的时长5秒，默认
    /// </summary>
    private long bjsc = 5;

    private int xtb = 1560;
    private int ytb = 228;

    private void BjTimeTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            bjsc = long.Parse(BjTimeTextBox.Text) * 1000;
            YiKit.IniWrite("bjsc", bjsc + "", area);
        }
        catch (Exception exception)
        {
            YiKit.ShowToastError(exception.Message);
        }
    }

    private void XTB_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            xtb = int.Parse(XTB.Text) * 1000;
            YiKit.IniWrite("xtb", xtb + "", area);
        }
        catch (Exception exception)
        {
            YiKit.ShowToastError(exception.Message);
        }
    }

    private void YTB_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            ytb = int.Parse(YTB.Text) * 1000;
            YiKit.IniWrite("ytb", ytb + "", area);
        }
        catch (Exception exception)
        {
            YiKit.ShowToastError(exception.Message);
        }
    }

    private void AutoStart_OnUnchecked(object sender, RoutedEventArgs e)
    {
        YiKit.SetAutoStart((Assembly.GetExecutingAssembly().Location), isAuto: false);
        YiKit.IniWrite("autostartpage", "");
    }

    private void AutoStart_OnChecked(object sender, RoutedEventArgs e)
    {
        YiKit.SetAutoStart((Assembly.GetExecutingAssembly().Location), isAuto: true);
        YiKit.IniWrite("autostartpage", this.GetType().ToString());
    }
}