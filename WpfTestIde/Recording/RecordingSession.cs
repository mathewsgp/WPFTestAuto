using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using WpfTestIde.Models;

namespace WpfTestIde.Recording
{
    /// <summary>Which element-resolution strategy a target uses.</summary>
    public enum ProbeMode { WPFSpy, FlaUI }

    /// <summary>
    /// Per-app configuration for a recording session. A single session can
    /// carry any number of these (multi-app recording). Each target knows
    /// its own probe, pipe, pid (if known), exe name, page map, appId, and
    /// probe mode (WPFSpy or FlaUI).
    /// </summary>
    public sealed class RecordingTarget
    {
        public string AppId { get; }
        public string PipeName { get; }
        public int ProcessId { get; set; }                // mutable: live pid may be discovered later
        public string? ExeName { get; }
        public List<(string TitleContains, string PageAlias)> PageMap { get; }
        public ProbeMode Mode { get; }
        public int Priority { get; }                       // lower = checked first

        // Lazily-constructed per-target probes. We do NOT construct the
        // ElementProbe at registration time so targets added before the
        // target process exists don't immediately try to open a pipe.
        internal ElementProbe? WpfProbe { get; set; }
        internal FlaUIElementProbe? FlaProbe { get; set; }

        public RecordingTarget(
            string appId,
            string pipeName,
            int processId,
            string? exeName,
            List<(string, string)> pageMap,
            ProbeMode mode,
            int priority = 0)
        {
            AppId = appId;
            PipeName = pipeName ?? "";
            ProcessId = processId;
            ExeName = exeName;
            PageMap = pageMap ?? new List<(string, string)>();
            Mode = mode;
            Priority = priority;

            if (mode == ProbeMode.WPFSpy)
            {
                WpfProbe = new ElementProbe(PipeName);
            }
            else
            {
                FlaProbe = new FlaUIElementProbe(processId, ResolveLivePid);
            }
        }

        private int ResolveLivePid()
        {
            // Mirror RecordingSession.ResolveTargetPid for this target's exe
            // name. We duplicate the algorithm here so each FlaUI target
            // independently finds its own process.
            if (ProcessId > 0) return ProcessId;
            if (string.IsNullOrEmpty(ExeName)) return 0;

            try
            {
                string exeName = System.IO.Path.GetFileNameWithoutExtension(ExeName);
                string fullExeName = System.IO.Path.GetFileName(ExeName);
                string[] candidates = new[]
                {
                    exeName,
                    fullExeName,
                    exeName.Replace(" ", ""),
                    exeName.Equals("calc", StringComparison.OrdinalIgnoreCase) ? "CalculatorApp" : null,
                    exeName.Equals("notepad", StringComparison.OrdinalIgnoreCase) ? "Notepad" : null,
                };
                foreach (string? candidate in candidates)
                {
                    if (string.IsNullOrEmpty(candidate)) continue;
                    var processes = Process.GetProcessesByName(candidate);
                    if (processes.Length == 0) continue;
                    foreach (var p in processes)
                    {
                        try
                        {
                            if (p.MainWindowHandle != IntPtr.Zero) return p.Id;
                        }
                        catch { }
                    }
                    return processes[0].Id;
                }
            }
            catch { }
            return 0;
        }
    }

    /// <summary>
    /// Orchestrates recording against one or more attached target apps:
    ///  - a global mouse hook (see GlobalMouseHook) detects clicks anywhere
    ///    on screen;
    ///  - for each click, the session finds the FIRST target whose
    ///    PointBelongsToTarget accepts the click point (pid/title match),
    ///    then resolves the element via that target's probe
    ///    (WPFSpy named pipe when an in-process agent is present, or the
    ///    system-wide UIA tree otherwise);
    ///  - text-entry controls commit their final value as a SetValue step
    ///    on the next click, so typed text is captured on "blur" rather
    ///    than per keystroke;
    ///  - a small, user-configured (window title substring -> page alias)
    ///    map per target assigns each element to a `Page.Element` alias.
    /// </summary>
    public class RecordingSession : IDisposable
    {
        private readonly GlobalMouseHook _mouseHook = new();
        private readonly Dictionary<string, RecordingTarget> _targets = new();
        private readonly object _targetsLock = new();

