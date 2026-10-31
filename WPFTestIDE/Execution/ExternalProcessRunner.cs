using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WpfTestIde.Execution
{
    public sealed record ProcessRunResult(int ExitCode, TimeSpan Duration);

    public static class ExternalProcessRunner
    {
        public static async Task<ProcessRunResult> RunAsync(
            ProcessStartInfo startInfo,
            Action<string> onOutputLine,
            CancellationToken cancellationToken = default)
        {
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.StandardOutputEncoding ??= Encoding.UTF8;
            startInfo.StandardErrorEncoding ??= Encoding.UTF8;

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var stdoutClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stderrClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    stdoutClosed.TrySetResult();
                else
                    onOutputLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    stderrClosed.TrySetResult();
                else
                    onOutputLine(e.Data);
            };

            var startedAt = Stopwatch.StartNew();
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The child can exit between HasExited and Kill.
                }

                await process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(stdoutClosed.Task, stderrClosed.Task).ConfigureAwait(false);
                throw;
            }

            await Task.WhenAll(stdoutClosed.Task, stderrClosed.Task).ConfigureAwait(false);
            startedAt.Stop();
            return new ProcessRunResult(process.ExitCode, startedAt.Elapsed);
        }
    }
}
