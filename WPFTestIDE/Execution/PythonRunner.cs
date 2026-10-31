using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WpfTestIde.Execution
{
    public static class PythonRunner
    {
        public static Task<ProcessRunResult> RunAsync(
            string scriptPath,
            string workingDirectory,
            string frameworkRoot,
            Action<string> onOutputLine,
            System.Collections.Generic.IDictionary<string, string>? extraEnv = null,
            CancellationToken cancellationToken = default)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ResolvePythonExecutable(workingDirectory),
                WorkingDirectory = workingDirectory,
            };
            startInfo.ArgumentList.Add("-u");
            startInfo.ArgumentList.Add(scriptPath);

            if (extraEnv != null)
            {
                foreach (var pair in extraEnv)
                    startInfo.Environment[pair.Key] = pair.Value;
            }

            var currentPythonPath = startInfo.Environment.TryGetValue("PYTHONPATH", out var configuredPath)
                ? configuredPath
                : Environment.GetEnvironmentVariable("PYTHONPATH");
            startInfo.Environment["PYTHONPATH"] = string.IsNullOrWhiteSpace(currentPythonPath)
                ? frameworkRoot
                : frameworkRoot + Path.PathSeparator + currentPythonPath;
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

            return ExternalProcessRunner.RunAsync(startInfo, onOutputLine, cancellationToken);
        }

        internal static string ResolvePythonExecutable(string workingDirectory)
        {
            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                string venvPython = Path.Combine(workingDirectory, ".venv", "Scripts", "python.exe");
                if (File.Exists(venvPython))
                    return venvPython;
            }

            return "python";
        }
    }
}
