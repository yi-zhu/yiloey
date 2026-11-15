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
using System.Windows.Media.Animation;
using System.Windows.Threading;
using holey.Kits;
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
    /// <summary>运行时变量（一律按字符串存；数值比较时再解析）。</summary>
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);
    private bool _isExecuting = false;

    public sealed class ConditionOpOption
    {
        public string Code { get; set; }
        public string Label { get; set; }
        public ConditionOpOption(string code, string label) { Code = code; Label = label; }
    }

    /// <summary>If 比较方式下拉（Code 写入脚本，Label 给人看）。</summary>
    public static ConditionOpOption[] ConditionOperatorOptions { get; } =
    {
        new ConditionOpOption("==", "等于 =="),
        new ConditionOpOption("!=", "不等于 !="),
        new ConditionOpOption(">", "大于 >"),
        new ConditionOpOption(">=", "大于等于 >="),
        new ConditionOpOption("<", "小于 <"),
        new ConditionOpOption("<=", "小于等于 <="),
        new ConditionOpOption("反>", "目标大于变量"),
        new ConditionOpOption("反>=", "目标≥变量"),
        new ConditionOpOption("反<", "目标小于变量"),
        new ConditionOpOption("反<=", "目标≤变量"),
        new ConditionOpOption("含", "变量包含目标"),
        new ConditionOpOption("不含", "变量不含目标"),
        new ConditionOpOption("被含", "目标包含变量"),
        new ConditionOpOption("不被含", "目标不含变量"),
        new ConditionOpOption("始", "变量开头是目标"),
        new ConditionOpOption("反始", "目标开头是变量"),
        new ConditionOpOption("尾", "变量结尾是目标"),
        new ConditionOpOption("反尾", "目标结尾是变量"),
    };
    private bool _isRecording = false;
    public static MouseAndKeyboard me = null;
    private int _globalDelayMs = 0;

    // 跟踪当前按下的修饰键
    private HashSet<string> _pressedModifiers = new HashSet<string>();

    // 滚轮滚动累加
    private int _wheelAccumulator = 0;
    private DateTime _lastWheelTime = DateTime.MinValue;
    private const int WHEEL_ACCUMULATE_MS = 1000; // 1000ms内的滚轮操作累加

    // 录制：按下暂存，抬起时合并为单击（长按/拖拽仍记为按下+抬起）
    private bool _recMouseDownPending;
    private int _recMouseDownX;
    private int _recMouseDownY;
    private bool _recMouseDownIsRight;
    private DateTime _recMouseDownTime = DateTime.MinValue;
    private const int RecClickMaxMs = 500;
    private const int RecClickMaxMovePx = 8;
    private readonly HashSet<int> _recKeysHeld = new();

    // 录制鼠标移动节流
    private int _recLastMoveX = int.MinValue;
    private int _recLastMoveY = int.MinValue;
    private DateTime _recLastMoveTime = DateTime.MinValue;
    private const int RecMoveMinPx = 12;
    private const int RecMoveMinMs = 40;

    private CancellationTokenSource _cts = null;
    private bool _suppressScriptListChange;

    // === 文件 / 脚本列表管理 ===
    private string _currentFilePath = GetDefaultFilePath();

    private static string GetScriptsDir()
    {
        try
        {
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string dir = Path.Combine(exeDir, "mk_scripts");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }
        catch
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mk_scripts");
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir;
        }
    }

    // 获取默认文件路径
    private static string GetDefaultFilePath()
    {
        return Path.Combine(GetScriptsDir(), "默认脚本.ys");
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
        // 迁移旧默认脚本到脚本目录
        TryMigrateLegacyDefaultScript();
        RefreshScriptList(selectPath: _currentFilePath);
        LoadFromFile(_currentFilePath); // 自动加载
        SyncScriptListSelection();
        LoadHotKeyConfig(); // 加载热键设置
        RegisterHotKey();
    }

    private void ContextMenu_KeyDown(object sender, KeyEventArgs e)
    {
        // 菜单弹出时的快捷键处理（沿用字母快捷键）
        var contextMenu = sender as ContextMenu;
        if (contextMenu == null) return;
        switch (e.Key)
        {
            case Key.T:
                MoveToTop_Click(sender, e);
                contextMenu.IsOpen = false;
                e.Handled = true;
                break;
            case Key.B:
                MoveToBottom_Click(sender, e);
                contextMenu.IsOpen = false;
                e.Handled = true;
                break;
            case Key.U:
                MoveUpOne_Click(sender, e);
                contextMenu.IsOpen = false;
                e.Handled = true;
                break;
            case Key.N:
                MoveDownOne_Click(sender, e);
                contextMenu.IsOpen = false;
                e.Handled = true;
                break;
            case Key.C:
                CopySelected_Click(sender, e);
                contextMenu.IsOpen = false;
                e.Handled = true;
                break;
            case Key.E:
                EnableSelected_Click(sender, e);
                contextMenu.IsOpen = false;
                e.Handled = true;
                break;
            case Key.X:
                DisableSelected_Click(sender, e);
                contextMenu.IsOpen = false;
                e.Handled = true;
                break;
            case Key.D:
                DeleteCurrent_Click(sender, e);
                contextMenu.IsOpen = false;
                e.Handled = true;
                break;
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
            FileName = Path.GetFileNameWithoutExtension(_currentFilePath) + ".ys",
            InitialDirectory = GetScriptsDir()
        };

        if (dialog.ShowDialog() == true)
        {
            _currentFilePath = dialog.FileName;
            SaveToFile(_currentFilePath);
            RefreshScriptList(selectPath: _currentFilePath);
        }
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "YiScript 文件|*.ys|所有文件|*.*",
            InitialDirectory = GetScriptsDir()
        };

        if (dialog.ShowDialog() == true)
        {
            LoadFromFile(dialog.FileName);
            // 若在脚本目录外，复制一份进列表目录便于管理
            EnsureInScriptsList(dialog.FileName);
            RefreshScriptList(selectPath: _currentFilePath);
        }
    }

    private void DeleteCurrentFile_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show($"确定要删除文件 {Path.GetFileName(_currentFilePath)} 吗？", "确认删除",
                            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            try
            {
                if (File.Exists(_currentFilePath))
                    File.Delete(_currentFilePath);
                Instructions.Clear();
                StatusMessage = "文件已删除";
                RefreshScriptList();
                if (ScriptListCombo.Items.Count > 0 && ScriptListCombo.Items[0] is ScriptListItem first)
                {
                    LoadFromFile(first.FullPath);
                    SyncScriptListSelection();
                }
                else
                {
                    _currentFilePath = GetDefaultFilePath();
                    SaveToFile(_currentFilePath);
                    RefreshScriptList(selectPath: _currentFilePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    #region === 脚本列表管理 ===

    private sealed class ScriptListItem
    {
        public string Name { get; set; } = "";
        public string FullPath { get; set; } = "";
        public override string ToString() => Name;
    }

    private void TryMigrateLegacyDefaultScript()
    {
        try
        {
            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string legacy = Path.Combine(exeDir, "m_k.ys");
            string dest = GetDefaultFilePath();
            if (File.Exists(legacy) && !File.Exists(dest))
                File.Copy(legacy, dest);
            if (!File.Exists(dest))
                File.WriteAllText(dest, "[]", Encoding.UTF8);
        }
        catch
        {
            // ignore
        }
    }

    private void RefreshScriptList(string? selectPath = null)
    {
        if (ScriptListCombo == null) return;
        _suppressScriptListChange = true;
        try
        {
            string dir = GetScriptsDir();
            var files = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.ys").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();

            ScriptListCombo.Items.Clear();
            foreach (var f in files)
            {
                ScriptListCombo.Items.Add(new ScriptListItem
                {
                    Name = Path.GetFileNameWithoutExtension(f),
                    FullPath = f
                });
            }

            string prefer = selectPath ?? _currentFilePath;
            SyncScriptListSelection(prefer);
        }
        finally
        {
            _suppressScriptListChange = false;
        }
    }

    private void SyncScriptListSelection(string? preferPath = null)
    {
        if (ScriptListCombo == null) return;
        string path = preferPath ?? _currentFilePath;
        _suppressScriptListChange = true;
        try
        {
            for (int i = 0; i < ScriptListCombo.Items.Count; i++)
            {
                if (ScriptListCombo.Items[i] is ScriptListItem it &&
                    string.Equals(it.FullPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    ScriptListCombo.SelectedIndex = i;
                    return;
                }
            }

            // 当前文件不在列表中时显示文件名
            if (!string.IsNullOrEmpty(path))
                ScriptListCombo.Text = Path.GetFileNameWithoutExtension(path);
        }
        finally
        {
            _suppressScriptListChange = false;
        }
    }

    private void EnsureInScriptsList(string filePath)
    {
        try
        {
            string dir = GetScriptsDir();
            if (string.Equals(Path.GetDirectoryName(filePath), dir, StringComparison.OrdinalIgnoreCase))
            {
                _currentFilePath = filePath;
                return;
            }

            string name = Path.GetFileName(filePath);
            string dest = Path.Combine(dir, name);
            if (!File.Exists(dest))
                File.Copy(filePath, dest);
            _currentFilePath = dest;
        }
        catch
        {
            _currentFilePath = filePath;
        }
    }

    private void ScriptListCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressScriptListChange) return;
        if (ScriptListCombo.SelectedItem is not ScriptListItem item) return;
        if (string.Equals(item.FullPath, _currentFilePath, StringComparison.OrdinalIgnoreCase)) return;

        if (!_isExecuting && !_isRecording && Instructions.Count > 0)
            SaveToFile(_currentFilePath);

        LoadFromFile(item.FullPath);
        StatusMessage = "已切换脚本: " + item.Name;
    }

    private void NewScript_Click(object sender, RoutedEventArgs e)
    {
        if (!_isExecuting && !_isRecording && Instructions.Count > 0)
            SaveToFile(_currentFilePath);

        var inputs = YiKit.ShowInputDialog("新建脚本", "脚本名称:新脚本");
        if (inputs == null || inputs.Count == 0 || string.IsNullOrWhiteSpace(inputs[0])) return;

        string name = SanitizeScriptName(inputs[0].Trim());
        string path = Path.Combine(GetScriptsDir(), name + ".ys");
        if (File.Exists(path))
        {
            MessageBox.Show("已存在同名脚本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Instructions.Clear();
        _currentFilePath = path;
        SaveToFile(path);
        RefreshScriptList(selectPath: path);
        StatusMessage = "已新建: " + name;
    }

    private void RenameScript_Click(object sender, RoutedEventArgs e)
    {
        string oldName = Path.GetFileNameWithoutExtension(_currentFilePath);
        var inputs = YiKit.ShowInputDialog("重命名脚本", "新名称:" + oldName);
        if (inputs == null || inputs.Count == 0 || string.IsNullOrWhiteSpace(inputs[0])) return;

        string name = SanitizeScriptName(inputs[0].Trim());
        string newPath = Path.Combine(GetScriptsDir(), name + ".ys");
        if (string.Equals(newPath, _currentFilePath, StringComparison.OrdinalIgnoreCase)) return;
        if (File.Exists(newPath))
        {
            MessageBox.Show("已存在同名脚本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            SaveToFile(_currentFilePath);
            if (!string.Equals(Path.GetDirectoryName(_currentFilePath), GetScriptsDir(),
                    StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(_currentFilePath, newPath, false);
            }
            else
            {
                File.Move(_currentFilePath, newPath);
            }

            _currentFilePath = newPath;
            RefreshScriptList(selectPath: newPath);
            StatusMessage = "已重命名为: " + name;
        }
        catch (Exception ex)
        {
            MessageBox.Show("重命名失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshScriptList_Click(object sender, RoutedEventArgs e)
    {
        RefreshScriptList(selectPath: _currentFilePath);
        StatusMessage = "脚本列表已刷新";
    }

    private static string SanitizeScriptName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        if (name.EndsWith(".ys", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 3);
        return string.IsNullOrWhiteSpace(name) ? "新脚本" : name.Trim();
    }

    #endregion

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
        var selected = GetSelectedInstructionsOrdered();
        if (selected.Count == 0)
        {
            var one = GetCurrentInstruction(sender);
            if (one != null) selected.Add(one);
        }

        if (selected.Count == 0) return;

        var rets = YiKit.ShowInputDialog("移动行数", ["要移动的行数:1"]);
        if (rets == null || rets.Count < 1) return;
        if (!int.TryParse(rets[0], out int n) || n <= 0)
        {
            MessageBox.Show("请输入有效的正整数。", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MoveSelectedBlock(selected, isUp ? -n : n);
    }

    /// <summary>将选中项作为整体上/下平移 delta 行（负=上，正=下），保持相对顺序。</summary>
    private void MoveSelectedBlock(List<Instruction> selected, int delta)
    {
        if (selected == null || selected.Count == 0 || delta == 0) return;
        var items = selected.OrderBy(i => Instructions.IndexOf(i)).ToList();
        int minIdx = Instructions.IndexOf(items[0]);
        int maxIdx = Instructions.IndexOf(items[items.Count - 1]);
        if (minIdx < 0 || maxIdx < 0) return;

        int newMin = minIdx + delta;
        newMin = Math.Max(0, Math.Min(newMin, Instructions.Count - items.Count));
        if (newMin == minIdx) return;

        foreach (var inst in items.AsEnumerable().Reverse())
            Instructions.Remove(inst);

        int insertAt = newMin;
        foreach (var inst in items)
            Instructions.Insert(insertAt++, inst);

        ReselectInstructions(items);
        StatusMessage = $"已移动 {items.Count} 项";
    }

    private List<Instruction> GetSelectedInstructionsOrdered()
    {
        return InstructionList.SelectedItems.Cast<Instruction>()
            .OrderBy(i => Instructions.IndexOf(i))
            .ToList();
    }

    private void ReselectInstructions(IEnumerable<Instruction> items)
    {
        InstructionList.SelectedItems.Clear();
        foreach (var inst in items)
            InstructionList.SelectedItems.Add(inst);
    }

    private void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedInstructionsOrdered();
        if (selected.Count == 0)
        {
            var one = GetCurrentInstruction(sender);
            if (one != null) selected.Add(one);
        }

        if (selected.Count == 0) return;

        int insertAt = Instructions.IndexOf(selected[selected.Count - 1]) + 1;
        var copies = new List<Instruction>();
        foreach (var src in selected)
        {
            var copy = CloneInstruction(src);
            AddGenericInstructionAt(copy, insertAt++);
            copies.Add(copy);
        }

        ReselectInstructions(copies);
        StatusMessage = $"已复制 {copies.Count} 项";
    }

    private void EnableSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedInstructionsOrdered();
        if (selected.Count == 0)
        {
            var one = GetCurrentInstruction(sender);
            if (one != null) selected.Add(one);
        }

        foreach (var inst in selected)
            inst.IsEnabled = true;
        StatusMessage = $"已启用 {selected.Count} 项";
    }

    private void DisableSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedInstructionsOrdered();
        if (selected.Count == 0)
        {
            var one = GetCurrentInstruction(sender);
            if (one != null) selected.Add(one);
        }

        foreach (var inst in selected)
            inst.IsEnabled = false;
        StatusMessage = $"已禁用 {selected.Count} 项";
    }

    private Instruction CloneInstruction(Instruction src)
    {
        return new Instruction
        {
            Type = src.Type,
            Target = src.Target,
            VariableName = src.VariableName,
            ConditionOperator = src.ConditionOperator,
            DelayMs = src.DelayMs,
            Modifiers = src.Modifiers,
            IsEnabled = src.IsEnabled
        };
    }

    private void AddGenericInstructionAt(Instruction inst, int index)
    {
        inst.OnModified = () =>
        {
            if (!_isExecuting && !_isRecording)
                SaveToFile(_currentFilePath);
        };
        if (index < 0 || index > Instructions.Count)
            Instructions.Add(inst);
        else
            Instructions.Insert(index, inst);
    }

    private void DeleteCurrent_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedInstructionsOrdered();
        if (selected.Count == 0)
        {
            var inst = GetCurrentInstruction(sender);
            if (inst != null) selected.Add(inst);
        }

        foreach (var item in selected)
            Instructions.Remove(item);
        if (selected.Count > 0)
            StatusMessage = $"已删除 {selected.Count} 项";
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
        // 左点 = 完整单击一条指令；按下/抬起请用「左按」「左弹」
        AddGenericInstruction(new Instruction { Type = "MouseClick", Target = "+0,+0", DelayMs = 0 });
    }

    private void AddMouseRightClick_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "MouseRightClick", Target = "+0,+0", DelayMs = 0 });
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
        // If：变量名 比较方式 目标/值（可文字可数值；「被含/反*」为左右对调）
        AddGenericInstruction(new Instruction
        {
            Type = "If",
            VariableName = "ocr_text",
            ConditionOperator = "含",
            Target = "确定"
        });
    }

    private void AddElse_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "Else" });
    }

    private void AddEndIf_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "EndIf" });
    }

    private void AddLabel_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "Label", Target = "start" });
    }

    private void AddGoto_Click(object sender, RoutedEventArgs e)
    {
        AddGenericInstruction(new Instruction { Type = "Goto", Target = "start" });
    }

    private void AddOcrWait_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureOcrReady()) return;
        // 目标=等待关键字(可空)；变量名=写入OCR全文；修饰符=区域；延迟=超时
        AddGenericInstruction(new Instruction
        {
            Type = "OcrWait",
            Target = "确定",
            VariableName = "ocr_text",
            DelayMs = 10000,
            Modifiers = "full"
        });
    }

    private void AddOcrClick_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureOcrReady()) return;
        // 目标=点击关键字；变量名=写入OCR全文；修饰符=区域
        AddGenericInstruction(new Instruction
        {
            Type = "OcrClick",
            Target = "确定",
            VariableName = "ocr_text",
            Modifiers = "full",
            DelayMs = 0
        });
    }

    private void AddOcrCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureOcrReady()) return;
        // OCR读取：目标=区域；变量名=写入OCR全文，再用 If 判断
        AddGenericInstruction(new Instruction
        {
            Type = "OcrCheck",
            Target = "full",
            VariableName = "ocr_text",
            Modifiers = "",
            DelayMs = 0
        });
    }

    private void PickOcrRegion_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
        try
        {
            string? region = OcrHelper.PickRegionString();
            if (string.IsNullOrEmpty(region))
            {
                StatusMessage = "已取消框选";
                return;
            }

            try { System.Windows.Clipboard.SetText(region); } catch { /* ignore */ }
            StatusMessage = "OCR 区域已复制: " + region + "（填到指令「变量名」）";
        }
        finally
        {
            WindowState = WindowState.Normal;
            Activate();
        }
    }

    private bool EnsureOcrReady()
    {
        if (OcrHelper.IsReady()) return true;
        MessageBox.Show("离线 OCR 未安装。请先在主菜单「三方 → ocr」安装语言包（安装后识别不联网）。",
            "OCR", MessageBoxButton.OK, MessageBoxImage.Information);
        StatusMessage = OcrHelper.StatusText();
        return false;
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

    private void AddMouseMove_Click(object sender, RoutedEventArgs e)
    {
        var pos = System.Windows.Forms.Cursor.Position;
        AddGenericInstruction(new Instruction
        {
            Type = "MouseMove",
            Target = $"{pos.X},{pos.Y}",
            DelayMs = 0
        });
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
        
        // 重置滚轮累加器 / 录制暂存
        _wheelAccumulator = 0;
        _lastWheelTime = DateTime.MinValue;
        _recMouseDownPending = false;
        _recKeysHeld.Clear();
        _recLastMoveX = int.MinValue;
        _recLastMoveY = int.MinValue;
        _recLastMoveTime = DateTime.MinValue;
        
        StatusMessage = "录制中...（移动/点击/按键将写入脚本）";

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
        _recMouseDownPending = false;
        _recKeysHeld.Clear();
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

    private const int WM_MOUSEMOVE = 0x0200;
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

            if (msg == WM_MOUSEMOVE)
            {
                // 按下拖拽过程中不单独记移动（抬起时用 down/up 坐标表达）
                if (_recMouseDownPending) { }
                else
                {
                    int x = mouseStruct.pt.x;
                    int y = mouseStruct.pt.y;
                    int dx = Math.Abs(x - _recLastMoveX);
                    int dy = Math.Abs(y - _recLastMoveY);
                    double ms = (DateTime.Now - _recLastMoveTime).TotalMilliseconds;
                    if (_recLastMoveX == int.MinValue ||
                        ((dx >= RecMoveMinPx || dy >= RecMoveMinPx) && ms >= RecMoveMinMs))
                    {
                        _recLastMoveX = x;
                        _recLastMoveY = y;
                        _recLastMoveTime = DateTime.Now;
                        Dispatcher.Invoke(() =>
                        {
                            AddGenericInstruction(new Instruction
                            {
                                Type = "MouseMove",
                                Target = $"{x},{y}",
                                DelayMs = GlobalDelayMs
                            });
                        });
                    }
                }
            }
            else if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN)
            {
                _recMouseDownPending = true;
                _recMouseDownX = mouseStruct.pt.x;
                _recMouseDownY = mouseStruct.pt.y;
                _recMouseDownIsRight = msg == WM_RBUTTONDOWN;
                _recMouseDownTime = DateTime.Now;
            }
            else if (msg == WM_LBUTTONUP || msg == WM_RBUTTONUP)
            {
                bool isRight = msg == WM_RBUTTONUP;
                int upX = mouseStruct.pt.x;
                int upY = mouseStruct.pt.y;
                bool pending = _recMouseDownPending && _recMouseDownIsRight == isRight;
                _recMouseDownPending = false;

                Dispatcher.Invoke(() =>
                {
                    if (pending)
                    {
                        int dt = (int)(DateTime.Now - _recMouseDownTime).TotalMilliseconds;
                        int dx = Math.Abs(upX - _recMouseDownX);
                        int dy = Math.Abs(upY - _recMouseDownY);
                        // 短按且几乎未移动 → 记为一条单击
                        if (dt <= RecClickMaxMs && dx <= RecClickMaxMovePx && dy <= RecClickMaxMovePx)
                        {
                            AddGenericInstruction(new Instruction
                            {
                                Type = isRight ? "MouseRightClick" : "MouseClick",
                                Target = $"{_recMouseDownX},{_recMouseDownY}",
                                DelayMs = GlobalDelayMs
                            });
                            return;
                        }

                        // 长按/拖拽 → 按下 + 抬起
                        string btn = isRight ? "Right" : "Left";
                        AddGenericInstruction(new Instruction
                        {
                            Type = "MouseDown",
                            Target = $"{_recMouseDownX},{_recMouseDownY},{btn}",
                            DelayMs = GlobalDelayMs
                        });
                        AddGenericInstruction(new Instruction
                        {
                            Type = "MouseUp",
                            Target = $"{upX},{upY},{btn}",
                            DelayMs = 0
                        });
                    }
                    else
                    {
                        string btn = isRight ? "Right" : "Left";
                        AddGenericInstruction(new Instruction
                        {
                            Type = "MouseUp",
                            Target = $"{upX},{upY},{btn}",
                            DelayMs = GlobalDelayMs
                        });
                    }
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
                // 过滤系统按键自动重复，避免一次按住生成多条 KeyPress
                if (!_recKeysHeld.Add(vkCode))
                    return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);

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
                _recKeysHeld.Remove(vkCode);
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

    #region === ListView 长按拖拽 + 右键多选 ===

    private Point? _dragStartPoint;
    private List<Instruction> _dragItems = new();
    private bool _longPressReady;
    private bool _isDragging;
    private DispatcherTimer _longPressTimer;
    private const int LongPressMs = 320;
    private int _previewInsertIndex = -1;
    private readonly List<ListViewItem> _dimmedItems = new();
    private bool _insertLinePulseStarted;
    /// <summary>插入点判定迟滞，避免在行中间来回抖。</summary>
    private const double InsertHysteresisPx = 6;

    private void EnsureLongPressTimer()
    {
        if (_longPressTimer != null) return;
        _longPressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(LongPressMs)
        };
        _longPressTimer.Tick += (s, e) =>
        {
            _longPressTimer.Stop();
            if (_dragItems.Count > 0 && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                _longPressReady = true;
                StatusMessage = $"长按就绪，拖动可移动 {_dragItems.Count} 项";
                Mouse.OverrideCursor = System.Windows.Input.Cursors.SizeAll;
            }
        };
    }

    private void ResetDragState()
    {
        _longPressTimer?.Stop();
        _dragStartPoint = null;
        _dragItems.Clear();
        _longPressReady = false;
        _isDragging = false;
        ClearDropPreview();
        ClearDimmedItems();
        HideDragChrome();
        if (Mouse.OverrideCursor == System.Windows.Input.Cursors.SizeAll)
            Mouse.OverrideCursor = null;
    }

    private void HideDragChrome()
    {
        if (DropInsertLine != null)
        {
            DropInsertLine.BeginAnimation(UIElement.OpacityProperty, null);
            DropInsertLine.Visibility = Visibility.Collapsed;
            DropInsertLine.Opacity = 0;
        }
        if (DragGhost != null)
            DragGhost.Visibility = Visibility.Collapsed;
        _previewInsertIndex = -1;
        _insertLinePulseStarted = false;
    }

    private void ClearDimmedItems()
    {
        foreach (var item in _dimmedItems)
        {
            item.BeginAnimation(UIElement.OpacityProperty, null);
            item.Opacity = 1;
        }
        _dimmedItems.Clear();
    }

    private void DimDragSourceItems(IEnumerable<Instruction> moving)
    {
        ClearDimmedItems();
        foreach (var inst in moving)
        {
            var container = InstructionList.ItemContainerGenerator.ContainerFromItem(inst) as ListViewItem;
            if (container == null) continue;
            _dimmedItems.Add(container);
            // 直接设透明度，避免动画期间与布局抢刷新
            container.BeginAnimation(UIElement.OpacityProperty, null);
            container.Opacity = 0.4;
        }
    }

    private void ClearDropPreview()
    {
        _previewInsertIndex = -1;
        if (DropInsertLine != null)
            DropInsertLine.Visibility = Visibility.Collapsed;
    }

    /// <summary>只更新落点线/幽灵，不改列表布局（改 Margin 会导致抖动）。</summary>
    private int UpdateDropPreview(Point dropPointInList, int dragCount)
    {
        int insertIndex = ResolveInsertIndex(dropPointInList, out double lineY);
        _previewInsertIndex = insertIndex;
        ShowInsertLine(lineY);
        UpdateDragGhost(dropPointInList, dragCount, insertIndex);
        return insertIndex;
    }

    private int ResolveInsertIndex(Point dropPointInList, out double lineY)
    {
        lineY = 8;
        var targetItem = GetListViewItemAt(dropPointInList);

        if (targetItem?.Content is Instruction targetInst && Instructions.Contains(targetInst))
        {
            int idx = Instructions.IndexOf(targetInst);
            var bounds = GetListViewItemBounds(targetItem);
            if (!bounds.HasValue)
            {
                lineY = dropPointInList.Y;
                return idx;
            }

            double mid = bounds.Value.Top + bounds.Value.Height / 2;
            // 迟滞：沿用上一插入点，只有明显越过中线才切换，避免中间来回抖
            bool below;
            if (_previewInsertIndex == idx)
                below = dropPointInList.Y > mid + InsertHysteresisPx;
            else if (_previewInsertIndex == idx + 1)
                below = dropPointInList.Y >= mid - InsertHysteresisPx;
            else
                below = dropPointInList.Y > mid;

            int insertIndex = below ? idx + 1 : idx;
            lineY = below ? bounds.Value.Bottom : bounds.Value.Top;
            return insertIndex;
        }

        if (Instructions.Count > 0)
        {
            var first = InstructionList.ItemContainerGenerator.ContainerFromIndex(0) as ListViewItem;
            var last = InstructionList.ItemContainerGenerator.ContainerFromIndex(Instructions.Count - 1) as ListViewItem;
            var firstB = first != null ? GetListViewItemBounds(first) : null;
            var lastB = last != null ? GetListViewItemBounds(last) : null;
            if (firstB.HasValue && dropPointInList.Y < firstB.Value.Top + 8)
            {
                lineY = firstB.Value.Top;
                return 0;
            }

            lineY = lastB?.Bottom ?? dropPointInList.Y;
            return Instructions.Count;
        }

        lineY = 8;
        return 0;
    }

    private void ShowInsertLine(double yInList)
    {
        if (DropInsertLine == null || DragOverlay == null) return;
        DropInsertLine.Visibility = Visibility.Visible;
        DropInsertLine.Width = Math.Max(120, InstructionList.ActualWidth - 24);
        Canvas.SetLeft(DropInsertLine, 8);
        // 直接定位，不每帧重启透明度动画
        Canvas.SetTop(DropInsertLine, yInList - 2);

        if (!_insertLinePulseStarted)
        {
            _insertLinePulseStarted = true;
            DropInsertLine.BeginAnimation(UIElement.OpacityProperty, null);
            DropInsertLine.Opacity = 0.95;
        }
    }

    private void UpdateDragGhost(Point posInList, int dragCount, int insertIndex)
    {
        if (DragGhost == null || DragGhostText == null) return;
        string tip;
        if (_dragItems.Count > 0)
        {
            var first = _dragItems[0];
            tip = dragCount == 1
                ? $"移动 → 第 {insertIndex + 1} 行  [{first.Type} {first.Target}]"
                : $"移动 {dragCount} 项 → 第 {insertIndex + 1} 行";
        }
        else
        {
            tip = $"插入到第 {insertIndex + 1} 行";
        }

        DragGhostText.Text = tip;
        DragGhost.Visibility = Visibility.Visible;
        Canvas.SetLeft(DragGhost, Math.Min(posInList.X + 16, Math.Max(0, DragOverlay.ActualWidth - 220)));
        Canvas.SetTop(DragGhost, Math.Max(0, posInList.Y + 12));
    }

    private void PulseDroppedItems(IEnumerable<Instruction> items)
    {
        foreach (var inst in items)
        {
            var container = InstructionList.ItemContainerGenerator.ContainerFromItem(inst) as ListViewItem;
            if (container == null) continue;

            var flash = new ColorAnimation(
                (Color)ColorConverter.ConvertFromString("#BBDEFB"),
                Colors.Transparent,
                TimeSpan.FromMilliseconds(450))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var brush = new SolidColorBrush(Colors.Transparent);
            container.Background = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, flash);

            if (container.RenderTransform is not TranslateTransform)
                container.RenderTransform = new TranslateTransform();
            var tt = (TranslateTransform)container.RenderTransform;
            var bounce = new DoubleAnimationUsingKeyFrames();
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(-6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
            bounce.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
                new QuadraticEase { EasingMode = EasingMode.EaseOut }));
            tt.BeginAnimation(TranslateTransform.YProperty, bounce);
        }
    }

    private static ListViewItem FindListViewItem(DependencyObject source)
    {
        while (source != null)
        {
            if (source is ListViewItem item) return item;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private static bool IsEditableHit(DependencyObject source)
    {
        while (source != null)
        {
            if (source is System.Windows.Controls.TextBox ||
                source is System.Windows.Controls.CheckBox ||
                source is System.Windows.Controls.ComboBox ||
                source is System.Windows.Controls.Primitives.ButtonBase)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void ListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var original = e.OriginalSource as DependencyObject;
        if (IsEditableHit(original))
        {
            ResetDragState();
            return;
        }

        var listViewItem = FindListViewItem(original);
        if (listViewItem?.Content is not Instruction inst) return;

        if (InstructionList.SelectedItems.Contains(inst) && InstructionList.SelectedItems.Count > 1)
            _dragItems = GetSelectedInstructionsOrdered();
        else
            _dragItems = new List<Instruction> { inst };

        _dragStartPoint = e.GetPosition(null);
        _longPressReady = false;
        _isDragging = false;
        EnsureLongPressTimer();
        _longPressTimer.Stop();
        _longPressTimer.Start();
    }

    private void ListView_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
            ResetDragState();
    }

    private void ListView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItems.Count == 0 || _dragStartPoint == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var currentPoint = e.GetPosition(null);
        var dx = Math.Abs(currentPoint.X - _dragStartPoint.Value.X);
        var dy = Math.Abs(currentPoint.Y - _dragStartPoint.Value.Y);

        if (!_longPressReady)
        {
            if (dx > 6 || dy > 6)
            {
                _longPressTimer?.Stop();
                ResetDragState();
            }
            return;
        }

        if (dx < SystemParameters.MinimumHorizontalDragDistance &&
            dy < SystemParameters.MinimumVerticalDragDistance)
            return;

        _isDragging = true;
        _longPressTimer?.Stop();
        Mouse.OverrideCursor = null;
        var payload = _dragItems.ToList();
        DimDragSourceItems(payload);
        try
        {
            var data = new DataObject("InstructionDrag", payload);
            DragDrop.DoDragDrop(InstructionList, data, DragDropEffects.Move);
        }
        finally
        {
            ResetDragState();
        }
    }

    private void ListView_GiveFeedback(object sender, System.Windows.GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        Mouse.SetCursor(System.Windows.Input.Cursors.SizeAll);
        // 拖动过程中持续刷新幽灵位置（相对列表）
        if (_isDragging && DragOverlay != null)
        {
            var p = Mouse.GetPosition(InstructionList);
            if (DragGhost != null && DragGhost.Visibility == Visibility.Visible)
            {
                Canvas.SetLeft(DragGhost, Math.Min(p.X + 16, Math.Max(0, DragOverlay.ActualWidth - 220)));
                Canvas.SetTop(DragGhost, Math.Max(0, p.Y + 12));
            }
        }
        e.Handled = true;
    }

    private void ListView_QueryContinueDrag(object sender, System.Windows.QueryContinueDragEventArgs e)
    {
        if (e.EscapePressed)
        {
            e.Action = System.Windows.DragAction.Cancel;
            ClearDropPreview();
            HideDragChrome();
        }
    }

    private void ListView_DragLeave(object sender, DragEventArgs e)
    {
        // 真正离开列表时再收起（子元素冒泡会误触发，用命中测试过滤）
        var pos = e.GetPosition(InstructionList);
        if (pos.X < 0 || pos.Y < 0 || pos.X > InstructionList.ActualWidth || pos.Y > InstructionList.ActualHeight)
        {
            ClearDropPreview();
            if (DropInsertLine != null) DropInsertLine.Visibility = Visibility.Collapsed;
        }
    }

    private void ListView_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("InstructionDrag"))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        var moving = e.Data.GetData("InstructionDrag") as List<Instruction>;
        int count = moving?.Count ?? _dragItems.Count;
        if (moving != null && moving.Count > 0)
            _dragItems = moving;

        var dropPoint = e.GetPosition(InstructionList);
        UpdateDropPreview(dropPoint, count);
        StatusMessage = $"拖到第 {_previewInsertIndex + 1} 行（共 {count} 项）";
        e.Handled = true;
    }

    private void ListView_Drop(object sender, DragEventArgs e)
    {
        var moving = e.Data.GetData("InstructionDrag") as List<Instruction>;
        if (moving == null || moving.Count == 0)
        {
            e.Handled = true;
            return;
        }

        moving = moving.Where(i => Instructions.Contains(i))
            .OrderBy(i => Instructions.IndexOf(i))
            .ToList();
        if (moving.Count == 0)
        {
            e.Handled = true;
            return;
        }

        Point dropPoint = e.GetPosition(InstructionList);
        int insertIndex = UpdateDropPreview(dropPoint, moving.Count);

        ClearDropPreview();
        ClearDimmedItems();
        HideDragChrome();

        int removeBefore = moving.Count(i => Instructions.IndexOf(i) < insertIndex);
        foreach (var inst in moving.AsEnumerable().Reverse())
            Instructions.Remove(inst);
        insertIndex -= removeBefore;
        insertIndex = Math.Max(0, Math.Min(insertIndex, Instructions.Count));

        for (int i = 0; i < moving.Count; i++)
            Instructions.Insert(insertIndex + i, moving[i]);

        ReselectInstructions(moving);
        StatusMessage = $"已拖动 {moving.Count} 项 → 第 {insertIndex + 1} 行";

        // 等容器生成后再做落点弹跳/闪绿
        Dispatcher.BeginInvoke(new Action(() => PulseDroppedItems(moving)), DispatcherPriority.Loaded);
        e.Handled = true;
    }

    private Rect? GetListViewItemBounds(ListViewItem item)
    {
        try
        {
            var transform = item.TransformToAncestor(InstructionList);
            var topLeft = transform.Transform(new Point(0, 0));
            return new Rect(topLeft, new System.Windows.Size(item.ActualWidth, item.ActualHeight));
        }
        catch
        {
            return null;
        }
    }

    private ListViewItem GetListViewItemAt(Point point)
    {
        var element = InstructionList.InputHitTest(point) as DependencyObject;
        return FindListViewItem(element);
    }

    /// <summary>右键已选中的项时保留多选，避免 ListView 清成单项。</summary>
    private void ListView_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindListViewItem(e.OriginalSource as DependencyObject);
        if (item?.Content is not Instruction inst) return;

        if (InstructionList.SelectedItems.Contains(inst) && InstructionList.SelectedItems.Count > 1)
        {
            e.Handled = true;
            var menu = item.ContextMenu ?? (ContextMenu)FindResource("InstructionContextMenu");
            if (menu != null)
            {
                menu.PlacementTarget = item;
                menu.IsOpen = true;
            }
        }
        else if (!InstructionList.SelectedItems.Contains(inst))
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control &&
                (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                InstructionList.SelectedItems.Clear();
            }
            InstructionList.SelectedItems.Add(inst);
        }
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
        var ifStack = new Stack<bool>(); // true=条件成立，正在执行 then 分支
        int maxGoto = 100000; // 防止死循环

        for (int i = 0; i < insts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inst = insts[i];
            if (!inst.IsEnabled) continue;

            // 显示当前执行的指令
            StatusMessage = $"执行中 ({i + 1}/{insts.Count}): {inst.Type} {inst.Target}";

            // 执行指令前清空修饰键状态（确保每次执行从干净状态开始）
            if (i == 0)
                _pressedModifiers.Clear();

            // Label 不延迟
            bool skipDelay = inst.Type is "Label" or "Else" or "EndIf";
            int delay = inst.DelayMs > 0 ? inst.DelayMs : GlobalDelayMs;
            if (!skipDelay && delay > 0 && inst.Type is not ("OcrWait"))
                await Task.Delay(delay, cancellationToken);

            switch (inst.Type)
            {
                case "SetVariable":
                    SetVar(inst.VariableName, inst.Target ?? "");
                    break;

                case "Label":
                    break;

                case "Goto":
                {
                    int idx = FindLabelIndex(insts, inst.Target);
                    if (idx < 0)
                    {
                        StatusMessage = "找不到标签: " + inst.Target;
                        break;
                    }

                    if (--maxGoto <= 0)
                        throw new InvalidOperationException("Goto 次数过多，可能死循环");
                    i = idx - 1;
                    break;
                }

                case "If":
                {
                    string left = GetVar(inst.VariableName);
                    string right = ResolveCompareRight(inst.Target);
                    bool conditionMet = EvaluateCondition(left, inst.ConditionOperator, right);
                    ifStack.Push(conditionMet);
                    if (!conditionMet)
                    {
                        int jump = FindMatchingElseOrEndIf(insts, i);
                        if (jump >= 0) i = jump - 1;
                    }
                    break;
                }

                case "Else":
                {
                    if (ifStack.Count > 0 && ifStack.Peek())
                    {
                        int end = FindMatchingEndIf(insts, i);
                        if (end >= 0) i = end - 1;
                    }
                    break;
                }

                case "EndIf":
                    if (ifStack.Count > 0) ifStack.Pop();
                    break;

                case "OcrWait":
                {
                    if (!OcrHelper.IsReady())
                        throw new InvalidOperationException(OcrHelper.StatusText());
                    string keyword = (inst.Target ?? "").Trim();
                    string region = ResolveOcrRegion(inst, forRead: false);
                    string outVar = string.IsNullOrWhiteSpace(inst.VariableName) ? "ocr_text" : inst.VariableName.Trim();
                    int timeout = inst.DelayMs > 0 ? inst.DelayMs : 10000;
                    var sw = Stopwatch.StartNew();
                    bool found = false;
                    string lastText = "";
                    while (sw.ElapsedMilliseconds < timeout)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var ocr = await OcrHelper.RecognizeAsync(region);
                        lastText = ocr?.Text ?? "";
                        StoreOcrText(outVar, lastText);

                        if (string.IsNullOrEmpty(keyword))
                        {
                            if (!string.IsNullOrWhiteSpace(lastText))
                            {
                                found = true;
                                break;
                            }
                        }
                        else if (OcrHelper.TextContains(ocr, keyword) ||
                                 OcrHelper.TryFindText(ocr, keyword, out _))
                        {
                            found = true;
                            break;
                        }

                        await Task.Delay(300, cancellationToken);
                    }

                    StoreOcrText(outVar, lastText);
                    SetVar("ocr_ok", found ? "1" : "0");
                    StatusMessage = found
                        ? $"OCR 等到 → {outVar}"
                        : $"OCR 超时 → {outVar}（仍已写入最后一次识别）";
                    break;
                }

                case "OcrClick":
                {
                    if (!OcrHelper.IsReady())
                        throw new InvalidOperationException(OcrHelper.StatusText());
                    string keyword = (inst.Target ?? "").Trim();
                    string region = ResolveOcrRegion(inst, forRead: false);
                    string outVar = string.IsNullOrWhiteSpace(inst.VariableName) ? "ocr_text" : inst.VariableName.Trim();
                    var ocr = await OcrHelper.RecognizeAsync(region);
                    string text = ocr?.Text ?? "";
                    StoreOcrText(outVar, text);

                    if (!string.IsNullOrEmpty(keyword) && OcrHelper.TryFindText(ocr, keyword, out var box))
                    {
                        int cx = box.Left + box.Width / 2;
                        int cy = box.Top + box.Height / 2;
                        SetCursorPos(cx, cy);
                        await Task.Delay(30, cancellationToken);
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                        SetVar("ocr_ok", "1");
                        StatusMessage = $"OCR 点击 [{keyword}] @ {cx},{cy} → {outVar}";
                    }
                    else
                    {
                        SetVar("ocr_ok", "0");
                        StatusMessage = "OCR 未找到可点击文字: " + keyword + " → " + outVar;
                    }

                    break;
                }

                case "OcrCheck":
                {
                    // OCR 读取：只写入变量，判断交给 If
                    if (!OcrHelper.IsReady())
                        throw new InvalidOperationException(OcrHelper.StatusText());
                    string region = ResolveOcrRegion(inst, forRead: true);
                    string outVar = string.IsNullOrWhiteSpace(inst.VariableName) ? "ocr_text" : inst.VariableName.Trim();
                    var ocr = await OcrHelper.RecognizeAsync(region);
                    string text = ocr?.Text ?? "";
                    StoreOcrText(outVar, text);
                    SetVar("ocr_ok", string.IsNullOrWhiteSpace(text) ? "0" : "1");
                    StatusMessage = $"OCR 读取 → {outVar}（{text.Length}字）";
                    break;
                }

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

                case "MouseMove":
                    if (TryParseMousePosition(inst.Target, out int mx, out int my))
                    {
                        SetCursorPos(mx, my);
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

    private void SetVar(string? name, string value)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        _variables[name.Trim()] = value ?? "";
    }

    private string GetVar(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        return _variables.TryGetValue(name.Trim(), out var v) ? (v ?? "") : "";
    }

    private void StoreOcrText(string outVar, string text)
    {
        SetVar(outVar, text ?? "");
        if (!string.Equals(outVar, "ocr_text", StringComparison.OrdinalIgnoreCase))
            SetVar("ocr_text", text ?? "");
    }

    /// <summary>目标/值：若写成 $变量名 则取变量内容，否则当字面量。</summary>
    private string ResolveCompareRight(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return "";
        target = target.Trim();
        if (target.StartsWith("$") && target.Length > 1)
            return GetVar(target.Substring(1));
        return target;
    }

    /// <summary>
    /// OCR 区域：读指令优先 Target，再 Modifiers；等/点优先 Modifiers，再兼容旧脚本 VariableName 当区域。
    /// </summary>
    private static string ResolveOcrRegion(Instruction inst, bool forRead)
    {
        string target = (inst.Target ?? "").Trim();
        string mod = (inst.Modifiers ?? "").Trim();
        string varName = (inst.VariableName ?? "").Trim();

        if (forRead)
        {
            if (LooksLikeOcrRegion(target)) return target;
            if (LooksLikeOcrRegion(mod)) return mod;
            return "full";
        }

        if (LooksLikeOcrRegion(mod)) return mod;
        // 兼容旧脚本：变量名写成 full / x,y,w,h
        if (LooksLikeOcrRegion(varName)) return varName;
        if (LooksLikeOcrRegion(target) && string.IsNullOrEmpty(mod)) return target;
        return string.IsNullOrEmpty(mod) ? "full" : mod;
    }

    private static bool LooksLikeOcrRegion(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        if (string.Equals(s, "full", StringComparison.OrdinalIgnoreCase)) return true;
        var parts = s.Split(',');
        if (parts.Length != 4) return false;
        return parts.All(p => int.TryParse(p.Trim(), out _));
    }

    private static bool TextContainsIgnoreCase(string hay, string needle)
    {
        if (string.IsNullOrEmpty(needle)) return true;
        if (string.IsNullOrEmpty(hay)) return false;
        return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 比较：左=变量值，右=目标/值。
    /// 「含」=变量包含目标；「被含」=目标包含变量；「反*」=数值/关系比较时左右对调。
    /// </summary>
    private bool EvaluateCondition(string leftRaw, string? opRaw, string rightRaw)
    {
        string left = leftRaw ?? "";
        string right = rightRaw ?? "";
        string op = (opRaw ?? "==").Trim();
        if (op.Length == 0) op = "==";

        // 文字专用（含反向）
        switch (op)
        {
            case "含":
            case "contains":
                return TextContainsIgnoreCase(left, right);
            case "不含":
            case "!contains":
                return !TextContainsIgnoreCase(left, right);
            case "被含":
            case "反含":
                return TextContainsIgnoreCase(right, left);
            case "不被含":
            case "反不含":
                return !TextContainsIgnoreCase(right, left);
            case "始":
            case "startswith":
                return left.StartsWith(right, StringComparison.OrdinalIgnoreCase);
            case "反始":
                return right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
            case "尾":
            case "endswith":
                return left.EndsWith(right, StringComparison.OrdinalIgnoreCase);
            case "反尾":
                return right.EndsWith(left, StringComparison.OrdinalIgnoreCase);
        }

        // 关系/相等：支持「反」前缀 → 左右对调
        bool reverse = op.StartsWith("反", StringComparison.Ordinal);
        if (reverse) op = op.Substring(1);
        if (reverse)
        {
            var tmp = left;
            left = right;
            right = tmp;
        }

        if (TryParseNumber(left, out double ln) && TryParseNumber(right, out double rn))
        {
            return op switch
            {
                "==" => Math.Abs(ln - rn) < 1e-9,
                "!=" => Math.Abs(ln - rn) >= 1e-9,
                ">" => ln > rn,
                ">=" => ln >= rn,
                "<" => ln < rn,
                "<=" => ln <= rn,
                _ => false
            };
        }

        int cmp = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        return op switch
        {
            "==" => cmp == 0,
            "!=" => cmp != 0,
            ">" => cmp > 0,
            ">=" => cmp >= 0,
            "<" => cmp < 0,
            "<=" => cmp <= 0,
            _ => false
        };
    }

    private static bool TryParseNumber(string s, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        return double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value)
            || double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.CurrentCulture, out value);
    }

    private bool Evaluate(int left, string? op, int right) =>
        EvaluateCondition(left.ToString(), op, right.ToString());

    private static int FindLabelIndex(List<Instruction> insts, string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return -1;
        label = label.Trim();
        for (int i = 0; i < insts.Count; i++)
        {
            if (insts[i].Type == "Label" &&
                string.Equals((insts[i].Target ?? "").Trim(), label, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    /// <summary>从 If 位置找同级 Else 或 EndIf。</summary>
    private static int FindMatchingElseOrEndIf(List<Instruction> insts, int ifIndex)
    {
        int depth = 0;
        for (int i = ifIndex + 1; i < insts.Count; i++)
        {
            string t = insts[i].Type ?? "";
            if (t == "If") depth++;
            else if (t == "EndIf")
            {
                if (depth == 0) return i;
                depth--;
            }
            else if (t == "Else" && depth == 0) return i;
        }

        return -1;
    }

    /// <summary>从 Else 位置找同级 EndIf。</summary>
    private static int FindMatchingEndIf(List<Instruction> insts, int elseIndex)
    {
        int depth = 0;
        for (int i = elseIndex + 1; i < insts.Count; i++)
        {
            string t = insts[i].Type ?? "";
            if (t == "If") depth++;
            else if (t == "EndIf")
            {
                if (depth == 0) return i;
                depth--;
            }
        }

        return -1;
    }

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
        private string _modifiers = "";
        public string Modifiers { get => _modifiers; set { _modifiers = value; OnPropertyChanged(); } }
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