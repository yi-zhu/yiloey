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
using System.Windows.Controls.Primitives;
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
        private int _tabSeq;
        private bool _pageThemeEnabled;

        public YiWebView()
        {
            // 不使用 AllowsTransparency，才能运行时立刻切换有框/无框
            _isBorderless = YiKit.IniGetBool("uiw", area: TAG, def: false);
            ApplyWindowChrome(_isBorderless);
            InitializeComponent();
            me = this;

            UiW.Content = _isBorderless ? "▯" : "▮";
            string bg = NormalizeColor(YiKit.IniGet("bgc", TAG, def: "#FFFFFF"), "#FFFFFF");
            string fg = NormalizeColor(YiKit.IniGet("fgc", TAG, def: "#000000"), "#000000");
            BackgroundColorCombo.Text = bg;
            ForegroundColorCombo.Text = fg;
            BackgroundColorStr = bg;
            ForegroundColorStr = fg;

            // 默认开启页面着色（与原先行为一致）
            _pageThemeEnabled = YiKit.IniGetBool("pageTheme", area: TAG, def: true);
            PageThemeToggle.IsChecked = _pageThemeEnabled;

            loadSize();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            foreach (TabItem item in TabControl.Items)
            {
                if (item.Content is WebViewContainer c)
                    c.DisposeWebView();
            }

            me = null;
            base.OnClosing(e);
        }

        public void loadSize()
        {
            Left = YiKit.IniGetDouble("mainleft", 500, TAG);
            Top = YiKit.IniGetDouble("maintop", 300, TAG);
            Width = YiKit.IniGetDouble("mainsizew", 960, TAG);
            Height = YiKit.IniGetDouble("mainsizeh", 560, TAG);
            LocationChanged += (_, _) =>
            {
                YiKit.IniWrite("mainleft", Left < 1 ? "0" : Left.ToString(), TAG);
                YiKit.IniWrite("maintop", Top < 1 ? "0" : Top.ToString(), TAG);
            };
            SizeChanged += (_, _) =>
            {
                YiKit.IniWrite("mainsizew", Width.ToString(), TAG);
                YiKit.IniWrite("mainsizeh", Height.ToString(), TAG);
            };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            NewTab_Click(null!, null!);
        }

        public bool PageThemeEnabled => _pageThemeEnabled;

        public void SetStatus(string text)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    StatusText.Text = string.IsNullOrWhiteSpace(text) ? "就绪" : text;
                });
            }
            catch
            {
                // window may be closing
            }
        }

        private async void NewTab_Click(object sender, RoutedEventArgs e)
        {
            var container = new WebViewContainer();
            container.Background = Brushes.Transparent;
            container.BorderThickness = new Thickness(0);
            container.OnStatusChanged += SetStatus;

            int indexTab = ++_tabSeq;
            var titleBlock = new TextBlock
            {
                Text = "新标签",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 140
            };

            var closeBtn = new Button
            {
                Content = "×",
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                FontSize = 14,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "关闭标签页"
            };

            var header = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(closeBtn, Dock.Right);
            header.Children.Add(closeBtn);
            header.Children.Add(titleBlock);

            var tab = new TabItem
            {
                Header = header,
                Content = container,
                Tag = indexTab.ToString(),
                BorderThickness = new Thickness(0)
            };

            closeBtn.Click += (_, _) => CloseTab(tab);

            tab.MouseWheel += (_, r) =>
            {
                if (tab.Content is not WebViewContainer wc) return;
                var zoomFactor = wc.WebView.ZoomFactor;
                wc.WebView.ZoomFactor = r.Delta switch
                {
                    > 0 => Math.Min(100.0, zoomFactor * 1.1),
                    < 0 => Math.Max(0.001, zoomFactor * 0.9),
                    _ => wc.WebView.ZoomFactor
                };
                wc.IsHeadless = wc.WebView.ZoomFactor < 0.1;
                YiKit.IniWrite(tab.Tag + "zoom", wc.WebView.ZoomFactor.ToString(), TAG);
            };

            var contextMenu = BuildTabContextMenu(tab, titleBlock);
            tab.ContextMenu = contextMenu;
            titleBlock.ContextMenu = contextMenu;

            container.TitleChanged += title =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrWhiteSpace(title))
                        titleBlock.Text = title.Length > 24 ? title.Substring(0, 24) + "…" : title;
                });
            };
            container.UrlChanged += url =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (ReferenceEquals(TabControl.SelectedItem, tab))
                        AddressBox.Text = url ?? "";
                    UpdateNavButtons();
                });
            };

            TabControl.Items.Add(tab);
            TabControl.SelectedItem = tab;

            string zoom = YiKit.IniGet(tab.Tag + "zoom", TAG);
            if (YiKit.IsNotNE(zoom) && double.TryParse(zoom, out double z))
                container.WebView.ZoomFactor = z;

            if (AddressBox.Text.Trim().Length > 0)
                container.Navigate(AddressBox.Text.Trim());
            else
            {
                string s = YiKit.IniGet("设置" + tab.Tag + " URL", "iDialog");
                if (YiKit.IsNotNE(s))
                    container.Navigate(s);
            }

            await Task.CompletedTask;
            UpdateNavButtons();
        }

        private ContextMenu BuildTabContextMenu(TabItem tab, TextBlock titleBlock)
        {
            var contextMenu = new ContextMenu();
            var refreshItem = new MenuItem { Header = "刷新页面" };
            refreshItem.Click += (_, _) =>
            {
                try
                {
                    if (tab.Content is WebViewContainer wc && wc.WebView.CoreWebView2 != null)
                        wc.WebView.CoreWebView2.Reload();
                }
                catch (Exception ex)
                {
                    YiKit.Print($"刷新失败: {ex.Message}");
                }
            };

            var renameItem = new MenuItem { Header = "重命名标签" };
            renameItem.Click += (_, _) =>
            {
                var inputs = YiKit.ShowInputDialog("输入新标签名" + tab.Tag,
                    "重命名" + tab.Tag + ":" + titleBlock.Text);
                if (inputs != null && inputs.Count > 0 && !string.IsNullOrWhiteSpace(inputs[0]))
                    titleBlock.Text = inputs[0];
            };

            var resetItem = new MenuItem { Header = "重置计数" };
            resetItem.Click += (_, _) =>
            {
                (tab.Content as WebViewContainer)?.wupins.Clear();
                (tab.Content as WebViewContainer)?.UpdateWebViewDisplay();
            };

            var setUrlItem = new MenuItem { Header = "设置初始 URL" };
            setUrlItem.Click += (_, _) =>
            {
                var inputs = YiKit.ShowInputDialog("设置初始" + tab.Tag + " URL", "设置" + tab.Tag + " URL");
                if (inputs == null || inputs.Count == 0 || string.IsNullOrWhiteSpace(inputs[0])) return;
                if (tab.Content is WebViewContainer wc)
                    wc.Navigate(inputs[0]);
            };

            var closeItem = new MenuItem { Header = "关闭标签页" };
            closeItem.Click += (_, _) => CloseTab(tab);

            contextMenu.Items.Add(refreshItem);
            contextMenu.Items.Add(renameItem);
            contextMenu.Items.Add(resetItem);
            contextMenu.Items.Add(setUrlItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(closeItem);
            return contextMenu;
        }

        private void CloseTab(TabItem tab)
        {
            if (tab.Content is WebViewContainer wc)
            {
                wc.OnStatusChanged -= SetStatus;
                wc.DisposeWebView();
            }

            int idx = TabControl.Items.IndexOf(tab);
            TabControl.Items.Remove(tab);

            if (TabControl.Items.Count == 0)
            {
                NewTab_Click(null!, null!);
                return;
            }

            if (idx >= TabControl.Items.Count) idx = TabControl.Items.Count - 1;
            if (idx >= 0) TabControl.SelectedIndex = idx;
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GetActiveContainer(out var container, true))
                UpdateNavButtons();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (GetActiveContainer(out var c, false) && c.WebView.CoreWebView2?.CanGoBack == true)
                c.WebView.CoreWebView2.GoBack();
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (GetActiveContainer(out var c, false) && c.WebView.CoreWebView2?.CanGoForward == true)
                c.WebView.CoreWebView2.GoForward();
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            if (GetActiveContainer(out var c, false) && c.WebView.CoreWebView2 != null)
                c.WebView.CoreWebView2.Reload();
        }

        private void UpdateNavButtons()
        {
            bool canBack = false, canForward = false;
            if (GetActiveContainer(out var c, false) && c.WebView.CoreWebView2 != null)
            {
                canBack = c.WebView.CoreWebView2.CanGoBack;
                canForward = c.WebView.CoreWebView2.CanGoForward;
            }

            BackBtn.IsEnabled = canBack;
            ForwardBtn.IsEnabled = canForward;
        }

        private void GoToAddress_Click(object sender, RoutedEventArgs e) => NavigateCurrentTab();

        private void AddressBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NavigateCurrentTab();
                e.Handled = true;
            }
        }

        private void NavigateCurrentTab()
        {
            if (!GetActiveContainer(out var container, false)) return;
            string url = AddressBox.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;
            container.Navigate(url);
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
                FileName = "webpage",
                Filter = "文件夹|*.folder",
                DefaultExt = ".folder"
            };

            if (dialog.ShowDialog() != true) return;

            string folderPath = Path.GetDirectoryName(dialog.FileName);
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                MessageBox.Show("无效的保存路径。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string target = Path.Combine(folderPath, "webpage_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            try
            {
                SetStatus("正在下载网页...");
                await container.DownloadFullPageAsync(target);
                SetStatus("下载完成: " + target);
                MessageBox.Show($"网页已成功下载至：\n{target}", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetStatus("下载失败");
                MessageBox.Show($"下载失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
            container = null!;
            if (TabControl.SelectedItem is TabItem item && item.Content is WebViewContainer c)
            {
                container = c;
                if (showurl && !string.IsNullOrEmpty(c.CurrentUrl))
                    AddressBox.Text = c.CurrentUrl;
                return true;
            }

            return false;
        }

        public string ForegroundColorStr = "#1A1A1A";
        public string BackgroundColorStr = "#FFFFFF";

        private void BackgroundColorCombo_OnLostFocus(object sender, RoutedEventArgs e)
        {
            BackgroundColorStr = NormalizeColor(chageCombo(BackgroundColorCombo), "#FFFFFF");
            BackgroundColorCombo.Text = BackgroundColorStr;
            YiKit.IniWrite("bgc", BackgroundColorStr, TAG);
            // Window.Background 已绑定 Combo.Text；同步刷新所有页着色
            RefreshAllPageThemes();
        }

        private void ForegroundColorCombo_OnLostFocus(object sender, RoutedEventArgs e)
        {
            ForegroundColorStr = NormalizeColor(chageCombo(ForegroundColorCombo), "#000000");
            ForegroundColorCombo.Text = ForegroundColorStr;
            YiKit.IniWrite("fgc", ForegroundColorStr, TAG);
            RefreshAllPageThemes();
        }

        private void PageThemeToggle_Changed(object sender, RoutedEventArgs e)
        {
            _pageThemeEnabled = PageThemeToggle.IsChecked == true;
            YiKit.IniWrite("pageTheme", _pageThemeEnabled ? "true" : "false", TAG);
            RefreshAllPageThemes();
            SetStatus(_pageThemeEnabled ? "页面着色已开启" : "页面着色已关闭");
        }

        private void RefreshAllPageThemes()
        {
            foreach (TabItem item in TabControl.Items)
            {
                if (item.Content is WebViewContainer c)
                    _ = c.ApplyPageThemeAsync();
            }
        }

        private static string NormalizeColor(string? color, string fallback)
        {
            if (string.IsNullOrWhiteSpace(color)) return fallback;
            color = color.Trim();
            if (!color.StartsWith("#")) color = "#" + color;
            if (color.Length != 4 && color.Length != 7 && color.Length != 9) return fallback;
            return color;
        }

        private string chageCombo(ComboBox combo)
        {
            var comboStr = (combo.Text ?? "").Trim();
            if (comboStr.Contains("#"))
            {
                var indexOf = comboStr.IndexOf("#", StringComparison.Ordinal);
                if (indexOf > 0) comboStr = comboStr.Substring(indexOf) + comboStr.Substring(0, indexOf);
            }

            if (!comboStr.StartsWith("#")) combo.Text = "#" + comboStr;
            else combo.Text = comboStr;
            return combo.Text;
        }

        private void ToolBarTray_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is TextBox or ComboBox or ButtonBase)
                return;

            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                else
                {
                    try { DragMove(); } catch { /* ignored */ }
                }
            }
            else if (e is { ChangedButton: MouseButton.Right, ClickCount: 2 })
            {
                var result = MessageBox.Show("是否关闭窗口？", "是否关闭窗口", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes) Close();
            }
        }

        private bool _isBorderless;

        private void ApplyWindowChrome(bool borderless)
        {
            WindowStyle = borderless ? WindowStyle.None : WindowStyle.SingleBorderWindow;
            ResizeMode = borderless ? ResizeMode.CanResize : ResizeMode.CanResizeWithGrip;
        }

        private void ToggleBorder_Click(object sender, RoutedEventArgs e)
        {
            _isBorderless = !_isBorderless;
            YiKit.IniWrite("uiw", _isBorderless ? "true" : "false", TAG);
            ApplyWindowChrome(_isBorderless);
            UiW.Content = _isBorderless ? "▯" : "▮";
            SetStatus(_isBorderless ? "已切换为无边框" : "已切换为有边框");
        }

        private void TopItem_OnClick(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            PinBtn.Content = Topmost ? "🔝" : "📌";
            SetStatus(Topmost ? "已置顶" : "已取消置顶");
        }
    }

    // ======================== WebView 容器 ========================
    public class WebViewContainer : UserControl
    {
        public static int delay = 550;
        public WebView2 WebView { get; set; }
        public ObservableCollection<ScriptAction> Actions { get; } = new();
        public Dictionary<string, string> Variables { get; } = new();
        public string CurrentUrl => WebView?.Source?.ToString() ?? _pendingUrl ?? "";

        private bool _isRecording;
        private bool _isPlaying;
        private bool _isHeadless;
        private string? _pendingUrl;
        private bool _disposed;
        public Dictionary<string, long> wupins = new();
        public string url = "";

        public event Action<string>? TitleChanged;
        public event Action<string>? UrlChanged;

        public bool IsHeadless
        {
            get => _isHeadless;
            set
            {
                _isHeadless = value;
                if (WebView != null)
                    WebView.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public event Action<string>? OnStatusChanged;

        public WebViewContainer()
        {
            var grid = new Grid();
            Content = grid;
            WebView = new WebView2();
            WebView.DefaultBackgroundColor = Color.Transparent;
            grid.Children.Add(WebView);
            _ = InitWebView();
        }

        public void DisposeWebView()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                WebView?.Dispose();
            }
            catch
            {
                // ignore
            }
        }

        private async Task InitWebView()
        {
            try
            {
                await WebView.EnsureCoreWebView2Async();
                if (_disposed || WebView.CoreWebView2 == null) return;

                WebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                document.addEventListener('click', e => {
                    const t = e.target;
                    if (!t) return;
                    const text = (t.innerText || t.value || '').trim().substring(0, 30).replace(/\s+/g, ' ');
                    const id = t.id ? '#' + t.id : '';
                    const cls = t.className && typeof t.className === 'string' ? '.' + t.className.split(' ')[0] : '';
                    const tag = t.tagName.toLowerCase();
                    const index = t.parentNode ? (Array.from(t.parentNode.children).indexOf(t) + 1) : 1;
                    const nth = ':nth-child(' + index + ')';
                    const selector = id || (cls ? cls + nth : tag + nth);
                    window.chrome.webview.postMessage(JSON.stringify({
                        type: 'record_click',
                        data: { text, selector }
                    }));
                });
            ");

                WebView.CoreWebView2.WebMessageReceived += WebMessageReceived;
                WebView.CoreWebView2.DocumentTitleChanged += (_, _) =>
                {
                    try { TitleChanged?.Invoke(WebView.CoreWebView2.DocumentTitle); }
                    catch { /* ignore */ }
                };
                WebView.CoreWebView2.SourceChanged += (_, _) =>
                {
                    try
                    {
                        string src = WebView.Source?.ToString() ?? "";
                        UrlChanged?.Invoke(src);
                    }
                    catch { /* ignore */ }
                };
                WebView.CoreWebView2.NavigationCompleted += async (_, e) =>
                {
                    if (_disposed || WebView?.CoreWebView2 == null) return;
                    try
                    {
                        if (e.IsSuccess)
                        {
                            string html = await WebView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
                            html = UnwrapJsString(html);
                            progressHtml(html);
                            await ApplyPageThemeAsync();
                            UpdateWebViewDisplay();
                            OnStatusChanged?.Invoke("已加载");
                        }
                        else
                        {
                            OnStatusChanged?.Invoke("加载失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        OnStatusChanged?.Invoke("页面处理异常: " + ex.Message);
                    }
                };

                if (!string.IsNullOrWhiteSpace(_pendingUrl))
                    Navigate(_pendingUrl);
                else if (!string.IsNullOrWhiteSpace(url))
                    Navigate(url);
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke("WebView 初始化失败: " + ex.Message);
            }
        }

        public async Task ApplyPageThemeAsync()
        {
            if (_disposed || WebView?.CoreWebView2 == null) return;
            var host = YiWebView.me;
            if (host == null || !host.PageThemeEnabled)
            {
                try
                {
                    await WebView.CoreWebView2.ExecuteScriptAsync(@"
(function(){
  var s=document.getElementById('yikits-theme');
  if(s) s.remove();
})();");
                }
                catch { /* ignore */ }
                return;
            }

            // 同步最新配色（绑定可能刚改 Combo.Text）
            string bg = NormalizeThemeColor(host.BackgroundColorCombo?.Text, host.BackgroundColorStr);
            string fg = NormalizeThemeColor(host.ForegroundColorCombo?.Text, host.ForegroundColorStr);
            host.BackgroundColorStr = bg;
            host.ForegroundColorStr = fg;

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
        html::-webkit-scrollbar, body::-webkit-scrollbar {{ display: none !important; }}
        *, *::before, *::after {{
            background-color: {bg} !important;
            color: {fg} !important;
            border-color: {fg} !important;
        }}
        a, a:link, a:visited, a:hover, a:active {{
            color: {fg} !important;
            text-decoration: underline !important;
        }}
        img, video, canvas, svg {{
            filter: brightness(90%) !important;
        }}
        body, html {{
            background: {bg} !important;
            color: {fg} !important;
            overflow: auto;
            scrollbar-width: none;
            -ms-overflow-style: none;
            margin: 0;
            padding: 0;
        }}
    `;
    (document.head || document.documentElement).appendChild(style);
    if (document.body && document.body.innerText.indexOf('wml') > -1) {{
        document.body.innerHTML = document.body.innerText;
    }}
}})();";
            try
            {
                await WebView.CoreWebView2.ExecuteScriptAsync(css);
            }
            catch
            {
                // ignore
            }
        }

        private static string NormalizeThemeColor(string? fromCombo, string fallback)
        {
            string c = (fromCombo ?? fallback ?? "#FFFFFF").Trim();
            if (c.Length == 0) c = fallback;
            if (!c.StartsWith("#")) c = "#" + c;
            int hash = c.IndexOf('#');
            if (hash > 0) c = c.Substring(hash);
            return c.Length is 4 or 7 or 9 ? c : (fallback.StartsWith("#") ? fallback : "#FFFFFF");
        }

        public void progressHtml(string html)
        {
            if (YiWebView.me == null) return;
            wupins ??= new Dictionary<string, long>();

            Dictionary<string, long> rewards;
            if (html.Contains("报时:") || html.Contains("请选择出口:") || html.Contains("[系统]"))
                return;

            if (html.Contains("战斗已经结束!"))
            {
                rewards = ParseRewards(html,
                    triggerWords: new[] { "捡到", "打到" },
                    valueSeparators: new[] { 'x' });
            }
            else if (html.Contains("鱼饵-"))
            {
                rewards = ParseRewards(html,
                    triggerWords: new[] { "脱掉了海马身上的装备", "掉出", "给出", "捡到", "获得", "吐出" },
                    valueSeparators: new[] { 'x', '*' }, true);
            }
            else
            {
                rewards = ParseRewards(html,
                    triggerWords: new[] { "脱掉了海马身上的装备", "掉出", "给出", "捡到", "获得", "吐出" },
                    valueSeparators: new[] { 'x', '*' });
            }

            foreach (var kvp in rewards)
            {
                if (wupins.ContainsKey(kvp.Key))
                    wupins[kvp.Key] += kvp.Value;
                else
                    wupins[kvp.Key] = kvp.Value;
            }

            UpdateWebViewDisplay();
        }

        public Dictionary<string, long> ParseRewards(string html, string[] triggerWords, char[] valueSeparators,
            bool has_zkh = false)
        {
            html = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, @"<[^>]+>", "\n");
            html = html.Replace(@"\n", Environment.NewLine);
            html = html.Replace(@"<", Environment.NewLine);
            html = html.Replace(@"\u003C", Environment.NewLine);
            html = html.Replace(@">", Environment.NewLine);

            var rewards = new Dictionary<string, long>();
            if (string.IsNullOrWhiteSpace(html))
                return rewards;

            try
            {
                using var reader = new StringReader(html);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;

                    // 兜底：名称[+/-]数字，如 鱼饵-10 / 潜能+100
                    var fallback = Regex.Match(line, @"^([\u4e00-\u9fa5A-Za-z0-9_（）()·\-]+?)\s*([+\-])\s*(\d+)\s*$");
                    if (fallback.Success)
                    {
                        string name = fallback.Groups[1].Value.Trim();
                        string sign = fallback.Groups[2].Value;
                        if (name.Length is > 0 and < 20 && long.TryParse(fallback.Groups[3].Value, out long count))
                        {
                            if (sign == "-") count = -count;
                            if (rewards.ContainsKey(name)) rewards[name] += count;
                            else rewards[name] = count;
                            continue;
                        }
                    }

                    bool hasTrigger = false;
                    foreach (var word in triggerWords)
                    {
                        int idx = line.IndexOf(word, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            hasTrigger = true;
                            line = line.Substring(idx + word.Length);
                            break;
                        }
                    }

                    if (!hasTrigger) continue;

                    string cleanContent = line.Trim();
                    if (has_zkh)
                    {
                        var bracketMatches = Regex.Matches(line, @"\[([^\[\]]+)\]");
                        foreach (Match m in bracketMatches)
                        {
                            string inner = m.Groups[1].Value.Trim();
                            if (inner.Contains("当前")) continue;
                            bool parsed = false;
                            foreach (char sep in valueSeparators)
                            {
                                int sepIdx = inner.LastIndexOf(sep);
                                if (sepIdx > 0 && sepIdx < inner.Length - 1 &&
                                    long.TryParse(inner.Substring(sepIdx + 1), out long count))
                                {
                                    string name = inner.Substring(0, sepIdx).Trim();
                                    if (rewards.ContainsKey(name)) rewards[name] += count;
                                    else rewards[name] = count;
                                    parsed = true;
                                    break;
                                }
                            }

                            if (parsed) continue;
                            if (string.IsNullOrEmpty(inner)) continue;
                            if (rewards.ContainsKey(inner)) rewards[inner] += 1;
                            else rewards[inner] = 1;
                        }

                        cleanContent = Regex.Replace(line, @"\[[^\]]*\]", "").Trim();
                    }

                    var items = cleanContent.Split(new[] { ',', '，', '、', ';', '；' },
                        StringSplitOptions.RemoveEmptyEntries);

                    foreach (var rawItem in items)
                    {
                        string item = rawItem.Trim();
                        if (string.IsNullOrEmpty(item)) continue;

                        foreach (var word in triggerWords)
                        {
                            int widx = item.IndexOf(word, StringComparison.Ordinal);
                            if (widx >= 0)
                                item = item.Substring(widx + word.Length);
                        }

                        bool foundSeparator = false;
                        foreach (char sep in valueSeparators)
                        {
                            int sepIndex = item.LastIndexOf(sep);
                            if (sepIndex > 0 && sepIndex < item.Length - 1 &&
                                long.TryParse(item.Substring(sepIndex + 1), out long count))
                            {
                                string name = item.Substring(0, sepIndex).Trim();
                                if (rewards.ContainsKey(name)) rewards[name] += count;
                                else rewards[name] = count;
                                foundSeparator = true;
                                break;
                            }
                        }

                        if (foundSeparator) continue;

                        for (int i = 0; i < item.Length; i++)
                        {
                            if (char.IsDigit(item[i]) ||
                                (item[i] is '-' or '+' && i + 1 < item.Length && char.IsDigit(item[i + 1])))
                            {
                                string name = item.Substring(0, i).Trim();
                                if (long.TryParse(item.Substring(i), out long count))
                                {
                                    if (string.IsNullOrEmpty(name)) break;
                                    if (rewards.ContainsKey(name)) rewards[name] += count;
                                    else rewards[name] = count;
                                }

                                break;
                            }

                            if (i == item.Length - 1)
                            {
                                if (rewards.ContainsKey(item)) rewards[item] += 1;
                                else rewards[item] = 1;
                            }
                        }
                    }
                }
            }
            catch
            {
                // ignore parse errors
            }

            return rewards;
        }

        public string GetDisplayWupins()
        {
            var wupinsorts = new SortedDictionary<string, long>(wupins);
            return wupinsorts.Count == 0
                ? "获得物品"
                : string.Join("\\n", wupinsorts.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
        }

        public string UpdateWebViewDisplay()
        {
            string text = GetDisplayWupins();
            string escapedText = text.Replace("\\", "\\\\").Replace("'", "\\'");

            string script = $@"
(function() {{
    var elem = document.getElementById('reward');
    if (!elem) {{
        elem = document.createElement('div');
        elem.id = 'reward';
        elem.style.cssText = `
            position: fixed;
            bottom: 10px;
            right: 8px;
            background: rgba(20, 20, 24, 0.78) !important;
            color: #F5D76E;
            padding: 8px 10px;
            font-size: 13px;
            z-index: 9999;
            border: 1px solid rgba(255,255,255,0.25);
            border-radius: 8px;
            font-family: Consolas, monospace;
            max-width: 40vw;
            box-sizing: border-box;
            overflow-y: auto;
            scrollbar-width: none;
            -ms-overflow-style: none;
            white-space: pre-line;
            pointer-events: none;
        `;
        elem.classList.add('no-scrollbar');
        document.body.appendChild(elem);
    }}
    elem.innerText = '{escapedText}';
    setTimeout(function() {{
        var clientHeight = window.innerHeight || document.documentElement.clientHeight;
        var targetMaxHeight = clientHeight * 0.6;
        if (elem.scrollHeight > targetMaxHeight) {{
            elem.style.maxHeight = targetMaxHeight + 'px';
        }} else {{
            elem.style.maxHeight = 'none';
            elem.style.overflowY = 'hidden';
        }}
    }}, 10);
    if (!document.head.querySelector('style#hidden-scroll-style')) {{
        var style = document.createElement('style');
        style.id = 'hidden-scroll-style';
        style.textContent = `
            .no-scrollbar::-webkit-scrollbar {{ display: none !important; }}
            .no-scrollbar {{ -ms-overflow-style: none; scrollbar-width: none; overflow-y: auto; }}
        `;
        document.head.appendChild(style);
    }}
}})();
";
            try
            {
                if (!_disposed && WebView?.CoreWebView2 != null)
                    _ = WebView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("WebView script error: " + ex.Message);
            }

            return text;
        }

        private void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var msg = JsonSerializer.Deserialize<RecordMessage>(e.TryGetWebMessageAsString());
                if (msg?.type == "record_click" && _isRecording)
                {
                    var act = new ClickAction
                    {
                        Name = $"点击 [{msg.data?.text}]",
                        Text = msg.data?.text ?? "",
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

        public void Navigate(string rawUrl)
        {
            if (YiKit.IsNullOrEmpty(rawUrl)) return;
            string navUrl = rawUrl.Trim();
            if (!navUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !navUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !navUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
                !navUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                navUrl = "http://" + navUrl;
            }

            // 允许 localhost / IP / 域名
            if (!IsLikelyUrl(navUrl))
            {
                OnStatusChanged?.Invoke("无效地址");
                return;
            }

            url = navUrl;
            _pendingUrl = navUrl;
            if (_disposed) return;
            if (WebView.CoreWebView2 == null)
            {
                OnStatusChanged?.Invoke("正在初始化浏览器...");
                return;
            }

            try
            {
                OnStatusChanged?.Invoke("正在打开: " + navUrl);
                WebView.CoreWebView2.Navigate(navUrl);
            }
            catch (Exception ex)
            {
                OnStatusChanged?.Invoke("导航失败: " + ex.Message);
            }
        }

        private static bool IsLikelyUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme is "http" or "https" or "file" or "about")
                    return !string.IsNullOrWhiteSpace(uri.Host) || uri.Scheme == "about" || uri.Scheme == "file";
            }

            return url.Contains('.') ||
                   url.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public async Task<bool> PlayScriptsAsync(int repeatCount = 1)
        {
            if (_isPlaying || Actions.Count == 0) return false;
            _isPlaying = true;

            int totalClicks = 0;
            long totalTimeMs = 0;
            List<long> roundTimes = new();
            double avgDelay = Actions.Where(a => a != null).DefaultIfEmpty().Average(a => a?.DelayAfterMs ?? delay);

            OnStatusChanged?.Invoke($"▶ 开始执行：共 {repeatCount} 轮，平均每步延迟 {avgDelay:F0}ms");
            double roundCount;
            for (roundCount = 1; (roundCount <= repeatCount || repeatCount == 0) && _isPlaying; roundCount++)
            {
                var sw = Stopwatch.StartNew();
                foreach (var action in Actions)
                {
                    if (!_isPlaying) break;
                    try
                    {
                        await action.Execute(WebView, Variables);
                        totalClicks++;
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
                double roundTime = roundTimes.Average();
                OnStatusChanged?.Invoke(
                    $"🔁 第 {roundCount} 轮完成 | 用时 {thisRound}ms | 总点击 {totalClicks} | 平均 {roundTime:F0}ms");
                if (thisRound < 10) _isPlaying = false;
            }

            double avgRound = roundTimes.Count > 0 ? roundTimes.Average() : 0;
            OnStatusChanged?.Invoke(_isPlaying
                ? $"🏁 完成 | 轮数 {roundCount} | 点击 {totalClicks} | 平均 {avgRound:F0}ms | 总耗时 {YiKit.SumByteTime(totalTimeMs)}"
                : $"⏹ 已停止 | 轮数 {roundCount} | 点击 {totalClicks} | 总耗时 {YiKit.SumByteTime(totalTimeMs)}");

            _isPlaying = false;
            return true;
        }

        public void StopPlayback() => _isPlaying = false;

        public void StartRecording()
        {
            if (_isRecording) return;
            _isRecording = true;
            OnStatusChanged?.Invoke("开始录制...");
        }

        public void StopRecording()
        {
            if (!_isRecording) return;
            _isRecording = false;
            OnStatusChanged?.Invoke("停止录制");
        }

        public bool IsRecording => _isRecording;
        public bool IsPlaying => _isPlaying;

        private class RecordMessage
        {
            public string? type { get; set; }
            public Data? data { get; set; }

            public class Data
            {
                public string? text { get; set; }
                public string? selector { get; set; }
            }
        }

        public class ResourceInfo
        {
            public string? Type { get; set; }
            public string? Url { get; set; }
            public string? SavePath { get; set; }
        }

        private static string UnwrapJsString(string? jsResult)
        {
            if (string.IsNullOrEmpty(jsResult) || jsResult == "null") return "";
            try
            {
                return JsonSerializer.Deserialize<string>(jsResult) ?? "";
            }
            catch
            {
                return jsResult.Trim('"');
            }
        }

        public async Task DownloadFullPageAsync(string saveDir)
        {
            if (WebView?.CoreWebView2 == null)
                throw new InvalidOperationException("WebView2 未初始化。");

            Directory.CreateDirectory(saveDir);

            string htmlRaw = await WebView.ExecuteScriptAsync(
                "(function(){ return document.documentElement.outerHTML; })();");
            string html = UnwrapJsString(htmlRaw);
            string indexPath = Path.Combine(saveDir, "index.html");
            File.WriteAllText(indexPath, html, Encoding.UTF8);

            string urlsScript = @"
        (function() {
            const resources = [];
            document.querySelectorAll('link[rel=""stylesheet""]').forEach(el => {
                const href = el.href;
                if (href) resources.push({ Type: 'css', Url: href, SavePath: 'css/' + (href.split('/').pop().split('?')[0] || 'style.css') });
            });
            document.querySelectorAll('script[src]').forEach(el => {
                const src = el.src;
                if (src) resources.push({ Type: 'js', Url: src, SavePath: 'js/' + (src.split('/').pop().split('?')[0] || 'script.js') });
            });
            document.querySelectorAll('img[src]').forEach(el => {
                const src = el.src;
                if (src) resources.push({ Type: 'img', Url: src, SavePath: 'img/' + (src.split('/').pop().split('?')[0] || 'image.png') });
            });
            return JSON.stringify(resources);
        })();
    ";

            string jsonResources = UnwrapJsString(await WebView.ExecuteScriptAsync(urlsScript));
            var resourceList = string.IsNullOrWhiteSpace(jsonResources)
                ? new List<ResourceInfo>()
                : JsonSerializer.Deserialize<List<ResourceInfo>>(jsonResources) ?? new List<ResourceInfo>();

            var downloaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(20);

            foreach (var res in resourceList)
            {
                if (string.IsNullOrEmpty(res.Url) || string.IsNullOrEmpty(res.SavePath) || !downloaded.Add(res.Url))
                    continue;

                string fullPath = Path.Combine(saveDir, res.SavePath);
                string? dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
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

            string updatedHtml = html;
            updatedHtml = RewriteAttr(updatedHtml, "link", "href", "css/");
            updatedHtml = RewriteAttr(updatedHtml, "script", "src", "js/");
            updatedHtml = RewriteAttr(updatedHtml, "img", "src", "img/");
            File.WriteAllText(indexPath, updatedHtml, Encoding.UTF8);
        }

        private static string RewriteAttr(string html, string tag, string attr, string folder)
        {
            return Regex.Replace(html,
                $@"<{tag}\s+[^>]*?{attr}\s*=\s*[""']([^""']+)[""'][^>]*>",
                match =>
                {
                    string url = match.Groups[1].Value;
                    try
                    {
                        string fileName = Path.GetFileName(new Uri(url, UriKind.RelativeOrAbsolute).IsAbsoluteUri
                            ? new Uri(url).LocalPath
                            : url.Split('?')[0]);
                        if (string.IsNullOrEmpty(fileName)) return match.Value;
                        return match.Value.Replace(url, folder + fileName);
                    }
                    catch
                    {
                        return match.Value;
                    }
                },
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
        public string Text { get; set; } = "";

        public override async Task Execute(WebView2 webView, Dictionary<string, string> variables)
        {
            var targetText = Text;
            if (string.IsNullOrEmpty(targetText) || webView.CoreWebView2 == null) return;

            var escapedText = targetText.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"");
            var ret = await webView.CoreWebView2.ExecuteScriptAsync($@"
                (function() {{
                    function clickTarget(target) {{
                        const elements = document.querySelectorAll('*');
                        for (const el of elements) {{
                            if (el.offsetParent === null || window.getComputedStyle(el).display === 'none') continue;
                            if (el.textContent.trim() === target || el.getAttribute('aria-label') === target ||
                                el.getAttribute('title') === target || el.getAttribute('placeholder') === target) {{
                                el.click();
                                return 1;
                            }}
                        }}
                        return -1;
                    }}
                    if (document.body && document.body.innerText.indexOf('您的点击频度过快，请稍候再继续...') > -1) {{
                        return -2;
                    }}
                    return clickTarget('{escapedText}'.trim());
                }})();
            ");

            // ExecuteScriptAsync 返回 JSON，数字为 "1" / "-2"
            ret = (ret ?? "").Trim();
            if (ret == "1")
            {
                await Task.Delay(DelayAfterMs);
            }
            else if (ret == "-2")
            {
                await Task.Delay(1000 + DelayAfterMs);
                await webView.CoreWebView2.ExecuteScriptAsync($@"
                    (function() {{
                        const elements = document.querySelectorAll('*');
                        let target = '继续';
                        for (const el of elements) {{
                            if (el.offsetParent === null || window.getComputedStyle(el).display === 'none') continue;
                            if (el.textContent.trim() === target) {{ el.click(); return 1; }}
                        }}
                        return -1;
                    }})();");
                await Task.Delay(DelayAfterMs);
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
            if (webView.CoreWebView2 == null) return;
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
        public string TextExists { get; set; } = "";
        public int TimeoutMs { get; set; } = 10000;

        public override async Task Execute(WebView2 webView, Dictionary<string, string> variables)
        {
            if (!string.IsNullOrEmpty(UrlContains))
            {
                var tcs = new TaskCompletionSource<bool>();
                void Handler(object? s, CoreWebView2NavigationCompletedEventArgs ev)
                {
                    if (webView.Source?.ToString().Contains(UrlContains) == true)
                        tcs.TrySetResult(true);
                }

                webView.NavigationCompleted += Handler;
                using (var cts = new CancellationTokenSource(TimeoutMs))
                {
                    cts.Token.Register(() => tcs.TrySetResult(false));
                    await tcs.Task;
                }

                webView.NavigationCompleted -= Handler;
            }
            else if (!string.IsNullOrEmpty(ElementExists) && webView.CoreWebView2 != null)
            {
                var sel = ElementExists.Replace("'", "\\'");
                for (int i = 0; i < TimeoutMs / 100; i++)
                {
                    var result = await webView.CoreWebView2.ExecuteScriptAsync(
                        $"!!document.querySelector('{sel}')");
                    if (result == "true") break;
                    await Task.Delay(100);
                }
            }
            else if (!string.IsNullOrEmpty(TextExists) && webView.CoreWebView2 != null)
            {
                var escaped = TextExists.Replace("\\", "\\\\").Replace("'", "\\'");
                for (int i = 0; i < TimeoutMs / 100; i++)
                {
                    var result = await webView.CoreWebView2.ExecuteScriptAsync($@"
                        (function() {{
                            const text = '{escaped}';
                            for (const el of document.querySelectorAll('*')) {{
                                if (el.offsetParent && el.textContent.trim() === text) return true;
                            }}
                            return false;
                        }})();");
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
            var nav = Url;
            foreach (var v in variables)
                nav = nav.Replace($"${{{v.Key}}}", v.Value);
            if (webView.CoreWebView2 != null)
                webView.CoreWebView2.Navigate(nav);
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
            var list = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(json)
                       ?? new List<Dictionary<string, object>>();
            var actions = new ObservableCollection<ScriptAction>();
            int id = 1;

            foreach (var dict in list)
            {
                string type = dict.TryGetValue("Type", out var t) ? t?.ToString() ?? "" : "";
                ScriptAction action = type switch
                {
                    "click" => new ClickAction { Text = GetString(dict, "Text") },
                    "type" => new TypeAction
                        { Selector = GetString(dict, "Selector"), Text = GetString(dict, "Text") },
                    "wait" => new WaitAction
                    {
                        UrlContains = GetString(dict, "UrlContains"),
                        ElementExists = GetString(dict, "ElementExists"),
                        TextExists = GetString(dict, "TextExists"),
                        TimeoutMs = GetInt(dict, "TimeoutMs", 10000)
                    },
                    "variable" => new VariableAction
                        { Key = GetString(dict, "Key"), Value = GetString(dict, "Value") },
                    "navigate" => new NavigateAction { Url = GetString(dict, "Url") },
                    _ => new ClickAction()
                };

                action.Id = id++;
                action.Name = GetString(dict, "Name");
                if (string.IsNullOrEmpty(action.Name)) action.Name = $"动作 {action.Id}";
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
        private string? _scriptFilePath;
        private TextBlock StatusText = null!;
        private ListView ActionList = null!;

        public ScriptManagerWindow(WebViewContainer container)
        {
            _container = container;
            _actions = container.Actions;
            BuildUi();
            ActionList.ItemsSource = _actions;
            _actions.CollectionChanged += (_, _) => SaveToLastPath();
            _container.OnStatusChanged += s => Dispatcher.Invoke(() => StatusText.Text = s);
        }

        private static Button MakeBtn(string content, Brush fg, Brush bg)
        {
            return new Button
            {
                Content = content,
                Width = 88,
                Margin = new Thickness(0, 4, 0, 0),
                Padding = new Thickness(8, 6, 8, 6),
                Foreground = fg,
                Background = bg,
                BorderBrush = new SolidColorBrush(ColorFromHex("#D8DADF")),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
        }

        private static System.Windows.Media.Color ColorFromHex(string hex)
        {
            return (System.Windows.Media.Color)ColorConverter.ConvertFromString(hex)!;
        }

        private void BuildUi()
        {
            var fcolor = TryBrush(YiWebView.me?.ForegroundColorStr, Brushes.Black);
            var bcolor = TryBrush(YiWebView.me?.BackgroundColorStr, new SolidColorBrush(ColorFromHex("#F7F7F8")));

            Title = "脚本管理器";
            Width = 1000;
            Height = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = bcolor;
            Foreground = fcolor;
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");

            var grid = new Grid { Margin = new Thickness(10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });

            ActionList = new ListView
            {
                Margin = new Thickness(0, 0, 8, 0),
                Background = Brushes.White,
                Foreground = fcolor,
                BorderBrush = new SolidColorBrush(ColorFromHex("#D8DADF")),
                BorderThickness = new Thickness(1)
            };
            ActionList.MouseDoubleClick += (_, _) =>
            {
                if (ActionList.SelectedItem is ScriptAction action)
                    EditAction(action);
            };
            ActionList.View = new GridView
            {
                Columns =
                {
                    new GridViewColumn { Header = "ID", DisplayMemberBinding = new Binding("Id"), Width = 40 },
                    new GridViewColumn { Header = "名称", DisplayMemberBinding = new Binding("Name"), Width = 160 },
                    new GridViewColumn { Header = "类型", DisplayMemberBinding = new Binding("Type"), Width = 80 },
                    new GridViewColumn
                        { Header = "延迟(ms)", DisplayMemberBinding = new Binding("DelayAfterMs"), Width = 80 }
                }
            };
            Grid.SetColumn(ActionList, 0);

            var btnStack = new StackPanel
            {
                Margin = new Thickness(4, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            var addBtn = MakeBtn("添加", fcolor, Brushes.White);
            addBtn.Click += (_, _) => EditAction(null);
            var delBtn = MakeBtn("删除", fcolor, Brushes.White);
            delBtn.Click += (_, _) =>
            {
                if (ActionList.SelectedItem is ScriptAction a)
                    _actions.Remove(a);
            };
            var upBtn = MakeBtn("上移", fcolor, Brushes.White);
            upBtn.Click += (_, _) =>
            {
                var i = ActionList.SelectedIndex;
                if (i <= 0) return;
                _actions.Move(i, i - 1);
                ActionList.SelectedIndex = i - 1;
            };
            var downBtn = MakeBtn("下移", fcolor, Brushes.White);
            downBtn.Click += (_, _) =>
            {
                int i = ActionList.SelectedIndex;
                if (i < 0 || i >= _actions.Count - 1) return;
                _actions.Move(i, i + 1);
                ActionList.SelectedIndex = i + 1;
            };
            btnStack.Children.Add(addBtn);
            btnStack.Children.Add(upBtn);
            btnStack.Children.Add(downBtn);
            btnStack.Children.Add(delBtn);
            Grid.SetColumn(btnStack, 1);

            var rightStack = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
            rightStack.Children.Add(new TextBlock
            {
                Text = "控制", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), FontSize = 14
            });

            var recordBtn = MakeBtn("开始录制", fcolor, Brushes.White);
            recordBtn.Click += (_, _) =>
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

            var playBtn = MakeBtn("播放脚本", fcolor, Brushes.White);
            var repBox = new TextBox { Text = "0", Width = 64, Margin = new Thickness(4, 0, 0, 0) };
            var delayBox = new TextBox
            {
                Text = $"{WebViewContainer.delay}", Width = 64, Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "默认延迟（毫秒）"
            };
            delayBox.LostFocus += (_, _) =>
            {
                if (int.TryParse(delayBox.Text, out int d) && d > 0)
                    WebViewContainer.delay = d;
            };

            var headlessCb = new CheckBox
            {
                Content = "隐藏页面", Margin = new Thickness(0, 8, 0, 8), Foreground = fcolor
            };
            headlessCb.Checked += (_, _) => _container.IsHeadless = true;
            headlessCb.Unchecked += (_, _) => _container.IsHeadless = false;

            playBtn.Click += async (_, _) =>
            {
                if (!_container.IsPlaying)
                {
                    playBtn.Content = "停止播放";
                    int count = int.TryParse(repBox.Text, out int c) ? c : 1;
                    if (count < 0) count = 0;
                    _container.IsHeadless = headlessCb.IsChecked == true;
                    await _container.PlayScriptsAsync(count);
                    playBtn.Content = "播放脚本";
                }
                else
                {
                    _container.StopPlayback();
                }
            };

            rightStack.Children.Add(new WrapPanel
            {
                Children =
                {
                    new TextBlock { Text = "重复", VerticalAlignment = VerticalAlignment.Center }, repBox
                }
            });
            rightStack.Children.Add(new WrapPanel
            {
                Margin = new Thickness(0, 6, 0, 0),
                Children =
                {
                    new TextBlock { Text = "延迟", VerticalAlignment = VerticalAlignment.Center }, delayBox
                }
            });
            rightStack.Children.Add(headlessCb);
            rightStack.Children.Add(recordBtn);
            rightStack.Children.Add(playBtn);
            rightStack.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 12) });
            rightStack.Children.Add(new TextBlock
            {
                Text = "文件", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), FontSize = 14
            });

            var saveBtn = MakeBtn("保存脚本", fcolor, Brushes.White);
            saveBtn.Click += SaveScript_Click;
            var loadBtn = MakeBtn("加载脚本", fcolor, Brushes.White);
            loadBtn.Click += LoadScript_Click;
            rightStack.Children.Add(saveBtn);
            rightStack.Children.Add(loadBtn);
            rightStack.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 12) });

            StatusText = new TextBlock
            {
                Text = "就绪",
                Foreground = new SolidColorBrush(ColorFromHex("#5F6368")),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            rightStack.Children.Add(StatusText);
            Grid.SetColumn(rightStack, 2);

            grid.Children.Add(ActionList);
            grid.Children.Add(btnStack);
            grid.Children.Add(rightStack);
            Content = grid;
        }

        private static Brush TryBrush(string? color, Brush fallback)
        {
            if (string.IsNullOrWhiteSpace(color)) return fallback;
            try
            {
                return (Brush)new BrushConverter().ConvertFromString(color)!;
            }
            catch
            {
                return fallback;
            }
        }

        private void EditAction(ScriptAction? original)
        {
            var win = new Window
            {
                Title = original == null ? "添加动作" : "编辑动作",
                Width = 420,
                Height = 380,
                Owner = this,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Background,
                Foreground = Foreground
            };

            var stack = new StackPanel { Margin = new Thickness(14) };
            var typeCb = new ComboBox { Margin = new Thickness(0, 0, 0, 6) };
            typeCb.Items.Add("点击元素");
            typeCb.Items.Add("输入文本");
            typeCb.Items.Add("等待URL");
            typeCb.Items.Add("等待元素");
            typeCb.Items.Add("等待文本");
            typeCb.Items.Add("设置变量");
            typeCb.Items.Add("跳转页面");

            if (original != null)
            {
                typeCb.SelectedIndex = original switch
                {
                    ClickAction => 0,
                    TypeAction => 1,
                    WaitAction w1 when !string.IsNullOrEmpty(w1.UrlContains) => 2,
                    WaitAction w2 when !string.IsNullOrEmpty(w2.ElementExists) => 3,
                    WaitAction w3 when !string.IsNullOrEmpty(w3.TextExists) => 4,
                    VariableAction => 5,
                    NavigateAction => 6,
                    _ => 0
                };
            }
            else typeCb.SelectedIndex = 0;

            var nameTb = new TextBox { Text = original?.Name ?? "新动作", Margin = new Thickness(0, 0, 0, 6) };
            var selTb = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
            var valTb = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
            var delayTb = new TextBox
            {
                Text = (original?.DelayAfterMs ?? 1000).ToString(), Margin = new Thickness(0, 0, 0, 6)
            };

            void Update()
            {
                var typeName = typeCb.SelectedItem?.ToString() ?? "";
                selTb.Visibility = (typeName.Contains("点击") || typeName.Contains("输入") || typeName == "等待元素" ||
                                    typeName == "设置变量")
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                valTb.Visibility = typeName is "点击元素" ? Visibility.Collapsed : Visibility.Visible;
                valTb.ToolTip = typeName switch
                {
                    "等待URL" => "包含的URL片段",
                    "等待文本" => "要等待出现的文本",
                    "设置变量" => "变量值",
                    "跳转页面" => "目标URL",
                    "输入文本" => "输入的文本",
                    _ => ""
                };
                selTb.ToolTip = typeName switch
                {
                    "输入文本" => "CSS选择器",
                    "等待元素" => "CSS选择器",
                    "点击元素" => "元素显示文本",
                    "设置变量" => "变量名",
                    _ => ""
                };
            }

            typeCb.SelectionChanged += (_, _) => Update();
            Update();

            if (original is ClickAction ca) selTb.Text = ca.Text;
            if (original is TypeAction ta)
            {
                selTb.Text = ta.Selector;
                valTb.Text = ta.Text;
            }

            if (original is WaitAction wa)
            {
                if (!string.IsNullOrEmpty(wa.UrlContains)) valTb.Text = wa.UrlContains;
                else if (!string.IsNullOrEmpty(wa.ElementExists)) selTb.Text = wa.ElementExists;
                else if (!string.IsNullOrEmpty(wa.TextExists)) valTb.Text = wa.TextExists;
            }

            if (original is VariableAction va)
            {
                selTb.Text = va.Key;
                valTb.Text = va.Value;
            }

            if (original is NavigateAction na) valTb.Text = na.Url;

            var okBtn = new Button
            {
                Content = "确定", IsDefault = true, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(12, 6, 12, 6)
            };
            okBtn.Click += (_, _) =>
            {
                ScriptAction? action = null;
                var typeName = typeCb.SelectedItem?.ToString();
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
                        if (index >= 0)
                        {
                            action.Id = original.Id;
                            _actions[index] = action;
                        }
                    }
                    else
                    {
                        action.Id = (_actions.Count == 0 ? 0 : _actions.Max(a => a.Id)) + 1;
                        _actions.Add(action);
                    }
                }

                win.Close();
            };

            stack.Children.Add(new TextBlock { Text = "类型", Margin = new Thickness(0, 0, 0, 2) });
            stack.Children.Add(typeCb);
            stack.Children.Add(new TextBlock { Text = "名称", Margin = new Thickness(0, 4, 0, 2) });
            stack.Children.Add(nameTb);
            stack.Children.Add(new TextBlock { Text = "参数1", Margin = new Thickness(0, 4, 0, 2) });
            stack.Children.Add(selTb);
            stack.Children.Add(new TextBlock { Text = "参数2", Margin = new Thickness(0, 4, 0, 2) });
            stack.Children.Add(valTb);
            stack.Children.Add(new TextBlock { Text = "延迟(ms)", Margin = new Thickness(0, 4, 0, 2) });
            stack.Children.Add(delayTb);
            stack.Children.Add(okBtn);

            win.Content = stack;
            win.ShowDialog();
        }

        private void SaveScript_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new Microsoft.Win32.SaveFileDialog { Filter = "JSON脚本|*.json" };
            if (sfd.ShowDialog() != true) return;
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

        private void LoadScript_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog { Filter = "JSON脚本|*.json" };
            if (ofd.ShowDialog() != true) return;
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

        private void SaveToLastPath()
        {
            if (_scriptFilePath == null) return;
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
}
