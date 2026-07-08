using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using YiKits;
using YiKits.Dialog;
using ComboBox = System.Windows.Controls.ComboBox;
using ContextMenu = System.Windows.Controls.ContextMenu;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListViewItem = System.Windows.Controls.ListViewItem;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace holey.KitWin;

public partial class MouseAndKeyboard : Window, INotifyPropertyChanged
{
    public ObservableCollection<Instruction> Instructions { get; } = new();
    private readonly Dictionary<string, int> _variables = new();
    private bool _isExecuting = false;
    private bool _isRecording = false;
    public static MouseAndKeyboard me = null;
    private int _globalDelayMs = 0;

    // 跟踪当前按下的修饰键
    private HashSet<string> _pressedModifiers = new HashSet<string>();

    // 滚轮滚动累加
    private int _wheelAccumulator = 0;
    private DateTime _lastWheelTime = DateTime.MinValue;
    private const int WHEEL_ACCUMULATE_MS = 1000; // 1000ms内的滚轮操作累加

    private CancellationTokenSource _cts = null;

    // === 新增：文件路径管理 ===
    private string _currentFilePath = GetDefaultFilePath();

    // 获取默认文件路径
    private static string GetDefaultFilePath()
    {
        try
        {
            // 获取当前程序所在路径
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(exeDir, "m_k.ys");
        }
        catch
        {
            // 出错时使用当前程序所在目录
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "m_k.ys");
        }
    }

    public int GlobalDelayMs
    {
        get => _globalDelayMs;
        set { _globalDelayMs = value; OnPropertyChanged(); }
    }

    private string _statusMessage = "就绪";
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    private const int HOTKEY_ID = 9001;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private LowLevelProc _mouseProc;
    private LowLevelProc _keyboardProc;

    // 热键修饰符常量
    private const uint MOD_NONE = 0x0000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    // 虚拟键码常量
    private const uint VK_E = 0x45;
    private const uint VK_R = 0x52;

    // 热键设置属性
    public List<string> HotKeyModifiers { get; } = new List<string> { "", "Alt", "Ctrl", "Shift", "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift" };
    public List<string> HotKeys { get; } = new List<string> {
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        ",", ".", "/", ";", "'", "[", "]", "\\", "`", "-", "=", "~", "!", "@", "#", "$", "%", "^", "&", "*", "(", ")", "_", "+"
    };

    private string _selectedHotKeyModifier = "Alt";
    public string SelectedHotKeyModifier
    {
        get => _selectedHotKeyModifier;
        set { _selectedHotKeyModifier = value; OnPropertyChanged(); }
    }

    private string _selectedHotKey = "R";
    public string SelectedHotKey
    {
        get => _selectedHotKey;
        set { _selectedHotKey = value; OnPropertyChanged(); }
    }

    public MouseAndKeyboard()
    {
        InitializeComponent();
        me = this;
        DataContext = this;

        // 初始化热键ComboBox的ItemsSource
        var modifierComboBox = (ComboBox)FindName("HotKeyModifierComboBox");
        var keyComboBox = (ComboBox)FindName("HotKeyComboBox");
        if (modifierComboBox != null)
            modifierComboBox.ItemsSource = HotKeyModifiers;
        if (keyComboBox != null)
            keyComboBox.ItemsSource = HotKeys;

        // 监听集合变化（增删）
        Instructions.CollectionChanged += (s, e) =>
        {
            if (!_isExecuting && !_isRecording)
                SaveToFile(_currentFilePath);
        };

        // 启动鼠标坐标监听
        var timer = new System.Windows.Threading.DispatcherTimer();
        timer.Interval = TimeSpan.FromMilliseconds(50);
        timer.Tick += (s, e) => UpdateMousePosition();
        timer.Start();
    }

    // 应用热键按钮点击事件
    private void ApplyHotKey_Click(object sender, RoutedEventArgs e)
    {
        // 先注销当前热键
        UnregisterHotKey();
        // 重新注册新热键
        RegisterHotKey();
        // 保存热键设置
        SaveHotKeyConfig();
    }

    // 加载热键设置
    private void LoadHotKeyConfig()
    {
        try
        {
            // 使用YiKit.IniGet加载热键设置
            string modifier = YiKit.IniGet("hotkey_modifier", "config", def: "Alt");
            string key = YiKit.IniGet("hotkey_key", "config", def: "R");
            GlobalDelayMs = YiKit.IniGetInt("global_delay_ms", area:"config", def: 0);
            // 验证设置是否有效
            if (HotKeyModifiers.Contains(modifier))
                SelectedHotKeyModifier = modifier;
            else
                SelectedHotKeyModifier = "Alt";
            
            if (HotKeys.Contains(key))
                SelectedHotKey = key;
            else
                SelectedHotKey = "R";
            
            StatusMessage = "热键设置已加载:"+SelectedHotKeyModifier+":"+SelectedHotKey;
            YiKit.Print(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = "加载热键设置失败: " + ex.Message;
            YiKit.Print(StatusMessage);
        }
    }

    // 保存热键设置
    private void SaveHotKeyConfig()
    {
        try
        {
            // 使用YiKit.IniWrite保存热键设置
            YiKit.IniWrite("hotkey_modifier", SelectedHotKeyModifier, "config");
            YiKit.IniWrite("hotkey_key", SelectedHotKey, "config");
            YiKit.IniWrite("global_delay_ms", GlobalDelayMs+"", "config");
            StatusMessage = "热键设置已保存";
            YiKit.Print(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = "保存热键设置失败: " + ex.Message;
            YiKit.Print(StatusMessage);
        }
    }

    // 根据选择的修饰符获取对应的MOD值
    private uint GetModifierValue(string modifier)
    {
        switch (modifier)
        {
            case "":
                return MOD_NONE;
            case "Alt":
                return MOD_ALT;
            case "Ctrl":
                return MOD_CONTROL;
            case "Shift":
                return MOD_SHIFT;
            case "Ctrl+Alt":
                return MOD_CONTROL | MOD_ALT;
            case "Ctrl+Shift":
                return MOD_CONTROL | MOD_SHIFT;
            case "Alt+Shift":
                return MOD_ALT | MOD_SHIFT;
            default:
                return MOD_ALT;
        }
    }

    // 根据选择的按键获取对应的虚拟键码
    private uint GetVirtualKeyCode(string key)
    {
        // 处理功能键
        if (key.StartsWith("F"))
        {
            if (int.TryParse(key.Substring(1), out int fKeyNumber))
            {
                if (fKeyNumber >= 1 && fKeyNumber <= 12)
                    return 0x70 + (uint)(fKeyNumber - 1); // F1=0x70, F2=0x71, ..., F12=0x7B
            }
        }
        // 处理特殊字符
        else if (key.Length == 1)
        {
            switch (key[0])
            {
                case ',': return 0xBC; // 逗号
                case '.': return 0xBE; // 句号
                case '/': return 0xBF; // 斜杠
                case ';': return 0xBA; // 分号
                case '\'': return 0xDE; // 单引号
                case '[': return 0xDB; // 左方括号
                case ']': return 0xDD; // 右方括号
                case '\\': return 0xDC; // 反斜杠
                case '`': return 0xC0; // 反引号
                case '-': return 0xBD; // 减号
                case '=': return 0xBB; // 等号
                case '~': return 0xC0; // 波浪号（与反引号相同）
                case '!': return 0x31; // 感叹号（与1相同）
                case '@': return 0x32; // @符号（与2相同）
                case '#': return 0x33; // 井号（与3相同）
                case '$': return 0x34; // 美元符号（与4相同）
                case '%': return 0x35; // 百分号（与5相同）
                case '^': return 0x36; // 脱字符（与6相同）
                case '&': return 0x37; // &符号（与7相同）
                case '*': return 0x38; // 星号（与8相同）
                case '(': return 0x39; // 左括号（与9相同）
                case ')': return 0x30; // 右括号（与0相同）
                case '_': return 0xBD; // 下划线（与减号相同）
                case '+': return 0xBB; // 加号（与等号相同）
                // 处理数字键
                case var c when char.IsDigit(c):
                    return 0x30 + (uint)(c - '0'); // 0=0x30, 1=0x31, ..., 9=0x39
                // 处理字母键
                case var c when char.IsLetter(c):
                    return (uint)char.ToUpper(c);
            }
        }
        return VK_R; // 默认返回R键的虚拟键码
    }

    private void UpdateMousePosition()
    {
        var pos = System.Windows.Forms.Cursor.Position;
        MousePositionText.Text = $"鼠标坐标: ({pos.X}, {pos.Y})";
    }

    private void MousePositionText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var pos = System.Windows.Forms.Cursor.Position;
        string coordText = $"{pos.X},{pos.Y}";
        System.Windows.Clipboard.SetText(coordText);
        StatusMessage = $"坐标 ({pos.X}, {pos.Y}) 已复制到剪贴板";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        LoadFromFile(_currentFilePath); // 自动加载
        LoadHotKeyConfig(); // 加载热键设置
        RegisterHotKey();
    }

    private void ContextMenu_KeyDown(object sender, KeyEventArgs e)
    {
        // 菜单弹出时的快捷键处理
        var contextMenu = sender as ContextMenu;
        if (contextMenu != null)
        {
            switch (e.Key)
            {
                case Key.T:
                    MoveToTop_Click(sender, e);
                    contextMenu.IsOpen = false;
                    // e.Handled = true;
                    break;
                case Key.B:
                    MoveToBottom_Click(sender, e);
                    // contextMenu.IsOpen = false;
                    e.Handled = true;
                    break;
                case Key.U:
                    MoveUpOne_Click(sender, e);
                    // contextMenu.IsOpen = false;
                    e.Handled = true;
                    break;
                case Key.N:
                    MoveDownOne_Click(sender, e);
                    // contextMenu.IsOpen = false;
                    e.Handled = true;
                    break;
                case Key.D:
                    DeleteCurrent_Click(sender, e);
                    contextMenu.IsOpen = false;
                    e.Handled = true;
                    break;
            }
        }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        UnregisterHotKey();
        StopRecording();
        _cts?.Cancel();
        _cts?.Dispose();
        me = null;
        
    }

    #region === 热键注册 ===

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private void RegisterHotKey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        uint modifier = GetModifierValue(SelectedHotKeyModifier);
        uint virtualKey = GetVirtualKeyCode(SelectedHotKey);
        YiKit.Print(modifier+virtualKey+":"+SelectedHotKeyModifier+SelectedHotKey);
        // 注册用户选择的热键
        bool success = RegisterHotKey(hwnd, HOTKEY_ID, modifier, virtualKey);
        if (success)
        {
            StatusMessage = $"热键 {SelectedHotKeyModifier}+{SelectedHotKey} 已注册成功";
            YiKit.Print(StatusMessage);
        }
        else
        {
            // 如果注册失败，尝试使用 Ctrl+Alt+R 组合（备用热键）
            success = RegisterHotKey(hwnd, HOTKEY_ID, MOD_CONTROL | MOD_ALT, VK_R);
            if (success)
            {
                StatusMessage = "热键 Ctrl+Alt+R 已注册成功（备用）";
                YiKit.Print(StatusMessage);
            }
            else
            {
                StatusMessage = "热键注册失败，请重启应用程序";
                YiKit.Print(StatusMessage);
            }
        }
    }

    private void UnregisterHotKey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd, HOTKEY_ID);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        
       
        
        if (source != null)
        {
            source.AddHook(HwndHook);
            StatusMessage = "消息钩子已添加";
            YiKit.Print(StatusMessage);
            // 在消息钩子添加后注册热键
            // RegisterHotKey();
        }
        else
        {
            StatusMessage = "消息钩子添加失败";
            YiKit.Print(StatusMessage);
        }
    }

    private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            StatusMessage = "热键被触发，正在执行/停止脚本";
            YiKit.Print(StatusMessage);
            _ = ToggleExecutionAsync();
            handled = true;
        }
        return IntPtr.Zero;
    }

    #endregion

    #region === 文件操作 ===

    private void SaveToFile(string filePath)
    {
        try
        {
            var json = JsonSerializer.Serialize(Instructions.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json, Encoding.UTF8);
            StatusMessage = $"已保存到 {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "保存失败";
        }
    }

    private void LoadFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Instructions.Clear();
                _currentFilePath = filePath;
                StatusMessage = "未找到脚本文件，已创建新脚本";
                return;
            }

            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize<List<Instruction>>(json) ?? new List<Instruction>();
            Instructions.Clear();
            foreach (var inst in loaded)
            {
                // 绑定修改回调
                inst.OnModified = () =>
                {
                    if (!_isExecuting && !_isRecording)
                        SaveToFile(_currentFilePath);
                };
                Instructions.Add(inst);
            }
            _currentFilePath = filePath;
            StatusMessage = $"已加载 {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"加载失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "加载失败";
        }
    }

    // UI 操作方法（绑定到按钮或菜单项）
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveToFile(_currentFilePath);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "YiScript 文件|*.ys|所有文件|*.*",
            DefaultExt = ".ys",
            FileName = "脚本.ys",
            InitialDirectory = System.IO.Path.GetTempPath()
        };

        if (dialog.ShowDialog() == true)
        {
            _currentFilePath = dialog.FileName;
            SaveToFile(_currentFilePath);
        }
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "YiScript 文件|*.ys|所有文件|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            LoadFromFile(dialog.FileName);
        }
    }

    private void DeleteCurrentFile_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show($"确定要删除文件 {_currentFilePath} 吗？", "确认删除",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            try
            {
                if (File.Exists(_currentFilePath))
                {
                    File.Delete(_currentFilePath);
                    Instructions.Clear();
                    StatusMessage = "文件已删除，脚本已清空";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    #endregion

    #region === UI 操作 ===

    private Instruction GetCurrentInstruction(object sender)
    {
        var menuItem = sender as MenuItem;
        var contextMenu = menuItem?.Parent as ContextMenu;
        var listViewItem = contextMenu?.PlacementTarget as ListViewItem;
        return listViewItem?.Content as Instruction;
    }

    private void MoveToTop_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = InstructionList.SelectedItems.Cast<Instruction>().ToList();
        if (selectedItems.Count == 0) return;

        // 按索引排序，从前往后删除，避免索引变化
        var sortedItems = selectedItems.OrderBy(i => Instructions.IndexOf(i)).ToList();
        int insertIndex = 0;
        foreach (var inst in sortedItems)
        {
            int currentIndex = Instructions.IndexOf(inst);
            if (currentIndex > insertIndex)
            {
                Instructions.RemoveAt(currentIndex);
                Instructions.Insert(insertIndex, inst);
            }
            insertIndex++;
        }

        // 重新选中移动的项目
        InstructionList.SelectedItems.Clear();
        foreach (var inst in selectedItems)
        {
            InstructionList.SelectedItems.Add(inst);
        }
    }

    private void MoveToBottom_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = InstructionList.SelectedItems.Cast<Instruction>().ToList();
        if (selectedItems.Count == 0) return;

        // 按索引排序，从后往前删除，避免索引变化
        var sortedItems = selectedItems.OrderByDescending(i => Instructions.IndexOf(i)).ToList();
        foreach (var inst in sortedItems)
        {
            int currentIndex = Instructions.IndexOf(inst);
            if (currentIndex < Instructions.Count - 1)
            {
                Instructions.RemoveAt(currentIndex);
                Instructions.Add(inst);
            }
        }

        // 重新选中移动的项目
        InstructionList.SelectedItems.Clear();
        foreach (var inst in selectedItems)
        {
            InstructionList.SelectedItems.Add(inst);
        }
    }

    private void MoveUpOne_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = InstructionList.SelectedItems.Cast<Instruction>().ToList();
        if (selectedItems.Count == 0) return;

        // 按索引排序，从前往后处理
        var sortedItems = selectedItems.OrderBy(i => Instructions.IndexOf(i)).ToList();
        foreach (var inst in sortedItems)
        {
            int index = Instructions.IndexOf(inst);
            if (index > 0)
            {
                Instructions.RemoveAt(index);
                Instructions.Insert(index - 1, inst);
            }
        }

        // 重新选中移动的项目
        InstructionList.SelectedItems.Clear();
        foreach (var inst in selectedItems)
        {
            InstructionList.SelectedItems.Add(inst);
        }
    }

    private void MoveDownOne_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = InstructionList.SelectedItems.Cast<Instruction>().ToList();
        if (selectedItems.Count == 0) return;

        // 按索引排序，从后往前处理
        var sortedItems = selectedItems.OrderByDescending(i => Instructions.IndexOf(i)).ToList();
        foreach (var inst in sortedItems)
        {
            int index = Instructions.IndexOf(inst);
            if (index < Instructions.Count - 1)
            {
                Instructions.RemoveAt(index);
                Instructions.Insert(index + 1, inst);
            }
        }

        // 重新选中移动的项目
        InstructionList.SelectedItems.Clear();
        foreach (var inst in selectedItems)
        {
            InstructionList.SelectedItems.Add(inst);
        }
    }

    private void MoveUpN_Click(object sender, RoutedEventArgs e)
    {
        MoveByOffset(sender, isUp: true);
    }

    private void MoveDownN_Click(object sender, RoutedEventArgs e)
    {
        MoveByOffset(sender, isUp: false);
    }

    private void MoveByOffset(object sender, bool isUp)
    {
        var inst = GetCurrentInstruction(sender);
        if (inst == null) return;

        var rets = YiKit.ShowInputDialog("移动行数", ["要移动的行数:1"]);
        if (rets.Count < 1) return;
        if (!int.TryParse(rets[0], out int n) || n <= 0)
        {
            MessageBox.Show("请输入有效的正整数。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int currentIndex = Instructions.IndexOf(inst);
        if (currentIndex == -1) return;

        int targetIndex = isUp
            ? Math.Max(0, currentIndex - n)
            : Math.Min(Instructions.Count - 1, currentIndex + n);

        if (targetIndex == currentIndex) return;

        Instructions.RemoveAt(currentIndex);

        if (!isUp && targetIndex > currentIndex)
        {
            targetIndex--;
        }

        Instructions.Insert(targetIndex, inst);
    }

    private void DeleteCurrent_Click(object sender, RoutedEventArgs e)
    {
        // 支持多选删除
        if (InstructionList.SelectedItems.Count > 0)
        {
            var toRemove = new List<Instruction>(InstructionList.SelectedItems.Cast<Instruction>());
            foreach (var item in toRemove)
            {
                Instructions.Remove(item);
            }
        }
        else
        {
            // 单选删除（从ContextMenu触发）
            var inst = GetCurrentInstruction(sender);
            if (inst != null)
            {
                Instructions.Remove(inst);
            }
        }
    }

    private void AddGenericInstruction(Instruction inst)
    {
        inst.OnModified = () =>
        {
            if (!_isExecuting && !_isRecording)
                SaveToFile(_currentFilePath);
        };
        Instructions.Add(inst);
    }

    private void AddMouseClick_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "MouseDown", Target = "+0,+0,Left", DelayMs = 0 });
        AddGenericInstruction(new Instruction { Type = "MouseUp", Target = "+0,+0,Left", DelayMs = 0 });
    }

    private void AddMouseRightClick_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "MouseDown", Target = "+0,+0,Right", DelayMs = 0 });
        AddGenericInstruction(new Instruction { Type = "MouseUp", Target = "+0,+0,Right", DelayMs = 0 });
    }

    private void AddKeyPress_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "KeyPress", Target = "A", DelayMs = 0 });
    }

    private void AddSetVar_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "SetVariable", VariableName = "counter", Target = "0" });
    }

    private void AddIfCondition_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "If", VariableName = "counter", ConditionOperator = ">=", Target = "5" });
    }

    private void AddLoopStart_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "LoopStart" });
    }

    private void AddLoopEnd_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "LoopEnd", Target = "3" });
    }

    private void AddMouseWheel_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "MouseWheel", Target = "120", DelayMs = 0 });
    }

    private void AddKeyDown_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "KeyDown", Target = "A", DelayMs = 0 });
    }

    private void AddKeyUp_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "KeyUp", Target = "A", DelayMs = 0 });
    }

    private void AddMouseDown_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "MouseDown", Target = "+0,+0,Left", DelayMs = 0 });
    }

    private void AddMouseUp_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "MouseUp", Target = "+0,+0,Left", DelayMs = 0 });
    }

    private void AddMouseRightDown_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "MouseDown", Target = "+0,+0,Right", DelayMs = 0 });
    }

    private void AddMouseRightUp_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "MouseUp", Target = "+0,+0,Right", DelayMs = 0 });
    }

    private async void Execute_Click(object sender, RoutedEventArgs e)
    {
        await ToggleExecutionAsync();
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (InstructionList.SelectedItems.Count == 0)
        {
            MessageBox.Show("请先选择要删除的指令。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var toRemove = new List<Instruction>(InstructionList.SelectedItems.Cast<Instruction>());
        foreach (var item in toRemove)
        {
            Instructions.Remove(item);
        }
    }

    private void ClearList_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("确定要清空所有指令吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            Instructions.Clear();
        }
    }

    private void ToggleRecord_Click(object sender, RoutedEventArgs e)
    {
        if (!_isRecording)
        {
            StartRecording();
        }
        else
        {
            StopRecording();
        }
    }

    #endregion

    #region === 录制功能 ===

    private void StartRecording()
    {
        if (_isRecording) return;
        _isRecording = true;
        
        // 重置滚轮累加器
        _wheelAccumulator = 0;
        _lastWheelTime = DateTime.MinValue;
        
        StatusMessage = "录制中...（点击鼠标或按键以记录）";

        _mouseProc = HookCallbackMouse;
        _keyboardProc = HookCallbackKeyboard;

        _mouseHookId = SetHook(_mouseProc, WH_MOUSE_LL);
        _keyboardHookId = SetHook(_keyboardProc, WH_KEYBOARD_LL);
    }

    private void StopRecording()
    {
        if (!_isRecording) return;
        
        // 发送剩余的滚轮累加值
        if (_wheelAccumulator != 0)
        {
            Dispatcher.Invoke(() =>
            {
                AddGenericInstruction(new Instruction
                {
                    Type = "MouseWheel",
                    Target = _wheelAccumulator.ToString(),
                    DelayMs = GlobalDelayMs
                });
            });
            _wheelAccumulator = 0;
        }
        
        _isRecording = false;
        StatusMessage = "就绪";

        if (_mouseHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
        if (_keyboardHookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }
    }

    private IntPtr SetHook(LowLevelProc proc, int hookType)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule)
        {
            return SetWindowsHookEx(hookType, proc, GetModuleHandle(curModule.ModuleName), 0);
        }
    }

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);
    private const int WH_MOUSE_LL = 14;
    private const int WH_KEYBOARD_LL = 13;

    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private IntPtr HookCallbackMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isRecording)
        {
            var mouseStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();

            if (msg == WM_LBUTTONDOWN)
            {
                Dispatcher.Invoke(() =>
                {
                    AddGenericInstruction(new Instruction
                    {
                        Type = "MouseDown",
                        Target = $"{mouseStruct.pt.x},{mouseStruct.pt.y},Left",
                        DelayMs = GlobalDelayMs
                    });
                });
            }
            else if (msg == WM_LBUTTONUP)
            {
                Dispatcher.Invoke(() =>
                {
                    AddGenericInstruction(new Instruction
                    {
                        Type = "MouseUp",
                        Target = $"{mouseStruct.pt.x},{mouseStruct.pt.y},Left",
                        DelayMs = GlobalDelayMs
                    });
                });
            }
            else if (msg == WM_RBUTTONDOWN)
            {
                Dispatcher.Invoke(() =>
                {
                    AddGenericInstruction(new Instruction
                    {
                        Type = "MouseDown",
                        Target = $"{mouseStruct.pt.x},{mouseStruct.pt.y},Right",
                        DelayMs = GlobalDelayMs
                    });
                });
            }
            else if (msg == WM_RBUTTONUP)
            {
                Dispatcher.Invoke(() =>
                {
                    AddGenericInstruction(new Instruction
                    {
                        Type = "MouseUp",
                        Target = $"{mouseStruct.pt.x},{mouseStruct.pt.y},Right",
                        DelayMs = GlobalDelayMs
                    });
                });
            }
            else if (msg == WM_MOUSEWHEEL)
            {
                short wheelDelta = (short)(mouseStruct.mouseData >> 16);
                
                // 累加滚轮滚动
                _wheelAccumulator += wheelDelta;
                
                // 检查是否需要发送累加的滚轮指令
                DateTime now = DateTime.Now;
                if (now - _lastWheelTime > TimeSpan.FromMilliseconds(WHEEL_ACCUMULATE_MS))
                {
                    // 发送累加的滚轮指令
                    if (_wheelAccumulator != 0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AddGenericInstruction(new Instruction
                            {
                                Type = "MouseWheel",
                                Target = _wheelAccumulator.ToString(),
                                DelayMs = GlobalDelayMs
                            });
                        });
                    }
                    // 重置累加器
                    _wheelAccumulator = 0;
                    _lastWheelTime = now;
                }
            }
        }
        return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    private IntPtr HookCallbackKeyboard(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isRecording)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            int msg = wParam.ToInt32();

            if (msg == WM_KEYDOWN)
            {
                Key key = KeyInterop.KeyFromVirtualKey(vkCode);
                string keyName = key.ToString();

                // 检查是否是修饰键
                if (IsModifierKey(keyName.ToUpper()))
                {
                    Dispatcher.Invoke(() =>
                    {
                        AddGenericInstruction(new Instruction
                        {
                            Type = "KeyDown",
                            Target = keyName,
                            DelayMs = GlobalDelayMs
                        });
                    });
                }
                else if (keyName != "None" && keyName.Length > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        AddGenericInstruction(new Instruction
                        {
                            Type = "KeyPress",
                            Target = keyName,
                            DelayMs = GlobalDelayMs
                        });
                    });
                }
            }
            else if (msg == WM_KEYUP)
            {
                Key key = KeyInterop.KeyFromVirtualKey(vkCode);
                string keyName = key.ToString();

                // 检查是否是修饰键
                if (IsModifierKey(keyName.ToUpper()))
                {
                    Dispatcher.Invoke(() =>
                    {
                        AddGenericInstruction(new Instruction
                        {
                            Type = "KeyUp",
                            Target = keyName,
                            DelayMs = GlobalDelayMs
                        });
                    });
                }
            }
        }
        return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    #endregion

    #region === ListView 拖拽 ===

    private Point? _dragStartPoint = null;
    private Instruction _draggedItem = null;

    private void ListView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedItem == null || _dragStartPoint == null) return;

        var currentPoint = e.GetPosition(null);
        var dragDistance = Math.Abs(currentPoint.X - _dragStartPoint.Value.X) +
                           Math.Abs(currentPoint.Y - _dragStartPoint.Value.Y);

        var minDragDistance = SystemParameters.MinimumHorizontalDragDistance +
                              SystemParameters.MinimumVerticalDragDistance;

        if (dragDistance > minDragDistance && e.LeftButton == MouseButtonState.Pressed)
        {
            var data = new DataObject();
            DragDrop.DoDragDrop(InstructionList, data, DragDropEffects.Move);
            _draggedItem = null;
            _dragStartPoint = null;
        }
    }

    private void ListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var originalSource = e.OriginalSource as DependencyObject;
        ListViewItem? listViewItem = null;
        while (originalSource != null)
        {
            if (originalSource is ListViewItem item)
            {
                listViewItem = item;
                break;
            }
            originalSource = VisualTreeHelper.GetParent(originalSource);
        }

        if (listViewItem == null) return;

        _draggedItem = listViewItem.Content as Instruction;
        _dragStartPoint = e.GetPosition(null);
    }

    private void ListView_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void ListView_Drop(object sender, DragEventArgs e)
    {
        if (_draggedItem == null || !Instructions.Contains(_draggedItem))
        {
            _draggedItem = null;
            _dragStartPoint = null;
            return;
        }

        Point dropPoint = e.GetPosition(InstructionList);
        ListViewItem targetItemContainer = GetListViewItemAt(dropPoint);

        int newIndex;
        if (targetItemContainer != null && targetItemContainer.Content is Instruction targetInst)
        {
            newIndex = Instructions.IndexOf(targetInst);
        }
        else
        {
            newIndex = Instructions.Count;
        }

        int oldIndex = Instructions.IndexOf(_draggedItem);

        if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
        {
            if (newIndex > oldIndex)
            {
                newIndex--;
            }

            Instructions.RemoveAt(oldIndex);
            Instructions.Insert(newIndex, _draggedItem);
        }

        _draggedItem = null;
        _dragStartPoint = null;
        e.Handled = true;
    }

    private ListViewItem? GetListViewItemAt(Point point)
    {
        var element = InstructionList.InputHitTest(point) as DependencyObject;
        while (element != null && !(element is ListViewItem))
        {
            element = VisualTreeHelper.GetParent(element);
        }
        return element as ListViewItem;
    }

    #endregion

    #region === 执行引擎 ===

    private async Task ToggleExecutionAsync()
    {
        if (_isExecuting)
        {
            _cts?.Cancel();
            StatusMessage = "正在停止...";
        }
        else
        {
            await StartExecutionAsync();
        }
    }

    private async Task StartExecutionAsync()
    {
        await Task.Delay(600);

        if (_isExecuting) return;

        _isExecuting = true;
        _cts = new CancellationTokenSource();
        StatusMessage = "执行中...（再次点击可停止）";

        try
        {
            await ExecuteInstructions(new List<Instruction>(Instructions), _cts.Token);
            StatusMessage = "执行完成";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已停止";
        }
        catch (Exception ex)
        {
            StatusMessage = "错误: " + ex.Message;
        }
        finally
        {
            _isExecuting = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task ExecuteInstructions(List<Instruction> insts, CancellationToken cancellationToken)
    {
        _variables.Clear();
        var stack = new Stack<LoopContext>();
        
        for (int i = 0; i < insts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inst = insts[i];
            if (!inst.IsEnabled) continue;

            // 显示当前执行的指令
            StatusMessage = $"执行中 ({i+1}/{insts.Count}): {inst.Type} {inst.Target}";

            // 执行指令前清空修饰键状态（确保每次执行从干净状态开始）
            if (i == 0)
                _pressedModifiers.Clear();

            int delay = inst.DelayMs > 0 ? inst.DelayMs : GlobalDelayMs;
            if (delay > 0)
             await Task.Delay(delay, cancellationToken); // 指令之间的延迟,放在开头等待
            switch (inst.Type)
            {
                case "SetVariable":
                    if (int.TryParse(inst.Target, out int val))
                        _variables[inst.VariableName] = val;
                    break;

                case "If":
                    if (!_variables.TryGetValue(inst.VariableName, out int varVal)) varVal = 0;
                    if (!int.TryParse(inst.Target, out int cmpVal)) cmpVal = 0;
                    bool conditionMet = Evaluate(varVal, inst.ConditionOperator, cmpVal);
                    if (!conditionMet)
                    {
                        i++; // 跳过下一条（假设是 Else 或 EndIf）
                    }
                    break;

                case "LoopStart":
                    stack.Push(new LoopContext { StartIndex = i, Count = 0 });
                    break;

                case "LoopEnd":
                    if (stack.Count == 0) break;
                    var loop = stack.Peek();
                    int times = int.TryParse(inst.Target, out int t) ? t : 1;
                    loop.Count++;
                    if (loop.Count < times)
                    {
                        i = loop.StartIndex;
                    }
                    else
                    {
                        stack.Pop();
                    }
                    break;

                case "MouseClick":
                    if (TryParseMousePosition(inst.Target, out int x1, out int y1))
                    {
                        
                        SetCursorPos(x1, y1);
                        if (delay > 0)
                            await Task.Delay(delay/2, cancellationToken); // 指令之间的延迟,放在开头等待
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                        if (delay > 0)
                            await Task.Delay(delay/2, cancellationToken); // 指令之间的延迟,放在开头等待
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    }
                    break;

                case "MouseRightClick":
                    if (TryParseMousePosition(inst.Target, out int x2, out int y2))
                    {
                     
                        SetCursorPos(x2, y2);// 移动鼠标到目标位置
                        if (delay > 0)
                            await Task.Delay(delay/2, cancellationToken); // 指令之间的延迟,放在开头等待
                        mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                        if (delay > 0)
                            await Task.Delay(delay/2, cancellationToken); // 指令之间的延迟,放在开头等待
                        
                        mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                    }
                    break;

                case "KeyPress":
                    SendKeyWithModifiers(inst.Target);
                    break;

                case "MouseWheel":
                    if (int.TryParse(inst.Target, out int wheelDelta))
                    {
                        mouse_event(MOUSEEVENTF_WHEEL, 0, 0, (uint)wheelDelta, UIntPtr.Zero);
                    }
                    break;

                case "KeyDown":
                    SendKeyDownWithModifiers(inst.Target);
                    break;

                case "KeyUp":
                    SendKeyUpWithModifiers(inst.Target);
                    break;

                case "MouseDown":
                    if (TryParseMousePosition(inst.Target, out int mx1, out int my1, out string button1))
                    {
                        SetCursorPos(mx1, my1);
                        await Task.Delay(1, cancellationToken);
                        if (button1?.ToUpper() == "RIGHT")
                            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, UIntPtr.Zero);
                        else
                            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                    }
                    break;

                case "MouseUp":
                    if (TryParseMousePosition(inst.Target, out int mx2, out int my2, out string button2))
                    {
                        SetCursorPos(mx2, my2);
                        await Task.Delay(1, cancellationToken);
                        if (button2?.ToUpper() == "RIGHT")
                            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, UIntPtr.Zero);
                        else
                            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                    }
                    break;
            }

           
        }
    }

    private bool TryParseMousePosition(string target, out int x, out int y, out string button)
    {
        x = 0; y = 0; button = null;
        var parts = target.Split(',');
        if (parts.Length < 2) return false;

        string xPart = parts[0].Trim();
        string yPart = parts[1].Trim();

        var current = System.Windows.Forms.Cursor.Position;

        if (xPart.StartsWith("+") || xPart.StartsWith("-"))
        {
            if (!int.TryParse(xPart, out int dx)) return false;
            x = current.X + dx;
        }
        else
        {
            if (!int.TryParse(xPart, out x)) return false;
        }

        if (yPart.StartsWith("+") || yPart.StartsWith("-"))
        {
            if (!int.TryParse(yPart, out int dy)) return false;
            y = current.Y + dy;
        }
        else
        {
            if (!int.TryParse(yPart, out y)) return false;
        }

        if (parts.Length >= 3)
        {
            button = parts[2].Trim();
        }

        return true;
    }

    private bool TryParseMousePosition(string target, out int x, out int y)
    {
        return TryParseMousePosition(target, out x, out y, out _);
    }

    private bool Evaluate(int left, string op, int right) => op switch
    {
        "==" => left == right,
        "!=" => left != right,
        ">" => left > right,
        ">=" => left >= right,
        "<" => left < right,
        "<=" => left <= right,
        _ => false
    };

    private void SendKeyWithModifiers(string keyInput)
    {
        string trimmedKey = keyInput.Trim();
        string upperKey = trimmedKey.ToUpper();

        // 检查是否有修饰键按下
        if (_pressedModifiers.Count > 0)
        {
            // 有修饰键按下，使用 keybd_event 发送组合键
            SendKeyWithPressedModifiers(trimmedKey);
            return;
        }

        // 检查是否是纯修饰键（单独按ALT/CTRL/SHIFT）
        if (IsModifierKey(upperKey))
        {
            SendModifierKeyAlone(upperKey);
            return;
        }

        // 检查是否是特殊功能键（单个键）
        if (IsSpecialKey(upperKey))
        {
            string sendKey = ConvertToSendKeysFormat(trimmedKey);
            SendKeys.SendWait("{" + sendKey + "}");
            return;
        }

        // 如果是多个字符，直接发送文本
        if (trimmedKey.Length > 1)
        {
            SendKeys.SendWait(trimmedKey);
        }
        else
        {
            // 单个字符，使用原来的方式
            string sendKey = ConvertToSendKeysFormat(trimmedKey);
            SendKeys.SendWait("{" + sendKey + "}");
        }
    }

    private void SendKeyWithPressedModifiers(string keyInput)
    {
        // 构建组合键：修饰键 + 主键
        string trimmedKey = keyInput.Trim();
        string upperKey = trimmedKey.ToUpper();

        // 获取主键的虚拟键码
        byte mainVkCode = GetVirtualKeyCodeForSend(trimmedKey);

        // 按顺序发送：按下所有修饰键 -> 按下主键 -> 弹起主键 -> 弹起所有修饰键
        foreach (string mod in _pressedModifiers)
        {
            byte modVkCode = mod switch
            {
                "ALT" => 0x12,
                "CTRL" or "CONTROL" => 0x11,
                "SHIFT" => 0x10,
                "LWIN" => 0x5B,
                "RWIN" => 0x5C,
                _ => 0
            };
            if (modVkCode != 0)
                keybd_event(modVkCode, 0, 0, UIntPtr.Zero);
        }

        if (mainVkCode != 0)
        {
            keybd_event(mainVkCode, 0, 0, UIntPtr.Zero);  // 按下主键
            Thread.Sleep(10);
            keybd_event(mainVkCode, 0, 2, UIntPtr.Zero);  // 弹起主键
        }

        // 弹起所有修饰键（逆序）
        foreach (string mod in _pressedModifiers.Reverse())
        {
            byte modVkCode = mod switch
            {
                "ALT" => 0x12,
                "CTRL" or "CONTROL" => 0x11,
                "SHIFT" => 0x10,
                "LWIN" => 0x5B,
                "RWIN" => 0x5C,
                _ => 0
            };
            if (modVkCode != 0)
                keybd_event(modVkCode, 0, 2, UIntPtr.Zero);
        }
    }

    private byte GetVirtualKeyCodeForSend(string keyInput)
    {
        string upperKey = keyInput.ToUpper().Trim();
        
        // 处理字母和数字
        if (upperKey.Length == 1)
        {
            char c = upperKey[0];
            if (char.IsLetterOrDigit(c))
                return (byte)c;
        }

        // 处理特殊键
        return upperKey switch
        {
            "ENTER" => 0x0D,
            "TAB" => 0x09,
            "BACKSPACE" => 0x08,
            "DELETE" => 0x2E,
            "ESC" => 0x1B,
            "SPACE" => 0x20,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "HOME" => 0x24,
            "END" => 0x23,
            "PAGEUP" => 0x21,
            "PAGEDOWN" => 0x22,
            "INSERT" => 0x2D,
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            _ => 0
        };
    }

    private void SendKeyDownWithModifiers(string keyInput)
    {
        string upperKey = keyInput.ToUpper().Trim();
        if (IsModifierKey(upperKey))
        {
            SendModifierKeyDown(upperKey);
            return;
        }

        string sendKey = ConvertToSendKeysFormat(keyInput);
        SendKeys.SendWait("{" + sendKey + " DOWN}");
    }

    private void SendKeyUpWithModifiers(string keyInput)
    {
        string upperKey = keyInput.ToUpper().Trim();
        if (IsModifierKey(upperKey))
        {
            SendModifierKeyUp(upperKey);
            return;
        }

        string sendKey = ConvertToSendKeysFormat(keyInput);
        SendKeys.SendWait("{" + sendKey + " UP}");
    }

    private bool IsModifierKey(string key)
    {
        return key == "ALT" || key == "CTRL" || key == "CONTROL" || key == "SHIFT" ||
               key == "LWIN" || key == "RWIN";
    }

    private bool IsSpecialKey(string key)
    {
        return key is "ENTER" or "RETURN" or "TAB" or "BACKSPACE" or "DELETE" or "ESC" or "ESCAPE"
               or "SPACE" or "UP" or "DOWN" or "LEFT" or "RIGHT" or "HOME" or "END"
               or "PAGEUP" or "PAGEDOWN" or "INSERT" or "F1" or "F2" or "F3" or "F4"
               or "F5" or "F6" or "F7" or "F8" or "F9" or "F10" or "F11" or "F12"
               or "CAPSLOCK" or "NUMLOCK" or "SCROLLLOCK";
    }

    private void SendModifierKeyAlone(string key)
    {
        // 使用 keybd_event 发送单独的修饰键
        byte vkCode = key switch
        {
            "ALT" => 0x12,      // VK_MENU
            "CTRL" or "CONTROL" => 0x11,  // VK_CONTROL
            "SHIFT" => 0x10,    // VK_SHIFT
            "LWIN" => 0x5B,     // VK_LWIN
            "RWIN" => 0x5C,     // VK_RWIN
            _ => 0
        };

        if (vkCode != 0)
        {
            keybd_event(vkCode, 0, 0, UIntPtr.Zero);  // 按下
            Thread.Sleep(50);
            keybd_event(vkCode, 0, 2, UIntPtr.Zero);  // 弹起
        }
    }

    private void SendModifierKeyDown(string key)
    {
        byte vkCode = key switch
        {
            "ALT" => 0x12,
            "CTRL" or "CONTROL" => 0x11,
            "SHIFT" => 0x10,
            "LWIN" => 0x5B,
            "RWIN" => 0x5C,
            _ => 0
        };

        if (vkCode != 0)
        {
            keybd_event(vkCode, 0, 0, UIntPtr.Zero);
            // 添加到按下的修饰键集合中
            _pressedModifiers.Add(key.ToUpper());
        }
    }

    private void SendModifierKeyUp(string key)
    {
        byte vkCode = key switch
        {
            "ALT" => 0x12,
            "CTRL" or "CONTROL" => 0x11,
            "SHIFT" => 0x10,
            "LWIN" => 0x5B,
            "RWIN" => 0x5C,
            _ => 0
        };

        if (vkCode != 0)
        {
            keybd_event(vkCode, 0, 2, UIntPtr.Zero);
            // 从按下的修饰键集合中移除
            _pressedModifiers.Remove(key.ToUpper());
        }
    }

    private string ConvertToSendKeysFormat(string keyInput)
    {
        string upperKey = keyInput.ToUpper().Trim();
        return upperKey switch
        {
            "ENTER" => "ENTER",
            "RETURN" => "ENTER",
            "TAB" => "TAB",
            "BACKSPACE" => "BACKSPACE",
            "DELETE" => "DELETE",
            "ESC" => "ESC",
            "ESCAPE" => "ESC",
            "SPACE" => " ",
            "UP" => "UP",
            "DOWN" => "DOWN",
            "LEFT" => "LEFT",
            "RIGHT" => "RIGHT",
            "HOME" => "HOME",
            "END" => "END",
            "PAGEUP" => "PGUP",
            "PAGEDOWN" => "PGDN",
            "INSERT" => "INSERT",
            "F1" => "F1",
            "F2" => "F2",
            "F3" => "F3",
            "F4" => "F4",
            "F5" => "F5",
            "F6" => "F6",
            "F7" => "F7",
            "F8" => "F8",
            "F9" => "F9",
            "F10" => "F10",
            "F11" => "F11",
            "F12" => "F12",
            "CAPSLOCK" => "CAPSLOCK",
            "NUMLOCK" => "NUMLOCK",
            "SCROLLLOCK" => "SCROLLLOCK",
            _ => keyInput
        };
    }

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    #endregion

    #region === Win32 API ===

    const uint MOUSEEVENTF_LEFTDOWN = 0x02;
    const uint MOUSEEVENTF_LEFTUP = 0x04;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x08;
    const uint MOUSEEVENTF_RIGHTUP = 0x10;
    const uint MOUSEEVENTF_WHEEL = 0x0800;

    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    #endregion

    #region === 辅助类 ===

    public class Instruction : INotifyPropertyChanged
    {
        private string _type = "";
        private string _target = "";
        private string _variableName = "";
        private string _conditionOperator = "==";
        private int _delayMs = 0;
        private bool _isEnabled = true;

        public string Type { get => _type; set { _type = value; OnPropertyChanged(); } }
        public string Target { get => _target; set { _target = value; OnPropertyChanged(); } }
        public string VariableName { get => _variableName; set { _variableName = value; OnPropertyChanged(); } }
        public string ConditionOperator { get => _conditionOperator; set { _conditionOperator = value; OnPropertyChanged(); } }
        public int DelayMs { get => _delayMs; set { _delayMs = value; OnPropertyChanged(); } }
        public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPropertyChanged(); } }

        [JsonIgnore]
        public Action OnModified { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            OnModified?.Invoke();
        }
    }

    private class LoopContext
    {
        public int StartIndex { get; set; }
        public int Count { get; set; }
    }

    #endregion

    #region === INotifyPropertyChanged ===

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    #endregion


    private void UIElement_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        YiKit.IniWrite("global_delay_ms", GlobalDelayMs+"", "config");
        StatusMessage = $"全局延迟{GlobalDelayMs}ms已保存!";
    }
}