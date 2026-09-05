using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WpfTestIde.Execution
{
    public class RunSummary
    {
        public int Total { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public bool Success => Failed == 0 && Total > 0;
    }

    /// <summary>
    /// Runs a generated .robot script by shelling out to Robot Framework
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
            System.Collections.Generic.IDictionary<string, string>? extraEnv = null)
        {
            Directory.CreateDirectory(outputDir);

            string pythonExe = ResolvePythonExecutable(workingDirectory);

            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"-m robot --outputdir \"{outputDir}\" \"{scriptPath}\"",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            if (extraEnv != null)
            {
                foreach (var kv in extraEnv)
                {
                    psi.Environment[kv.Key] = kv.Value;
                }
            }

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            var summary = new RunSummary();
            // Robot Framework prints a final line like:
            //   "4 tests, 4 passed, 0 failed"
            var summaryRegex = new Regex(@"(\d+)\s+tests?,\s+(\d+)\s+passed,\s+(\d+)\s+failed", RegexOptions.IgnoreCase);

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                onOutputLine(e.Data);
                var match = summaryRegex.Match(e.Data);
                if (match.Success)
                {
                    summary.Total = int.Parse(match.Groups[1].Value);
                    summary.Passed = int.Parse(match.Groups[2].Value);
                    summary.Failed = int.Parse(match.Groups[3].Value);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) onOutputLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            return summary;
        }

        private static string ResolvePythonExecutable(string workingDirectory)
        {
            if (!string.IsNullOrEmpty(workingDirectory))
            {
                string venvPython = Path.Combine(workingDirectory, ".venv", "Scripts", "python.exe");
                if (File.Exists(venvPython))
                {
                    return venvPython;
                }
            }

            return "python";
        }
    }
}
