using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using WpfTestIde.Models;

namespace WpfTestIde.Recording
{
    /// <summary>
    /// Orchestrates recording against an attached target process:
    ///  - a global mouse hook (see GlobalMouseHook) detects clicks anywhere
    ///    on screen and, when they land within an attached-process window,
    ///    resolves the element via ElementProbe (WPFSpy-only mode);
    ///  - a focus-changed handler detects when focus leaves a
    ///    text-entry control and commits its final value as a SetValue
    ///    step, so typed text is captured on "blur" rather than per
    ///    keystroke;
    ///  - a small, user-configured (window title substring -> page alias)
    ///    map assigns each element to a `Page.Element` alias, matching the
    ///    convention used throughout the Python framework's repositories.
    /// </summary>
    public class RecordingSession : IDisposable
    {
        private readonly ElementProbe _probe;
        private readonly GlobalMouseHook _mouseHook = new();
        private readonly List<(string TitleContains, string PageAlias)> _pageMap;
        private readonly int _targetProcessId;
        private readonly string _pipeName;

        private ProbedElement? _pendingFocusedInput;
        private string? _pendingFocusedAlias;
        private bool _running;

        public event Action<RecordedStep, ElementEntry>? StepCaptured;

        public RecordingSession(string pipeName, int targetProcessId, List<(string, string)> pageMap)
        {
            _probe = new ElementProbe(pipeName);
            _pipeName = pipeName;
            _targetProcessId = targetProcessId;
            _pageMap = pageMap;
        }

        public void Start()
        {
            if (_running)
            {
                return;
            }
            _running = true;
            _mouseHook.LeftButtonDown += OnClick;
            _mouseHook.Start();
            // Focus tracking removed in WPFSpy-only mode.
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }
            _running = false;
            _mouseHook.LeftButtonDown -= OnClick;
            _mouseHook.Stop();
            CommitPendingValueIfAny();
        }

        private void OnClick(int x, int y)
        {
            CommitPendingValueIfAny();

            if (!PointBelongsToTargetProcess(x, y))
            {
                Log($"[OnClick] rejected: point ({x},{y}) not in target process");
                return;
            }

            var probed = _probe.ProbeAt(x, y);
            if (probed is null)
            {
                Log($"[OnClick] rejected: ProbeAt({x},{y}) returned null");
                return;
            }

            Log($"[OnClick] accepted: {probed.ControlType} name={probed.Name} automationId={probed.AutomationId}");

            string page = ResolvePage(x, y);
            string ancestorPath = BuildAncestorPath(probed);
            string idOrName = string.IsNullOrEmpty(probed.AutomationId) ? probed.Name : probed.AutomationId!;
            string alias = string.IsNullOrEmpty(ancestorPath)
                ? $"{page}.{idOrName}"
                : $"{page}.{ancestorPath}.{idOrName}";

            bool isTextEntry = probed.ControlType is "TextBox" or "Edit" or "ComboBox" or "PasswordBox";
            if (isTextEntry)
            {
                _pendingFocusedInput = probed;
                _pendingFocusedAlias = alias;
                Log($"[OnClick] text entry pending: alias={alias}");
                return;
            }

            bool isToggle = probed.ControlType == "CheckBox" || probed.ControlType.Contains("Toggle");
            var entry = BuildEntry(alias, probed);
            var step = new RecordedStep
            {
                Kind = StepKind.Action,
                Alias = alias,
                Action = isToggle ? ActionKind.Toggle : ActionKind.Invoke,
                NonStandard = probed.ResolvedVia == "WPFSpy",
                Value = isToggle ? (probed.Text ?? "") : null,
            };
            Log($"[OnClick] firing StepCaptured: alias={alias}, action={step.Action}");
            StepCaptured?.Invoke(step, entry);
        }

        private void CommitPendingValueIfAny()
        {
            if (_pendingFocusedInput is null || _pendingFocusedAlias is null)
            {
                return;
            }

            string? value = _probe.GetCurrentValue(_pendingFocusedInput);
            var entry = BuildEntry(_pendingFocusedAlias, _pendingFocusedInput);
            var step = new RecordedStep
            {
                Kind = StepKind.Action,
                Alias = _pendingFocusedAlias,
                Action = ActionKind.SetValue,
                Value = value ?? "",
                NonStandard = _pendingFocusedInput.ResolvedVia == "WPFSpy",
            };
            StepCaptured?.Invoke(step, entry);

            _pendingFocusedInput = null;
            _pendingFocusedAlias = null;
        }

