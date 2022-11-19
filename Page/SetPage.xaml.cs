using System.Windows;
using System.Windows.Controls;

namespace holey.Page;

public partial class SetPage : System.Windows.Controls.Page
{
    public SetPage()
    {
        InitializeComponent();
    }

    private void AutoCheckBox_OnChecked(object sender, RoutedEventArgs e)
    {
        YiKit.SetAutoStart(isAuto:true);
    }

    private void AutoCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
    {
        YiKit.SetAutoStart(isAuto:false);

    }
}