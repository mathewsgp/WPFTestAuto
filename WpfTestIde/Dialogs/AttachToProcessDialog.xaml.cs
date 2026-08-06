using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WpfTestIde.Helpers;

namespace WpfTestIde.Dialogs
{
    public class PageMapRow
    {
        public string TitleContains { get; set; } = "";
        public string PageAlias { get; set; } = "";
    }

    public partial class AttachToProcessDialog : Window
    {
        public int? SelectedProcessId { get; private set; }
        public string PipeName { get; private set; } = "WPFSpyAgentPipe";
        public List<(string, string)> PageMap { get; private set; } = new();
        public string? ApplicationPath { get; private set; }
        public string? Arguments { get; private set; }
        public AttachMode Mode { get; private set; } = AttachMode.RuntimeAttach;

        private readonly ObservableCollection<PageMapRow> _pageMapRows = new()
        {
            new PageMapRow { TitleContains = "Login", PageAlias = "LoginPage" },
            new PageMapRow { TitleContains = "Orders", PageAlias = "OrdersPage" },
        };

        public AttachToProcessDialog()
        {
            InitializeComponent();
            PageMapGrid.ItemsSource = _pageMapRows;
            RefreshProcessList();
            StatusText.Text = "Select a process to attach to, or launch a new process";
        }

        private void RefreshProcessList()
        {
            try
            {
                IEnumerable<Process> candidates;

                if (ShowAllProcessesCheck.IsChecked == true)
                {
                    // Show all processes
                    candidates = Process.GetProcesses()
                        .Where(p => p.Id != Process.GetCurrentProcess().Id)
                        .OrderBy(p => p.ProcessName);
                }
                else
                {
                    // Show only processes with visible windows
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

        private async void Attach_Click(object sender, RoutedEventArgs e)
        {
            PipeName = string.IsNullOrWhiteSpace(PipeNameBox.Text) ? "WPFSpyAgentPipe" : PipeNameBox.Text.Trim();
            PageMap = _pageMapRows
                .Where(r => !string.IsNullOrWhiteSpace(r.TitleContains) && !string.IsNullOrWhiteSpace(r.PageAlias))
                .Select(r => (r.TitleContains, r.PageAlias))
                .ToList();

            if (Mode == AttachMode.RuntimeAttach)
            {
                // Runtime attach mode
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

                // Try runtime injection
                var result = await TryRuntimeAttachAsync(SelectedProcessId.Value, PipeName);

                if (result)
                {
                    StatusText.Text = "Successfully attached!";
                    DialogResult = true;
                }
                else
                {
                    // Show message about manual injection
                    var msgResult = MessageBox.Show(
                        this,
                        "Could not auto-inject Spy Agent into the running process.\n\n" +
                        "The Spy Agent needs to be running inside the target process.\n\n" +
                        "Options:\n" +
                        "1. Restart the target app with DOTNET_STARTUP_HOOKS set\n" +
                        "2. Add SpyAgentHost.Start() to the app's source code\n" +
                        "3. Use Snoop to inspect the app directly\n\n" +
                        "Would you like to proceed anyway (connection may fail)?",
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
                // Launch new mode
                if (string.IsNullOrWhiteSpace(ApplicationPathBox.Text))
                {
                    MessageBox.Show(this, "Select an application to launch.", "Attach to Process",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ApplicationPath = ApplicationPathBox.Text.Trim();
                Arguments = string.IsNullOrWhiteSpace(ArgumentsBox.Text) ? null : ArgumentsBox.Text.Trim();

                StatusText.Text = "Launching process with Spy Agent...";

                // Launch with startup hook environment variables
                var result = LaunchWithStartupHook(ApplicationPath, Arguments, PipeName);

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

        private async Task<bool> TryRuntimeAttachAsync(int processId, string pipeName)
        {
            try
            {
                StatusText.Text = $"Checking if Spy Agent is already running in PID {processId}...";

                // First, check if the agent is already running
                var (isSupported, reason) = RuntimeInjector.CheckSupport();

                if (!isSupported)
                {
                    StatusText.Text = reason;
                    return false;
                }

                // Try to connect to the existing agent
                if (await RuntimeInjector.TestExistingConnectionAsync(processId, pipeName))
                {
                    StatusText.Text = "Connected to existing Spy Agent!";
                    return true;
                }

                StatusText.Text = "Spy Agent not found. Attempting runtime injection...";

                // Try runtime injection
                var dllPath = GetStartupHookDllPath();
                if (string.IsNullOrEmpty(dllPath))
                {
                    StatusText.Text = "Startup hook DLL not found. Cannot inject.";
                    return false;
                }

                return await RuntimeInjector.InjectAsync(processId, dllPath, pipeName);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Injection error: {ex.Message}";
                return false;
            }
        }

        private string? GetStartupHookDllPath()
        {
            // Look for the startup hook DLL in common locations
            var paths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WpfSpyAgent.StartupHook.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "WpfSpyAgent.StartupHook", "bin", "Debug", "net6.0-windows", "WpfSpyAgent.StartupHook.dll"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "WpfSpyAgent.StartupHook", "bin", "Debug", "net6.0-windows", "WpfSpyAgent.StartupHook.dll"),
            };

            foreach (var path in paths)
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return null;
        }

        private Process? LaunchWithStartupHook(string appPath, string? arguments, string pipeName)
        {
            try
            {
                var startupHookPath = GetStartupHookDllPath();
                if (string.IsNullOrEmpty(startupHookPath))
                {
                    StatusText.Text = "Startup hook DLL not found";
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

                // Set environment variables for Spy Agent injection
                psi.Environment["DOTNET_STARTUP_HOOKS"] = startupHookPath;
                psi.Environment["WPFSPY_AGENT_ENABLED"] = "1";
                psi.Environment["WPFSPY_PIPE_NAME"] = pipeName;

                StatusText.Text = $"Launching {Path.GetFileName(appPath)} with Spy Agent...";

                var process = Process.Start(psi);
                return process;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Launch error: {ex.Message}";
                return null;
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
