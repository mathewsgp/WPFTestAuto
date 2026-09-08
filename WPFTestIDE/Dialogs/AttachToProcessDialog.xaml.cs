using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WpfTestIde.Helpers;

namespace WpfTestIde.Dialogs
{
    public partial class AttachToProcessDialog : Window
    {
        private readonly List<string> _allDrivers = new() { "WPFSpy", "FlaUI", "Sikuli" };
        private List<string> _selectedDrivers = new() { "WPFSpy", "FlaUI" };

        public int? SelectedProcessId { get; private set; }
        public string PipeName { get; private set; } = "WPFSpyAgentPipe";
        public string AppId { get; private set; } = "";
        public string? ApplicationPath { get; private set; }
        public string? Arguments { get; private set; }
        public string? StartIn { get; private set; }
        public List<string>? DriverList { get; private set; }
        public bool SpyAgentEnabled { get; private set; } = true;
        public AttachMode Mode { get; private set; } = AttachMode.RuntimeAttach;

        public AttachToProcessDialog()
        {
            InitializeComponent();
            RefreshDriverLists();
            RefreshProcessList();
            StatusText.Text = "Select a process to attach to, or launch a new process";
        }

        private void RefreshDriverLists()
        {
            var available = _allDrivers.Except(_selectedDrivers).ToList();
            AvailableDriversList.ItemsSource = available;
            SelectedDriversList.ItemsSource = _selectedDrivers.ToList();
            UpdatePipeNameVisibility();
        }

        private void UpdatePipeNameVisibility()
        {
            if (PipeNamePanel == null) return;
            PipeNamePanel.Visibility = _selectedDrivers.Contains("WPFSpy") ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshProcessList()
        {
            try
            {
                IEnumerable<Process> candidates;

                if (ShowAllProcessesCheck.IsChecked == true)
                {
                    candidates = Process.GetProcesses()
                        .Where(p => p.Id != Process.GetCurrentProcess().Id)
                        .OrderBy(p => p.ProcessName);
                }
                else
                {
                    candidates = Process.GetProcesses()
                        .Where(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle) && p.Id != Process.GetCurrentProcess().Id)
                        .OrderBy(p => p.MainWindowTitle);
                }

                ProcessListView.ItemsSource = candidates.ToList();
                StatusText.Text = $"Found {((List<Process>)ProcessListView.ItemsSource).Count} processes";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error listing processes: {ex.Message}";
            }
        }

        private void RefreshProcessList_Click(object sender, RoutedEventArgs e)
        {
            RefreshProcessList();
        }

        private void ShowAllProcessesCheck_Changed(object sender, RoutedEventArgs e)
        {
            RefreshProcessList();
        }

        private void RuntimeAttachRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (RuntimeAttachPanel != null)
            {
                RuntimeAttachPanel.Visibility = Visibility.Visible;
                NewProcessPanel.Visibility = Visibility.Collapsed;
                Mode = AttachMode.RuntimeAttach;
                StatusText.Text = "Select a running process to attach to";
            }
        }

