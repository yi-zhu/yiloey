using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using YiKits;

namespace holey.Vdd.Vdd
{
    internal static class Controller
    {
        public static Thread UpdateThread;
        static Thread StatusThread;
        static CancellationTokenSource Cancellation;
        public static IntPtr VddHandle = IntPtr.Zero;

        static Device.Status LastStatus;

        static bool Running = false;
        private static WindowsIdentity wid;

        public static void Start()
        {
            wid = WindowsIdentity.GetCurrent();
            if (Running) return;
            Running = true;
            Cancellation = new CancellationTokenSource();

            UpdateThread = new Thread(() => UpdateLoop(Cancellation.Token));
            UpdateThread.IsBackground = true;
            UpdateThread.Priority = ThreadPriority.Highest;

            StatusThread = new Thread(() => StatusLoop(Cancellation.Token));
            StatusThread.IsBackground = true;
            StatusThread.Priority = ThreadPriority.BelowNormal;

            UpdateThread.Start();
            // StatusThread.Start();
            if (!VddHandle.IsValidHandle())
            {
                if (!Device.OpenHandle(Core.ADAPTER_GUID, out var handle))
                {
                    Console.WriteLine("Failed to open VDD driver handle!");
                    // 可以弹出 MessageBox 或记录日志
                }
                else
                {
                    Console.WriteLine($"Successfully opened handle: {handle}");
                    Interlocked.Exchange(ref VddHandle, handle);
                }
            }
        }

        public static void Stop()
        {
            Cancellation?.Cancel();
            UpdateThread?.Join();
            StatusThread?.Join();

            Device.CloseHandle(VddHandle);
        }

        static void UpdateLoop(CancellationToken cancellation)
        {
            ulong count = 0;
            while (PetWindow.VDD)
            {
                if (cancellation.IsCancellationRequested)
                    break;
                if (!Running)
                    Running = true;
                // if (VddHandle.IsValidHandle() && LastStatus == Device.Status.OK)
                Core.Update(VddHandle);
                if (count > 10)
                {
                    if (count % 100 == 1)
                    {
                        
                        // 只检查显示列表是否为空
                        // SYSTEM用户无法获取显示列表，所以不重启
                        if (!IsSystemUser())
                            try
                            {
                                
                                PetWindow.Me?.Dispatcher.Invoke(() =>
                                {
                                    var displays = Vdd.Core.GetDisplays(out bool noMonitors);
                                    // YiKit.ShowToast("获取显示"+IsSystemUser()+"数量:"+Core.GetDisplays().Count);
                                    if (displays.Count >= 1) return;
                                    YiKit.Print("VDD需要重启，正在添加显示...");
                                    AddDisplay();
                                    // ProgramVdd.StartVdd(["-a"]);
                                });
                            }
                            catch
                            {
                              YiKit.Print("获取显示异常");
                            }
                    }
                }

                count++;
                Thread.Sleep(600);
            }

            ProgramVdd.isStartup = false;
        }

        // 判断是否是SYSTEM用户或特殊用户
        static bool IsSystemUser()
        {
           return WindowsIdentity.GetCurrent().IsSystem;
            try
            {
                if (wid?.User == null) return false;

                string userValue = wid.User.Value.ToUpper();
                // SYSTEM用户的SID是S-1-5-18
                // LOCAL SERVICE用户的SID是S-1-5-19
                // NETWORK SERVICE用户的SID是S-1-5-20
                // LOCAL SYSTEM用户的SID是S-1-5-18 (与SYSTEM相同)
                // DIALUP用户的SID是S-1-5-1
                // NETWORK用户的SID是S-1-5-2
                // BATCH用户的SID是S-1-5-3
                // INTERACTIVE用户的SID是S-1-5-4
                // SERVICE用户的SID是S-1-5-6
                // ANONYMOUS LOGON用户的SID是S-1-5-7
                // PROXY用户的SID是S-1-5-8
                // ENTERPRISE CONTROLLERS用户的SID是S-1-5-9
                // AUTHENTICATED USERS用户的SID是S-1-5-11
                // TERMINAL SERVER用户的SID是S-1-5-13
                // RESTRICTED SERVICES BASE用户的SID是S-1-5-99
                // ALL SERVICE用户的SID是S-1-5-00
                // LOGON SESSION用户的SID是S-1-5-5-*
                return userValue.Contains("S-1-5-18") || userValue.Contains("S-1-5-19") ||
                       userValue.Contains("S-1-5-20") ||
                       userValue.Contains("S-1-5-1") || userValue.Contains("S-1-5-2") ||
                       userValue.Contains("S-1-5-3") ||
                       userValue.Contains("S-1-5-4") || userValue.Contains("S-1-5-6") ||
                       userValue.Contains("S-1-5-7") ||
                       userValue.Contains("S-1-5-8") || userValue.Contains("S-1-5-9") ||
                       userValue.Contains("S-1-5-11") ||
                       userValue.Contains("S-1-5-13") || userValue.Contains("S-1-5-99") ||
                       userValue.Contains("S-1-5-00") ||
                       userValue.Contains("S-1-5-5-");
            }
            catch
            {
                return false;
            }
        }

