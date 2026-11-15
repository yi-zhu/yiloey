using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YiKits;

namespace holey.Dialog
{
    public partial class ClipboardHistoryWindow : Window
    {
        private List<ClipboardItem> _clipboardHistory = new List<ClipboardItem>();
        private List<ClipboardItem> _originalHistory = new List<ClipboardItem>(); // 原始历史记录，用于筛选
        private string _autoSaveFilePath;
        private bool _isAutoSaveEnabled = false;
        private const int MaxHistoryItems = int.MaxValue;

        public ClipboardHistoryWindow()
        {
            InitializeComponent();
            // _autoSaveFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "holey", "clipboard_history.txt");
            _autoSaveFilePath = Path.Combine(YiKit.GetAppStartPath(), "clipboard_history.txt");
            EnsureAutoSaveDirectory();
        }

        private void EnsureAutoSaveDirectory()
        {
            string directory = Path.GetDirectoryName(_autoSaveFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 加载历史记录
            LoadHistory();
            _originalHistory = new List<ClipboardItem>(_clipboardHistory); // 保存原始历史记录
            UpdateHistoryList();


            if (PetWindow.YiWscClient != null)
            {
                PetWindow.YiWscClient.OnMessage += YiWscClientOnMessage;
            }
            else

                // 注册剪切板更新事件
                YiKit.ClipboardMonitor(this, OnClipboardUpdated, true);
        }

        string _cpyText = "";

        private void YiWscClientOnMessage(object sender, string data)
        {
            //处理的消息错误将会忽略
            try
            {
                //需要优化
                //处理消息
                if (data.StartsWith("cpy:"))
                {
                    /*YiKit.RunThread((delegate(object state)
                    {
                        YiKit.SetClipboardText(data.Remove(0,4));
                    }));*/
                    _cpyText = data.Remove(0, 4);
                    if (_clipboardHistory.Count == 0 || _clipboardHistory[0].Content != _cpyText)
                        if (_cpyText != allContent)
                            YiKit.RunOnUIThread(() => AddToHistory(_cpyText));
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // 取消注册剪切板监控
            YiKit.CloseClipboardMonitor(OnClipboardUpdated);
        }

        private void OnClipboardUpdated(object sender, EventArgs e)
        {
            try
            {
                if (System.Windows.Clipboard.ContainsText())
                {
                    string text = System.Windows.Clipboard.GetText();
                    if (!string.IsNullOrEmpty(text))
                    {
                        // 检查是否与最新的历史记录重复
                        if (_clipboardHistory.Count == 0 || _clipboardHistory[0].Content != text)
                        {
                            if (text != allContent)
                                YiKit.RunOnUIThread(() => AddToHistory(text));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "错误: " + ex.Message;
            }
        }

        private List<string> historyTxts = new List<string>();

        private void AddToHistory(string content)
        {
            if (historyTxts.Contains(content))
                return;
            var newItem = new ClipboardItem
            {
                Content = content,
                Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
            historyTxts.Add(content);
            // 添加到历史记录开头
            _clipboardHistory.Insert(0, newItem);
            _originalHistory = new List<ClipboardItem>(_clipboardHistory); // 更新原始历史记录

            // 限制历史记录数量
            if (_clipboardHistory.Count > MaxHistoryItems)
            {
                _clipboardHistory = _clipboardHistory.Take(MaxHistoryItems).ToList();
                _originalHistory = new List<ClipboardItem>(_clipboardHistory); // 更新原始历史记录
            }

            // 更新列表
            ApplyFilters();

            // 自动保存
            if (_isAutoSaveEnabled)
            {
                SaveHistoryToFile();
            }
        }

        private void UpdateHistoryList()
        {
            Dispatcher.Invoke(() =>
            {
                ClipboardHistoryList.ItemsSource = null;
                ClipboardHistoryList.ItemsSource = _clipboardHistory;
                StatusNum.Text = _clipboardHistory.Count + "";
            });
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(_autoSaveFilePath))
                {
                    string[] lines = File.ReadAllLines(_autoSaveFilePath);
                    for (int i = lines.Length - 1; i >= 0; i -= 2)
                    {
                        if (i - 1 >= 0)
                        {
                            string content = lines[i - 1];
                            string time = lines[i];
                            _clipboardHistory.Add(new ClipboardItem { Content = content, Time = time });
                        }
                    }

                    // 限制历史记录数量
                    if (_clipboardHistory.Count > MaxHistoryItems)
                    {
                        _clipboardHistory = _clipboardHistory.Take(MaxHistoryItems).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "加载历史记录失败: " + ex.Message;
            }
        }

        private void SaveHistoryToFile()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(_autoSaveFilePath))
                {
                    foreach (var item in _clipboardHistory)
                    {
                        writer.WriteLine(item.Content);
                        // writer.WriteLine(item.Time);
                    }
                }

                StatusText.Text = "已自动保存到文件";
            }
            catch (Exception ex)
            {
                StatusText.Text = "保存文件失败: " + ex.Message;
            }
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            _clipboardHistory.Clear();
            _originalHistory.Clear();
            historyTxts.Clear();
            UpdateHistoryList();
            StatusText.Text = "历史记录已清空";
        }

        private void SaveToFile_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                DefaultExt = "txt",
                FileName = "clipboard_history_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    using (StreamWriter writer = new StreamWriter(saveFileDialog.FileName))
                    {
                        foreach (var item in _clipboardHistory)
                        {
                            writer.WriteLine("时间: " + item.Time);
                            writer.WriteLine("内容: " + item.Content);
                            writer.WriteLine(new string('-', 50));
                        }
                    }

                    StatusText.Text = "已保存到文件";
                }
                catch (Exception ex)
                {
                    StatusText.Text = "保存文件失败: " + ex.Message;
                }
            }
        }

        private void AutoSaveCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            _isAutoSaveEnabled = true;
            StatusText.Text = "已开启自动保存";
            SaveHistoryToFile();
        }