        private void NewProcessRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (RuntimeAttachPanel != null)
            {
                RuntimeAttachPanel.Visibility = Visibility.Collapsed;
                NewProcessPanel.Visibility = Visibility.Visible;
                Mode = AttachMode.LaunchNew;
                StatusText.Text = "Launch new process with Spy Agent injected";
            }
        }

        private void BrowseApplication_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Application",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                ApplicationPathBox.Text = dialog.FileName;
            }
        }

        private void btnAddDriver_Click(object sender, RoutedEventArgs e)
        {
            if (AvailableDriversList.SelectedItem is string driver)
            {
                _selectedDrivers.Add(driver);
                RefreshDriverLists();
                SelectedDriversList.SelectedItem = driver;
            }
        }

        private void btnRemoveDriver_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedDriversList.SelectedItem is string driver)
            {
                _selectedDrivers.Remove(driver);
                RefreshDriverLists();
            }
        }

        private void btnDriverUp_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDrivers == null || SelectedDriversList.SelectedIndex < 1) return;
            int index = SelectedDriversList.SelectedIndex;
            var item = _selectedDrivers[index];
            _selectedDrivers.RemoveAt(index);
            _selectedDrivers.Insert(index - 1, item);
            RefreshDriverLists();
            SelectedDriversList.SelectedIndex = index - 1;
        }

        private void btnDriverDown_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedDrivers == null || SelectedDriversList.SelectedIndex < 0 || SelectedDriversList.SelectedIndex >= _selectedDrivers.Count - 1) return;
            int index = SelectedDriversList.SelectedIndex;
            var item = _selectedDrivers[index];
            _selectedDrivers.RemoveAt(index);
            _selectedDrivers.Insert(index + 1, item);
            RefreshDriverLists();
            SelectedDriversList.SelectedIndex = index + 1;
        }

        private void EnableSpyAgentCheck_Checked(object sender, RoutedEventArgs e)
        {
            SpyAgentEnabled = EnableSpyAgentCheck.IsChecked == true;
            UpdatePipeNameVisibility();
        }

        private async Task<bool> WaitForSpyAgentAsync(string pipeName, int timeoutSeconds = 10)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeoutSeconds)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                    client.Connect(2000);
                    if (client.IsConnected)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Pipe not ready yet
                }
                await Task.Delay(500);
            }
            return false;
        }

        private async void Attach_Click(object sender, RoutedEventArgs e)
        {
            AppId = string.IsNullOrWhiteSpace(AppIdBox.Text) ? "" : AppIdBox.Text.Trim();
            DriverList = _selectedDrivers != null && _selectedDrivers.Any() ? new List<string>(_selectedDrivers) : null;
            SpyAgentEnabled = EnableSpyAgentCheck.IsChecked == true;

            if (!string.IsNullOrWhiteSpace(PipeNameBox.Text))
            {
                PipeName = PipeNameBox.Text.Trim();
            }
            else if (Mode == AttachMode.RuntimeAttach && ProcessListView.SelectedItem is Process proc)
            {
                PipeName = $"WPFSpyAgentPipe_{proc.ProcessName.ToLowerInvariant()}";
            }
            else if (Mode != AttachMode.RuntimeAttach && !string.IsNullOrEmpty(AppId))
            {
                PipeName = $"WPFSpyAgentPipe_{AppId.ToLowerInvariant()}";
            }
            else
            {
                PipeName = "WPFSpyAgentPipe";
            }

            if (Mode == AttachMode.RuntimeAttach)
            {
                if (ProcessListView.SelectedItem is Process proc)
                {
                    SelectedProcessId = proc.Id;
                }

                if (SelectedProcessId is null)
                {
                    MessageBox.Show(this, "Select a process first.", "Attach to Process",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusText.Text = $"Attempting to attach to PID {SelectedProcessId}...";

                var result = await TryRuntimeAttachAsync(SelectedProcessId.Value, PipeName);

                if (result)
                {
                    StatusText.Text = "Successfully attached!";
                    DialogResult = true;
                }
                else
                {
                    var msgResult = MessageBox.Show(
                        this,
                        "Could not auto-inject Spy Agent into the running process.\n\n" +
                        "DLL injection requires:\n" +
                        "1. Build WpfSpyAgent.NativeInject project (C++ DLL)\n" +
                        "2. Run as Administrator\n" +
                        "3. Target app must have the same architecture (x64/x86)\n\n" +
                        "Alternative - use 'launch' mode instead:\n" +
                        "Select 'Launch New' and the app will be started with Spy Agent.\n\n" +
                        "Would you like to proceed anyway?",
                        "Injection Failed",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (msgResult == MessageBoxResult.Yes)
                    {
                        DialogResult = true;
                    }
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(ApplicationPathBox.Text))
                {
                    MessageBox.Show(this, "Select an application to launch.", "Attach to Process",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ApplicationPath = ApplicationPathBox.Text.Trim();
                Arguments = string.IsNullOrWhiteSpace(ArgumentsBox.Text) ? null : ArgumentsBox.Text.Trim();
                StartIn = string.IsNullOrWhiteSpace(StartInBox.Text) ? null : StartInBox.Text.Trim();

                LogDiagnostic($"=== Launch New Process ===");
                LogDiagnostic($"AppPath: {ApplicationPath}");
                LogDiagnostic($"Args: {Arguments ?? "(none)"}");
                LogDiagnostic($"StartIn: {StartIn ?? "(none)"}");
                LogDiagnostic($"SpyAgentEnabled: {SpyAgentEnabled}");
                LogDiagnostic($"PipeName: {PipeName}");
                LogDiagnostic($"IsNetFrameworkApp: {IsNetFrameworkApp(ApplicationPath)}");

                if (IsNetFrameworkApp(ApplicationPath))
                {
                    StatusText.Text = "Launching .NET Framework app with runtime injection...";
                    LogDiagnostic("Detected .NET Framework app - using launch + inject flow");
                    
                    var result = await LaunchFrameworkAppWithInjection(ApplicationPath, Arguments, StartIn, PipeName);
                    
                    if (result != null)
                    {
                        SelectedProcessId = result.Id;
                        StatusText.Text = $"Launched and injected PID {SelectedProcessId}";
                        DialogResult = true;
                    }
                    else
                    {
                        StatusText.Text = "Failed to launch/inject .NET Framework app";
                        MessageBox.Show(this, 
                            "Failed to launch and inject the .NET Framework application.\n\n" +
                            "This requires:\n" +
                            "1. Build WpfSpyAgent.NativeInject project (C++ DLL)\n" +
                            "2. Run IDE as Administrator\n" +
                            "3. Target app must match architecture (x64/x86)\n\n" +
                            "Check repository/attach_log.txt for details.",
                            "Launch/Inject Failed",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    StatusText.Text = "Launching process with Spy Agent...";
                    var result = await LaunchWithStartupHook(ApplicationPath, Arguments, PipeName);

                    if (result != null)
                    {
                        SelectedProcessId = result.Id;
                        StatusText.Text = $"Launched PID {SelectedProcessId}";
                        DialogResult = true;
                    }
                    else
                    {
                        StatusText.Text = "Failed to launch process";
                        MessageBox.Show(this, "Failed to launch the application.", "Launch Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async Task<bool> TryRuntimeAttachAsync(int processId, string pipeName)
        {
            try
            {
                StatusText.Text = $"Checking if Spy Agent is already running in PID {processId}...";

                if (await RuntimeInjector.TestExistingConnectionAsync(processId, pipeName))
                {
                    StatusText.Text = "Connected to existing Spy Agent!";
                    return true;
                }

                StatusText.Text = "Spy Agent not found. Attempting runtime injection...";

                try
                {
                    RuntimeInjector.StageAgentDllsForTarget(processId);
                }
                catch (Exception stageEx)
                {
                    StatusText.Text = $"Staging warning: {stageEx.Message}";
                }

                string dllPath = RuntimeInjector.FindNativeInjectDll() ?? "";
                if (!string.IsNullOrEmpty(dllPath))
                {
                    StatusText.Text = "Spy Agent not found. Attempting native runtime injection...";
                    LogDiagnostic($"Runtime attach: using NativeInject DLL: {dllPath}");
                }

                if (string.IsNullOrEmpty(dllPath))
                {
                    StatusText.Text = "NativeInject DLL not found. Cannot inject into running app.";
                    LogDiagnostic("Runtime attach: injection aborted - NativeInject DLL not found");
                    return false;
                }

                RuntimeInjector.StatusChanged += msg => LogDiagnostic($"[Inject] {msg}");
                bool injected = await RuntimeInjector.InjectAsync(processId, dllPath, pipeName);
                RuntimeInjector.StatusChanged -= msg => LogDiagnostic($"[Inject] {msg}");
                
                if (injected)
                {
                    StatusText.Text = "Waiting for Spy Agent pipe...";
                    bool pipeReady = await WaitForSpyAgentAsync(pipeName, timeoutSeconds: 15);
                    if (pipeReady)
                    {
                        StatusText.Text = "Spy Agent connected!";
                        LogDiagnostic($"Spy Agent pipe ready after injection: {pipeName}");
                    }
                    else
                    {
                        StatusText.Text = "Injected but agent initializing...";
                        LogDiagnostic($"Spy Agent pipe not ready after 15s: {pipeName}");
                    }
                }
                else
                {
                    StatusText.Text = "Failed to inject or start Spy Agent.";
                    LogDiagnostic($"Runtime injection failed for PID {processId}");
                }
                return injected;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Injection error: {ex.Message}";
                LogDiagnostic($"Runtime attach exception: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private bool IsProcessDotNetFramework(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                string? exePath = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    string dir = Path.GetDirectoryName(exePath)!;
                    string lowerDir = dir.ToLowerInvariant();
                    if (lowerDir.Contains("net461") || lowerDir.Contains("net48") || lowerDir.Contains("net472") || lowerDir.Contains("net462"))
                        return true;
                }

                foreach (ProcessModule module in process.Modules)
                {
                    string name = module.ModuleName.ToLowerInvariant();
                    if (name == "mscoree.dll")
                        return true;
                    if (name == "coreclr.dll")
                        return false;
                }
            }
            catch { }
            return false;
        }

        private string? GetStartupHookDllPath()
        {
            var searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WpfSpyAgent.StartupHook.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "WpfSpyAgent.StartupHook", "bin", "Debug", "net9.0-windows", "WpfSpyAgent.StartupHook.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "WpfSpyAgent.StartupHook", "bin", "Release", "net9.0-windows", "WpfSpyAgent.StartupHook.dll"),
            };

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            foreach (var relPath in searchPaths)
            {
                try
                {
                    var fullPath = Path.GetFullPath(Path.Combine(baseDir, relPath));
                    if (File.Exists(fullPath))
                    {
                        LogDiagnostic($"Found StartupHook DLL: {fullPath}");
                        return fullPath;
                    }
                }
                catch { }
            }

            LogDiagnostic("StartupHook DLL not found in any search path");
            return null;
        }

        private string? GetNativeInjectDllPath()
        {
            var searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WpfSpyAgent.NativeInject.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "WpfSpyAgent.NativeInject", "bin", "Debug", "x64", "WpfSpyAgent.NativeInject.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "WpfSpyAgent.NativeInject", "bin", "Release", "x64", "WpfSpyAgent.NativeInject.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "WpfSpyAgent.NativeInject", "bin", "Debug", "WpfSpyAgent.NativeInject.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "WpfSpyAgent.NativeInject", "bin", "Release", "WpfSpyAgent.NativeInject.dll"),
            };

            foreach (var relPath in searchPaths)
            {
                try
                {
                    var fullPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relPath));
                    if (File.Exists(fullPath))
                    {
                        LogDiagnostic($"Found NativeInject DLL: {fullPath}");
                        return fullPath;
                    }
                }
                catch { }
            }

            LogDiagnostic("NativeInject DLL not found in any search path");
            return null;
        }

        private async Task<Process?> LaunchFrameworkAppWithInjection(string appPath, string? arguments, string? startIn, string pipeName)
        {
            try
            {
                var targetDir = Path.GetDirectoryName(appPath);
                if (string.IsNullOrEmpty(targetDir))
                {
                    StatusText.Text = "Cannot resolve target application directory";
                    return null;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = appPath,
                    Arguments = arguments ?? "",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false,
                    WorkingDirectory = startIn ?? targetDir
                };

                LogDiagnostic($"Launching Framework app: {appPath}");
                StatusText.Text = $"Launching {Path.GetFileName(appPath)}...";

                var process = Process.Start(psi);
                if (process == null)
                {
                    LogDiagnostic("Process.Start returned null");
                    return null;
                }

                LogDiagnostic($"Process started: PID={process.Id}");
                StatusText.Text = $"Launched PID {process.Id}, waiting for initialization...";

                await Task.Delay(2000);

                LogDiagnostic($"Staging agent DLLs for PID {process.Id}");
                StatusText.Text = $"Staging agent DLLs...";
                try
                {
                    RuntimeInjector.StageAgentDllsForTarget(process.Id);
                }
                catch (Exception stageEx)
                {
                    LogDiagnostic($"Stage warning: {stageEx.Message}");
                }

                var nativeInjectDll = GetNativeInjectDllPath();
                if (string.IsNullOrEmpty(nativeInjectDll))
                {
                    StatusText.Text = "NativeInject DLL not found. Cannot inject.";
                    LogDiagnostic("NativeInject DLL not found - aborting injection");
                    return process;
                }

                LogDiagnostic($"Injecting NativeInject DLL: {nativeInjectDll}");
                StatusText.Text = "Injecting Spy Agent...";

                RuntimeInjector.StatusChanged += msg => LogDiagnostic($"[Inject] {msg}");
                bool injected = await RuntimeInjector.InjectAsync(process.Id, nativeInjectDll, pipeName);
                RuntimeInjector.StatusChanged -= msg => LogDiagnostic($"[Inject] {msg}");
                LogDiagnostic($"Injection result: {injected}");

                if (injected)
                {
                    StatusText.Text = "Waiting for Spy Agent pipe...";
                    bool pipeReady = await WaitForSpyAgentAsync(pipeName, timeoutSeconds: 15);
                    if (pipeReady)
                    {
                        StatusText.Text = $"Injected successfully, PID {process.Id}";
                    }
                    else
                    {
                        StatusText.Text = $"Launched PID {process.Id} (agent initializing...)";
                    }
                }
                else
                {
                    StatusText.Text = $"Launched PID {process.Id} (injection failed)";
                }

                return process;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Launch/inject error: {ex.Message}";
                LogDiagnostic($"LaunchFrameworkAppWithInjection exception: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private async Task<Process?> LaunchWithStartupHook(string appPath, string? arguments, string pipeName)
        {
            try
            {
                var startupHookPath = GetStartupHookDllPath();
                LogDiagnostic($"GetStartupHookDllPath returned: {startupHookPath ?? "null"}");
                
                if (string.IsNullOrEmpty(startupHookPath))
                {
                    StatusText.Text = "Startup hook DLL not found";
                    LogDiagnostic("Startup hook DLL not found - aborting launch");
                    return null;
                }

                var targetDir = Path.GetDirectoryName(appPath);
                if (string.IsNullOrEmpty(targetDir))
                {
                    StatusText.Text = "Cannot resolve target application directory";
                    LogDiagnostic($"Cannot resolve target dir for: {appPath}");
                    return null;
                }
                LogDiagnostic($"Target dir: {targetDir}");

                var agentDllName = "WpfSpyAgent.dll";
                var agentDllPath = Path.Combine(Path.GetDirectoryName(startupHookPath) ?? "", agentDllName);
                LogDiagnostic($"Agent DLL path (same dir as hook): {agentDllPath}, exists: {File.Exists(agentDllPath)}");
                
                if (!File.Exists(agentDllPath))
                {
                    agentDllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, agentDllName);
                    LogDiagnostic($"Agent DLL path (IDE base dir): {agentDllPath}, exists: {File.Exists(agentDllPath)}");
                }

                var stagedHookPath = Path.Combine(targetDir, "WpfSpyAgent.StartupHook.dll");
                var stagedAgentPath = Path.Combine(targetDir, agentDllName);

                try
                {
                    if (File.Exists(agentDllPath))
                    {
                        File.Copy(agentDllPath, stagedAgentPath, overwrite: true);
                        LogDiagnostic($"Copied agent DLL to: {stagedAgentPath}");
                    }
                    else
                    {
                        LogDiagnostic($"Agent DLL not found at: {agentDllPath} - skipping copy");
                    }
                    File.Copy(startupHookPath, stagedHookPath, overwrite: true);
                    LogDiagnostic($"Copied startup hook to: {stagedHookPath}");
                }
                catch (Exception copyEx)
                {
                    StatusText.Text = $"Failed to stage agent DLLs: {copyEx.Message}";
                    LogDiagnostic($"Failed to stage DLLs: {copyEx.Message}");
                    return null;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = appPath,
                    Arguments = arguments ?? "",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false
                };

                psi.Environment["DOTNET_STARTUP_HOOKS"] = stagedHookPath;
                psi.Environment["WPFSPY_AGENT_ENABLED"] = "1";
                psi.Environment["WPFSPY_PIPE_NAME"] = pipeName;

                LogDiagnostic($"Launching: {appPath}");
                LogDiagnostic($"DOTNET_STARTUP_HOOKS={stagedHookPath}");
                LogDiagnostic($"WPFSPY_AGENT_ENABLED=1");
                LogDiagnostic($"WPFSPY_PIPE_NAME={pipeName}");

                StatusText.Text = $"Launching {Path.GetFileName(appPath)} with Spy Agent...";

                var process = Process.Start(psi);
                LogDiagnostic($"Process started: PID={process?.Id}");

                if (process != null)
                {
                    StatusText.Text = $"Launched PID {process.Id}, waiting for Spy Agent...";
                    bool pipeReady = await WaitForSpyAgentAsync(pipeName, timeoutSeconds: 15);
                    if (pipeReady)
                    {
                        StatusText.Text = $"Launched PID {process.Id}, Spy Agent ready";
                        LogDiagnostic($"Spy Agent pipe ready: {pipeName}");
                    }
                    else
                    {
                        StatusText.Text = $"Launched PID {process.Id} (agent initializing...)";
                        LogDiagnostic($"Spy Agent pipe not ready after 15s: {pipeName}");
                    }
                }

                return process;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Launch error: {ex.Message}";
                LogDiagnostic($"Launch exception: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private void LogDiagnostic(string message)
        {
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "repository", "attach_log.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        private bool IsNetFrameworkApp(string appPath)
        {
            try
            {
                var targetDir = Path.GetDirectoryName(appPath);
                if (string.IsNullOrEmpty(targetDir))
                    return false;

                var exeName = Path.GetFileNameWithoutExtension(appPath);
                
                var net461Path = Path.Combine(targetDir, "net461", $"{exeName}.exe");
                if (File.Exists(net461Path))
                    return true;

                if (Directory.Exists(Path.Combine(targetDir, "net461")))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }

    public enum AttachMode
    {
        RuntimeAttach,
        LaunchNew
    }
}
