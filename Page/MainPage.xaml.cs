using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace holey.Page;

public partial class MainPage : System.Windows.Controls.Page
{
    public MainPage()
    {
        InitializeComponent();
    }

    private void MainBtn_OnClick(object sender, RoutedEventArgs e)
    {
       MainWindow.StarteYi();
       
    }
}