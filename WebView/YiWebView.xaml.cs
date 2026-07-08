using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using YiKits;
using Color = System.Drawing.Color;

namespace holey.WebView
{
    public partial class YiWebView : Window
    {
        public const string TAG = "yiwv";
        public static YiWebView? me;
        public CoreWebView2Environment env1 = null;
        public YiWebView()
        {
            _isBorderless = YiKit.IniGetBool("uiw", area: TAG, def: false);
            AllowsTransparency = !_isBorderless;
            WindowStyle = !AllowsTransparency ? WindowStyle.SingleBorderWindow : WindowStyle.None;
            ResizeMode = !AllowsTransparency ? ResizeMode.CanResizeWithGrip : ResizeMode.CanResize;
            InitializeComponent();
            me = this;

            UiW.Content = _isBorderless ? "▯" : "▮";
            BackgroundColorCombo.Text = YiKit.IniGet("bgc", "yiwv");
            ForegroundColorCombo.Text = YiKit.IniGet("fgc", "yiwv");
            loadSize();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            me = null;
            base.OnClosing(e);
        }

        public void loadSize()
        {
            Left = YiKit.IniGetDouble("mainleft", 500, TAG);
            Top = YiKit.IniGetDouble("maintop", 300, TAG);
            Width = YiKit.IniGetDouble("mainsizew", 750, TAG);
            Height = YiKit.IniGetDouble("mainsizeh", 450, TAG);
            LocationChanged += (s, e) =>
            {
                YiKit.IniWrite("mainleft", Left < 1 ? "0" : Left.ToString(), TAG);
                YiKit.IniWrite("maintop", Top < 1 ? "0" : Top.ToString(), TAG);
            };
            SizeChanged += (s, e) =>
            {
                YiKit.IniWrite("mainsizew", Width.ToString(), TAG);
                YiKit.IniWrite("mainsizeh", Height.ToString(), TAG);
            };
        }


        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            NewTab_Click(null!, null!);
        }

        private async void NewTab_Click(object sender, RoutedEventArgs e)
        {
            var container = new WebViewContainer();
            container.Background = Brushes.Transparent;
            container.BorderThickness = new Thickness(0);
            int indexTab = TabControl.Items.Count + 1;
            var tab = new TabItem
            {
                Header = "标签页",
                Content = container,
                Tag = indexTab.ToString(),
                BorderThickness = new Thickness(0),
                Background = new BrushConverter().ConvertFromString(BackgroundColorStr) as Brush,
                Foreground = new BrushConverter().ConvertFromString(ForegroundColorStr) as Brush,
            };
            tab.MouseWheel += (e, r) =>
            {
                if (tab.Content is not WebViewContainer wc) return;
                var zoomFactor = wc.WebView.ZoomFactor; // 获取当前的缩放比例
                wc.WebView.ZoomFactor = r.Delta switch
                {
                    > 0 => Math.Min(100.0, zoomFactor * 1.1),
                    < 0 => Math.Max(0.001, zoomFactor * 0.9),
                    _ => wc.WebView.ZoomFactor
                };
                wc.IsHeadless = wc.WebView.ZoomFactor < 0.1 ? true : false;


                YiKit.IniWrite(tab.Tag + "zoom", wc.WebView.ZoomFactor.ToString(), TAG);
            };
            // 创建上下文菜单
            var contextMenu = new ContextMenu();
            contextMenu.Background = new BrushConverter().ConvertFromString(BackgroundColorStr) as Brush;
            contextMenu.Foreground = new BrushConverter().ConvertFromString(ForegroundColorStr) as Brush;
            var closeItem = new MenuItem { Header = "关闭标签页", Background = new BrushConverter().ConvertFromString(BackgroundColorStr) as Brush, Foreground = new BrushConverter().ConvertFromString(ForegroundColorStr) as Brush };
            closeItem.Click += (s, ev) =>
            {
                if (TabControl.Items.Contains(tab))
                {
                    TabControl.Items.Remove(tab);
                    
                }
                (tab.Content  as WebViewContainer)?.WebView?.Dispose();
            };

            var refreshItem = new MenuItem { Header = "刷新页面", Background = new BrushConverter().ConvertFromString(BackgroundColorStr) as Brush, Foreground = new BrushConverter().ConvertFromString(ForegroundColorStr) as Brush };
            refreshItem.Click += (s, ev) =>
            {
                try
                {
                    if (tab.Content is WebViewContainer wc && wc.WebView.CoreWebView2 != null)
                    {
                        wc.WebView.CoreWebView2.Reload();
                    }
                }
                catch (Exception ex)
                {
                    YiKit.Print($"刷新失败: {ex.Message}");
                }
            };

            var renameItem = new MenuItem { Header = "重命名标签", Background = new BrushConverter().ConvertFromString(BackgroundColorStr) as Brush, Foreground = new BrushConverter().ConvertFromString(ForegroundColorStr) as Brush };
            renameItem.Click += (s, ev) =>
            {
                // var input = Microsoft.VisualBasic.Interaction.InputBox("输入新标签名：", "重命名", tab.Header.ToString());
                var inputs = YiKit.ShowInputDialog("输入新标签名" + tab.Tag,
                    "重命名" + tab.Tag + ":" + tab.Header.ToString() + "");
                if (inputs != null && inputs.Count > 0)
                {
                    string input = inputs[0];
                    if (!string.IsNullOrWhiteSpace(input))
                        tab.Header = input;
                }
            };

            var setUrlItem = new MenuItem { Header = "设置初始 URL", Background = new BrushConverter().ConvertFromString(BackgroundColorStr) as Brush, Foreground = new BrushConverter().ConvertFromString(ForegroundColorStr) as Brush };
            var resetItem = new MenuItem { Header = "重制计数", Background = new BrushConverter().ConvertFromString(BackgroundColorStr) as Brush, Foreground = new BrushConverter().ConvertFromString(ForegroundColorStr) as Brush };
            resetItem.Click += (s, ev) => { (tab.Content as WebViewContainer )?.wupins.Clear(); };
            setUrlItem.Click += (s, ev) =>
            {
                // var input = Microsoft.VisualBasic.Interaction.InputBox("输入默认导航 URL：", "设定 URL", AddressBox.Text);
                var inputs = YiKit.ShowInputDialog("设置初始" + tab.Tag + " URL", "设置" + tab.Tag + " URL");
                string input = "";
                if (inputs != null)
                {
                    if (inputs.Count > 0)
                        input = inputs[0];
                    if (!string.IsNullOrWhiteSpace(input))
                    {
                        if (tab.Content is WebViewContainer wc)
                        {
                            wc.Navigate(input);
                            // AddressBox.Text = input;
                        }
                    }
                }
            };

            contextMenu.Items.Add(refreshItem);
            contextMenu.Items.Add(renameItem);
            contextMenu.Items.Add(resetItem);
            contextMenu.Items.Add(setUrlItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(closeItem);

            tab.ContextMenu = contextMenu;

            TabControl.Items.Add(tab);
            TabControl.SelectedItem = tab;
            // await container.WebView.EnsureCoreWebView2Async();
            if (AddressBox.Text.Trim().Length > 0)
            {
                container.Navigate(AddressBox.Text.Trim());
            }
            else
            {
                string s = YiKit.IniGet("设置" + tab.Tag + " URL", "iDialog");
                if (YiKit.IsNotNE(s)) (tab.Content as WebViewContainer)?.Navigate(s);
                s = YiKit.IniGet(tab.Tag + "zoom", TAG);
                if (YiKit.IsNotNE(s)) ((tab.Content as WebViewContainer)!).WebView.ZoomFactor = double.Parse(s);
            }
        }

        private void GoToAddress_Click(object sender, RoutedEventArgs e) => NavigateCurrentTab();

        private void AddressBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                NavigateCurrentTab();
        }

