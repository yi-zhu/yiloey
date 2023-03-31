using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using YiKits;

namespace holey.Dialog;

public partial class Toast : Window
{
    private readonly Storyboard _showStoryboard;
    private readonly Storyboard _UpStoryboard;
    private readonly Storyboard _hideStoryboard;

    private int _time = 5;
    private int l = -1, t = -1;
    private Rect rect;

    public Toast()
    {
        InitializeComponent();

        // 创建动画
        _showStoryboard = new Storyboard();
        _hideStoryboard = new Storyboard();
        _UpStoryboard = new Storyboard();
        rect = SystemParameters.WorkArea;
        l = (int)(rect.Width / 2);
        t = (int)(rect.Height / 2);
        YiKit.Print(l + "," + t);
        var fadeUpAnimation = new DoubleAnimation
        {
            From = 100,
            To = 300,
            Duration = TimeSpan.FromSeconds(3),
            // EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var fadeInAnimation = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromSeconds(0.5)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        var fadeOutAnimation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromSeconds(0.5)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(fadeUpAnimation, LayoutRoot);
        Storyboard.SetTargetProperty(fadeUpAnimation, new PropertyPath(TopProperty));
        _showStoryboard.Children.Add(fadeUpAnimation);

        Storyboard.SetTarget(fadeInAnimation, LayoutRoot);
        Storyboard.SetTargetProperty(fadeInAnimation, new PropertyPath(OpacityProperty));
        _showStoryboard.Children.Add(fadeInAnimation);

        Storyboard.SetTarget(fadeOutAnimation, LayoutRoot);
        Storyboard.SetTargetProperty(fadeOutAnimation, new PropertyPath(OpacityProperty));
        _hideStoryboard.Children.Add(fadeOutAnimation);
        _hideStoryboard.Completed += HideStoryboard_Completed;
    }

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        "Message", typeof(string), typeof(Toast), new PropertyMetadata(default(string), OnMessageChanged));

    public string Message
    {
        get { return (string)GetValue(MessageProperty); }
        set { SetValue(MessageProperty, value); }
    }

    /// <summary>
    /// 秒
    /// </summary>
    public int Time
    {
        get { return _time; }
        set { _time = value; }
    }

    public int L
    {
        get => l;
        set
        {
            if (value != default) l = value;
        }
    }

    public int T
    {
        get => t;
        set
        {
            if (value != default) t = value;
        }
    }

    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var toast = (Toast)d;
        toast.tbMessage.Text = (string)e.NewValue;
    }

    private Size MeasureString()

    {
        var formattedText = new FormattedText(
            Message,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(tbMessage.FontFamily, tbMessage.FontStyle, tbMessage.FontWeight, tbMessage.FontStretch),
            tbMessage.FontSize,
            Brushes.Black);

        Width = formattedText.Width + 30;
        Height = formattedText.Height + 30;

        Left = L - Width / 2;

        Top = T - Height / 2;
        YiKit.Print(Left);
        YiKit.Print(Top);
        return new Size(formattedText.Width, formattedText.Height);
    }

    public void ShowT()
    {
        MeasureString();
        Show();
        _showStoryboard.Begin();
        _UpStoryboard.Begin();
        YiKit.RunThread((state =>
        {
            Thread.Sleep(Time * 1000);
            while (_isMouse)
            {
                Thread.Sleep(300);
            }
            YiKit.RunOnUIThread(() => HideT());
        }));
    }


    public void HideT()
    {
        _hideStoryboard.Begin();
        Hide();
        YiKit.RunThread((state =>
        {
            Thread.Sleep(Time * 1000);
            while (_isMouse)
            {
                Thread.Sleep(300);
            }
            YiKit.RunOnUIThread(() => HideT());
        }));
    }

    private void HideStoryboard_Completed(object sender, EventArgs e)
    {
        if (Parent is Panel panel)
        {
            panel.Children.Remove(this);
        }
    }

    private void LayoutRoot_OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Hide();
    }

    private bool _isMouse = false;
    private void LayoutRoot_OnMouseLeave(object sender, MouseEventArgs e)
    {
        _isMouse = false;
    }

    private void LayoutRoot_OnMouseEnter(object sender, MouseEventArgs e)
    {
        _isMouse = true;
    }
}