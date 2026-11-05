using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WpfTestIde.Models;
using WpfTestIde.Recording;

namespace WpfTestIde.Execution
{
    public sealed class ScriptExecutionRequest
    {
        public ScriptFormat Format { get; init; }
        public string SourceText { get; init; } = "";
        public string? SourcePath { get; init; }
        public IReadOnlyList<RecordedStep> Steps { get; init; } = Array.Empty<RecordedStep>();
        public IReadOnlyDictionary<string, List<string>> Datasets { get; init; } = new Dictionary<string, List<string>>();
        public string FrameworkRoot { get; init; } = "";
        public string WorkingDirectory { get; init; } = "";
        public string OutputDirectory { get; init; } = "";
        public string? Driver { get; init; }
        public string? Mode { get; init; }
        public string? AppId { get; init; }
        public IReadOnlyList<string> RecordingModes { get; init; } = Array.Empty<string>();
        public IDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    }

    public sealed record RobotScriptPreparation(string Content, IReadOnlyList<int> ProcessIds);

    public sealed record ScriptExecutionResult(
        ScriptFormat Format,
        int ExitCode,
        int TotalTests,
        int PassedTests,
        int FailedTests,
        TimeSpan Duration)
    {
        public bool Success => ExitCode == 0
            && (Format == ScriptFormat.Python || TotalTests > 0 && FailedTests == 0);
    }

    public interface IScriptExecutionService
    {
        Task<ScriptExecutionResult> RunAsync(
            ScriptExecutionRequest request,
            Action<string> onOutputLine,
            Func<string, CancellationToken, Task<RobotScriptPreparation>>? prepareRobotScriptAsync = null,
            CancellationToken cancellationToken = default);
    }

    public sealed class ScriptExecutionService : IScriptExecutionService
    {
        public async Task<ScriptExecutionResult> RunAsync(
            ScriptExecutionRequest request,
            Action<string> onOutputLine,
            Func<string, CancellationToken, Task<RobotScriptPreparation>>? prepareRobotScriptAsync = null,
            CancellationToken cancellationToken = default)
        {
            string content;
            ScriptFormat executionFormat;
            switch (request.Format)
            {
                case ScriptFormat.Steps:
                    content = GenerateRobotScript(request, request.Steps, datasets: request.Datasets);
                    executionFormat = ScriptFormat.Robot;
                    break;
                case ScriptFormat.Json:
                    var imported = JsonStepScript.DeserializeDocument(request.SourceText);
                    content = GenerateRobotScript(
                        request,
                        imported.Steps,
                        imported.Driver ?? request.Driver,
                        imported.Mode ?? request.Mode,
                        imported.AppId ?? request.AppId,
                        imported.Datasets);
                    executionFormat = ScriptFormat.Robot;
                    break;
                case ScriptFormat.Python:
                case ScriptFormat.Robot:
                    content = request.SourceText;
                    executionFormat = request.Format;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request.Format), request.Format, "Unsupported script format.");
            }

            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidDataException($"The {request.Format} script is empty.");

            Directory.CreateDirectory(request.OutputDirectory);
            var environment = new Dictionary<string, string>(request.Environment, StringComparer.OrdinalIgnoreCase);
            string extension = executionFormat == ScriptFormat.Python ? ".py" : ".robot";

            if (executionFormat == ScriptFormat.Robot && prepareRobotScriptAsync != null)
            {
                var prepared = await prepareRobotScriptAsync(content, cancellationToken).ConfigureAwait(true);
                content = prepared.Content;
                if (prepared.ProcessIds.Count > 0)
                    environment["WPFSPY_PROCESS_IDS"] = string.Join(",", prepared.ProcessIds);
            }

            var generatedRobot = executionFormat == ScriptFormat.Robot
                && request.Format is ScriptFormat.Steps or ScriptFormat.Json;
            var scriptDirectory = generatedRobot
                ? Path.Combine(request.FrameworkRoot, "tests")
                : GetScriptDirectory(request.SourcePath, request.FrameworkRoot);
            Directory.CreateDirectory(scriptDirectory);
            string scriptPath = Path.Combine(scriptDirectory, $"ide_run_{Guid.NewGuid():N}{extension}");

            try
            {
                await File.WriteAllTextAsync(scriptPath, content, cancellationToken).ConfigureAwait(false);
                if (executionFormat == ScriptFormat.Python)
                {
                    var result = await PythonRunner.RunAsync(
                        scriptPath,
                        request.WorkingDirectory,
                        request.FrameworkRoot,
                        onOutputLine,
                        environment,
                        cancellationToken).ConfigureAwait(false);
                    return new ScriptExecutionResult(request.Format, result.ExitCode, 0, 0, 0, result.Duration);
                }

                var robotResult = await RobotRunner.RunAsync(
                    scriptPath,
                    request.OutputDirectory,
                    request.WorkingDirectory,
                    onOutputLine,
                    environment,
                    cancellationToken).ConfigureAwait(false);
                return new ScriptExecutionResult(
                    request.Format,
                    robotResult.ExitCode,
                    robotResult.Total,
                    robotResult.Passed,
                    robotResult.Failed,
                    robotResult.Duration);
            }
            finally
            {
                try
                {
                    if (File.Exists(scriptPath))
                        File.Delete(scriptPath);
                }
                catch (IOException)
                {
                    // The run result and artifacts are more useful than failing cleanup.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best-effort cleanup for temporary run scripts.
                }
            }
        }

        private static string GenerateRobotScript(
            ScriptExecutionRequest request,
            IEnumerable<RecordedStep> steps,
            string? driver = null,
            string? mode = null,
            string? appId = null,
            IReadOnlyDictionary<string, List<string>>? datasets = null)
        {
            var nodeList = steps.ToList();
            return ScriptGenerator.Generate(
                nodeList,
                testCaseName: "Recorded Session Playback",
                driver: driver ?? request.Driver,
                mode: mode ?? request.Mode,
                recordingModes: request.RecordingModes.ToList(),
                appId: appId ?? request.AppId,
                datasets: datasets ?? request.Datasets);
        }

        private static string GetScriptDirectory(string? sourcePath, string frameworkRoot)
        {
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                string? sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
                if (!string.IsNullOrWhiteSpace(sourceDirectory))
                    return sourceDirectory;
            }

            return Path.Combine(frameworkRoot, "tests");
        }
    }
}
