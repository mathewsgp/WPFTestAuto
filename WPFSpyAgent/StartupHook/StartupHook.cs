using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WpfSpyAgent;

/// <summary>
/// A .NET "startup hook" — the officially documented, Microsoft-
/// supported mechanism (since .NET Core 3.0) for loading extra code
/// into a .NET application via the DOTNET_STARTUP_HOOKS environment
/// variable, with ZERO modification to the target application's
/// source or binaries. This is the same extensibility point real APM
/// / diagnostics vendors use for "codeless" .NET instrumentation.
///
/// Requirements imposed by the .NET runtime for this to work at all
/// (not this project's choice — these are the framework's rules):
///   - The class MUST be named exactly `StartupHook`, in the global
///     namespace (no namespace wrapper), and the public static
///     `Initialize()` method signature is fixed and non-negotiable.
///   - It runs extremely early — before the target app's Main() and
///     before WPF's Application object exists — so we defer the
///     actual agent start until the WPF Dispatcher is available.
///
/// See docs/INJECTION_OPTIONS.md for how this compares to true
/// "attach to an already-running process" mechanisms (which this is
/// NOT — this requires the target to be (re)launched with the
/// environment variable set, not modified in any other way).
/// </summary>
internal static class StartupHook
{
    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("WPFSPY_AGENT_ENABLED") != "1")
        {
            return;
        }

        string pipeName = Environment.GetEnvironmentVariable("WPFSPY_PIPE_NAME") ?? "WPFSpyAgentPipe";

        Task.Run(() => WaitForApplicationAndStart(pipeName));
    }

    private static void WaitForApplicationAndStart(string pipeName)
    {
        string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_hook_log.txt");
        try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] WaitForApplicationAndStart started{Environment.NewLine}"); } catch { }
        for (int attempt = 0; attempt < 100; attempt++)
        {
            Application? app = Application.Current;
            if (app != null)
            {
                Dispatcher dispatcher = app.Dispatcher;
                try
                {
                    dispatcher.Invoke(() => {
                        try
                        {
                            var agentAsmPath = System.IO.Path.Combine(
                                AppDomain.CurrentDomain.BaseDirectory, "WpfSpyAgent.dll");
                            try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Loading agent from: {agentAsmPath}{Environment.NewLine}"); } catch { }
                            if (System.IO.File.Exists(agentAsmPath))
                            {
                                var agentAsm = System.Reflection.Assembly.LoadFrom(agentAsmPath);
                                try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Agent loaded successfully{Environment.NewLine}"); } catch { }
                                
                                var spyAgentHostType = agentAsm.GetType("WpfSpyAgent.SpyAgentHost");
                                if (spyAgentHostType == null)
                                {
                                    throw new InvalidOperationException("Could not find WpfSpyAgent.SpyAgentHost type in loaded assembly");
                                }
                                spyAgentHostType.GetMethod("Start", new[] { typeof(string) })?.Invoke(null, new object[] { pipeName });
                                try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] SpyAgentHost.Start invoked{Environment.NewLine}"); } catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"StartupHook exception: {ex.GetType().Name}: {ex.Message}");
                            try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] StartupHook exception: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}"); } catch { }
                            // Fail silently — this is a diagnostic hook and must not
                            // crash the host process.
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"StartupHook dispatcher exception: {ex.GetType().Name}: {ex.Message}");
                    try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] StartupHook dispatcher exception: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}"); } catch { }
                }
                return;
            }
            Thread.Sleep(100);
        }
    }
}
