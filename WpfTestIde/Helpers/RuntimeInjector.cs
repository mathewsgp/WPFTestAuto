using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WpfTestIde.Helpers
{
    /// <summary>
    /// Provides runtime attaching to already-running WPF applications using Windows Hook API.
    /// </summary>
    public static class RuntimeInjector
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        private const int WH_GETMESSAGE = 3;

        private static IntPtr _hookHandle = IntPtr.Zero;
        private static HookProc? _hookProc;

        public static event Action<string>? StatusChanged;

        public static async Task<bool> InjectAsync(
            int targetProcessId,
            string nativeDllPath,
            string agentPipeName = "WPFSpyAgentPipe",
            CancellationToken cancellationToken = default)
        {
            StatusChanged?.Invoke($"Preparing to inject into PID {targetProcessId}...");

            try
            {
                Process targetProcess;
                try
                {
                    targetProcess = Process.GetProcessById(targetProcessId);
                }
                catch (ArgumentException)
                {
                    StatusChanged?.Invoke($"Process {targetProcessId} not found.");
                    return false;
                }

                if (targetProcess.HasExited)
                {
                    StatusChanged?.Invoke($"Process {targetProcessId} has already exited.");
                    return false;
                }

                StatusChanged?.Invoke($"Target: {targetProcess.ProcessName} (PID: {targetProcessId})");

                if (targetProcess.Threads.Count == 0)
                {
                    StatusChanged?.Invoke("Cannot access target process threads.");
                    return false;
                }

                uint threadId = (uint)targetProcess.Threads[0].Id;
                StatusChanged?.Invoke($"Target thread ID: {threadId}");

                return await InjectViaHookAsync(targetProcessId, threadId, nativeDllPath, agentPipeName, cancellationToken);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Injection error: {ex.Message}");
                return false;
            }
            finally
            {
                Cleanup();
            }
        }

        private static async Task<bool> InjectViaHookAsync(
            int processId,
            uint threadId,
            string dllPath,
            string agentPipeName,
            CancellationToken cancellationToken)
        {
            StatusChanged?.Invoke("Attempting hook-based injection...");

            try
            {
                IntPtr dllHandle = LoadLibrary(dllPath);
                if (dllHandle == IntPtr.Zero)
                {
                    StatusChanged?.Invoke($"Failed to load native DLL: {dllPath}");
                    return false;
                }

                IntPtr injectFunc = GetProcAddress(dllHandle, "InjectAndStartAgent");
                if (injectFunc == IntPtr.Zero)
                {
                    StatusChanged?.Invoke("InjectAndStartAgent function not found in DLL");
                    FreeLibrary(dllHandle);
                    return false;
                }

                StatusChanged?.Invoke("Setting up Windows hook...");

                _hookProc = HookCallback;
                _hookHandle = SetWindowsHookEx(WH_GETMESSAGE, _hookProc, dllHandle, threadId);

                if (_hookHandle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    StatusChanged?.Invoke($"SetWindowsHookEx failed with error: {error}");
                    FreeLibrary(dllHandle);
                    return false;
                }

                StatusChanged?.Invoke("Hook installed.");
                await Task.Delay(1000, CancellationToken.None);
                return true;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Hook injection error: {ex.Message}");
                return false;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public static async Task<bool> TestExistingConnectionAsync(int processId, string pipeName)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                client.Connect(2000);

                if (client.IsConnected)
                {
                    var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                    var reader = new StreamReader(client, Encoding.UTF8);

                    writer.WriteLine("{\"command\":\"GetVersion\"}");
                    var response = await reader.ReadLineAsync();

                    return response?.Contains("version") == true || response?.Contains("WPFSpy") == true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void Cleanup()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }

        public static (bool IsSupported, string Reason) CheckSupport()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return (false, "Runtime injection is only supported on Windows");
            }
            return (true, "Runtime injection is supported");
        }
    }

    public class ProcessInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "";
        public string WindowTitle { get; set; } = "";
        public bool IsWpf { get; set; }

        public override string ToString() => $"{ProcessName} (PID: {ProcessId}) - {WindowTitle}";
    }
}
