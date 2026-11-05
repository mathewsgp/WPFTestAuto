using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace WpfSpyAgent.FrameworkHook
{
    /// <summary>
    /// The .NET FRAMEWORK equivalent of WpfSpyAgent.StartupHook's
    /// `StartupHook` class — .NET Framework has no DOTNET_STARTUP_HOOKS
    /// mechanism (that's .NET Core 3.0+/.NET 5+ only), but it has an
    /// older, equally first-class extensibility point: a custom
    /// <see cref="AppDomainManager"/>, which the CLR instantiates for the
    /// default AppDomain when told to via configuration — either an
    /// environment variable (no file changes to the target at all) or
    /// the target's own .exe.config file (a config-file edit, not a
    /// source/binary change). See docs/INJECTION_OPTIONS.md for both
    /// activation methods and their tradeoffs.
    ///
    /// `InitializeNewDomain` runs very early in the target process's
    /// startup — even earlier than a .NET Core startup hook — so, exactly
    /// like the StartupHook counterpart, actually starting the agent is
    /// deferred until WPF's Application/Dispatcher exists.
    /// </summary>
    public class SpyAppDomainManager : AppDomainManager
    {
        public override void InitializeNewDomain(AppDomainSetup appDomainInfo)
        {
            base.InitializeNewDomain(appDomainInfo);

            string pipeName = Environment.GetEnvironmentVariable("WPFSPY_PIPE_NAME") ?? "WPFSpyAgentPipe";
            Task.Run(() => WaitForApplicationAndStart(pipeName));
        }

        private static void WaitForApplicationAndStart(string pipeName)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                Application app = Application.Current;
                if (app != null)
                {
                    Dispatcher dispatcher = app.Dispatcher;
                    dispatcher.Invoke(() => SpyAgentHost.Start(pipeName));
                    return;
                }
                Thread.Sleep(100);
            }
        }
    }
}
