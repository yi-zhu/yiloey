using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using holey.Page;

namespace holey
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainPage _mainPage;
        private TapPage _tapPage;
        public static MainWindow YiWindow { get; set; }


        public MainWindow()
        {
            InitializeComponent();
            YiWindow = this;
            _mainPage = new MainPage();
            _tapPage = new TapPage();
            MPage.Content = new Frame() { Content = _mainPage };
        }


        public static void StarteYi()
        {
            ShowLoading();
            YiKit.StartProcessSync(
                string.Format("Taskkill /IM {0} /F", "eyi*")
                , e =>
                {
                    YiKit.Print(e);
                    YiKit.StartProcessAsync(
                        "eyi -c xiuxingzhe -k yizhu -r -a  dhcp:0.0.0.0 -b  -i 20 -b -l yizhu.space:65533 "
                        , a =>
                        {
                            YiKit.Print(a);
                            string? msg = a as string;
                            NetworkInterface tap=null;
                            if (msg.Contains("Open device "))
                            {
                                string name = msg.Substring(msg.IndexOf("{"), msg.IndexOf("}")+1 - msg.IndexOf("{"));
                                 tap=  YiKit.GetInterface(name);
                                YiKit.Print(tap.GetIPv4Statistics());
                            }

                                YiKit.Print(tap?.GetIPv4Statistics().BytesReceived);
                        });
                    YiKit.RunOnUIThread(()=>ShowLoading(false));

                });
       
        }

        
        
        
        public static void ShowLoading(bool show = true)
        {
            if (show)
            {
                if (!YiWindow.LodingGrid.IsVisible)
                {
                    YiWindow.LodingGrid.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (YiWindow.LodingGrid.IsVisible)
                {
                    YiWindow.LodingGrid.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Info_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("版本" + ApplicationHoley.Version + " \n版权所有@yizhu \n日期：2022年10月28日");
        }

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("您的版本" + ApplicationHoley.Version + "已是最新");
        }

        private void TapItem_Click(object sender, RoutedEventArgs e)
        {
            MPage.Content = _tapPage;
        }

        private void IndexItem_Click(object sender, RoutedEventArgs e)
        {
            MPage.Content = _mainPage;
        }
    }
}