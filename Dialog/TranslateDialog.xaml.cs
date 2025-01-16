using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using YiKits;
using Control = System.Windows.Controls.Control;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Panel = System.Windows.Controls.Panel;

namespace holey.Dialog;

public partial class TranslateDialog : Window
{
    public static TranslateDialog? Me = null;
    private readonly Storyboard _showStoryboard;

    public TranslateDialog()
    {
        InitializeComponent();
        Me = this;
        HideStoty();
        ShowInTaskbar = false;
        _showStoryboard = new Storyboard();
        var fadeInAnimation = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromSeconds(0.5)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(fadeInAnimation, LayoutRoot);
        Storyboard.SetTargetProperty(fadeInAnimation, new PropertyPath(OpacityProperty));
        _showStoryboard.Children.Add(fadeInAnimation);
    }

    void FormLoaded(object sender, RoutedEventArgs args)
    {
        return;
        //Variable to hold the handle for the form
        var hwnd = new WindowInteropHelper(this).Handle;
        //Performing some magic to hide the form from Alt+Tab
        YiKits.YiKit.SetWindowLong(hwnd, YiKit.GwlExstyle,
            (YiKit.GetWindowLong(hwnd, YiKit.GwlExstyle) | YiKit.WsExToolwindow) & ~YiKit.WsExAppwindow);
        //鼠标穿透
        // SetWindowLong(hwnd, GWL_EX_STYLE,0x20);
        // SetWindowLong(hwnd, GWL_EX_STYLE, (GetWindowLong(hwnd, GWL_EX_STYLE) | 0x00000020));
    }

    private void PetWindow_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>
    /// 按下回车键
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ZhTx_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            try
            {
                PetWindow.YiWscClient.Send("ait::" + YiKit.RemoveSpecialCharacters(ZhTx.Text));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "Error");
            }
        }
    }


    private void EnTx_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            try
            {
                //do what you want
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "Error");
            }
        }
    }

    private void UIElement_OnFocusableChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
    }

    private void UIElement_OnMouseLeave(object sender, MouseEventArgs e)
    {
        (sender as TextBlock).Foreground = Brushes.Transparent;
    }

    private void UIElement_OnMouseEnter(object sender, MouseEventArgs e)
    {
        (sender as TextBlock).Foreground = Brushes.Black;
    }

    private void ButtonBase_OnClick(object sender, RoutedEventArgs e)
    {
        // this.Hide();
        this.Close();
        Me = null;
    }

    private void EleTransform(Control ele, double from = 0, double to = 360, bool forever = false)
    {
        if (ele != null)
        {
            //设置旋转的方向
            RotateTransform rotate = new RotateTransform();
            //给对象绑定旋转中心
            ele.RenderTransform = rotate;
            //旋转中心设置百分比
            ele.RenderTransformOrigin = new Point(0.5, 0.5);
            Storyboard story = new Storyboard();
            DoubleAnimation da = new DoubleAnimation(from, to, new Duration(TimeSpan.FromSeconds(0.3)));
            Storyboard.SetTarget(da, ele);
            //设置旋转属性 欧拉角
            Storyboard.SetTargetProperty(da, new PropertyPath("RenderTransform.Angle"));

            if (forever)
            {
                //反着播放
                da.AutoReverse = true;
                //重复播放
                da.RepeatBehavior = RepeatBehavior.Forever;
            }

            story.Children.Add(da);
            story.Begin();
        }
    }

    private void Close_OnMouseEnter(object sender, MouseEventArgs e)
    {
        EleTransform(sender as Control, 0, 360, true);
        (sender as Control).Foreground = Brushes.DarkRed;
    }

    public void show()
    {
       
        System.Drawing.Point point = System.Windows.Forms.Control.MousePosition;
        this.Left = point.X+10;
        this.Top = point.Y+10;
        if (this.Left+this.Width>System.Windows.SystemParameters.PrimaryScreenWidth)
        {
            this.Left = System.Windows.SystemParameters.PrimaryScreenWidth - this.Width;
        }

        if (this.Top + this.Height > System.Windows.SystemParameters.PrimaryScreenHeight)
        {
            this.Top = System.Windows.SystemParameters.PrimaryScreenHeight - this.Height;
        }
      
       
        // YiKit.Print(this.Left+" "+this.Top);
        if (Visibility==Visibility.Visible)
        {
            return;
        }
   
        _showStoryboard.Begin();

        
        this.Show();
    }

    private Storyboard _hideStoryboard;

    private void HideStoty()
    {
        _hideStoryboard = new Storyboard();
        var fadeOutAnimation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromSeconds(0.5)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(fadeOutAnimation, LayoutRoot);
        Storyboard.SetTargetProperty(fadeOutAnimation, new PropertyPath(OpacityProperty));
        _hideStoryboard.Children.Add(fadeOutAnimation);
        _hideStoryboard.Completed += ((sender, args) =>
        {
            if (Parent is Panel panel)
            {
                panel.Children.Remove(this);
            }
        });
    }

    public void HideT()
    {
        _hideStoryboard.Begin();
        YiKit.RunThread((state =>
        {
            Thread.Sleep(300);
            YiKit.RunOnUIThread(() => this.Hide());
        }));
    }
    private bool _isMouse = false;

    private void LayoutRoot_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isMouse)
        {   _isMouse = false;
            HideT();
        }
    }

    private void LayoutRoot_OnMouseEnter(object sender, MouseEventArgs e)
    {
        _isMouse = true;
    }
    private void Close_OnMouseLeave(object sender, MouseEventArgs e)
    {
        EleTransform(sender as Control, 360, 0, false);
        (sender as Control).Foreground = Brushes.Transparent;
    }
}