        private void NavigateCurrentTab()
        {
            if (GetActiveContainer(out var container, false))
            {
                string url = AddressBox.Text.Trim();
                if (!url.StartsWith("http")) url = "http://" + url;
                container.Navigate(url);
            }
        }

        private async void DownloadWebpage_Click(object sender, RoutedEventArgs e)
        {
            if (!GetActiveContainer(out var container))
            {
                MessageBox.Show("当前无活动标签页。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "选择保存目录",
                CheckPathExists = true,
                FileName = "选择一个文件夹",
                Filter = "文件夹|*.folder", // 伪装成文件选择文件夹
                DefaultExt = ".folder"
            };

            var result = dialog.ShowDialog();
            if (result != true) return;

            // 获取用户选择的文件夹（从路径中去掉“文件名”）
            string folderPath = Path.GetDirectoryName(dialog.FileName);
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                MessageBox.Show("无效的保存路径。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                await container.DownloadFullPageAsync(folderPath);
                MessageBox.Show($"网页已成功下载至：\n{folderPath}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"下载失败：{ex.Message}\n{ex.StackTrace}", "错误", MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenScriptManager_Click(object sender, RoutedEventArgs e)
        {
            if (!GetActiveContainer(out var container)) return;
            var manager = new ScriptManagerWindow(container);
            manager.Owner = this;
            manager.Show();
        }

        private bool GetActiveContainer(out WebViewContainer container, bool showurl = true)
        {
            container = null;
            if (TabControl.SelectedItem is TabItem item && item.Content is WebViewContainer c)
            {
                container = c;
                if (showurl) AddressBox.Text = c.CurrentUrl;
            }

            return container != null;
        }

        public string ForegroundColorStr = "#000000";
        public string BackgroundColorStr = "#ffffff";


        private void BackgroundColorCombo_OnLostFocus(object sender, RoutedEventArgs e)
        {
            BackgroundColorStr = chageCombo(BackgroundColorCombo);
            YiKit.IniWrite("bgc", BackgroundColorStr, "yiwv");
        }

        private void ForegroundColorCombo_OnLostFocus(object sender, RoutedEventArgs e)
        {
            ForegroundColorStr = chageCombo(ForegroundColorCombo);
            YiKit.IniWrite("fgc", ForegroundColorStr, "yiwv");
        }

        private string chageCombo(ComboBox combo)
        {
            var comboStr = combo.Text.Trim();
            if (comboStr.Contains("#"))
            {
                var indexOf = comboStr.IndexOf("#", StringComparison.Ordinal);
                if (indexOf > 0) comboStr = comboStr.Substring(indexOf) + comboStr.Substring(0, indexOf);
            }

            if (!comboStr.StartsWith("#")) combo.Text = "#" + comboStr;
            else combo.Text = comboStr;
            return comboStr;
        }

        private void ToolBarTray_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                else DragMove();
            }
            else if (e is { ChangedButton: MouseButton.Right, ClickCount: 2 })
            {
                var result = MessageBox.Show("是否关闭窗口？", "是否关闭窗口", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes) Close(); // 只有点击“是”才关闭
            }
        }

        private bool _isBorderless = false;

        private void ToggleBorder_Click(object sender, RoutedEventArgs e)
        {
            _isBorderless = !_isBorderless;
            YiKit.IniWrite("uiw", _isBorderless ? "true" : "false", TAG);
            ((sender as Button)!).Content = _isBorderless ? "▯" : "▮";
        }

        private void TopItem_OnClick(object sender, RoutedEventArgs e)
        {
            if (Topmost)
            {
                Topmost = false;
                (sender as Button).Content = "📌";
            }
            else
            {
                Topmost = true;
                (sender as Button).Content = "🔝";
            }
        }
    }

    // ======================== WebView 容器 ========================
    public class WebViewContainer : UserControl
    {
        public static int delay = 550;
        public WebView2 WebView { get; set; }
        public ObservableCollection<ScriptAction> Actions { get; } = new();
        public Dictionary<string, string> Variables { get; } = new();
        public string CurrentUrl => WebView.Source?.ToString() ?? "";

        private bool _isRecording = false;
        private bool _isPlaying = false;
        private bool _isHeadless = false;
        public Dictionary<string,long> wupins = new Dictionary<string, long>();
        public string url = "";
        
        public bool IsHeadless
        {
            get => _isHeadless;
            set
            {
                _isHeadless = value;
                WebView.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public event Action<string> OnStatusChanged;

        public WebViewContainer()
        {
            var grid = new Grid();
            Content = grid;
            WebView = new WebView2();;
            WebView.DefaultBackgroundColor = Color.Transparent;
            grid.Children.Add(WebView);
            InitWebView();
            
            Unloaded += ((sender, args) =>
            {
                // 不在Unloaded事件中释放WebView，避免切换标签页时页面消失
                // WebView会在窗口关闭时通过其他方式释放
            });
        }

        private async Task InitWebView()
        {

                await WebView.EnsureCoreWebView2Async();
            WebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;   
            string css = $@"
                    body, html, * {{
                        background-color: {YiWebView.me.BackgroundColorStr} !important;
                        color: {YiWebView.me.ForegroundColorStr} !important;
                    }}
                ";
            // 注入点击监听用于录制
            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync($@"
                document.addEventListener('click', e => {{
                    const t = e.target;
                    const text = (t.innerText || t.value || '').trim().substring(0, 30).replace(/\s+/g, ' ');
                    const id = t.id ? '#' + t.id : '';
                    const cls = t.className ? '.' + t.className.split(' ')[0] : '';
                    const tag = t.tagName.toLowerCase();
                    const index = Array.from(t.parentNode.children).indexOf(t) + 1;
                    const nth = ':nth-child(' + index + ')';
                    const selector = id || (cls ? cls + nth : tag + nth);
                    window.chrome.webview.postMessage(JSON.stringify({{
                        type: 'record_click',
                        data: {{ text, selector }}
                    }}));
                }});
                
                
            ");
            
            WebView.CoreWebView2.WebMessageReceived += WebMessageReceived;
            WebView.CoreWebView2.ContentLoading += async (s, e) =>
            {
                // 检查 WebViewContainer 是否已被释放
                if (WebView == null || WebView.CoreWebView2 == null)
                    return;
                
                // 此时可以执行 JS 获取当前已加载的 HTML
                string html = await WebView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
                // 返回的是 JSON 字符串，去掉首尾引号
                html = html.Trim('"');
                progressHtml(html);
                // OnStatusChanged?.Invoke($"导航完成: {CurrentUrl}");
                string css = $@"
(function() {{
    const styleId = 'yikits-theme';
    let style = document.getElementById(styleId);
    if (style) style.remove();

document.querySelectorAll('input').forEach(input => {{
    input.maxLength = 1000; 
input.setAttribute('maxlength', 1000);
}});
    style = document.createElement('style');
    style.id = styleId;
    style.type = 'text/css';
    style.textContent = `

    html::-webkit-scrollbar,
    body::-webkit-scrollbar {{
      display: none !important;
    }}
        *, *::before, *::after {{
            background-color: {YiWebView.me.BackgroundColorStr} !important;
            color: {YiWebView.me.ForegroundColorStr} !important;
            border-color: {YiWebView.me.ForegroundColorStr} !important;
        }}
        a, a:link, a:visited, a:hover, a:active {{
            color: {YiWebView.me.ForegroundColorStr} !important;
            text-decoration: underline !important;
        }}
        img, video, canvas, svg {{
            filter: brightness(90%) !important;
        }}
        body, html {{
            background: {YiWebView.me.BackgroundColorStr} !important;
            color: {YiWebView.me.ForegroundColorStr} !important;
      overflow: auto;
      scrollbar-width: none;
      -ms-overflow-style: none;
      margin: 0;
      padding: 0;
        }}
    `;
    document.head.appendChild(style);
    if (document.body.innerText.indexOf(""wml"")>-1){{
        document.body.innerHTML=document.body.innerText
       }}
}})();
";
                WebView.CoreWebView2.ExecuteScriptAsync(css);
            };
            
            Navigate(url);
        }

        public void progressHtml(string html)
        {
            if (YiWebView.me == null)
                return;

            if (wupins == null)
                wupins = new Dictionary<string, long>();

            Dictionary<string, long> rewards;

            if (html.Contains("报时:")||html.Contains("请选择出口:")||html.Contains("[系统]"))
            {
                // 游戏主场景，无需解析
                return;
            }
            else if (html.Contains("战斗已经结束!"))
            {
                // 战斗场景
                rewards = ParseRewards(html,
                    triggerWords: new[] { "捡到", "打到" },
                    valueSeparators: new[] { 'x' });
            }
            else  if (html.Contains("鱼饵-"))
            {
                // 钓鱼或其他场景
                rewards = ParseRewards(html,
                    triggerWords: new[] { "脱掉了海马身上的装备", "掉出", "给出", "捡到", "获得", "吐出", },
                    valueSeparators: new[] { 'x', '*', },true);
            }
            else
            {
                // 钓鱼或其他场景
                rewards = ParseRewards(html,
                    triggerWords: new[] { "脱掉了海马身上的装备", "掉出", "给出", "捡到", "获得", "吐出", },
                    valueSeparators: new[] { 'x', '*', });
            }

            // 累加到总物品
            foreach (var kvp in rewards)
            {
                if (wupins.ContainsKey(kvp.Key))
                    wupins[kvp.Key] += kvp.Value;
                else
                    wupins[kvp.Key] = kvp.Value;
            }
            


            UpdateWebViewDisplay();
        }

        /// <summary>
        /// 从 HTML 文本中解析物品奖励
        /// </summary>
        /// <param name="html">原始 HTML 内容</param>
        /// <param name="triggerWords">触发解析的关键词，如 "捡到"、"打到"、"获得"</param>
        /// <param name="valueSeparators">数量分隔符，如 'x' 或 '*'</param>
        /// <returns>物品名称 → 数量 的字典</returns>
        public Dictionary<string, long> ParseRewards(string html, string[] triggerWords, char[] valueSeparators,bool has_zkh=false)
        {
            // 预处理：将 <br> 标签替换为换行符
            html = System.Text.RegularExpressions.Regex.Replace(html, @"<br\s*/?>", "\n",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            html = System.Text.RegularExpressions.Regex.Replace(html, @"<[^>]+>", "\n"); // 去标签
            html = html.Replace(@"\n", Environment.NewLine);
            html = html.Replace(@"<", Environment.NewLine);
            html = html.Replace(@"\u003C", Environment.NewLine);
            html = html.Replace(@">", Environment.NewLine);

            var rewards = new Dictionary<string, long>();

            if (string.IsNullOrWhiteSpace(html))
                return rewards;

            try
            {
                using (var reader = new StringReader(html))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.Trim();

                        // ✅ 1. 兜底规则：直接匹配 "名称[-+]数字"（如 鱼饵-10）
                        bool lineProcessed = false;
                        string cleanLine = line.Trim();

                        if (!lineProcessed)
                        {
                            var m = Regex.Match(cleanLine, @"-");
                            if (m.Success)
                            {
                                string name = m.Groups[1].Value.Trim();
                                if (name.Length < 10 && long.TryParse(m.Groups[2].Value, out long count))
                                {
                                    if (rewards.ContainsKey(name))
                                        rewards[name] += count;
                                    else
                                        rewards[name] = count;
                                    lineProcessed = true;
                                }
                            }
                        }

                        // ✅ 2. 检查是否有任意触发词（不 break，只标记可解析）
                        bool hasTrigger = false;
                        foreach (var word in triggerWords)
                        {
                            if (line.IndexOf(word) >= 0)
                            {
                                hasTrigger = true;
                                line = line.Substring(line.IndexOf(word)+word.Length);
                                break;
                            }
                        }

                        if (!hasTrigger) continue;
                        string cleanContent=line.Trim();
                        if (has_zkh)
                        {
                            // ✅ 3. 先提取所有 [xxx] 中的物品（无论触发词在哪）
                            var bracketMatches = System.Text.RegularExpressions.Regex.Matches(line, @"\[([^\[\]]+)\]");
                            foreach (Match m in bracketMatches)
                            {
                                string inner = m.Groups[1].Value.Trim();
                                if (inner.Contains("当前")) continue;
                                // 尝试用 x/* 解析 [古玉兔(宠魂)x1]
                                bool parsed = false;
                                foreach (char sep in valueSeparators)
                                {
                                    int sepIdx = inner.LastIndexOf(sep);
                                    if (sepIdx > 0 && sepIdx < inner.Length - 1)
                                    {
                                        string name = inner.Substring(0, sepIdx).Trim();
                                        if (long.TryParse(inner.Substring(sepIdx + 1), out long count))
                                        {
                                            if (rewards.ContainsKey(name))
                                                rewards[name] += count;
                                            else
                                                rewards[name] = count;
                                            parsed = true;
                                            break;
                                        }
                                    }
                                }

                                if (parsed) continue;

                                // 否则视为数量为1
                                if (!string.IsNullOrEmpty(inner))
                                {
                                    if (rewards.ContainsKey(inner))
                                        rewards[inner] += 1;
                                    else
                                        rewards[inner] = 1;
                                }
                            }

                            // ✅ 4. 去除所有 [...] 部分，得到干净文本
                             cleanContent = System.Text.RegularExpressions.Regex.Replace(line, @"\[[^\]]*\]", "")
                                .Trim();
                        }

                        // ✅ 5. 分割物品（逗号、顿号等）
                        var items = cleanContent.Split(new[] { ',', '，', '、', ';', '；' },
                            StringSplitOptions.RemoveEmptyEntries);

                        foreach (var rawItem in items)
                        {
                            string item = rawItem.Trim();
                            if (string.IsNullOrEmpty(item)) continue;
                            
                            foreach (var word in triggerWords)
                            {
                                if (item.IndexOf(word, StringComparison.Ordinal) >= 0)
                                {
                                    item = item.Substring(item.IndexOf(word, StringComparison.Ordinal)+word.Length);
                                }
                            }
                            // 检查是否有分隔符 x/*
                            bool foundSeparator = false;
                            foreach (char sep in valueSeparators)
                            {
                                int sepIndex = item.LastIndexOf(sep);
                                if (sepIndex > 0 && sepIndex < item.Length - 1)
                                {
                                    string name = item.Substring(0, sepIndex).Trim();
                                    if (long.TryParse(item.Substring(sepIndex + 1), out long count))
                                    {
                                        if (rewards.ContainsKey(name))
                                            rewards[name] += count;
                                        else
                                            rewards[name] = count;
                                    }

                                    foundSeparator = true;
                                    break;
                                }
                            }

                            if (foundSeparator) continue;

                            // 尝试解析“名称+数字”格式（如 潜能469547 或 鱼饵-10）
                            for (int i = 0; i < item.Length; i++)
                            {
                                if (char.IsDigit(item[i]) ||
                                    (item[i] == '-' && i + 1 < item.Length && char.IsDigit(item[i + 1])) ||
                                    (item[i] == '+' && i + 1 < item.Length && char.IsDigit(item[i + 1])))
                                {
                                    string name = item.Substring(0, i).Trim();
                                    if (long.TryParse(item.Substring(i), out long count))
                                    {
                                        if (rewards.ContainsKey(name))
                                            rewards[name] += count;
                                        else
                                            rewards[name] = count;
                                    }

                                    break;
                                }
                                else if(i==item.Length-1)
                                {
                                    if (rewards.ContainsKey(item))
                                        rewards[item] += 1;
                                    else
                                        rewards[item] = 1;
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 可选日志
            }

            return rewards;
        }

        public string GetDisplayWupins()
        {
            var wupinsorts=new SortedDictionary<string,long>(wupins);
            // 更新显示
            string? text = wupinsorts.Count == 0
                ? "获得物品"
                : string.Join("\\n", wupinsorts.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
            text = text.Replace("'", "\\'");
            return text;
        }
public string UpdateWebViewDisplay()
{
    string text = GetDisplayWupins();
    string escapedText = text.Replace("'", "\\'");

    string script = $@"
(function() {{
    var elem = document.getElementById('reward');
    if (!elem) {{
        elem = document.createElement('div');
        elem.id = 'reward';
        elem.style.cssText = `
            position: fixed;
            bottom: 10px;
            right: -3px;
            background: rgba(0, 0, 0, 0.5) !important;
            color: gold;
            padding: 2px;
            font-size: 14px;
            z-index: 9999;
            border: 1px solid white;
            font-family: monospace;
            max-width: 80vw;
            box-sizing: border-box;

            /* 核心：隐藏滚动条，但可滚动 */
            overflow-y: auto;
            scrollbar-width: none; /* Firefox: 隐藏滚动条 */
            -ms-overflow-style: none; /* IE/Edge 旧版 */
        `;
        // WebKit 隐藏滚动条（Chrome/Edge/Chromium）
        elem.classList.add('no-scrollbar');

        document.body.appendChild(elem);
    }}

    elem.innerText = '{escapedText}';

    // 异步检测高度并决定是否启用滚动行为
    setTimeout(function() {{
        var clientHeight = window.innerHeight || document.documentElement.clientHeight;
        var targetMaxHeight = clientHeight * 0.6;

        if (elem.scrollHeight > targetMaxHeight) {{
            elem.style.maxHeight = targetMaxHeight + 'px';
            // 已通过 CSS 隐藏滚动条，这里只需确保 overflow-y = auto
        }} else {{
            elem.style.maxHeight = 'none';
            elem.style.overflowY = 'hidden';
        }}
    }}, 10);

    // 动态注入 CSS 样式（隐藏所有 WebKit 滚动条）
    if (!document.head.querySelector('style#hidden-scroll-style')) {{
        var style = document.createElement('style');
        style.id = 'hidden-scroll-style';
        style.textContent = `
            .no-scrollbar::-webkit-scrollbar {{
                display: none !important;
            }}
            .no-scrollbar {{
                -ms-overflow-style: none;
                scrollbar-width: none;
                overflow-y: auto;
            }}
        `;
        document.head.appendChild(style);
    }}

}})();
";

    try
    {
        WebView.CoreWebView2.ExecuteScriptAsync(script);
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine("WebView script error: " + ex.Message);
    }

    return text;
}
  
        private void WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<WebViewContainer.RecordMessage>(e.TryGetWebMessageAsString());
                if (msg?.type == "record_click" && _isRecording)
                {
                    var act = new ClickAction
                    {
                        Name = $"点击 [{msg.data.text}]",
                        Text = msg.data.text, // 使用 Text 字段
                        DelayAfterMs = delay
                    };
                    Actions.Add(act);
                    OnStatusChanged?.Invoke($"录制: {act.Name}");
                }
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke($"录制错误: {ex.Message}");
            }
        }

        public void Navigate(string url)
        {
            if (YiKit.IsNullOrEmpty(url) || !url.Contains("."))
                return;
            this.url = url;
            if (WebView.CoreWebView2 == null)
                return;
            if (!url.StartsWith("http")) url = "http://" + url;
            // 启动带超时的导航
            StartNavigationWithTimeout(url, TimeSpan.FromSeconds(1.5));
        }

        private async void StartNavigationWithTimeout(string url, TimeSpan timeout)
        {
            
            var cts = new CancellationTokenSource(timeout);
            bool success = false;

            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = (s, e) =>
            {
                if (e.IsSuccess)
                {
                    success = true;
                    cts.Cancel(); // 成功则取消重试
                }
            };

            WebView.CoreWebView2.NavigationCompleted += handler;
            try
            {
                WebView.CoreWebView2.Navigate(url);
                while (!success && !cts.IsCancellationRequested)
                {
                    await Task.Delay(100);
                    if (!success)
                    {
                        OnStatusChanged?.Invoke($"重试导航: {url}");
                        WebView.CoreWebView2.Navigate(url);
                    }
                }
            }
            finally
            {
                WebView.CoreWebView2.NavigationCompleted -= handler;
                cts.Dispose();
            }
        }

        public async Task<bool> PlayScriptsAsync(int repeatCount = 1)
        {
            if (_isPlaying || Actions.Count == 0) return false;
            _isPlaying = true;

            // 统计变量
            int totalClicks = 0;
            long totalTimeMs = 0;
            List<long> roundTimes = new();
            double avgDelay = Actions.Where(a => a != null).Average(a => a.DelayAfterMs);

            OnStatusChanged?.Invoke($"▶ 开始执行：共 {repeatCount} 轮，平均每步延迟 {avgDelay:F0}ms");
            Stopwatch sw;
            double roundTime;
            double roundCount;
            for (roundCount = 1; (roundCount <= repeatCount || repeatCount == 0) && _isPlaying; roundCount++)
            {
                sw = Stopwatch.StartNew();

                foreach (var action in Actions)
                {
                    if (!_isPlaying) break;
                    try
                    {
                        await action.Execute(WebView, Variables);
                        totalClicks++;
                        // await Task.Delay(action.DelayAfterMs);
                    }
                    catch (Exception ex)
                    {
                        OnStatusChanged?.Invoke($"💥 错误 [{action.Name}]: {ex.Message}");
                        _isPlaying = false;
                        break;
                    }
                }

                sw.Stop();
                roundTimes.Add(sw.ElapsedMilliseconds);
                long thisRound = sw.ElapsedMilliseconds;
                totalTimeMs += thisRound;
                roundTime = roundTimes.Any() ? roundTimes.Average() : 0;
                OnStatusChanged?.Invoke($@"🔁 第 {roundCount} 轮完成 | 用时 {thisRound}ms
🖱️  总点击次数: {totalClicks}
⏱️  本轮耗时: {thisRound}
⏱️  平均每轮耗时: {roundTime:F0}ms
⏳ 总耗时: {YiKit.SumByteTime(totalTimeMs)}
🔧 平均每步延迟: {avgDelay:F0}ms");
                if (thisRound < 10) _isPlaying = false;
            }

            roundTime = roundTimes.Any() ? roundTimes.Average() : 0;
            if (_isPlaying)
            {
                OnStatusChanged?.Invoke($@"🏁 执行完成！
📊 总轮数: {roundCount}
🖱️  总点击次数: {totalClicks}
⏱️  平均每轮耗时: {roundTime:F0}ms
⏳ 总耗时: {YiKit.SumByteTime(totalTimeMs)}
🔧 平均每步延迟: {avgDelay:F0}ms"
                    .Trim());
            }
            else
            {
                OnStatusChanged?.Invoke($@"⏹ 手动停止执行
📊 总轮数: {roundCount}
🖱️  总点击次数: {totalClicks}
⏱️  平均每轮耗时: {roundTime:F0}ms
⏳ 总耗时: {YiKit.SumByteTime(totalTimeMs)}
🔧 平均每步延迟: {avgDelay:F0}ms"
                );
            }

            _isPlaying = false;
            return true;
        }

        public void StopPlayback()
        {
            _isPlaying = false;
        }

        public void StartRecording()
        {
            if (!_isRecording)
            {
                _isRecording = true;
                OnStatusChanged?.Invoke("开始录制...");
            }
        }

        public void StopRecording()
        {
            if (_isRecording)
            {
                _isRecording = false;
                OnStatusChanged?.Invoke("停止录制");
            }
        }

        public bool IsRecording => _isRecording;
        public bool IsPlaying => _isPlaying;

        private class RecordMessage
        {
            public string type { get; set; }
            public Data data { get; set; }

            public class Data
            {
                public string text { get; set; }
                public string selector { get; set; }
            }
        }

        public class ResourceInfo
        {
            public string Type { get; set; }
            public string Url { get; set; }
            public string SavePath { get; set; }
        }

        public async Task DownloadFullPageAsync(string saveDir)
        {
            if (WebView == null)
                throw new InvalidOperationException("WebView2 未初始化。");

            // 确保目录存在
            Directory.CreateDirectory(saveDir);

            // 1. 获取完整 HTML
            string html = await WebView.ExecuteScriptAsync(@"
        (function() {
            return JSON.stringify(document.documentElement.outerHTML);
        })();");
            html = JsonSerializer.Deserialize<string>(html);

            string indexPath = Path.Combine(saveDir, "index.html");
            File.WriteAllText(indexPath, html, Encoding.UTF8);

            // 2. 提取资源 URL（CSS, JS, 图片）
            string urlsScript = @"
        (function() {
            const resources = [];
            // CSS 文件
            document.querySelectorAll('link[rel=""stylesheet""]').forEach(el => {
                const href = el.href;
                if (href) resources.push({ type: 'css', url: href, savePath: 'css/' + (href.split('/').pop() || 'style.css') });
            });
            // JS 文件
            document.querySelectorAll('script[src]').forEach(el => {
                const src = el.src;
                if (src) resources.push({ type: 'js', url: src, savePath: 'js/' + (src.split('/').pop() || 'script.js') });
            });
            // 图片文件
            document.querySelectorAll('img[src]').forEach(el => {
                const src = el.src;
                if (src) resources.push({ type: 'img', url: src, savePath: 'img/' + (src.split('/').pop() || 'image.png') });
            });
            return JSON.stringify(resources);
        })();
    ";

            string jsonResources = await WebView.ExecuteScriptAsync(urlsScript);
            var resourceList = JsonSerializer.Deserialize<List<ResourceInfo>>(jsonResources);

            // 去重并下载资源
            var downloaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var client = new HttpClient();

            foreach (var res in resourceList)
            {
                if (string.IsNullOrEmpty(res.Url) || downloaded.Contains(res.Url))
                    continue;

                downloaded.Add(res.Url);

                string fullPath = Path.Combine(saveDir, res.SavePath);
                string dir = Path.GetDirectoryName(fullPath);
                Directory.CreateDirectory(dir);

                try
                {
                    byte[] data = await client.GetByteArrayAsync(res.Url);
                    File.WriteAllBytes(fullPath, data);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"下载失败: {res.Url} - {ex.Message}");
                }
            }

            // 3. 使用正则表达式重写 HTML 中的资源路径（替代 HtmlAgilityPack）
            string updatedHtml = html;

            // 重写 <link rel="stylesheet" href="...">
            updatedHtml = Regex.Replace(updatedHtml,
                @"<link\s+[^>]*?rel\s*=\s*""stylesheet""[^>]*?href\s*=\s*""([^""]+)""",
                match =>
                {
                    string url = match.Groups[1].Value;
                    string fileName = Path.GetFileName(new Uri(url).LocalPath);
                    return match.Value.Replace($"href=\"{url}\"", $"href=\"css/{fileName}\"");
                },
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // 重写 <script src="...">
            updatedHtml = Regex.Replace(updatedHtml, @"<script\s+[^>]*?src\s*=\s*""([^""]+)""",
                match =>
                {
                    string url = match.Groups[1].Value;
                    string fileName = Path.GetFileName(new Uri(url).LocalPath);
                    return match.Value.Replace($"src=\"{url}\"", $"src=\"js/{fileName}\"");
                },
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // 重写 <img src="...">
            updatedHtml = Regex.Replace(updatedHtml, @"<img\s+[^>]*?src\s*=\s*""([^""]+)""",
                match =>
                {
                    string url = match.Groups[1].Value;
                    string fileName = Path.GetFileName(new Uri(url).LocalPath);
                    return match.Value.Replace($"src=\"{url}\"", $"src=\"img/{fileName}\"");
                },
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // 4. 保存修改后的 HTML
            File.WriteAllText(indexPath, updatedHtml, Encoding.UTF8);
        }
    }

    // ======================== 脚本动作基类 ========================
    public abstract class ScriptAction
    {
        public int Id { get; set; }
        public string Name { get; set; } = "未命名";
        public int DelayAfterMs { get; set; } = 1000;
        public abstract string Type { get; }
        public abstract Task Execute(WebView2 webView, Dictionary<string, string> variables);
    }

    public class ClickAction : ScriptAction
    {
        public override string Type => "click";
        public string Text { get; set; } = ""; // 显示文本

        public override async Task Execute(WebView2 webView, Dictionary<string, string> variables)
        {
            var targetText = Text;
            if (string.IsNullOrEmpty(targetText)) return;

            var escapedText = targetText.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"");

          

            var ret = await webView.CoreWebView2.ExecuteScriptAsync($@"

                function click(){{
                    const elements = document.querySelectorAll('*');
                    let target = '{escapedText}'.trim();
                    if (document.body.innerText.indexOf('您的点击频度过快，请稍候再继续...')>-1){{
                        target='继续'
                    }}
                    let found = false;
                    for (const el of elements) {{
                        
                        if (el.offsetParent === null || window.getComputedStyle(el).display === 'none') continue;
                        if (el.textContent.trim() === target || el.getAttribute('aria-label') === target ||
                            el.getAttribute('title') === target || el.getAttribute('placeholder') === target) {{
                            el.click();
                            found = true;
                            break;
                        }}
                    }}
                    if (!found) return -1;
                    return found ? 1 : -1;

                }}
                (function() {{
                   
                    if (document.body.innerText.indexOf('您的点击频度过快，请稍候再继续...')>-1){{
                      return -2;
                    setTimeout(() => {{click()}},1000);
                    }}else{{return click()}}
                    
                }})();
            ");

            if (ret == "1")
            {
                await Task.Delay(DelayAfterMs);
            }
            else if (ret == "-2")
            {
                await Task.Delay(1000+DelayAfterMs);
            }
        }
    }

    public class TypeAction : ScriptAction
    {
        public override string Type => "type";
        public string Selector { get; set; } = "";
        public string Text { get; set; } = "";

        private string Resolve(string input, Dictionary<string, string> vars)
        {
            foreach (var kv in vars)
                input = input.Replace($"${{{kv.Key}}}", kv.Value);
            return input;
        }

        public override async Task Execute(WebView2 webView, Dictionary<string, string> variables)
        {
            var text = Resolve(Text, variables);
            var sel = Selector?.Replace("'", "\\'") ?? "";
            var val = text?.Replace("'", "\\'") ?? "";
            await webView.CoreWebView2.ExecuteScriptAsync($@"
                const el = document.querySelector('{sel}');
                if (el) {{
                    el.value = '{val}';
                    el.dispatchEvent(new Event('input', {{ bubbles: true }}));
                    el.dispatchEvent(new Event('change', {{ bubbles: true }}));
                }}
            ");
            await Task.Delay(DelayAfterMs);
        }
    }

    public class WaitAction : ScriptAction
    {
        public override string Type => "wait";
        public string UrlContains { get; set; } = "";
        public string ElementExists { get; set; } = "";
        public string TextExists { get; set; } = ""; // 新增：等待文本出现
        public int TimeoutMs { get; set; } = 10000;

        public override async Task Execute(WebView2 webView, Dictionary<string, string> variables)
        {
            if (!string.IsNullOrEmpty(UrlContains))
            {
                var tcs = new TaskCompletionSource<bool>();
                EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = (s, ev) =>
                {
                    if (webView.Source?.ToString().Contains(UrlContains) == true)
                        tcs.TrySetResult(true);
                };
                webView.NavigationCompleted += handler;
                using (var cts = new CancellationTokenSource(TimeoutMs))
                {
                    cts.Token.Register(() => tcs.TrySetResult(false));
                    await tcs.Task;
                }

                webView.NavigationCompleted -= handler;
            }
            else if (!string.IsNullOrEmpty(ElementExists))
            {
                var script = $@"
                    (function waitLoop() {{
                        if (document.querySelector('{ElementExists?.Replace("'", "\\'") ?? ""}')) return true;
                        else if ((window._waitElTimeout = (window._waitElTimeout || 0) + 1) > {TimeoutMs / 100}) return false;
                        else setTimeout(waitLoop, 100);
                    }})();
                ";
                for (int i = 0; i < TimeoutMs / 100; i++)
                {
                    var result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                    if (result == "true") break;
                    await Task.Delay(100);
                }
            }
            else if (!string.IsNullOrEmpty(TextExists))
            {
                var escaped = TextExists.Replace("\\", "\\\\").Replace("'", "\\'");
                var script = $@"
                    (function waitLoop() {{
                        const text = '{escaped}';
                        for (const el of document.querySelectorAll('*')) {{
                            if (el.offsetParent && el.textContent.trim() === text) return true;
                        }}
                        if ((window._waitTextTimeout = (window._waitTextTimeout || 0) + 1) > {TimeoutMs / 100}) return false;
                        setTimeout(waitLoop, 100);
                    }})();
                ";
                for (int i = 0; i < TimeoutMs / 100; i++)
                {
                    var result = await webView.CoreWebView2.ExecuteScriptAsync(script);
                    if (result == "true") break;
                    await Task.Delay(100);
                }
            }

            await Task.Delay(DelayAfterMs);
        }
    }

    public class VariableAction : ScriptAction
    {
        public override string Type => "variable";
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";

        public override Task Execute(WebView2 webView, Dictionary<string, string> variables)
        {
            variables[Key] = Value;
            return Task.Delay(DelayAfterMs);
        }
    }

    public class NavigateAction : ScriptAction
    {
        public override string Type => "navigate";
        public string Url { get; set; } = "";

        public override async Task Execute(WebView2 webView, Dictionary<string, string> variables)
        {
            var url = Url;
            foreach (var v in variables)
                url = url.Replace($"${{{v.Key}}}", v.Value);
            webView.CoreWebView2.Navigate(url);
            await Task.Delay(DelayAfterMs);
        }
    }

    // ======================== 脚本序列化 ========================
    public static class ScriptSerializer
    {
        public static string Serialize(ObservableCollection<ScriptAction> actions)
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var a in actions)
            {
                var dict = new Dictionary<string, object>
                {
                    ["Id"] = a.Id,
                    ["Name"] = a.Name,
                    ["DelayAfterMs"] = a.DelayAfterMs,
                    ["Type"] = a.Type
                };

                switch (a)
                {
                    case ClickAction ca: dict["Text"] = ca.Text; break;
                    case TypeAction ta:
                        dict["Selector"] = ta.Selector;
                        dict["Text"] = ta.Text;
                        break;
                    case WaitAction wa:
                        if (!string.IsNullOrEmpty(wa.UrlContains)) dict["UrlContains"] = wa.UrlContains;
                        if (!string.IsNullOrEmpty(wa.ElementExists)) dict["ElementExists"] = wa.ElementExists;
                        if (!string.IsNullOrEmpty(wa.TextExists)) dict["TextExists"] = wa.TextExists;
                        dict["TimeoutMs"] = wa.TimeoutMs;
                        break;
                    case VariableAction va:
                        dict["Key"] = va.Key;
                        dict["Value"] = va.Value;
                        break;
                    case NavigateAction na: dict["Url"] = na.Url; break;
                }

                list.Add(dict);
            }

            return JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
        }

        public static ObservableCollection<ScriptAction> Deserialize(string json)
        {
            var list = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json);
            var actions = new ObservableCollection<ScriptAction>();
            int id = 1;

            foreach (var dict in list)
            {
                ScriptAction action = dict["Type"].ToString() switch
                {
                    "click" => new ClickAction { Text = GetString(dict, "Text") },
                    "type" => new TypeAction { Selector = GetString(dict, "Selector"), Text = GetString(dict, "Text") },
                    "wait" => new WaitAction
                    {
                        UrlContains = GetString(dict, "UrlContains"),
                        ElementExists = GetString(dict, "ElementExists"),
                        TextExists = GetString(dict, "TextExists"),
                        TimeoutMs = GetInt(dict, "TimeoutMs", 10000)
                    },
                    "variable" => new VariableAction { Key = GetString(dict, "Key"), Value = GetString(dict, "Value") },
                    "navigate" => new NavigateAction { Url = GetString(dict, "Url") },
                    _ => new ClickAction()
                };

                action.Id = id++;
                action.Name = GetString(dict, "Name") ?? $"动作 {id}";
                action.DelayAfterMs = GetInt(dict, "DelayAfterMs", WebViewContainer.delay);

                actions.Add(action);
            }

            return actions;
        }

        private static string GetString(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) ? (v?.ToString() ?? "") : "";

        private static int GetInt(Dictionary<string, object> d, string key, int def) =>
            d.TryGetValue(key, out var v) && int.TryParse(v?.ToString(), out int i) ? i : def;
    }

    // ======================== 脚本管理窗口 ========================
    public partial class ScriptManagerWindow : Window
    {
        private readonly WebViewContainer _container;
        private readonly ObservableCollection<ScriptAction> _actions;
        private string _scriptFilePath = null;

        public ScriptManagerWindow(WebViewContainer container)
        {
            _container = container;
            _actions = container.Actions;

            InitializeComponent();
            LoadActions();

            // 实时保存监听
            _actions.CollectionChanged += (s, e) => SaveToLastPath();

            _container.OnStatusChanged += s => Dispatcher.Invoke(() => StatusText.Text = s);
        }

        private void InitializeComponent()
        {
            var fcolor = (Brush)new BrushConverter().ConvertFromString(YiWebView.me.ForegroundColorStr);
            var bcolor = (Brush)new BrushConverter().ConvertFromString(YiWebView.me.BackgroundColorStr);

            Title = "脚本管理器";
            Width = 1000;
            Height = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            this.Background = bcolor;
            this.Foreground = fcolor;
            var grid = new Grid();
            ColumnDefinition col1 = new() { Width = new GridLength(2, GridUnitType.Star) };
            ColumnDefinition col2 = new() { Width = GridLength.Auto };
            ColumnDefinition col3 = new() { Width = new GridLength(300) };
            grid.ColumnDefinitions.Add(col1);
            grid.ColumnDefinitions.Add(col2);
            grid.ColumnDefinitions.Add(col3);

            // 左侧：脚本列表（支持双击编辑）
            var lv = new ListView { Name = "ActionList", Margin = new Thickness(5) };
            lv.Background = Background;
            lv.Foreground = Foreground;
            lv.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(""));
            lv.MouseDoubleClick += (s, e) =>
            {
                if (lv.SelectedItem is ScriptAction action)
                    EditAction(action);
            };

            lv.View = new GridView
            {
                Columns =
                {
                    new GridViewColumn { Header = "ID", DisplayMemberBinding = new Binding("Id"), Width = 40, },
                    new GridViewColumn { Header = "名称", DisplayMemberBinding = new Binding("Name"), Width = 150 },
                    new GridViewColumn { Header = "类型", DisplayMemberBinding = new Binding("Type"), Width = 80 },
                    new GridViewColumn
                    {
                        Header = "延迟(ms)", DisplayMemberBinding = new Binding("DelayAfterMs"),
                        Width = 80
                    }
                },
            };
            Grid.SetColumn(lv, 0);
            // 中间按钮
            var btnStack = new StackPanel
                { Margin = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Center, Background = bcolor };
            var addBtn = new Button
                { Content = "添加", Width = 80, Margin = new Thickness(0, 2, 0, 0), Foreground = fcolor };
            addBtn.Click += (s, e) => AddAction();
            var delBtn = new Button
                { Content = "删除", Width = 80, Margin = new Thickness(0, 2, 0, 0), Foreground = fcolor };
            delBtn.Click += (s, e) =>
            {
                if (lv.SelectedItem is ScriptAction a)
                    _actions.Remove(a);
            };
            var upBtn = new Button { Content = "上移", Width = 80, Margin = new Thickness(0, 2, 0, 0) };
            upBtn.Click += (s, e) =>
            {
                var i = lv.SelectedIndex;
                if (i <= 0) return;
                _actions.Move(i, i - 1);
                lv.SelectedIndex = i - 1;
            };
            var downBtn = new Button { Content = "下移", Width = 80, Margin = new Thickness(0, 2, 0, 0) };
            downBtn.Click += (s, e) =>
            {
                int i = lv.SelectedIndex;
                if (i >= _actions.Count - 1 || i < 0) return;
                _actions.Move(i, i + 1);
                lv.SelectedIndex = i + 1;
            };
            btnStack.Children.Add(addBtn);
            btnStack.Children.Add(upBtn);
            btnStack.Children.Add(downBtn);
            btnStack.Children.Add(delBtn);
            Grid.SetColumn(btnStack, 1);

            // 右侧面板
            var rightStack = new StackPanel { Margin = new Thickness(5) };
            rightStack.Children.Add(new TextBlock
                { Text = "控制", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });

            var recordBtn = new Button { Content = "开始录制", Margin = new Thickness(0, 0, 0, 5) };
            recordBtn.Click += (s, e) =>
            {
                if (!_container.IsRecording)
                {
                    _container.StartRecording();
                    recordBtn.Content = "停止录制";
                }
                else
                {
                    _container.StopRecording();
                    recordBtn.Content = "开始录制";
                }
            };

            var playBtn = new Button { Content = "播放脚本", Margin = new Thickness(0, 0, 0, 5) };
            var repBox = new TextBox { Text = "0", Width = 60, VerticalAlignment = VerticalAlignment.Center };
            var delayBox = new TextBox { Text = $"{WebViewContainer.delay}", Width = 60, ToolTip = "默认延迟（毫秒）" };

            var headlessCb = new CheckBox { Content = "隐藏页面", Margin = new Thickness(0, 0, 0, 0), Foreground = fcolor };
            headlessCb.Checked += (s, e) => _container.IsHeadless = true;
            headlessCb.Unchecked += (s, e) => _container.IsHeadless = false;

            playBtn.Click += async (s, e) =>
            {
                if (!_container.IsPlaying)
                {
                    playBtn.Content = "停止播放";
                    int count = int.TryParse(repBox.Text, out int c) ? c : 1;
                    if (count <= 0) count = 0;
                    _container.IsHeadless = headlessCb.IsChecked == true;
                    await _container.PlayScriptsAsync(count);
                    playBtn.Content = "播放脚本";
                }
                else
                {
                    _container.StopPlayback();
                }
            };

            rightStack.Children.Add(new WrapPanel { Children = { new TextBlock { Text = "重复:" }, repBox } });
            rightStack.Children.Add(new WrapPanel { Children = { new TextBlock { Text = "延迟:" }, delayBox } });
            rightStack.Children.Add(headlessCb);
            rightStack.Children.Add(recordBtn);
            rightStack.Children.Add(playBtn);

            rightStack.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 10) });
            rightStack.Children.Add(new TextBlock
                { Text = "文件", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) });

            var saveBtn = new Button { Content = "保存脚本", Margin = new Thickness(0, 0, 0, 5) };
            saveBtn.Click += SaveScript_Click;
            var loadBtn = new Button { Content = "加载脚本", Margin = new Thickness(0, 0, 0, 5) };
            loadBtn.Click += LoadScript_Click;

            rightStack.Children.Add(saveBtn);
            rightStack.Children.Add(loadBtn);

            rightStack.Children.Add(new Separator { Margin = new Thickness(0, 10, 0, 10) });
            StatusText = new TextBlock { Text = "就绪", Foreground = System.Windows.Media.Brushes.Gray, FontSize = 12 };
            rightStack.Children.Add(StatusText);

            Grid.SetColumn(rightStack, 2);

            grid.Children.Add(lv);
            grid.Children.Add(btnStack);
            grid.Children.Add(rightStack);

            Content = grid;
        }

        private TextBlock StatusText;

        private void LoadActions() => ActionList.ItemsSource = _actions;

        private void AddAction() => EditAction(null);

        private void EditAction(ScriptAction original)
        {
            var win = new Window
            {
                Title = original == null ? "添加动作" : "编辑动作",
                Width = 400, Height = 350,
                Owner = this, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            var stack = new StackPanel { Margin = new Thickness(10) };
            var typeCb = new ComboBox();
            typeCb.Items.Add(new { Name = "点击元素", Type = typeof(ClickAction) });
            typeCb.Items.Add(new { Name = "输入文本", Type = typeof(TypeAction) });
            typeCb.Items.Add(new { Name = "等待URL", Type = typeof(WaitAction) });
            typeCb.Items.Add(new { Name = "等待元素", Type = typeof(WaitAction) });
            typeCb.Items.Add(new { Name = "等待文本", Type = typeof(WaitAction) }); // 新增
            typeCb.Items.Add(new { Name = "设置变量", Type = typeof(VariableAction) });
            typeCb.Items.Add(new { Name = "跳转页面", Type = typeof(NavigateAction) });

            if (original != null)
            {
                switch (original)
                {
                    case ClickAction _: typeCb.SelectedIndex = 0; break;
                    case TypeAction _: typeCb.SelectedIndex = 1; break;
                    case WaitAction wa1 when !string.IsNullOrEmpty(wa1.UrlContains): typeCb.SelectedIndex = 2; break;
                    case WaitAction wa1 when !string.IsNullOrEmpty(wa1.UrlContains): typeCb.SelectedIndex = 3; break;
                    case WaitAction wa1 when !string.IsNullOrEmpty(wa1.UrlContains): typeCb.SelectedIndex = 4; break;
                    case VariableAction _: typeCb.SelectedIndex = 5; break;
                    case NavigateAction _: typeCb.SelectedIndex = 6; break;
                }
            }
            else
            {
                typeCb.SelectedIndex = 0;
            }

            var nameTb = new TextBox { Text = original?.Name ?? "新动作", Margin = new Thickness(0, 0, 0, 2) };
            var selTb = new TextBox { Margin = new Thickness(0, 0, 0, 2) };
            var valTb = new TextBox { Margin = new Thickness(0, 0, 0, 2) };
            var delayTb = new TextBox
                { Text = (original?.DelayAfterMs ?? 1000).ToString(), Margin = new Thickness(0, 0, 0, 2) };

            Action update = () =>
            {
                var sel = typeCb.SelectedItem;
                var typeName = ((dynamic)sel)?.Name;
                selTb.Visibility = (typeName.Contains("点击") || typeName.Contains("输入") || typeName == "等待元素")
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                valTb.Visibility = Visibility.Visible;
                valTb.ToolTip = typeName switch
                {
                    "等待URL" => "包含的URL片段",
                    "等待文本" => "要等待出现的文本",
                    "设置变量" => "变量值",
                    "跳转页面" => "目标URL",
                    _ => "输入的文本"
                };
                selTb.ToolTip = typeName switch
                {
                    "输入文本" => "CSS选择器",
                    "等待元素" => "CSS选择器",
                    "点击元素" => "元素显示文本",
                    _ => ""
                };
            };
            typeCb.SelectionChanged += (s, e) => update();
            update();

            if (original is ClickAction ca) selTb.Text = ca.Text;
            if (original is TypeAction ta)
            {
                selTb.Text = ta.Selector;
                valTb.Text = ta.Text;
            }

            if (original is WaitAction wa) valTb.Text = wa.UrlContains ?? wa.ElementExists ?? wa.TextExists;
            if (original is VariableAction va)
            {
                selTb.Text = va.Key;
                valTb.Text = va.Value;
            }

            if (original is NavigateAction na) valTb.Text = na.Url;

            var okBtn = new Button { Content = "确定", IsDefault = true };
            okBtn.Click += (s, e) =>
            {
                ScriptAction action = null;
                var typeName = ((dynamic)typeCb.SelectedItem)?.Name;

                switch (typeName)
                {
                    case "点击元素":
                        action = new ClickAction { Text = selTb.Text };
                        break;
                    case "输入文本":
                        action = new TypeAction { Selector = selTb.Text, Text = valTb.Text };
                        break;
                    case "等待URL":
                        action = new WaitAction { UrlContains = valTb.Text };
                        break;
                    case "等待元素":
                        action = new WaitAction { ElementExists = selTb.Text };
                        break;
                    case "等待文本":
                        action = new WaitAction { TextExists = valTb.Text };
                        break;
                    case "设置变量":
                        action = new VariableAction { Key = selTb.Text, Value = valTb.Text };
                        break;
                    case "跳转页面":
                        action = new NavigateAction { Url = valTb.Text };
                        break;
                }

                if (action != null)
                {
                    action.Name = nameTb.Text;
                    action.DelayAfterMs = int.TryParse(delayTb.Text, out int d) ? d : 1000;

                    if (original != null)
                    {
                        var index = _actions.IndexOf(original);
                        _actions[index] = action;
                    }
                    else
                    {
                        action.Id = _actions.Count + 1;
                        _actions.Add(action);
                    }
                }

                win.Close();
            };

            stack.Children.Add(new Label { Content = "类型：" });
            stack.Children.Add(typeCb);
            stack.Children.Add(new Label { Content = "名称：" });
            stack.Children.Add(nameTb);
            stack.Children.Add(new Label { Content = "参数1：" });
            stack.Children.Add(selTb);
            stack.Children.Add(new Label { Content = "参数2：" });
            stack.Children.Add(valTb);
            stack.Children.Add(new Label { Content = "延迟(ms)：" });
            stack.Children.Add(delayTb);
            stack.Children.Add(okBtn);

            win.Content = stack;
            win.ShowDialog();
        }

        private void SaveScript_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog { Filter = "JSON脚本|*.json" };
            if (sfd.ShowDialog() == true)
            {
                try
                {
                    File.WriteAllText(sfd.FileName, ScriptSerializer.Serialize(_actions), Encoding.UTF8);
                    _scriptFilePath = sfd.FileName;
                    StatusText.Text = "保存成功";
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存失败：" + ex.Message);
                }
            }
        }

        private void LoadScript_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "JSON脚本|*.json" };
            if (ofd.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(ofd.FileName, Encoding.UTF8);
                    var acts = ScriptSerializer.Deserialize(json);
                    _actions.Clear();
                    foreach (var a in acts) _actions.Add(a);
                    _scriptFilePath = ofd.FileName;
                    StatusText.Text = "加载成功";
                }
                catch (Exception ex)
                {
                    MessageBox.Show("加载失败：" + ex.Message);
                }
            }
        }

        private void SaveToLastPath()
        {
            if (_scriptFilePath != null)
            {
                try
                {
                    File.WriteAllText(_scriptFilePath, ScriptSerializer.Serialize(_actions), Encoding.UTF8);
                }
                catch
                {
                    /* 静默失败 */
                }
            }
        }

        private ListView ActionList => Content is Grid g ? (ListView)g.Children[0] : null;
    }
}