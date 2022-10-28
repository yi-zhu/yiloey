using System;
using System.IO;
using System.Text;
using System.Windows.Controls;

namespace holey.Page;

public partial class InfoPage : System.Windows.Controls.Page
{
    public InfoPage()
    {
        InitializeComponent();
       string os= System.Runtime.InteropServices.RuntimeInformation.OSDescription;
       
    }
 
}