        private ProbedElement? _pendingFocusedInput;
        private RecordingTarget? _pendingFocusedTarget;
        private string? _pendingFocusedAlias;
        private bool _running;

        public event Action<RecordedStep, ElementEntry>? StepCaptured;

        /// <summary>All currently registered targets, sorted by priority (lowest first).</summary>
        public IReadOnlyList<RecordingTarget> Targets
        {
            get
            {
                lock (_targetsLock)
                {
                    return _targets.Values.OrderBy(t => t.Priority).ToList();
                }
            }
        }

        public RecordingSession()
        {
            _mouseHook.LeftButtonDown += OnClick;
        }

        /// <summary>Add a target to this session. Idempotent on AppId.</summary>
        public void AddTarget(RecordingTarget target)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            lock (_targetsLock)
            {
                _targets[target.AppId] = target;
            }
            Log($"[RecordingSession] AddTarget appId={target.AppId} mode={target.Mode} pipe={target.PipeName} pid={target.ProcessId} exe={target.ExeName} priority={target.Priority}");
        }

        /// <summary>Remove a target by appId. No-op if not present.</summary>
        public void RemoveTarget(string appId)
        {
            lock (_targetsLock)
            {
                if (_targets.TryGetValue(appId, out var t))
                {
                    t.WpfProbe = null;
                    _targets.Remove(appId);
                    Log($"[RecordingSession] RemoveTarget appId={appId}");
                }
            }
        }

        /// <summary>Remove all targets and dispose their probes.</summary>
        public void ClearTargets()
        {
            lock (_targetsLock)
            {
                foreach (var t in _targets.Values)
                {
                    t.WpfProbe = null;
                }
                _targets.Clear();
            }
            Log($"[RecordingSession] ClearTargets");
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _mouseHook.Start();
            Log($"[RecordingSession] Start targets={_targets.Count}");
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _mouseHook.Stop();
            CommitPendingValueIfAny();
            Log($"[RecordingSession] Stop");
        }

        // Returns the first target that claims ownership of (x,y), or null.
        private RecordingTarget? FindOwningTarget(int x, int y)
        {
            List<RecordingTarget> snapshot;
            lock (_targetsLock) { snapshot = _targets.Values.OrderBy(t => t.Priority).ToList(); }
            foreach (var t in snapshot)
            {
                if (PointBelongsToTarget(x, y, t)) return t;
            }
            return null;
        }

        private void OnClick(int x, int y)
        {
            CommitPendingValueIfAny();

            RecordingTarget? target = FindOwningTarget(x, y);
            if (target is null)
            {
                Log($"[OnClick] rejected: point ({x},{y}) not in any target app");
                return;
            }

            ProbedElement? probed = target.Mode == ProbeMode.WPFSpy
                ? target.WpfProbe?.ProbeAt(x, y)
                : target.FlaProbe?.ProbeAt(x, y);

            if (probed is null)
            {
                Log($"[OnClick] ProbeAt({x},{y}) returned null for appId={target.AppId}, trying window-level fallback");
                probed = target.Mode == ProbeMode.FlaUI ? CreateWindowLevelElement(target) : null;
                if (probed is null)
                {
                    Log($"[OnClick] rejected: ProbeAt returned null and no window fallback for appId={target.AppId}");
                    return;
                }
                Log($"[OnClick] using window-level fallback for appId={target.AppId}: {probed.ControlType} name={probed.Name}");
            }

            Log($"[OnClick] accepted for appId={target.AppId}: {probed.ControlType} name={probed.Name} automationId={probed.AutomationId}");

            string page = ResolvePage(x, y, target);
            string ancestorPath = BuildAncestorPath(probed);
            string idOrName = string.IsNullOrEmpty(probed.AutomationId) ? probed.Name : probed.AutomationId!;
            string alias = string.IsNullOrEmpty(ancestorPath)
                ? $"{page}.{idOrName}"
                : $"{page}.{ancestorPath}.{idOrName}";

            bool isTextEntry = probed.ControlType is "TextBox" or "Edit" or "ComboBox" or "PasswordBox" or "Document";
            if (isTextEntry)
            {
                _pendingFocusedInput = probed;
                _pendingFocusedTarget = target;
                _pendingFocusedAlias = alias;
                Log($"[OnClick] text entry pending: appId={target.AppId} alias={alias}");
                return;
            }

            bool isToggle = probed.ControlType == "CheckBox" || probed.ControlType.Contains("Toggle");
            var entry = BuildEntry(alias, probed, target);
            var step = new RecordedStep
            {
                Kind = StepKind.Action,
                Alias = alias,
                Action = isToggle ? ActionKind.Toggle : ActionKind.Invoke,
                NonStandard = probed.ResolvedVia == "WPFSpy",
                Value = isToggle ? (probed.Text ?? "") : null,
                AppId = target.AppId,
            };
            Log($"[OnClick] firing StepCaptured: appId={target.AppId} alias={alias} action={step.Action}");
            StepCaptured?.Invoke(step, entry);
        }

