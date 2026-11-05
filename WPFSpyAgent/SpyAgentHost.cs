using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using WpfSpyAgent.Protocol;

namespace WpfSpyAgent
{
    /// <summary>
    /// The IPC server side of WPFSpy. Hosts a Named Pipe and, for every
    /// connected client (the WPFSpy driver's Python client on the
    /// test-runner side — see drivers_rf/wpfspy_robotframework/WPFSpyLibrary.py),
    /// reads one JSON request per line, dispatches it onto the WPF UI
    /// thread, and writes back one JSON response per line.
    ///
    /// This class is started IN-PROCESS by the WPF application itself
    /// (see SampleWpfApp/App.xaml.cs OnStartup) when built/launched in
    /// test mode — it is not injected into an arbitrary external process.
    /// This "cooperative, built-in test hook" pattern is the standard,
    /// safe way in-process test/diagnostic agents are hosted (the same
    /// approach APM and profiling agents use), and is what "injected Spy
    /// Agent" means throughout this repository's docs and architecture
    /// slides. Attaching an agent to an already-running, unmodified
    /// third-party process is a materially different (and much more
    /// invasive) technique — using OS-level hooking/profiling APIs — and
    /// is intentionally out of scope here.
    /// </summary>
    public static class SpyAgentHost
    {
        private static Thread? _listenerThread;
        private static volatile bool _running;
        private static ManualResetEvent? _readyEvent;

        /// <summary>
        /// Entry point for CLR Hosting (ExecuteInDefaultAppDomain).
        /// Returns int instead of void so it can be called via ExecuteInDefaultAppDomain.
        /// </summary>
        public static int StartWithPipe(string pipeName)
        {
            Log("StartWithPipe: BEGIN");
            _readyEvent = new ManualResetEvent(false);
            Start(pipeName);
            Log("StartWithPipe: Start() returned");
            return 0; // Exit code for ExecuteInDefaultAppDomain
        }

        public static void Start(string pipeName = "WPFSpyAgentPipe")
        {
            if (_running)
            {
                Log("Start: already running, returning");
                return;
            }
            _running = true;
            Log($"Start: called, pipe={pipeName}");

            try
            {
                // Use ThreadPool for .NET Framework compatibility
                Log($"Start: Queuing ListenLoop to ThreadPool...");
                ThreadPool.QueueUserWorkItem(_ => ListenLoop(pipeName));
                Log($"Start: Queued to ThreadPool");
            }
            catch (Exception ex)
            {
                Log($"Start: EXCEPTION - {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void Stop() => _running = false;

        private static void ListenLoop(string pipeName)
        {
            Log("ListenLoop: BEGIN");
            
            // Signal that we've started
            _readyEvent?.Set();
            
            while (_running)
            {
                try
                {
                    Log($"ListenLoop: Creating pipe server '{pipeName}'");
                    // Do NOT wrap server in `using` here — ownership transfers
                    // to the client thread below, which disposes it when the
                    // connection ends.
                    var server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.InOut,
                        maxNumberOfServerInstances: 5,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    Log("ListenLoop: Waiting for connection (60s timeout)...");
#if NET8_0_OR_GREATER
                    // .NET Core has WaitForConnectionAsync
                    var connectTask = server.WaitForConnectionAsync();
                    bool completedInTime = connectTask.Wait(TimeSpan.FromSeconds(60));
                    bool wasConnected = completedInTime && !connectTask.IsFaulted && !connectTask.IsCanceled;
#else
                    // .NET Framework - use BeginWaitForConnection/EndWaitForConnection with timeout
                    var iar = server.BeginWaitForConnection(null, null);
                    bool wasConnected = iar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(60));
                    if (wasConnected)
                    {
                        try { server.EndWaitForConnection(iar); }
                        catch { wasConnected = false; }
                    }
                    iar.AsyncWaitHandle.Close();
#endif
                    if (wasConnected)
                    {
                        Log("ListenLoop: Client connected!");
                        // Handle each client on its own thread so the listen
                        // loop can accept the next connection immediately.
                        var clientThread = new Thread(() => HandleClient(server))
                        {
                            IsBackground = false,  // Foreground thread
                            Name = "WpfSpyAgent-Client",
                        };
                        clientThread.Start();
                    }
                    else
                    {
                        Log("ListenLoop: Connection timeout, disposing server");
                        server.Dispose();
                    }
                }
                catch (IOException ex)
                {
                    Log($"ListenLoop: IOException - {ex.Message}");
                    // Client disconnected mid-stream — loop and accept the next connection.
                }
                catch (ObjectDisposedException)
                {
                    Log("ListenLoop: ObjectDisposedException - stopping");
                    // Stop() was called while WaitForConnection was blocked.
                    break;
                }
                catch (Exception ex)
                {
                    Log($"ListenLoop: Error - {ex.GetType().Name}: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"WpfSpyAgent ListenLoop error: {ex.GetType().Name}: {ex.Message}");
                }
            }
            Log("ListenLoop: Exiting");
        }

        private static void Log(string message)
        {
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent_probe_log.txt");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        private static void HandleClient(NamedPipeServerStream server)
        {
            var reader = new StreamReader(server, Encoding.UTF8, false, 4096, leaveOpen: true);
            var writer = new StreamWriter(server, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n",
            };

            try
            {
                while (server.IsConnected)
                {
                    string? line = reader.ReadLine();
                    if (line is null)
                    {
                        break;
                    }

                    try
                    {
                        string responseJson = DispatchOnUiThread(line);
                        writer.WriteLine(responseJson);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"WpfSpyAgent HandleClient error: {ex.GetType().Name}: {ex.Message}");
                        try
                        {
                            writer.WriteLine(JsonHelper.Serialize(SpyResponse.Fail($"Server error: {ex.GetType().Name}: {ex.Message}")));
                        }
                        catch { /* pipe may already be closed */ }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WpfSpyAgent HandleClient outer error: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                try { writer.Dispose(); } catch { /* ignore disposal errors */ }
                try { reader.Dispose(); } catch { /* ignore disposal errors */ }
                server.Dispose();
            }
        }

        private static string DispatchOnUiThread(string requestJson)
        {
            // All visual-tree access (finding elements, reading/setting
            // properties, raising events) must happen on the WPF UI
            // (dispatcher) thread — this listener runs on its own
            // background thread, so every request is marshalled over.
            Dispatcher dispatcher = System.Windows.Application.Current?.Dispatcher
                                     ?? Dispatcher.CurrentDispatcher;
            return dispatcher.Invoke(() => CommandDispatcher.Dispatch(requestJson));
        }
    }
}