        private void AutoSaveCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            _isAutoSaveEnabled = false;
            StatusText.Text = "已关闭自动保存";
        }

        private void ClipboardHistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ClipboardHistoryList.SelectedItem is ClipboardItem item)
            {
                try
                {
                    System.Windows.Clipboard.SetText(item.Content);
                    StatusText.Text = "已复制到剪切板";
                }
                catch (Exception ex)
                {
                    StatusText.Text = "复制失败: " + ex.Message;
                }
            }
        }

        private string allContent = "";

        // 复制所有按钮点击事件
        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                allContent = string.Join(Environment.NewLine,
                    _clipboardHistory.Select(item => $"{item.Content}"));
                System.Windows.Clipboard.SetText(allContent);
                StatusText.Text = "已复制所有内容到剪切板";
            }
            catch (Exception ex)
            {
                StatusText.Text = "复制失败: " + ex.Message;
            }
        }

        // 复制单个条目按钮点击事件
        private void CopyItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string content)
            {
                try
                {
                    System.Windows.Clipboard.SetText(content);
                    StatusText.Text = "已复制到剪切板";
                }
                catch (Exception ex)
                {
                    StatusText.Text = "复制失败: " + ex.Message;
                }
            }
        }

        // 筛选文本变化事件
        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        // 长度筛选文本变化事件
        private void LengthTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        // 应用筛选
        private void ApplyFilters()
        {
            string filterText = FilterTextBox.Text?.Trim() ?? "";
            int minLength = 0;
            int.TryParse(LengthTextBox.Text?.Trim(), out minLength);

            // 从原始历史记录中筛选
            var filtered = _originalHistory.Where(item =>
            {
                bool matchesFilter = string.IsNullOrEmpty(filterText) ||
                                     item.Content.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchesLength = item.Content.Length >= minLength;
                return matchesFilter && matchesLength;
            }).ToList();

            _clipboardHistory = filtered;
            UpdateHistoryList();
        }
    }

    public class ClipboardItem
    {
        public string Content { get; set; }
        public string Time { get; set; }
    }
}