        private bool PointBelongsToTargetProcess(int x, int y)
        {
            // In WPFSpy-only mode, use the pipe to verify the target process
            // instead of FlaUI desktop enumeration.
            try
            {
                var client = new SpyAgentClient(_pipeName);
                var response = client.Send("GetMainWindowTitle");
                Log($"[PointBelongsToTargetProcess] pipe success={response.Success}, data='{response.Data}', error='{response.Error}'");
                if (response.Success && !string.IsNullOrEmpty(response.Data))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"[PointBelongsToTargetProcess] exception: {ex.GetType().Name}: {ex.Message}");
            }
            return false;
        }

        private string ResolvePage(int x, int y)
        {
            // In WPFSpy-only mode, get the window title from the pipe
            // instead of FlaUI.
            try
            {
                var client = new SpyAgentClient(_pipeName);
                var response = client.Send("GetMainWindowTitle");
                if (response.Success && !string.IsNullOrEmpty(response.Data))
                {
                    string title = response.Data;
                    foreach (var (titleContains, pageAlias) in _pageMap)
                    {
                        if (title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
                        {
                            return pageAlias;
                        }
                    }
                }
            }
            catch { }
            return "UnknownPage";
        }

        /// <summary>
        /// Parses the XPath returned by ProbeAt and builds a dot-separated
        /// path of ancestor identifiers (AutomationId or Name), excluding
        /// the final element segment. Returns empty string if no ancestors
        /// have meaningful identifiers.
        /// Example: "/Window[@AutomationId='MainWindow']/Grid/TextBox[@AutomationId='txtUsername']"
        ///          -> "MainWindow" (Grid has no identifier, so it's skipped).
        /// </summary>
        private static string BuildAncestorPath(ProbedElement probed)
        {
            if (string.IsNullOrEmpty(probed.XPath))
            {
                return "";
            }

            var parts = new System.Collections.Generic.List<string>();
            var segments = probed.XPath.Split('/');
            for (int i = 1; i < segments.Length - 1; i++)
            {
                string segment = segments[i];
                if (string.IsNullOrEmpty(segment))
                {
                    continue;
                }

                string? identifier = ExtractIdentifier(segment);
                if (!string.IsNullOrEmpty(identifier))
                {
                    parts.Add(identifier);
                }
            }
            return string.Join(".", parts);
        }

        private static string? ExtractIdentifier(string segment)
        {
            int bracketIndex = segment.IndexOf('[');
            if (bracketIndex >= 0)
            {
                string predicate = segment.Substring(bracketIndex + 1);
                if (predicate.StartsWith("@AutomationId='"))
                {
                    int start = "@AutomationId='".Length;
                    int end = predicate.IndexOf('\'', start);
                    if (end > start)
                    {
                        return predicate.Substring(start, end - start);
                    }
                }
                else if (predicate.StartsWith("@Name='"))
                {
                    int start = "@Name='".Length;
                    int end = predicate.IndexOf('\'', start);
                    if (end > start)
                    {
                        string name = predicate.Substring(start, end - start);
                        // Skip WPF template parts (PART_*) and Adorner layers E                        // they are internal visuals, not user-facing elements.
                        if (name.StartsWith("PART_") || name == "AdornerLayer")
                            return null;
                        return name;
                    }
                }
            }
            return null;
        }

        private static ElementEntry BuildEntry(string alias, ProbedElement probed) => new()
        {
            Alias = alias,
            DisplayName = probed.Name,
            ControlType = probed.ControlType,
            AutomationId = probed.AutomationId,
            Name = probed.Name,
            XPath = probed.XPath,
        };

        internal static void Log(string message)
        {
            try
            {
                string logPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "WpfTestIde_recording_log.txt");
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        public void Dispose()
        {
            Stop();
            _probe.Dispose();
        }
    }
}

