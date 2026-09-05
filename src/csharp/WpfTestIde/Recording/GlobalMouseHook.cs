using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace WpfTestIde.Recording
{
    /// <summary>
    /// A system-wide low-level mouse hook (WH_MOUSE_LL) — needed because
    /// the recorder must see clicks on the TARGET application's window,
    /// which is a separate process from this IDE. Standard WPF event
    /// handlers only see events within your own process, so recording an
    /// external app's UI requires this Win32-level approach.
    /// </summary>
    public class GlobalMouseHook : IDisposable
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private readonly LowLevelMouseProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        public event Action<int, int>? LeftButtonDown;

        public GlobalMouseHook()
        {
            _proc = HookCallback;
        }

        public void Start()
        {
            if (_hookId != IntPtr.Zero)
            {
                return;
            }
            using Process curProcess = Process.GetCurrentProcess();
            using ProcessModule curModule = curProcess.MainModule!;
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN)
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int x = hookStruct.pt.x;
                int y = hookStruct.pt.y;
                // Queue to thread-pool so this hook proc returns immediately.
                // A blocking handler here would freeze the system-wide mouse
                // pipeline (Windows waits for low-level hooks to return).
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        LeftButtonDown?.Invoke(x, y);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"GlobalMouseHook handler error: {ex.GetType().Name}: {ex.Message}");
                    }
                });
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public void Dispose() => Stop();
    }
}
