using System;
using System.Collections.Generic;
using System.Linq;
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
        public MainWindow()
        {
            InitializeComponent();
            MPage.Content =new  Frame(){Content =  new MainPage()};
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Info_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("版本"+ApplicationHoley.Version+" \n版权所有@yizhu \n日期：2022年10月28日");
        }

        private void Update_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("您的版本"+ApplicationHoley.Version+"已是最新");
        }

        private void TapItem_Click(object sender, RoutedEventArgs e)
        {
            MPage.Content =new  TapPage();

        }
    }
}