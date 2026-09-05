using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
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
        /// Stage the managed Spy Agent DLLs into the target process's directory
        /// so the NativeInject C++ shim can find them after LoadLibrary.
        ///
        /// The target process's `AppDomain.BaseDirectory` is its own exe folder,
        /// not the framework's bin folder, so the managed DLLs must be copied there.
        /// We copy only when the source mtime is strictly newer than the destination
        /// (idempotent for re-attach scenarios).
        ///
        /// TFM-aware: this inspects the target's loaded modules to pick the right
        /// build. If coreclr.dll is present, only the modern (.NET Core / .NET 5+)
        /// pair is staged at the AUT root. If mscoree.dll / clr.dll is present,
        /// only the .NET Framework 4.x trio is staged under `net461\`. Mixed-mode
        /// and unknown targets fall back to the modern pair.
        ///
        /// Returns the list of relative paths (relative to the AUT folder) of
        /// files that were actually copied. Caller is responsible for cleanup
        /// (UnstageAgentDlls) when the attach ends.
        /// </summary>
        public static List<string> StageAgentDllsForTarget(int targetProcessId)
        {
            var copied = new List<string>();
            try
            {
                var target = Process.GetProcessById(targetProcessId);
                string? targetExe = target.MainModule?.FileName;
                if (string.IsNullOrEmpty(targetExe) || !File.Exists(targetExe))
                {
                    StatusChanged?.Invoke($"Stage: cannot resolve target exe path for PID {targetProcessId}");
                    return copied;
                }

                var targetDir = Path.GetDirectoryName(targetExe);
                if (string.IsNullOrEmpty(targetDir))
                    return copied;

                // Pick the TFM by inspecting the target's already-loaded modules.
                string clr = DetectTargetClr(target);
                StatusChanged?.Invoke($"Stage: target CLR detected as {clr}");

                if (clr == "framework")
                {
                    StageFrameworkOnly(targetDir, copied);
                }
                else
                {
                    StageModernOnly(targetDir, copied);
                }

                if (copied.Count > 0)
                {
                    StatusChanged?.Invoke($"Stage: copied {string.Join(", ", copied)} -> {targetDir}");
                }
                else
                {
                    StatusChanged?.Invoke("Stage: target already up to date");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Stage: error - {ex.GetType().Name}: {ex.Message}");
            }
            return copied;
        }

        /// <summary>
        /// Inspect a target Process's loaded modules and decide which Spy Agent
        /// build to use. Returns:
        ///   "modern"    -> .NET Core / .NET 5+ runtime is loaded
        ///   "framework" -> .NET Framework 4.x runtime is loaded
        /// Defaults to "modern" for any ambiguous/unknown case so the modern
        /// pair (the only one that loads via DOTNET_STARTUP_HOOKS) is staged.
        /// </summary>
        public static string DetectTargetClr(Process target)
        {
            if (target is null) return "modern";
            try
            {
                bool hasCoreClr = false;
                bool hasFrameworkClr = false;
                foreach (ProcessModule m in target.Modules)
                {
                    if (m is null || string.IsNullOrEmpty(m.ModuleName))
                        continue;
                    var name = m.ModuleName!;
                    var lname = name.ToLowerInvariant();
                    if (lname == "coreclr.dll")
                        hasCoreClr = true;
                    else if (lname == "clr.dll" || lname == "mscoree.dll" || lname == "mscoreei.dll")
                        hasFrameworkClr = true;
                }
                // Framework takes priority when both are present (some hosts load
                // both; mscoree shows up whenever a managed assembly is loaded
                // on .NET Framework). But a pure .NET Core app does not have
                // mscoree.dll loaded, so coreclr alone means modern.
                if (hasFrameworkClr && !hasCoreClr) return "framework";
                if (hasCoreClr) return "modern";
                return "modern";
            }
            catch
            {
                // Access to Modules can be denied on protected processes; default
                // to modern since that path is the safer fallback (StartupHook
                // refuses to load unless WPFSPY_AGENT_ENABLED=1, but at least we
                // don't pollute the AUT with a net461\ folder that won't be used).
                return "modern";
            }
        }

        /// <summary>
        /// Stage only the modern (.NET Core / .NET 5+) build of the Spy Agent
        /// into the AUT root. Layout:
        ///   &lt;AUT&gt;\WpfSpyAgent.dll
        ///   &lt;AUT&gt;\WpfSpyAgent.StartupHook.dll
        /// </summary>
        private static void StageModernOnly(string targetDir, List<string> copied)
        {
            var modernSource = FindAgentSourceDir();
            if (modernSource is null)
            {
                StatusChanged?.Invoke("Stage: modern Spy Agent source directory not found");
                return;
            }
            CopyNewerIfMissing(
                Path.Combine(modernSource, "WpfSpyAgent.dll"),
                Path.Combine(targetDir, "WpfSpyAgent.dll"),
                "WpfSpyAgent.dll", copied);
            CopyNewerIfMissing(
                Path.Combine(modernSource, "WpfSpyAgent.StartupHook.dll"),
                Path.Combine(targetDir, "WpfSpyAgent.StartupHook.dll"),
                "WpfSpyAgent.StartupHook.dll", copied);
        }

        /// <summary>
        /// Stage only the .NET Framework 4.x build of the Spy Agent into the
        /// AUT. The C++ injector probes for "&lt;dllDir&gt;\net461\WpfSpyAgent.dll"
        /// when coreclr is not loaded, so the subfolder layout must be preserved.
        /// Layout:
        ///   &lt;AUT&gt;\net461\WpfSpyAgent.dll
        ///   &lt;AUT&gt;\net461\WpfSpyAgent.FrameworkHook.dll
        ///   &lt;AUT&gt;\net461\Newtonsoft.Json.dll
        /// </summary>
        private static void StageFrameworkOnly(string targetDir, List<string> copied)
        {
            var fwSource = FindFrameworkAgentSourceDir();
            if (fwSource is null)
            {
                StatusChanged?.Invoke("Stage: .NET Framework Spy Agent source directory not found (net461 build)");
                return;
            }
            var net461Subdir = Path.Combine(targetDir, "net461");
            Directory.CreateDirectory(net461Subdir);

            CopyNewerIfMissing(
                Path.Combine(fwSource, "WpfSpyAgent.dll"),
                Path.Combine(net461Subdir, "WpfSpyAgent.dll"),
                Path.Combine("net461", "WpfSpyAgent.dll"), copied);
            CopyNewerIfMissing(
                Path.Combine(fwSource, "WpfSpyAgent.FrameworkHook.dll"),
                Path.Combine(net461Subdir, "WpfSpyAgent.FrameworkHook.dll"),
                Path.Combine("net461", "WpfSpyAgent.FrameworkHook.dll"), copied);
            // Newtonsoft.Json is only required by the net461 build (see
            // WpfSpyAgent.csproj:24). Include it so the Framework CLR can
            // resolve its dependency once ExecuteInDefaultAppDomain starts.
            CopyNewerIfMissing(
                Path.Combine(fwSource, "Newtonsoft.Json.dll"),
                Path.Combine(net461Subdir, "Newtonsoft.Json.dll"),
                Path.Combine("net461", "Newtonsoft.Json.dll"), copied);
        }

        /// <summary>
        /// Copy `src` -> `dst` only if `src` is strictly newer than `dst` (or dst is
        /// missing). On success, append `relativePath` to `copied`. Errors are
        /// logged via StatusChanged but do not abort the overall staging.
        /// </summary>
        private static void CopyNewerIfMissing(
            string src,
            string dst,
            string relativePath,
            List<string> copied)
        {
            try
            {
                if (!File.Exists(src))
                    return;
                if (File.Exists(dst))
                {
                    var srcTime = File.GetLastWriteTimeUtc(src);
                    var dstTime = File.GetLastWriteTimeUtc(dst);
                    if (dstTime >= srcTime)
                        return;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(src, dst, overwrite: true);
                copied.Add(relativePath);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"Stage: failed to copy {relativePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Remove DLLs previously staged into a target folder. Defensive:
        /// only removes the names that were passed in. Cleans up a `net461\`
        /// subfolder of managed files (and removes the subfolder itself if empty).
        /// </summary>
        public static void UnstageAgentDlls(string? targetExePath, IEnumerable<string> names)
        {
            if (string.IsNullOrEmpty(targetExePath) || names is null)
                return;
            try
            {
                var dir = Path.GetDirectoryName(targetExePath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return;
                foreach (var n in names)
                {
                    try
                    {
                        var p = Path.Combine(dir, n);
                        if (File.Exists(p))
                            File.Delete(p);
                    }
                    catch
                    {
                        // Best-effort: don't fail the detach on a delete error.
                    }
                }
                // If the net461 subfolder ended up empty after cleanup, remove it too.
                try
                {
                    var sub = Path.Combine(dir, "net461");
                    if (Directory.Exists(sub) && !Directory.EnumerateFileSystemEntries(sub).Any())
                    {
                        Directory.Delete(sub);
                    }
                }
                catch { }
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// Resolve the directory holding the framework's modern Spy Agent DLLs.
        /// `AppDomain.CurrentDomain.BaseDirectory` for WpfTestIde is
        /// <repo>\bin\Debug\net9.0-windows\ (per WpfTestIde.csproj's
        /// BaseOutputPath=..\bin). The framework's other builds live at:
        ///   <repo>\bin\Debug\net9.0-windows\WpfSpyAgent.dll (same dir as IDE)
        ///   <repo>\bin\Debug\net8.0-windows\WpfSpyAgent.dll
        ///   <repo>\bin\Debug\net461\WpfSpyAgent.dll (Framework — also in same parent Debug\)
        ///   <repo>\src\csharp\WpfSpyAgent\bin\Debug\net9.0-windows\WpfSpyAgent.dll (per-project)
        ///   <repo>\src\csharp\WpfSpyAgent\bin\Debug\net8.0-windows\WpfSpyAgent.dll
        ///   <repo>\src\csharp\WpfSpyAgent\bin\Debug\net461\WpfSpyAgent.dll
        ///   <repo>\src\csharp\WpfSpyAgent.FrameworkHook\bin\Debug\net461\WpfSpyAgent.dll
        /// </summary>
        private static string? FindAgentSourceDir()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var searchPaths = new[]
            {
                // Same dir as the IDE (most common case — both share BaseOutputPath=..\bin)
                Path.Combine(baseDir, "WpfSpyAgent.dll"),
                // Sibling builds in the same shared <repo>\bin\Debug\ folder
                Path.Combine(baseDir, "..", "..", "Debug", "net9.0-windows", "WpfSpyAgent.dll"),
                Path.Combine(baseDir, "..", "..", "Debug", "net8.0-windows", "WpfSpyAgent.dll"),
                Path.Combine(baseDir, "..", "..", "Debug", "net6.0-windows", "WpfSpyAgent.dll"),
                // Per-project build outputs (3 levels up to repo root)
                Path.Combine(baseDir, "..", "..", "..", "WpfSpyAgent", "bin", "Debug", "net9.0-windows", "WpfSpyAgent.dll"),
                Path.Combine(baseDir, "..", "..", "..", "WpfSpyAgent", "bin", "Debug", "net8.0-windows", "WpfSpyAgent.dll"),
                Path.Combine(baseDir, "..", "..", "..", "WpfSpyAgent", "bin", "Release", "net9.0-windows", "WpfSpyAgent.dll"),
                Path.Combine(baseDir, "..", "..", "..", "WpfSpyAgent", "bin", "Release", "net8.0-windows", "WpfSpyAgent.dll"),
            };
            foreach (var p in searchPaths)
            {
                try
                {
                    var full = Path.GetFullPath(p);
                    if (File.Exists(full))
                        return Path.GetDirectoryName(full);
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Resolve the directory holding the framework's .NET Framework 4.x build
        /// of the Spy Agent. The C++ injector (NativeInject.cpp) probes
        /// <dllDir>\net461\WpfSpyAgent.dll for Framework targets.
        ///
        /// From the IDE's BaseDirectory (bin\Debug\net9.0-windows\), the framework
        /// net461 build lives at one of:
        ///   bin\Debug\net461\ (shared bin folder)
        ///   WpfSpyAgent\bin\Debug\net461\ (per-project, multi-target)
        ///   WpfSpyAgent.FrameworkHook\bin\Debug\net461\ (per-project, net461-only)
        /// </summary>
        private static string? FindFrameworkAgentSourceDir()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var searchPaths = new[]
            {
                // Shared <repo>\bin\Debug\net461\ (most common — WpfSpyAgent.csproj multi-targets net9;net461
                // and uses BaseOutputPath=..\bin, so its net461 build lives next to the IDE's build).
                Path.Combine(baseDir, "..", "..", "Debug", "net461"),
                Path.Combine(baseDir, "..", "..", "Release", "net461"),
                // Per-project WpfSpyAgent multi-target output
                Path.Combine(baseDir, "..", "..", "..", "WpfSpyAgent", "bin", "Debug", "net461"),
                Path.Combine(baseDir, "..", "..", "..", "WpfSpyAgent", "bin", "Release", "net461"),
                // FrameworkHook project (net461-only)
                Path.Combine(baseDir, "..", "..", "..", "WpfSpyAgent.FrameworkHook", "bin", "Debug", "net461"),
                Path.Combine(baseDir, "..", "..", "..", "WpfSpyAgent.FrameworkHook", "bin", "Release", "net461"),
            };
            foreach (var p in searchPaths)
            {
                try
                {
                    var full = Path.GetFullPath(p);
                    if (Directory.Exists(full) && File.Exists(Path.Combine(full, "WpfSpyAgent.dll")))
                        return full;
                }
                catch { }
            }
            return null;
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
