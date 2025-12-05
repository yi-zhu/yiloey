using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using YiKits;

namespace holey.Vdd.Vdd
{
    internal static class Controller
    {
        static Thread UpdateThread;
        static Thread StatusThread;
        static CancellationTokenSource Cancellation;
        static IntPtr VddHandle = IntPtr.Zero;

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
                if (VddHandle.IsValidHandle() && LastStatus == Device.Status.OK)
                    Core.Update(VddHandle);
                if (count > 10 )
                {
                    if (count % 5100000 == 1)
                    {
                        if (wid.User != null && wid.User.Value.StartsWith("S-1-5-"))
                        {
                            Thread.Sleep(1600);
                            YiKit.RunThread((e) =>
                            {
                                AddDisplay();
                            });
                        }
                    } else if (count % 100 == 1)
                    {
                        YiKit.RunThread((e) =>
                        {
                            if (Core.GetDisplays().Count != 0) return;
                            YiKit.Print("重启!");
                        
                            CLI.Execute(["-r", "all"]);
                            AddDisplay();
                        });
                    }

                }
                count++;
                Thread.Sleep(600);
            }

            ProgramVdd.isStartup = false;
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
                throw new ErrorDriverStatus(status);

            int limit = Core.MAX_DISPLAYS;
            var displays = Core.GetDisplays();

            if (displays.Count >= limit)
            {
                throw new ErrorExceededLimit(limit);
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