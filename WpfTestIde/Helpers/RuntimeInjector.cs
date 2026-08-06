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
    /// Provides runtime attaching to already-running WPF applications using DLL injection.
    /// Uses CreateRemoteThread + LoadLibrary technique (same approach as Snoop, Spy++, etc.)
    /// </summary>
    public static class RuntimeInjector
    {
        // ============================================================
        // Windows API declarations for cross-process DLL injection
        // ============================================================
        
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize,
            IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, IntPtr lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeThread(IntPtr hThread, out uint lpExitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // Constants
        private const uint PROCESS_CREATE_THREAD = 0x0002;
        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint INFINITE = 0xFFFFFFFF;

        public static event Action<string>? StatusChanged;

        /// <summary>
        /// Inject the Spy Agent into a running process using DLL injection.
        /// This uses CreateRemoteThread + LoadLibrary technique.
        /// </summary>
        public static async Task<bool> InjectAsync(
            int targetProcessId,
            string startupHookDllPath,
            string agentPipeName = "WPFSpyAgentPipe",
            CancellationToken cancellationToken = default)
        {
            StatusChanged?.Invoke($"Preparing to inject into PID {targetProcessId}...");

            if (!File.Exists(startupHookDllPath))
            {
                StatusChanged?.Invoke($"DLL not found: {startupHookDllPath}");
                return false;
            }

            IntPtr processHandle = IntPtr.Zero;
            IntPtr remoteThreadHandle = IntPtr.Zero;

            try
            {
                // Verify target process exists
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

                // Step 1: Open target process with necessary access rights
                StatusChanged?.Invoke("Opening target process...");
                processHandle = OpenProcess(PROCESS_ALL_ACCESS, false, targetProcessId);
                if (processHandle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    StatusChanged?.Invoke($"OpenProcess failed with error: {error}. Try running as Administrator.");
                    return false;
                }

                // Step 2: Allocate memory in target process for DLL path
                byte[] dllPathBytes = Encoding.ASCII.GetBytes(startupHookDllPath + "\0");
                uint bytesToAllocate = (uint)dllPathBytes.Length;

                StatusChanged?.Invoke($"Allocating memory in target process ({bytesToAllocate} bytes)...");
                IntPtr remoteMemory = VirtualAllocEx(
                    processHandle,
                    IntPtr.Zero,
                    bytesToAllocate,
                    MEM_COMMIT | MEM_RESERVE,
                    PAGE_READWRITE);

                if (remoteMemory == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    StatusChanged?.Invoke($"VirtualAllocEx failed with error: {error}");
                    return false;
                }

                // Step 3: Write DLL path to allocated memory
                StatusChanged?.Invoke("Writing DLL path to target process...");
                bool writeResult = WriteProcessMemory(
                    processHandle,
                    remoteMemory,
                    dllPathBytes,
                    bytesToAllocate,
                    out IntPtr bytesWritten);

                if (!writeResult || bytesWritten.ToInt64() != bytesToAllocate)
                {
                    int error = Marshal.GetLastWin32Error();
                    StatusChanged?.Invoke($"WriteProcessMemory failed with error: {error}");
                    VirtualFreeEx(processHandle, remoteMemory, 0, MEM_RELEASE);
                    return false;
                }

                // Step 4: Get address of LoadLibraryA in kernel32.dll
                StatusChanged?.Invoke("Resolving LoadLibraryA...");
                IntPtr loadLibraryAddr = GetProcAddress(
                    GetModuleHandle("kernel32.dll"),
                    "LoadLibraryA");

                if (loadLibraryAddr == IntPtr.Zero)
                {
                    StatusChanged?.Invoke("Failed to get LoadLibraryA address");
                    VirtualFreeEx(processHandle, remoteMemory, 0, MEM_RELEASE);
                    return false;
                }

                // Step 5: Create remote thread that calls LoadLibraryA
                StatusChanged?.Invoke("Creating remote thread for DLL injection...");
                remoteThreadHandle = CreateRemoteThread(
                    processHandle,
                    IntPtr.Zero,
                    0,
                    loadLibraryAddr,
                    remoteMemory,
                    0,
                    out uint threadId);

                if (remoteThreadHandle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    StatusChanged?.Invoke($"CreateRemoteThread failed with error: {error}");
                    VirtualFreeEx(processHandle, remoteMemory, 0, MEM_RELEASE);
                    return false;
                }

                StatusChanged?.Invoke($"Remote thread created (Thread ID: {threadId})");

                // Step 6: Wait for the thread to complete (LoadLibrary returns)
                StatusChanged?.Invoke("Waiting for DLL to load...");
                uint waitResult = WaitForSingleObject(remoteThreadHandle, 10000);

                if (waitResult == 0xFFFFFFFF)
                {
                    StatusChanged?.Invoke("Wait failed");
                    return false;
                }

                GetExitCodeThread(remoteThreadHandle, out uint exitCode);
                
                if (exitCode == 0)
                {
                    StatusChanged?.Invoke($"DLL injection failed - LoadLibrary returned NULL. DLL: {startupHookDllPath}");
                    StatusChanged?.Invoke("The target process may be running a different .NET version or architecture.");
                    return false;
                }

                StatusChanged?.Invoke($"DLL loaded successfully! Module handle: 0x{exitCode:X}");

                // Step 7: Set environment variables for the agent (via temp file)
                StatusChanged?.Invoke("Setting up agent environment...");
                await SetupAgentEnvironmentAsync(targetProcessId, agentPipeName);

                // Step 8: Try to start the agent by calling the exported function
                bool agentStarted = await StartAgentInProcessAsync(
                    processHandle, 
                    (IntPtr)(long)exitCode, 
                    agentPipeName,
                    cancellationToken);

                if (agentStarted)
                {
                    StatusChanged?.Invoke("Spy Agent started successfully!");
                    return true;
                }
                else
                {
                    // The DLL is loaded but we couldn't start the agent
                    // This is OK - the app might work without the agent starting
                    StatusChanged?.Invoke("DLL injected but agent start function not called.");
                    StatusChanged?.Invoke("The app may need to be restarted with agent support.");
                    return true; // Still consider this a partial success
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Injection error: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            finally
            {
                // Cleanup
                if (remoteThreadHandle != IntPtr.Zero)
                    CloseHandle(remoteThreadHandle);
                if (processHandle != IntPtr.Zero)
                    CloseHandle(processHandle);
            }
        }

        /// <summary>
        /// Try to call the exported InjectAndStartAgent function in the injected DLL.
        /// This is optional - the DLL might auto-start on load.
        /// </summary>
        private static async Task<bool> StartAgentInProcessAsync(
            IntPtr processHandle,
            IntPtr dllModuleHandle,
            string pipeName,
            CancellationToken cancellationToken)
        {
            try
            {
                // Get the address of our exported function in the remote process
                IntPtr funcAddress = GetProcAddress(dllModuleHandle, "InjectAndStartAgent");
                if (funcAddress == IntPtr.Zero)
                {
                    // Function not found - that's OK, the DLL might auto-start
                    StatusChanged?.Invoke("InjectAndStartAgent not found - DLL may auto-initialize.");
                    return false;
                }

                // Allocate memory for pipe name in remote process
                byte[] pipeNameBytes = Encoding.ASCII.GetBytes(pipeName + "\0");
                IntPtr remotePipeName = VirtualAllocEx(
                    processHandle,
                    IntPtr.Zero,
                    (uint)pipeNameBytes.Length,
                    MEM_COMMIT | MEM_RESERVE,
                    PAGE_READWRITE);

                if (remotePipeName == IntPtr.Zero)
                    return false;

                WriteProcessMemory(processHandle, remotePipeName, pipeNameBytes, (uint)pipeNameBytes.Length, out _);

                // Create remote thread to call the function
                IntPtr remoteThread = CreateRemoteThread(
                    processHandle,
                    IntPtr.Zero,
                    0,
                    funcAddress,
                    remotePipeName,
                    0,
                    out _);

                if (remoteThread != IntPtr.Zero)
                {
                    WaitForSingleObject(remoteThread, 5000);
                    CloseHandle(remoteThread);
                }

                VirtualFreeEx(processHandle, remotePipeName, 0, MEM_RELEASE);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Setup agent environment via a named pipe that the injected DLL can read.
        /// </summary>
        private static async Task SetupAgentEnvironmentAsync(int processId, string pipeName)
        {
            try
            {
                // Write config to a known location that the native DLL can read
                var configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WpfSpyAgent",
                    $"config_{processId}.txt");

                Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

                // Find the StartupHook DLL path
                var startupHookPath = FindStartupHookDll();
                var config = $"PIPE_NAME={pipeName}\nPID={processId}\n";
                if (!string.IsNullOrEmpty(startupHookPath))
                {
                    config += $"STARTUP_HOOK={startupHookPath}\n";
                }

                await File.WriteAllTextAsync(configPath, config);

                StatusChanged?.Invoke($"Agent config written to: {configPath}");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Warning: Could not write config: {ex.Message}");
            }
        }

        /// <summary>
        /// Find the .NET StartupHook DLL path.
        /// </summary>
        private static string? FindStartupHookDll()
        {
            var searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WpfSpyAgent.StartupHook.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "WpfSpyAgent.StartupHook", "bin", "Debug", "net8.0-windows", "WpfSpyAgent.StartupHook.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "WpfSpyAgent.StartupHook", "bin", "Debug", "net6.0-windows", "WpfSpyAgent.StartupHook.dll"),
            };

            foreach (var path in searchPaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    StatusChanged?.Invoke($"Found StartupHook: {fullPath}");
                    return fullPath;
                }
            }
            return null;
        }

        /// <summary>
        /// Test if Spy Agent is already running in the target process.
        /// </summary>
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

        /// <summary>
        /// Check if runtime injection is supported on this platform.
        /// </summary>
        public static (bool IsSupported, string Reason) CheckSupport()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return (false, "Runtime injection is only supported on Windows");
            }

            // Check if running as admin (needed for some injection scenarios)
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            bool isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

            if (!isAdmin)
            {
                return (true, "Runtime injection supported (run as Admin for best results)");
            }

            return (true, "Runtime injection is supported (Administrator mode)");
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
