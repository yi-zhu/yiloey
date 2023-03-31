using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace holey.Dialog;

public partial class InputDialog : Window
{

    
    private List<TextBox> textBoxes=new();
    /// <summary>
    /// 
    /// </summary>
    /// <param name="txt">第一个输入框</param>
    /// <param name="txt2">第二个输入框</param>
    /// <param name="txt3">第三个输入框</param>
    public InputDialog(string txt,params  string[] txts)
    { 
        InitializeComponent();
        Label.Text = txt;
        textBoxes.Add(TextBox);
        TextBox.Focus();
        if (txts!=null)
        {
            foreach (string s in txts)
            {
                DockPanel dockPanel = new DockPanel();
                TextBlock label = new TextBlock();
                label.Text = s+"";
                TextBox textBox = new TextBox();
                textBoxes.Add(textBox);
                dockPanel.Children.Add(label);
                dockPanel.Children.Add(textBox);
                DialogContent.Children.Add(dockPanel);
            }
        }
    }

    public event EventHandler Result;
    
    void acceptButton_Click(object sender, RoutedEventArgs e)
    {
        // Accept the dialog and return the dialog result
        DialogResult = true;
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        List<string> list = new();
        foreach (TextBox box in textBoxes)
        {
            list.Add(box.Text);
            
        }

        if (Result != null) Result(list, EventArgs.Empty);
        DialogResult = true;
        Close();
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}