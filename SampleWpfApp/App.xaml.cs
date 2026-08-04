using System;
using System.IO;
using System.Windows;

namespace SampleWpfApp
{
    /// <summary>
    /// Deliberately plain. This app has NO reference to WpfSpyAgent, no
    /// startup hook logic, no awareness that any spy/injection mechanism
    /// exists — it is written exactly as if it were a genuine third-party
    /// WPF application someone else built. That's the point: every
    /// injection mechanism documented in docs/INJECTION_OPTIONS.md
    /// (WpfSpyAgent.StartupHook for modern .NET,
    /// WpfSpyAgent.FrameworkHook for .NET Framework) is demonstrated
    /// against this exact, unmodified class.
    /// </summary>
    public partial class App : Application
    {
        private static readonly string LogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "samplewpfapp_crash.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                File.WriteAllText(LogPath, $"UnhandledException: {args.ExceptionObject}");
            };
            DispatcherUnhandledException += (s, args) =>
            {
                File.WriteAllText(LogPath, $"DispatcherUnhandledException: {args.Exception}");
                args.Handled = true;
            };
            base.OnStartup(e);
        }
    }
}
