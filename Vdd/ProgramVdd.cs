using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using holey.Vdd.Vdd;

namespace holey.Vdd
{
    public static class ProgramVdd
    {
        public const string AppId = "QpHOX8IBUHBznGtqk9xm1";
        public const string AppName = "VDisplay";
        public const string AppVersion = "1.0.2";
        public const string GitHubRepo = "-vdd";
        public static bool isStartup = false;

        [STAThread]
        public static int StartVdd(string[] args,bool issingle=false)
        {
            var diss= Core.GetDisplays();
            if (issingle)
            {
               
               if (diss.Count<1)
                   isStartup = false;
               /*foreach (var display in diss)
               {
                   if (display.Adapter.Contains(Core.ADAPTER)) return -1;
               }*/
               if(isStartup)return 0;
            }

            
            if (args.Length >= 2 && args[0] == "-custom")
            {
                var modes = Display.ParseModes(args[1]);
                Vdd.Utils.SetCustomDisplayModes(modes);

                if (args.Length >= 3)
                {
                    if (Enum.TryParse<Vdd.Utils.ParentGPU>(args[2], true, out var kind))
                    {
                        Vdd.Utils.SetParentGPU(kind);
                    }
                }

                return 0;
            }

            if (args.Length > 0 && args[0] == "-cli")
            {
                args = args.Skip(1).ToArray();
            }
            else
            {
                //轮询保持显示器有效
                Vdd.Controller.Start();
            }

            isStartup = true;
            if (issingle && diss.Count > 0)
                return 0;
            else
                return CLI.Execute(args);

        }
       

    static bool SingleInstance()
    {
        bool isOwned = false;
        var signal = new EventWaitHandle(false,
            EventResetMode.AutoReset, AppId, out isOwned);

        if (isOwned)
        {
            /*Task.Run(() =>
            {
                while (signal.WaitOne())
                {
                    Tray.Instance?.Invoke(Tray.Instance.ShowApp);
                }
            });*/
        }
        else
        {
            signal.Set();
            signal.Dispose();
        }

        return isOwned;
    }
}

}