        private void CommitPendingValueIfAny()
        {
            if (_pendingFocusedInput is null || _pendingFocusedTarget is null || _pendingFocusedAlias is null) return;

            string? value = _pendingFocusedTarget.Mode == ProbeMode.WPFSpy
                ? _pendingFocusedTarget.WpfProbe?.GetCurrentValue(_pendingFocusedInput)
                : _pendingFocusedTarget.FlaProbe?.GetCurrentValue(_pendingFocusedInput);

            var entry = BuildEntry(_pendingFocusedAlias, _pendingFocusedInput, _pendingFocusedTarget);
            var step = new RecordedStep
            {
                Kind = StepKind.Action,
                Alias = _pendingFocusedAlias,
                Action = ActionKind.SetValue,
                Value = value ?? "",
                NonStandard = _pendingFocusedInput.ResolvedVia == "WPFSpy",
                AppId = _pendingFocusedTarget.AppId,
            };
            StepCaptured?.Invoke(step, entry);
            _pendingFocusedInput = null;
            _pendingFocusedTarget = null;
            _pendingFocusedAlias = null;
        }

        /// <summary>
        /// Returns true if (x,y) is inside any window owned by the given target.
        /// For WPFSpy mode the agent reports its main window title; we layer a
        /// Win32 WindowFromPoint pid check on top so we can distinguish between
        /// multiple WPF apps that happen to all expose agents.
        /// For FlaUI mode we use the same pid-comparison strategies as before
        /// (WindowFromPoint, bounds scan, foreground).
        /// </summary>
        private bool PointBelongsToTarget(int x, int y, RecordingTarget target)
        {
            // Compute the live pid for this target. For WPFSpy we ask the
            // agent (it owns its process) but also layer the Win32 check.
            int livePid = target.ProcessId;
            if (livePid <= 0 && target.Mode == ProbeMode.FlaUI)
            {
                livePid = ResolveLivePidForTarget(target);
            }
            if (livePid <= 0 && target.Mode == ProbeMode.WPFSpy)
            {
                livePid = ResolveLivePidForTarget(target);
            }

            if (target.Mode == ProbeMode.WPFSpy)
            {
                // 1) Pipe-level liveness + title presence.
                bool agentOk = false;
                try
                {
                    if (!string.IsNullOrEmpty(target.PipeName))
                    {
                        var client = new SpyAgentClient(target.PipeName);
                        var response = client.Send("GetMainWindowTitle");
                        if (response.Success && !string.IsNullOrEmpty(response.Data))
                        {
                            agentOk = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[PointBelongsToTarget] WPFSpy agent exception for appId={target.AppId}: {ex.GetType().Name}: {ex.Message}");
                }

                if (!agentOk) return false;

                // 2) Layered pid disambiguation. If we have a live pid for this
                // target, confirm the click belongs to it via WindowFromPoint.
                // This is what makes multi-app recording work: two WPF apps
                // can both have agents, but only one owns the cursor.
                if (livePid > 0)
                {
                    try
                    {
                        IntPtr hwndAtPoint = WindowFromPoint(x, y);
                        if (hwndAtPoint != IntPtr.Zero)
                        {
                            GetWindowThreadProcessId(hwndAtPoint, out uint pidAtPoint);
                            if ((int)pidAtPoint == livePid) return true;

                            IntPtr ownerHwnd = GetAncestor(hwndAtPoint, GA_ROOTOWNER);
                            if (ownerHwnd != IntPtr.Zero && ownerHwnd != hwndAtPoint)
                            {
                                GetWindowThreadProcessId(ownerHwnd, out uint pidOwner);
                                if ((int)pidOwner == livePid) return true;
                            }
                        }
                        if (IsPointInTargetProcessWindows(x, y, livePid)) return true;
                    }
                    catch (Exception ex)
                    {
                        Log($"[PointBelongsToTarget] WPFSpy pid check exception for appId={target.AppId}: {ex.GetType().Name}: {ex.Message}");
                    }
                    // Agent responded but the click isn't in this app's window.
                    return false;
                }
                // No pid known — fall back to the original "agent says it's OK"
                // behavior (single-app case where the pipe IS the source of truth).
                return true;
            }
            else
            {
                // FlaUI mode: trust UIA hit-test + pid filter inside the probe.
                if (livePid == 0) return false;

                try
                {
                    Log($"[PointBelongsToTarget] FlaUI click at ({x},{y}) appId={target.AppId} target_pid={livePid}");

                    IntPtr hwndAtPoint = WindowFromPoint(x, y);
                    if (hwndAtPoint != IntPtr.Zero)
                    {
                        GetWindowThreadProcessId(hwndAtPoint, out uint pidAtPoint);
                        string titleAtPoint = GetWindowTitle(hwndAtPoint);
                        Log($"[PointBelongsToTarget] WindowFromPoint hwnd={hwndAtPoint} pid={pidAtPoint} title='{titleAtPoint}'");
                        if ((int)pidAtPoint == livePid) return true;

                        IntPtr ownerHwnd = GetAncestor(hwndAtPoint, GA_ROOTOWNER);
                        if (ownerHwnd != IntPtr.Zero && ownerHwnd != hwndAtPoint)
                        {
                            GetWindowThreadProcessId(ownerHwnd, out uint pidOwner);
                            string titleOwner = GetWindowTitle(ownerHwnd);
                            Log($"[PointBelongsToTarget] Owner hwnd={ownerHwnd} pid={pidOwner} title='{titleOwner}'");
                            if ((int)pidOwner == livePid) return true;
                        }
                    }
                    else
                    {
                        Log($"[PointBelongsToTarget] WindowFromPoint returned NULL");
                    }

                    if (IsPointInTargetProcessWindows(x, y, livePid))
                    {
                        Log($"[PointBelongsToTarget] Strategy 2 (bounds) ACCEPTED click at ({x},{y}) for appId={target.AppId}");
                        return true;
                    }
                    Log($"[PointBelongsToTarget] Strategy 2 (bounds) rejected click at ({x},{y}) for appId={target.AppId}");

                    IntPtr hwnd = GetForegroundWindow();
                    if (hwnd != IntPtr.Zero)
                    {
                        GetWindowThreadProcessId(hwnd, out uint pid);
                        string title = GetWindowTitle(hwnd);
                        Log($"[PointBelongsToTarget] Foreground hwnd={hwnd} pid={pid} title='{title}'");
                        if ((int)pid == livePid) return true;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[PointBelongsToTarget] FlaUI exception for appId={target.AppId}: {ex.GetType().Name}: {ex.Message}");
                }
                return false;
            }
        }

        private int ResolveLivePidForTarget(RecordingTarget target)
        {
            if (target.ProcessId > 0) return target.ProcessId;
            if (string.IsNullOrEmpty(target.ExeName)) return 0;

            try
            {
                string exeName = System.IO.Path.GetFileNameWithoutExtension(target.ExeName);
                string fullExeName = System.IO.Path.GetFileName(target.ExeName);
                string[] candidates = new[]
                {
                    exeName,
                    fullExeName,
                    exeName.Replace(" ", ""),
                    exeName.Equals("calc", StringComparison.OrdinalIgnoreCase) ? "CalculatorApp" : null,
                    exeName.Equals("notepad", StringComparison.OrdinalIgnoreCase) ? "Notepad" : null,
                };
                foreach (string? candidate in candidates)
                {
                    if (string.IsNullOrEmpty(candidate)) continue;
                    Log($"[ResolveLivePidForTarget] appId={target.AppId} searching by name='{candidate}'");
                    var processes = Process.GetProcessesByName(candidate);
                    Log($"[ResolveLivePidForTarget] appId={target.AppId} found {processes.Length} process(es) for '{candidate}'");
                    if (processes.Length == 0) continue;
                    foreach (var p in processes)
                    {
                        try
                        {
                            if (p.MainWindowHandle != IntPtr.Zero)
                            {
                                string title = GetWindowTitle(p.MainWindowHandle);
                                if (!string.IsNullOrEmpty(title))
                                {
                                    Log($"[ResolveLivePidForTarget] appId={target.AppId} picked pid={p.Id} (visible, title='{title}')");
                                    target.ProcessId = p.Id;
                                    return p.Id;
                                }
                            }
                        }
                        catch { }
                    }
                    foreach (var p in processes)
                    {
                        try
                        {
                            if (p.MainWindowHandle != IntPtr.Zero)
                            {
                                Log($"[ResolveLivePidForTarget] appId={target.AppId} picked pid={p.Id} (visible handle)");
                                target.ProcessId = p.Id;
                                return p.Id;
                            }
                        }
                        catch { }
                    }
                    Log($"[ResolveLivePidForTarget] appId={target.AppId} picked pid={processes[0].Id} (no visible handle)");
                    target.ProcessId = processes[0].Id;
                    return processes[0].Id;
                }
            }
            catch (Exception ex)
            {
                Log($"[ResolveLivePidForTarget] appId={target.AppId} exception: {ex.GetType().Name}: {ex.Message}");
            }
            return 0;
        }

        private ProbedElement? CreateWindowLevelElement(RecordingTarget target)
        {
            try
            {
                int livePid = ResolveLivePidForTarget(target);
                if (livePid == 0) return null;

                ProbedElement? found = null;
                System.Text.StringBuilder titleLog = new System.Text.StringBuilder();
                EnumWindows((hWnd, lParam) =>
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if ((int)pid != livePid) return true;
                    string title = GetWindowTitle(hWnd);
                    if (string.IsNullOrEmpty(title)) return true;
                    titleLog.AppendLine($"  candidate: hwnd={hWnd} title='{title}'");
                    if (found == null || title.Length > found.Name.Length)
                    {
                        found = new ProbedElement
                        {
                            AutomationId = null,
                            Name = title,
                            ControlType = "Window",
                            Text = null,
                            ResolvedVia = "FlaUI",
                            XPath = $"/Window[@Name='{title}']",
                        };
                    }
                    return true;
                }, IntPtr.Zero);
                Log($"[CreateWindowLevelElement] appId={target.AppId} candidates:{Environment.NewLine}{titleLog}");
                return found;
            }
            catch (Exception ex)
            {
                Log($"[CreateWindowLevelElement] appId={target.AppId} exception: {ex.Message}");
                return null;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(int xPoint, int yPoint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

        private const uint GA_ROOTOWNER = 3;

        private static string GetWindowTitle(IntPtr hwnd)
        {
            try
            {
                int len = GetWindowTextLength(hwnd);
                if (len > 0)
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder(len + 1);
                    GetWindowText(hwnd, sb, sb.Capacity);
                    return sb.ToString();
                }
            }
            catch { }
            return "";
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private bool IsPointInTargetProcessWindows(int x, int y, int targetPid)
        {
            try
            {
                bool result = false;
                int windowsChecked = 0;
                int windowsMatchedPid = 0;
                System.Text.StringBuilder boundsLog = new System.Text.StringBuilder();
                EnumWindows((hWnd, lParam) =>
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    windowsChecked++;
                    if ((int)pid != targetPid) return true;
                    windowsMatchedPid++;
                    if (GetWindowRect(hWnd, out RECT rect))
                    {
                        string title = GetWindowTitle(hWnd);
                        boundsLog.AppendLine($"  hwnd={hWnd} title='{title}' bounds={rect.Left},{rect.Top},{rect.Right},{rect.Bottom}");
                        if (x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom)
                        {
                            Log($"[IsPointInTargetProcessWindows] point ({x},{y}) is in window hwnd={hWnd} title='{title}' bounds={rect.Left},{rect.Top},{rect.Right},{rect.Bottom}");
                            result = true;
                            return false;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
                Log($"[IsPointInTargetProcessWindows] checked {windowsChecked} windows, {windowsMatchedPid} owned by pid={targetPid}, point ({x},{y}) result={result}. Target windows:{Environment.NewLine}{boundsLog}");
                return result;
            }
            catch (Exception ex)
            {
                Log($"[IsPointInTargetProcessWindows] exception: {ex.Message}");
                return false;
            }
        }

        private string ResolvePage(int x, int y, RecordingTarget target)
        {
            if (target.Mode == ProbeMode.WPFSpy)
            {
                try
                {
                    if (!string.IsNullOrEmpty(target.PipeName))
                    {
                        var client = new SpyAgentClient(target.PipeName);
                        var response = client.Send("GetMainWindowTitle");
                        if (response.Success && !string.IsNullOrEmpty(response.Data))
                        {
                            string title = response.Data;
                            foreach (var (titleContains, pageAlias) in target.PageMap)
                            {
                                if (title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
                                {
                                    return pageAlias;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            else
            {
                try
                {
                    IntPtr hwnd = GetForegroundWindow();
                    if (hwnd != IntPtr.Zero)
                    {
                        int len = GetWindowTextLength(hwnd);
                        if (len > 0)
                        {
                            System.Text.StringBuilder sb = new System.Text.StringBuilder(len + 1);
                            GetWindowText(hwnd, sb, sb.Capacity);
                            string title = sb.ToString();
                            foreach (var (titleContains, pageAlias) in target.PageMap)
                            {
                                if (title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
                                {
                                    return pageAlias;
                                }
                            }
                            return title;
                        }
                    }
                }
                catch { }
            }
            return "UnknownPage";
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private static string BuildAncestorPath(ProbedElement probed)
        {
            if (string.IsNullOrEmpty(probed.XPath)) return "";
            var parts = new System.Collections.Generic.List<string>();
            var segments = probed.XPath.Split('/');
            for (int i = 1; i < segments.Length - 1; i++)
            {
                string segment = segments[i];
                if (string.IsNullOrEmpty(segment)) continue;
                string? identifier = ExtractIdentifier(segment);
                if (!string.IsNullOrEmpty(identifier)) parts.Add(identifier);
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
                    if (end > start) return predicate.Substring(start, end - start);
                }
                else if (predicate.StartsWith("@Name='"))
                {
                    int start = "@Name='".Length;
                    int end = predicate.IndexOf('\'', start);
                    if (end > start)
                    {
                        string name = predicate.Substring(start, end - start);
                        if (IsLikelyTemplatePartName(name)) return null;
                        return name;
                    }
                }
            }
            return null;
        }

        private static bool IsLikelyTemplatePartName(string name)
        {
            return name.StartsWith("PART_", StringComparison.Ordinal)
                || name == "AdornerLayer"
                || name == "border"
                || name == "Background"
                || name == "contentPresenter"
                || name == "templateRoot"
                || name == "splitBorder"
                || name == "dropDownButton"
                || name == "popup"
                || name == "scrollViewer"
                || name == "itemsPresenter"
                || name == "grid"
                || name == "stackPanel"
                || name == "dockPanel"
                || name == "wrapPanel"
                || name == "uniformGrid";
        }

        private ElementEntry BuildEntry(string alias, ProbedElement probed, RecordingTarget target) => new()
        {
            Alias = alias,
            DisplayName = probed.Name,
            ControlType = probed.ControlType,
            AutomationId = probed.AutomationId,
            Name = probed.Name,
            XPath = probed.XPath,
            RecordingModes = target.Mode == ProbeMode.FlaUI
                ? new List<string> { "FlaUI" }
                : new List<string> { "FlaUI", "WPFSpy" },
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
            ClearTargets();
        }
    }
}
