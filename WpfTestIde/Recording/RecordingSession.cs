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
        public ProbeMode Mode { get; }
        public int Priority { get; }                       // lower = checked first

        // Lazily-constructed per-target probes. We do NOT construct the
        // ElementProbe at registration time so targets added before the
        // target process exists don't immediately try to open a pipe.
        internal ElementProbe? WpfProbe { get; set; }
        internal FlaUIElementProbe? FlaProbe { get; set; }

        // Cached window identifier (AutomationId / Name / sanitized Title)
        // for this target. Resolved on the FIRST click in this target's
        // window and reused for all subsequent steps so that apps whose
        // window title changes per-keystroke (Notepad) don't produce a
        // new alias on every typed character.
        internal string? CachedWindowSegment { get; set; }

        public RecordingTarget(
            string appId,
            string pipeName,
            int processId,
            string? exeName,
            ProbeMode mode,
            int priority = 0)
        {
            AppId = appId;
            PipeName = pipeName ?? "";
            ProcessId = processId;
            ExeName = exeName;
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
    ///  - for each click, FindOwningTarget picks the target that owns
    ///    the click point: WindowFromPoint, then root-owner walk, then
    ///    a topmost-aware bounds scan across all registered targets'
    ///    windows; the picked target's probe (WPFSpy named pipe when
    ///    an in-process agent is present, or the system-wide UIA tree
    ///    otherwise) resolves the element;
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

        /// <summary>When true, OnClick calls the agent's CaptureElement
        /// command for every accepted step and writes a template PNG
        /// under repository/sikuli/. The recorded element's ImagePath is
        /// then propagated through to the emitted YAML so the Sikuli
        /// strategy uses the captured image rather than a placeholder.
        /// Default is false so existing self-tests are unaffected.</summary>
        public bool RecordSikuli { get; set; }

        /// <summary>Add a target to this session. Idempotent on AppId.</summary>
        public void AddTarget(RecordingTarget target)
        {
            if (target is null) throw new ArgumentNullException(nameof(target));
            lock (_targetsLock)
            {
                _targets[target.AppId] = target;
            }
            target.CachedWindowSegment = null;
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
                    t.CachedWindowSegment = null;
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
                    t.CachedWindowSegment = null;
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

        // Returns the target that claims ownership of (x,y), or null.
        // Strategy:
        //  1) WindowFromPoint -> if its pid is a registered target, accept.
        //  2) Owner-walk (top-level root owner of the topmost HWND) -> if
        //     its pid is a registered target, accept.
        //  3) Bounds scan across ALL registered targets' top-level windows.
        //     If multiple targets' rects contain the point, prefer the
        //     topmost (last HWND yielded by EnumWindows for that target).
        //     This handles overlapping windows and UWP / non-client clicks
        //     where WindowFromPoint doesn't attribute to the right HWND.
        private RecordingTarget? FindOwningTarget(int x, int y)
        {
            List<RecordingTarget> snapshot;
            lock (_targetsLock) { snapshot = _targets.Values.OrderBy(t => t.Priority).ToList(); }
            if (snapshot.Count == 0) return null;

            // Build pid -> target map (resolving live pids on demand).
            Dictionary<int, RecordingTarget> pidMap = new();
            foreach (var t in snapshot)
            {
                int livePid = t.ProcessId > 0 ? t.ProcessId : ResolveLivePidForTarget(t);
                if (livePid > 0) pidMap[livePid] = t;
            }
            if (pidMap.Count == 0) return null;

            // 1) Topmost HWND.
            IntPtr topHwnd = IntPtr.Zero;
            int topPid = 0;
            try
            {
                topHwnd = WindowFromPoint(x, y);
                if (topHwnd != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(topHwnd, out uint pid);
                    topPid = (int)pid;
                    if (pidMap.TryGetValue(topPid, out var t)) return t;
                }
            }
            catch (Exception ex)
            {
                Log($"[FindOwningTarget] WindowFromPoint exception: {ex.GetType().Name}: {ex.Message}");
            }

            // 2) Root-owner walk of the topmost HWND.
            try
            {
                if (topHwnd != IntPtr.Zero)
                {
                    IntPtr ownerHwnd = GetAncestor(topHwnd, GA_ROOTOWNER);
                    if (ownerHwnd != IntPtr.Zero && ownerHwnd != topHwnd)
                    {
                        GetWindowThreadProcessId(ownerHwnd, out uint pidOwner);
                        if (pidMap.TryGetValue((int)pidOwner, out var t)) return t;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[FindOwningTarget] owner-walk exception: {ex.GetType().Name}: {ex.Message}");
            }

            // 3) Bounds scan across all registered targets' windows. For each
            //    target, remember the LAST window in EnumWindows order whose
            //    rect contains (x,y); that's the topmost candidate for that
            //    target. Then return the topmost candidate across targets by
            //    re-scanning all windows in Z-order and returning the first
            //    (topmost) one whose target also had a rect match.
            Dictionary<int, IntPtr> topMatchPerTarget = new();
            HashSet<int> targetPids = new(pidMap.Keys);
            try
            {
                EnumWindows((hWnd, lParam) =>
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (!targetPids.Contains((int)pid)) return true;
                    if (GetWindowRect(hWnd, out RECT rect))
                    {
                        if (x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom)
                        {
                            // Keep the most recently seen (topmost) HWND per target.
                            topMatchPerTarget[(int)pid] = hWnd;
                        }
                    }
                    return true;
                }, IntPtr.Zero);

                // Now find the overall topmost (first in EnumWindows order) HWND
                // among the matching set.
                IntPtr topmost = IntPtr.Zero;
                int topmostPid = 0;
                EnumWindows((hWnd, lParam) =>
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (!targetPids.Contains((int)pid)) return true;
                    if (topMatchPerTarget.TryGetValue((int)pid, out IntPtr recorded) && recorded == hWnd)
                    {
                        topmost = hWnd;
                        topmostPid = (int)pid;
                        return false; // stop enumeration
                    }
                    return true;
                }, IntPtr.Zero);

                if (topmostPid > 0 && pidMap.TryGetValue(topmostPid, out var matchedTarget))
                {
                    Log($"[FindOwningTarget] bounds-scan matched pid={topmostPid} appId={matchedTarget.AppId}");
                    return matchedTarget;
                }
            }
            catch (Exception ex)
            {
                Log($"[FindOwningTarget] bounds-scan exception: {ex.GetType().Name}: {ex.Message}");
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

            string processName = ResolveProcessName(target);
            string windowSegment;
            string ancestorPath;
            string idOrName = string.IsNullOrEmpty(probed.AutomationId) ? probed.Name : probed.AutomationId!;

            if (!string.IsNullOrEmpty(probed.XPath))
            {
                var (xpathWindow, xpathAncestor) = SplitXPathForAlias(probed.XPath);
                if (!string.IsNullOrEmpty(xpathWindow))
                {
                    windowSegment = xpathWindow;
                    ancestorPath = xpathAncestor;
                }
                else
                {
                    windowSegment = ResolveWindowSegment(x, y, target);
                    ancestorPath = BuildAncestorPath(probed);
                }
            }
            else
            {
                windowSegment = ResolveWindowSegment(x, y, target);
                ancestorPath = BuildAncestorPath(probed);
            }

            // Alias format: <ProcessName>.<WindowSegment>[.<AncestorPath>].<Element>
            var parts = new List<string> { processName, windowSegment };
            if (!string.IsNullOrEmpty(ancestorPath)) parts.Add(ancestorPath);
            parts.Add(idOrName);
            string alias = string.Join(".", parts);

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

            // Phase 2.3: when the operator has Sikuli recording turned on
            // and the agent can see the element, capture a template PNG of
            // its on-screen rect and write it to repository/sikuli/. The
            // resulting imagePath is propagated through BuildEntry -> step
            // -> RepositoryWriter so the emitted YAML uses a real image
            // instead of the placeholder name.
            if (RecordSikuli)
            {
                TryCaptureElementImage(entry, target);
            }

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

        /// <summary>
        /// Returns the first segment of the alias: a stable process-derived
        /// name taken from the target's exe path. e.g. "SampleWpfApp.exe" ->
        /// "SampleWpfApp", "notepad.exe" -> "notepad".
        /// </summary>
        internal static string ResolveProcessName(RecordingTarget target)
        {
            if (!string.IsNullOrEmpty(target.ExeName))
            {
                string stem = System.IO.Path.GetFileNameWithoutExtension(target.ExeName);
                if (!string.IsNullOrEmpty(stem)) return stem;
            }
            if (!string.IsNullOrEmpty(target.AppId)) return target.AppId;
            return "App";
        }

        /// <summary>
        /// Returns the second segment of the alias: a window-level identifier.
        /// Precedence: WindowAutomationId -> WindowName (UIA Name) ->
        /// sanitized WindowTitle. Notepad-style dynamic prefixes are stripped
        /// from the title. The result is cached per target for the lifetime
        /// of the recording session.
        /// </summary>
        private string ResolveWindowSegment(int x, int y, RecordingTarget target)
        {
            if (!string.IsNullOrEmpty(target.CachedWindowSegment)) return target.CachedWindowSegment!;

            string rawTitle = "";
            string? windowAutomationId = null;
            string? windowName = null;

            if (target.Mode == ProbeMode.WPFSpy)
            {
                try
                {
                    if (!string.IsNullOrEmpty(target.PipeName))
                    {
                        var client = new SpyAgentClient(target.PipeName);
                        var response = client.Send("GetMainWindow");
                        if (response.Success && !string.IsNullOrEmpty(response.Data))
                        {
                            try
                            {
                                using var doc = System.Text.Json.JsonDocument.Parse(response.Data);
                                var root = doc.RootElement;
                                if (root.TryGetProperty("automationId", out var aidEl)
                                    && aidEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    windowAutomationId = aidEl.GetString();
                                }
                                if (root.TryGetProperty("name", out var nameEl)
                                    && nameEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    windowName = nameEl.GetString();
                                }
                                if (root.TryGetProperty("title", out var titleEl)
                                    && titleEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    rawTitle = titleEl.GetString() ?? "";
                                }
                            }
                            catch (Exception parseEx)
                            {
                                Log($"[ResolveWindowSegment] GetMainWindow JSON parse failed: {parseEx.GetType().Name}: {parseEx.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"[ResolveWindowSegment] WPFSpy agent exception: {ex.GetType().Name}: {ex.Message}");
                }
            }
            else
            {
                int livePid = ResolveLivePidForTarget(target);
                if (livePid > 0)
                {
                    // Pick the window containing the click point, else the
                    // largest visible window of the target process.
                    var picked = PickBestWindowForTarget(livePid, x, y);
                    if (picked.HasValue)
                    {
                        windowAutomationId = TryGetWindowAutomationId(picked.Value.hwnd);
                        windowName = TryGetWindowName(picked.Value.hwnd);
                        rawTitle = picked.Value.title;
                    }
                }
            }

            // Precedence: AutomationId -> Name -> sanitized Title.
            string resolved = "";
            if (IsMeaningfulWindowIdentifier(windowAutomationId)) resolved = windowAutomationId!;
            else if (IsMeaningfulWindowIdentifier(windowName)) resolved = windowName!;
            else resolved = StripDynamicDocumentPrefix(rawTitle);

            if (string.IsNullOrEmpty(resolved)) resolved = "Window";
            target.CachedWindowSegment = resolved;
            return resolved;
        }

        /// <summary>
        /// A non-empty window identifier is considered meaningful - "MainWindow"
        /// (the WPF default x:Name) is just as valid as any other explicit
        /// name. Empty / null are not.
        /// </summary>
        private static bool IsMeaningfulWindowIdentifier(string? value)
        {
            return !string.IsNullOrEmpty(value);
        }

        /// <summary>
        /// Among all top-level visible windows owned by <paramref name="targetPid"/>,
        /// pick the one we should treat as the click's target window. Prefer
        /// the window containing the click point; otherwise the largest
        /// visible window of the target process.
        /// </summary>
        private (IntPtr hwnd, string title)? PickBestWindowForTarget(int targetPid, int x, int y)
        {
            try
            {
                IntPtr? containing = null;
                string? containingTitle = null;
                IntPtr? largest = null;
                string? largestTitle = null;
                int largestArea = 0;
                EnumWindows((hWnd, lParam) =>
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if ((int)pid != targetPid) return true;
                    string title = GetWindowTitle(hWnd);
                    if (string.IsNullOrEmpty(title)) return true;

                    if (GetWindowRect(hWnd, out RECT rect))
                    {
                        int area = (rect.Right - rect.Left) * (rect.Bottom - rect.Top);
                        if (area > largestArea)
                        {
                            largestArea = area;
                            largest = hWnd;
                            largestTitle = title;
                        }
                        if (x >= rect.Left && x <= rect.Right && y >= rect.Top && y <= rect.Bottom)
                        {
                            containing = hWnd;
                            containingTitle = title;
                        }
                    }
                    return true;
                }, IntPtr.Zero);
                if (containing.HasValue) return (containing.Value, containingTitle ?? "");
                if (largest.HasValue) return (largest.Value, largestTitle ?? "");
                return null;
            }
            catch (Exception ex)
            {
                Log($"[PickBestWindowForTarget] exception: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads a window's AutomationId via UIA. Returns null if the
        /// HWND has no UIA representation, no AutomationId, or if the
        /// process is elevated differently.
        /// </summary>
        private string? TryGetWindowAutomationId(IntPtr hwnd)
        {
            try
            {
                var el = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                if (el == null) return null;
                string? id = el.Current.AutomationId;
                if (string.IsNullOrEmpty(id)) return null;
                return id;
            }
            catch (Exception ex)
            {
                Log($"[TryGetWindowAutomationId] exception: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads a window's UIA Name property. This is the same value that
        /// appears in the window's title bar for most apps.
        /// </summary>
        private string? TryGetWindowName(IntPtr hwnd)
        {
            try
            {
                var el = System.Windows.Automation.AutomationElement.FromHandle(hwnd);
                if (el == null) return null;
                string? name = el.Current.Name;
                if (string.IsNullOrEmpty(name)) return null;
                return name;
            }
            catch (Exception ex)
            {
                Log($"[TryGetWindowName] exception: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Strips the dynamic document-name prefix from app window titles.
        /// "Untitled - Notepad"  -> "Notepad"
        /// "*1 - Notepad"        -> "Notepad"
        /// "*12345 - Notepad"    -> "Notepad"
        /// "Sample WPF App Login" -> "Sample WPF App Login" (unchanged)
        /// </summary>
        internal static string StripDynamicDocumentPrefix(string title)
        {
            if (string.IsNullOrEmpty(title)) return "UnknownPage";
            int idx = title.LastIndexOf(" - ", StringComparison.Ordinal);
            if (idx > 0 && idx < title.Length - 3)
            {
                string suffix = title.Substring(idx + 3).Trim();
                if (!string.IsNullOrEmpty(suffix)) return suffix;
            }
            return title.Trim();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private static (string WindowName, string AncestorPath) SplitXPathForAlias(string xpath)
        {
            if (string.IsNullOrEmpty(xpath)) return (string.Empty, string.Empty);

            var parts = xpath.Split('/');
            int windowIdx = -1;
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (parts[i].StartsWith("Window[", StringComparison.Ordinal))
                {
                    windowIdx = i;
                    break;
                }
            }
            if (windowIdx < 0) return (string.Empty, xpath);

            string windowFromSegment = ExtractAliasName(parts[windowIdx]);
            if (!string.IsNullOrEmpty(windowFromSegment))
            {
                var ancestorOnly = new System.Text.StringBuilder();
                for (int k = windowIdx + 1; k < parts.Length; k++)
                {
                    if (!IsMeaningfulAliasName(parts[k])) continue;
                    if (ancestorOnly.Length > 0) ancestorOnly.Append('.');
                    ancestorOnly.Append(ExtractAliasName(parts[k]));
                }
                return (windowFromSegment, ancestorOnly.ToString());
            }

            int nameStart = -1;
            for (int j = windowIdx + 1; j < parts.Length; j++)
            {
                if (IsMeaningfulAliasName(parts[j]))
                {
                    nameStart = j;
                    break;
                }
            }
            if (nameStart < 0) return (string.Empty, string.Empty);

            string windowName = ExtractAliasName(parts[nameStart]);
            var ancestor = new System.Text.StringBuilder();
            for (int k = nameStart + 1; k < parts.Length; k++)
            {
                if (!IsMeaningfulAliasName(parts[k])) continue;
                if (ancestor.Length > 0) ancestor.Append('.');
                ancestor.Append(ExtractAliasName(parts[k]));
            }
            return (windowName, ancestor.ToString());
        }

        private static bool IsMeaningfulAliasName(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return false;
            if (!segment.StartsWith("[", StringComparison.Ordinal)) return false;
            if (segment.StartsWith("[@", StringComparison.Ordinal)) return false;
            var name = ExtractAliasName(segment);
            return !string.IsNullOrEmpty(name) && name != "MainWindow";
        }

        /// <summary>
        /// Returns the @Name='...' value if present, otherwise the
        /// @AutomationId='...' value. Both are meaningful identifiers for
        /// the alias path; the agent prefers AutomationId when naming the
        /// Window segment, so this must accept both.
        /// </summary>
        private static string ExtractAliasName(string segment)
        {
            int at = segment.IndexOf("@Name='", StringComparison.Ordinal);
            if (at >= 0)
            {
                int start = at + "@Name='".Length;
                int end = segment.IndexOf("'", start, StringComparison.Ordinal);
                if (end > start) return segment.Substring(start, end - start);
            }
            at = segment.IndexOf("@AutomationId='", StringComparison.Ordinal);
            if (at >= 0)
            {
                int start = at + "@AutomationId='".Length;
                int end = segment.IndexOf("'", start, StringComparison.Ordinal);
                if (end > start) return segment.Substring(start, end - start);
            }
            return string.Empty;
        }

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

        private ElementEntry BuildEntry(string alias, ProbedElement probed, RecordingTarget target)
        {
            var modes = target.Mode == ProbeMode.FlaUI
                ? new List<string> { "FlaUI" }
                : new List<string> { "FlaUI", "WPFSpy" };

            if (RecordSikuli)
            {
                modes.Add("Sikuli");
            }

            return new ElementEntry
            {
                Alias = alias,
                DisplayName = probed.Name,
                ControlType = probed.ControlType,
                AutomationId = probed.AutomationId,
                Name = probed.Name,
                XPath = probed.XPath,
                RecordingModes = modes,
            };
        }

        /// <summary>
        /// Phase 2.3: ask the WpfSpyAgent to capture a template PNG of the
        /// element's on-screen rect and save it to
        /// repository/sikuli/&lt;safe-alias&gt;.png. The agent's
        /// CaptureElement command returns the PNG as base64 plus the rect;
        /// we set entry.ImagePath on success so RepositoryWriter emits
        /// a Sikuli strategy that points at the captured template.
        /// </summary>
        private void TryCaptureElementImage(ElementEntry entry, RecordingTarget target)
        {
            try
            {
                if (string.IsNullOrEmpty(target.PipeName))
                {
                    Log("[CaptureElementImage] skipped: target has no pipe name");
                    return;
                }

                var client = new SpyAgentClient(target.PipeName);
                // Prefer XPath (resolves the right element on page
                // navigation), fall back to AutomationId / Name.
                string? xpath = entry.XPath;
                string? name = !string.IsNullOrEmpty(entry.AutomationId) ? null : entry.Name;
                if (string.IsNullOrEmpty(xpath) && string.IsNullOrEmpty(name))
                {
                    Log($"[CaptureElementImage] skipped: no locator for {entry.Alias}");
                    return;
                }

                var response = client.Send(
                    "CaptureElement",
                    name: name,
                    xpath: xpath,
                    width: 4 /* padding px */);

                if (!response.Success || string.IsNullOrEmpty(response.Data))
                {
                    Log($"[CaptureElementImage] agent failed for {entry.Alias}: {response.Error ?? "(no data)"}");
                    return;
                }

                // The response is the JSON payload {pngBase64, x, y, ...}
                // — extract pngBase64 and decode it.
                using var doc = System.Text.Json.JsonDocument.Parse(response.Data!);
                if (!doc.RootElement.TryGetProperty("pngBase64", out var base64El))
                {
                    Log($"[CaptureElementImage] response missing pngBase64 for {entry.Alias}");
                    return;
                }
                byte[] png = Convert.FromBase64String(base64El.GetString() ?? "");

                // Resolve the repository root from the IDE's working
                // directory (we don't have a direct FrameworkRoot here,
                // so we walk up from the bin directory).
                string? repoRoot = ResolveRepositoryRoot();
                if (repoRoot is null)
                {
                    Log("[CaptureElementImage] skipped: could not resolve repository root");
                    return;
                }
                string sikuliDir = System.IO.Path.Combine(repoRoot, "repository", "sikuli");
                System.IO.Directory.CreateDirectory(sikuliDir);
                string fileName = SafeFileName(entry.Alias) + ".png";
                string fullPath = System.IO.Path.Combine(sikuliDir, fileName);
                System.IO.File.WriteAllBytes(fullPath, png);

                entry.ImagePath = $"sikuli/{fileName}";
                Log($"[CaptureElementImage] saved {fullPath} ({png.Length} bytes) for {entry.Alias}");
            }
            catch (Exception ex)
            {
                Log($"[CaptureElementImage] exception for {entry.Alias}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string SafeFileName(string alias)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var chars = alias.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }

        private static string? ResolveRepositoryRoot()
        {
            try
            {
                string dir = System.AppContext.BaseDirectory;
                for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(dir, "setup_env.bat"))
                        || System.IO.Directory.Exists(System.IO.Path.Combine(dir, "repository")))
                    {
                        return dir;
                    }
                    dir = System.IO.Path.GetDirectoryName(dir) ?? "";
                }
            }
            catch { }
            return null;
        }

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
