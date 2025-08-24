using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using YiKits;

namespace holey;

public partial class RowAndCol : Window
{
    public static RowAndCol me;

    public RowAndCol()
    {
        InitializeComponent();
        me = this;
        InitializeDisplayTable();
        dataGrid.ItemsSource = displayTable.DefaultView;
    }

    private List<string[]> cellData; // 原始分割数据
    private List<string> originalLines; // 原始行（用于保存）
    private DataTable displayTable;
    private string currentFilePath = null;


    private void InitializeDisplayTable()
    {
        displayTable = new DataTable("显示表");
        displayTable.Columns.Add("列1", typeof(string));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        me = null;
    }

    private async void LoadFile_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new OpenFileDialog
        {
            Filter = "文本文件 (*.txt;*.csv;*.dat)|*.txt;*.csv;*.dat|所有文件 (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadFileAsync(dialog.FileName);
        }
    }

    private async Task LoadFileAsync(string filePath)
    {
        try
        {
            this.Dispatcher.Invoke(() =>
            {
                tbFilePath.Text = "正在加载...";
                dataGrid.ItemsSource = null;
            });

            string separator = txtSeparator.Text;
            string newLineStr = txtNewLine.Text;
            int maxRows = int.TryParse(txtMaxRows.Text, out int temp) ? temp : 10000;
            bool hasHeader = chkHasHeader.IsChecked == true;

            // ========== 修复 1：正确解析 \r\n 等转义字符 ==========
            separator = ParseEscapeSequences(separator);
            newLineStr = ParseEscapeSequences(newLineStr);

            var lines = await Task.Run(() =>
            {
                var result = new List<string>();
                using (var reader = new StreamReader(filePath, Encoding.UTF8))
                {
                    string content = reader.ReadToEnd();

                    // 使用自定义换行符分割
                    string[] split;
                    if (newLineStr.Length == 1)
                    {
                        split = content.Split(newLineStr[0]);
                    }
                    else
                    {
                        split = content.Split(new string[] { newLineStr }, StringSplitOptions.None);
                    }

                    int count = 0;
                    foreach (string line in split)
                    {
                        if (maxRows > 0 && count >= maxRows) break;
                        result.Add(line);
                        count++;
                    }

                    return result;
                }
            });

            if (lines.Count == 0)
            {
                MessageBox.Show("文件为空或无法读取。", "提示");
                return;
            }

            originalLines = new List<string>(lines);
            cellData = new List<string[]>();
            int maxCol = 0;

            foreach (string line in lines)
            {
                string[] cells = line.Split(new string[] { separator }, StringSplitOptions.None);
                cellData.Add(cells);
                if (cells.Length > maxCol) maxCol = cells.Length;
            }

            if (maxCol == 0) maxCol = 1;

            const int MAX_COLUMNS = 500;
            if (maxCol > MAX_COLUMNS)
            {
                MessageBox.Show($"列数超过 {MAX_COLUMNS}，已截断显示。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                maxCol = MAX_COLUMNS;
            }

            displayTable = new DataTable("数据");
            for (int i = 0; i < maxCol; i++)
            {
                string colName = hasHeader && i < cellData[0].Length ? cellData[0][i] : $"列{i + 1}";
                string uniqueName = MakeUniqueColumnName(displayTable, colName);
                displayTable.Columns.Add(uniqueName, typeof(string));
            }

            int startRow = hasHeader ? 1 : 0;
            for (int i = startRow; i < cellData.Count; i++)
            {
                DataRow row = displayTable.NewRow();
                for (int j = 0; j < maxCol; j++)
                {
                    row[j] = j < cellData[i].Length ? cellData[i][j] : "";
                }

                displayTable.Rows.Add(row);
            }

            this.Dispatcher.Invoke(() =>
            {
                dataGrid.ItemsSource = displayTable.DefaultView;
                currentFilePath = filePath;
                tbFilePath.Text = $"当前文件: {filePath} | 行数: {displayTable.Rows.Count} | 列数: {maxCol}";

                UpdateColumnComboBox();
            });
        }
        catch (Exception ex)
        {
            this.Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"加载失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }

    // ========== 修复 1：转义字符解析 ==========
    private string ParseEscapeSequences(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        return input
            .Replace("\\r", "\r")
            .Replace("\\n", "\n")
            .Replace("\\t", "\t")
            .Replace("\\\\", "\\");
    }

    private string MakeUniqueColumnName(DataTable table, string name)
    {
        string unique = name;
        int suffix = 1;
        while (table.Columns.Contains(unique))
            unique = name + (++suffix);
        return unique;
    }

    private void UpdateColumnComboBox()
    {
        cboColumn.Items.Clear();

        for (int i = 0; i < displayTable.Columns.Count; i++)
        {
            cboColumn.Items.Add(displayTable.Columns[i].ColumnName);
        }

        if (cboColumn.Items.Count > 0)
            cboColumn.SelectedIndex = 0;
        cboColumnif.Items.Clear();

        for (int i = 0; i < displayTable.Columns.Count; i++)
        {
            cboColumnif.Items.Add(displayTable.Columns[i].ColumnName);
        }

        if (cboColumnif.Items.Count > 0)
            cboColumnif.SelectedIndex = 0;
    }

    // ========== 修复 2：提取数字并保留非数字部分 ==========
    private void ApplyMathOperation_Click(object sender, RoutedEventArgs e)
    {
        if (cboColumn.SelectedItem == null || string.IsNullOrWhiteSpace(txtValue.Text))
        {
            MessageBox.Show("请选择列并输入表达式。");
            return;
        }

        var scs = dataGrid.SelectedItems;
        bool is_cboCK = cboCK.IsChecked == true;

        bool is_ifck = cboCKif.IsChecked == true;
        bool is_ifconck = cboCKifCon.IsChecked == true;
        bool is_selctcol = cboCKifcol.IsChecked == true;
        int colIfIndex = cboColumnif.SelectedIndex;
        string conStr = txtCon.Text;
        int colIndex = cboColumn.SelectedIndex;
        string expressionStr = txtValue.Text.Trim();
        // double result = YiMathKit.EvaluateAdvanced("if(x > 0 || x == -1, sqrt(x), 0)", 25);
        int count = 0;
        bool is_mouse = cborowCK.IsChecked == true;
        DataRowCollection rows = displayTable.Rows;
        List<DataRow> is_mouse_rows = new List<DataRow>();
        if (is_mouse)
        {
            foreach (DataRowView selectedItem in dataGrid.SelectedItems)
            {
                is_mouse_rows.Add(selectedItem.Row);
            }
        }

        foreach (DataRow row in rows)
        {
            if (is_mouse)
                if (!is_mouse_rows.Contains(row))
                    continue;

            if (is_ifck)
            {
                if (is_selctcol)
                    if (row.ItemArray.Length > colIfIndex)
                    {
                        if (is_ifconck)
                        {
                            if (!row.ItemArray[colIfIndex].ToString().Contains(conStr))
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (row.ItemArray[colIfIndex].ToString().Contains(conStr))
                            {
                                continue;
                            }
                        }
                    }
                    else
                    {
                        if (is_ifconck)
                            continue;
                    }

                {
                }
            }

            if (is_cboCK)
            {
                string original = row[colIndex]?.ToString() ?? "";

                if (string.IsNullOrEmpty(original)) continue;
                if (is_ifck)
                    if (is_selctcol == false)
                        if (is_ifconck != original.Contains(conStr))
                            continue;

                // 提取第一个数字
                var match = System.Text.RegularExpressions.Regex.Match(original, @"-?\d+\.?\d*");
                if (!match.Success) continue;

                if (double.TryParse(match.Value, out double num))
                {
                    try
                    {
                        double resultValue = YiMathKit.EvaluateAdvanced(expressionStr, num);

                        string replaced = original.Substring(0, match.Index) +
                                          resultValue.ToString() +
                                          original.Substring(match.Index + match.Length);
                        count++;
                        row[colIndex] = replaced;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"表达式错误: {ex.Message}\n表达式: {expressionStr}",
                            "公式错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }
            else
            {
                for (int x = 0; x < row.ItemArray.Length; x++)
                {
                    string original = row[x]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(original)) continue;
                    if (is_ifck)
                        if (is_selctcol == false)
                            if (is_ifconck != original.Contains(conStr))
                                continue;
                    // 提取第一个数字
                    var match = System.Text.RegularExpressions.Regex.Match(original, @"-?\d+\.?\d*");
                    if (!match.Success) continue;

                    if (double.TryParse(match.Value, out double num))
                    {
                        try
                        {
                            double resultValue = YiMathKit.EvaluateAdvanced(expressionStr, num);

                            string replaced = original.Substring(0, match.Index) +
                                              resultValue.ToString() +
                                              original.Substring(match.Index + match.Length);
                            count++;
                            row[x] = replaced;
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"表达式错误: {ex.Message}\n表达式: {expressionStr}",
                                "公式错误", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                }
            }

            YiKit.ShowToast("运算完成，共运算" + count + "次");
        }
    }


    private void ApplyReplace_Click(object sender, RoutedEventArgs e)
    {
        if (cboColumn.SelectedItem == null || string.IsNullOrWhiteSpace(txtFind.Text))
        {
            MessageBox.Show("请选择列并输入查找内容。");
            return;
        }

        bool is_selctck = replaceCK.IsChecked == true;
        bool is_ifck = cboCKif.IsChecked == true;
        bool is_ifconck = cboCKifCon.IsChecked == true;
        bool is_selctcol = cboCKifcol.IsChecked == true;
        int colIfIndex = cboColumnif.SelectedIndex;
        string conStr = txtCon.Text;
        int colIndex = cboColumn.SelectedIndex;
        string find = txtFind.Text;
        string replace = txtReplace.Text ?? "";

        if (string.IsNullOrEmpty(find)) return;

        bool is_mouse = replaceRowCK.IsChecked == true;
        DataRowCollection rows = displayTable.Rows;
        List<DataRow> is_mouse_rows = new List<DataRow>();
        if (is_mouse)
        {
            foreach (DataRowView selectedItem in dataGrid.SelectedItems)
            {
                is_mouse_rows.Add(selectedItem.Row);
            }
        }

        // 将通配符 * 和 ? 转换为正则表达式
        string pattern = Regex.Escape(find)
            .Replace("\\*", ".*") // * -> 任意字符（多次）
            .Replace("\\?", "."); // ? -> 任意单字符

        try
        {
            // 编译正则表达式，支持忽略大小写（可选）
            var regex = new System.Text.RegularExpressions.Regex(
                pattern,
                System.Text.RegularExpressions.RegexOptions.Compiled |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant);

            // 遍历所有行
            foreach (DataRow row in rows)
            {
                if (is_mouse)
                    if (!is_mouse_rows.Contains(row))
                        continue;
                if (is_ifck)
                {
                    if (is_selctcol)
                        if (row.ItemArray.Length > colIfIndex)
                        {
                            if (is_ifconck)
                            {
                                if (!row.ItemArray[colIfIndex].ToString().Contains(conStr))
                                {
                                    continue;
                                }
                            }
                            else
                            {
                                if (row.ItemArray[colIfIndex].ToString().Contains(conStr))
                                {
                                    continue;
                                }
                            }
                        }
                        else
                        {
                            if (is_ifconck)
                                continue;
                        }
                }

                if (is_selctck)
                {
                    string cellValue = row[colIndex]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(cellValue)) continue;
                    if (is_ifck)
                        if (is_selctcol == false)
                            if (is_ifconck != cellValue.Contains(conStr))
                                continue;
                    // 替换所有匹配项
                    string newValue = regex.Replace(cellValue, replace);
                    row[colIndex] = newValue;
                }
                else
                {
                    for (int x = 0; x < row.ItemArray.Length; x++)
                    {
                        string cellValue = row[x]?.ToString() ?? "";
                        if (string.IsNullOrEmpty(cellValue)) continue;
                        if (is_ifck)
                            if (is_selctcol == false)
                                if (is_ifconck != cellValue.Contains(conStr))
                                    continue;
                        // 替换所有匹配项
                        string newValue = regex.Replace(cellValue, replace);
                        row[x] = newValue;
                    }
                }
            }

            // 可选：提示成功
            YiKit.ShowToast("替换完成");
            // MessageBox.Show("替换完成。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"正则表达式错误：{ex.Message}\n检查查找表达式是否包含非法通配符。",
                "替换错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SaveOriginalFormat_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(currentFilePath))
        {
            MessageBox.Show("无文件可保存。");
            return;
        }

        try
        {
            bool hasHeader = chkHasHeader.IsChecked == true;

            string separator = txtSeparator.Text;
            string newLineStr = txtNewLine.Text;
            await Task.Run(() =>
            {
                int startRow = hasHeader ? 1 : 0;


                separator = ParseEscapeSequences(separator);
                newLineStr = ParseEscapeSequences(newLineStr);

                for (int i = startRow; i < cellData.Count; i++)
                {
                    int displayRowIndex = i - startRow;
                    if (displayRowIndex >= displayTable.Rows.Count) continue;

                    DataRow row = displayTable.Rows[displayRowIndex];
                    for (int j = 0; j < row.ItemArray.Length && j < cellData[i].Length; j++)
                    {
                        cellData[i][j] = row[j]?.ToString() ?? "";
                    }
                }

                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < cellData.Count; i++)
                {
                    sb.Append(string.Join(separator, cellData[i]));
                    sb.Append(newLineStr);
                }

                if (sb.Length >= newLineStr.Length)
                    sb.Length -= newLineStr.Length;

                File.WriteAllText(currentFilePath, sb.ToString(), Encoding.UTF8);
            });

            MessageBox.Show("保存成功（保留原格式）！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshPreview_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(currentFilePath))
        {
            LoadFileAsync(currentFilePath);
        }
        else
        {
            MessageBox.Show("请先加载文件。", "提示");
        }
    }
}