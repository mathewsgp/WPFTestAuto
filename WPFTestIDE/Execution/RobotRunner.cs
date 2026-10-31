using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WpfTestIde.Execution
{
    public class RunSummary
    {
        public int Total { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int ExitCode { get; set; }
        public TimeSpan Duration { get; set; }
        public bool Success => ExitCode == 0 && Failed == 0 && Total > 0;
    }

    /// <summary>
    /// Runs a .robot script by shelling out to Robot Framework
    /// (`python -m robot`) exactly the way `run_tests.sh` does, and
    /// streams its console output live. This is intentionally a thin
    /// process wrapper rather than a re-implementation of Robot
    /// Framework's runner — the IDE and the command line always execute
    /// tests identically.
    /// </summary>
    public static class RobotRunner
    {
        public static async Task<RunSummary> RunAsync(
            string scriptPath,
            string outputDir,
            string workingDirectory,
            Action<string> onOutputLine,
            IDictionary<string, string>? extraEnv = null,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(outputDir);

            var psi = new ProcessStartInfo
            {
                FileName = PythonRunner.ResolvePythonExecutable(workingDirectory),
                WorkingDirectory = workingDirectory,
            };
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add("robot");
            psi.ArgumentList.Add("--outputdir");
            psi.ArgumentList.Add(outputDir);
            psi.ArgumentList.Add(scriptPath);

            if (extraEnv != null)
            {
                foreach (var kv in extraEnv)
                    psi.Environment[kv.Key] = kv.Value;
            }

            var summary = new RunSummary();
            // Robot Framework prints a final line like:
            //   "4 tests, 4 passed, 0 failed"
            var summaryRegex = new Regex(@"(\d+)\s+tests?,\s+(\d+)\s+passed,\s+(\d+)\s+failed", RegexOptions.IgnoreCase);

            var processResult = await ExternalProcessRunner.RunAsync(psi, line =>
            {
                onOutputLine(line);
                var match = summaryRegex.Match(line);
                if (match.Success)
                {
                    summary.Total = int.Parse(match.Groups[1].Value);
                    summary.Passed = int.Parse(match.Groups[2].Value);
                    summary.Failed = int.Parse(match.Groups[3].Value);
                }
            }, cancellationToken).ConfigureAwait(false);

            summary.ExitCode = processResult.ExitCode;
            summary.Duration = processResult.Duration;
            return summary;
        }
    }
}