        static void StatusLoop(CancellationToken cancellation)
        {
            bool first = true;
            var sw = Stopwatch.StartNew();
            while (true)
            {
                if (cancellation.IsCancellationRequested)
                    break;

                if (first || sw.ElapsedMilliseconds >= 2000)
                {
                    first = false;

                    var status = QueryStatus(out var _);
                    unsafe
                    {
                        fixed (Device.Status* s = &LastStatus)
                        {
                            Interlocked.Exchange(ref *(int*)s, (int)status);
                        }
                    }

                    if (status == Device.Status.OK)
                    {
                        if (!VddHandle.IsValidHandle())
                        {
                            if (!Device.OpenHandle(Core.ADAPTER_GUID, out var handle))
                            {
                                Console.WriteLine("Failed to open VDD driver handle!");
                                // 可以弹出 MessageBox 或记录日志
                            }
                            else
                            {
                                Console.WriteLine($"Successfully opened handle: {handle}");
                                Interlocked.Exchange(ref VddHandle, handle);
                            }
                        }
                    }
                    else
                    {
                        var handle = VddHandle;
                        Interlocked.Exchange(ref VddHandle, IntPtr.Zero);
                        Device.CloseHandle(handle);
                    }

                    sw.Restart();
                }

                Thread.Sleep(50);
            }
        }

        public static Device.Status QueryStatus(out Version version)
        {
            return Device.QueryStatus(Core.CLASS_GUID, Core.HARDWARE_ID, out version);
        }

        public static Device.Status QueryStatus()
        {
            return QueryStatus(out var _);
        }

        public static void AddDisplay()
        {
            var status = QueryStatus();
            if (status != Device.Status.OK)
            {
                YiKit.ShowToastWaring("设备已存在不需要重启");
                return;
            }

            int limit = Core.MAX_DISPLAYS;
            var displays = Core.GetDisplays();

            if (displays.Count >= limit)
            {
                YiKit.ShowToastError("显示器到达限制" + displays.Count + "/" + limit);
                return;
            }

            // ✅ 调试：打印当前句柄
            Console.WriteLine($"[AddDisplay] Current VddHandle: {VddHandle}");

            if (!Core.AddDisplay(VddHandle, out var errorCode))
            {
                // ✅ 调试：打印错误码（如果 errorCode 存在）
                Console.WriteLine($"[AddDisplay] Failed with error code: {errorCode}");
                // throw new ErrorOperationFailed(ErrorOperationFailed.Operation.AddDisplay);
            }
        }

        public static void RemoveDisplay(int index)
        {
            var status = QueryStatus();
            if (status != Device.Status.OK)
                YiKit.Print("throw new ErrorDriverStatus" + status.ToString());

            if (index >= 0)
            {
                if (!Core.RemoveDisplay(VddHandle, index))
                {
                    YiKit.Print(" throw new ErrorOperationFailed" +
                                ErrorOperationFailed.Operation.RemoveDisplay.ToString());
                }
            }
        }

        public static void RemoveLastDisplay()
        {
            var displays = Core.GetDisplays();
            if (displays.Count > 0)
            {
                var last = displays[displays.Count - 1];
                RemoveDisplay(last.DisplayIndex);
            }
        }
    }
}