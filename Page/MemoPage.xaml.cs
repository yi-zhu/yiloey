using System.Windows;
using System.Windows.Controls;
namespace holey.Page;


/// <summary>
/// 备忘录内容设置页面
/// </summary>
public partial class MemoPage : System.Windows.Controls.Page
{
    public MemoPage()
    {
        InitializeComponent();
    }

    
    /// <summary>
    /// show or hide memo 备忘录
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void SOH_OnClick(object sender, RoutedEventArgs e)
    {
        if (MainWindow.MemoWindow == null)
        {
            MainWindow.MemoWindow=  new MemoWindow();
            MainWindow.MemoWindow.Show();
        }
        else
        {
            if (PresentationSource.FromVisual(MainWindow.MemoWindow) == null)
            {
               
                MainWindow. MemoWindow.Show();
            }
            else MainWindow.MemoWindow?.Hide();
        }
    }
}