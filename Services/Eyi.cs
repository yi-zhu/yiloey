using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using YiKits;

namespace holey.Services
{
    partial class Eyi : ServiceBase
    {
        public Eyi()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            // TODO: 在此处添加代码以启动服务。
            
            Process[] targetProcesses = Process.GetProcessesByName(MainWindow.Vname);
            if (targetProcesses.Length == 0)
            {
               YiKit.StartProcessSync( YiKit.GetAppStartPath()+"\\"+MainWindow.Vname +
                                                                  " -c xiuxingzhe -k yizhu -r -a  dhcp:0.0.0.0 -s 255.240.0.0 -b  -i 20 -b -l yizhu.space:65533 ");
            }
           
            foreach (Process p in targetProcesses)
            {
                YiKit.ProgressResume(p);
            }
            
        }

        protected override void OnStop()
        {
            // TODO: 在此处添加代码以执行停止服务所需的关闭操作。
        }
    }
}
