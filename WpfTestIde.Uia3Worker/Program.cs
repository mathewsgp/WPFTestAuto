using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WpfTestIde.Uia3Worker.Core;

namespace WpfTestIde.Uia3Worker;

class Program
{
    static async Task<int> Main(string[] args)
    {
        int pid = 0;
        string? pipeName = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var p))
                        pid = p;
                    break;
                case "--title":
                    if (i + 1 < args.Length)
                    {
                        pid = FindPidByWindowTitle(args[++i]);
                    }
                    break;
                case "--pipe":
                    if (i + 1 < args.Length)
                        pipeName = args[++i];
                    break;
            }
        }

        if (pipeName == null)
            pipeName = $"Uia3WorkerPipe_{pid}";

        Console.WriteLine($"[Uia3Worker] Starting. pid={pid} pipe={pipeName}");

        using var probe = new Uia3Probe();
        using var server = new Uia3NamedPipeServer(probe, pipeName);
        server.Start();

        // Wait for cancellation (Ctrl+C)
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException) { }

        Console.WriteLine("[Uia3Worker] Shutting down.");
        return 0;
    }

    static int FindPidByWindowTitle(string titleSubstring)
    {
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    if (!string.IsNullOrEmpty(p.MainWindowTitle) &&
                        p.MainWindowTitle.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase))
                    {
                        return p.Id;
                    }
                }
                catch { }
            }
        }
        catch { }
        return 0;
    }
}
