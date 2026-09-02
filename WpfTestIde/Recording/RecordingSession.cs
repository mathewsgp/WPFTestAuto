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
    ///    resolves the element via either the WPFSpy named pipe (preferred,
    ///    when the target has the in-process agent) or the system-wide
    ///    UIA tree (fallback, for plain Win32 apps like Notepad that have
    ///    no in-process instrumentation);
    ///  - text-entry controls commit their final value as a SetValue step
    ///    on the next click, so typed text is captured on "blur" rather
    ///    than per keystroke;
    ///  - a small, user-configured (window title substring -> page alias)
    ///    map assigns each element to a `Page.Element` alias, matching the
    ///    convention used throughout the Python framework's repositories.
    /// </summary>
    public class RecordingSession : IDisposable
    {
        /// <summary>Which element-resolution strategy this session uses.</summary>
        public enum ProbeMode { WPFSpy, FlaUI }

        private readonly ElementProbe? _wpfProbe;          // present when ProbeMode=WPFSpy
        private readonly FlaUIElementProbe? _flaProbe;     // present when ProbeMode=FlaUI
        private readonly GlobalMouseHook _mouseHook = new();
        private readonly List<(string TitleContains, string PageAlias)> _pageMap;
        private readonly int _targetProcessId;
        private readonly string _pipeName;
        private readonly string? _appId;
        private readonly ProbeMode _mode;
        private readonly string? _appExeName;              // for dynamic pid lookup (FlaUI mode)

        private ProbedElement? _pendingFocusedInput;
        private string? _pendingFocusedAlias;
        private bool _running;

        public event Action<RecordedStep, ElementEntry>? StepCaptured;

        public ProbeMode Mode => _mode;

        /// <summary>
        /// Creates a recording session. Pass either a non-empty pipeName
        /// (WPFSpy mode) OR a positive processId (FlaUI mode). For FlaUI
        /// mode, pass an empty pipeName. If processId is 0, exeName can be
        /// supplied so the session dynamically resolves the pid at click time.
        /// </summary>
        public RecordingSession(string pipeName, int targetProcessId, List<(string, string)> pageMap, string? appId = null, ProbeMode mode = ProbeMode.WPFSpy, string? exeName = null)
        {
            _pipeName = pipeName;
            _targetProcessId = targetProcessId;
            _pageMap = pageMap;
            _appId = appId;
            _mode = mode;
            _appExeName = exeName;

            if (mode == ProbeMode.WPFSpy)
            {
                _wpfProbe = new ElementProbe(pipeName);
            }
            else
            {
                // If pid is 0 (not yet launched), pass 0 to the probe — the
                // probe will skip the per-process filter and just hit-test
                // whatever is under the cursor. The PointBelongsToTargetProcess
                // guard still ensures we only record clicks in the target app
                // once the process is known.
                _flaProbe = new FlaUIElementProbe(targetProcessId, ResolveTargetPid);
            }
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _mouseHook.LeftButtonDown += OnClick;
            _mouseHook.Start();
            Log($"[RecordingSession] Start mode={_mode} pid={_targetProcessId} pipe={_pipeName} exe={_appExeName}");

            // Diagnostic: log the resolved process info at start time so the
            // user can see what process this session is targeting.
            if (_mode == ProbeMode.FlaUI)
            {
                int livePid = ResolveTargetPid();
                if (livePid > 0)
                {
                    try
                    {
                        var p = System.Diagnostics.Process.GetProcessById(livePid);
                        string mainTitle = "";
                        System.Drawing.Rectangle bounds = System.Drawing.Rectangle.Empty;
                        try
                        {
                            if (p.MainWindowHandle != IntPtr.Zero)
                            {
                                mainTitle = GetWindowTitle(p.MainWindowHandle);
                                if (GetWindowRect(p.MainWindowHandle, out RECT rect))
                                {
                                    bounds = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                                }
                            }
                        }
                        catch { }
                        Log($"[RecordingSession] Target process: pid={livePid} name='{p.ProcessName}' mainWindowTitle='{mainTitle}' bounds={bounds}");
                    }
                    catch (Exception ex)
                    {
                        Log($"[RecordingSession] Could not get process info: {ex.Message}");
                    }
                }
                else
                {
                    Log($"[RecordingSession] WARNING: Could not resolve target process at Start time (pid=0)");
                }
            }
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _mouseHook.LeftButtonDown -= OnClick;
            _mouseHook.Stop();
            CommitPendingValueIfAny();
            Log($"[RecordingSession] Stop");
        }

        private void OnClick(int x, int y)
        {
            CommitPendingValueIfAny();

            if (!PointBelongsToTargetProcess(x, y))
            {
                Log($"[OnClick] rejected: point ({x},{y}) not in target process");
                return;
            }

            ProbedElement? probed = _mode == ProbeMode.WPFSpy
                ? _wpfProbe?.ProbeAt(x, y)
                : _flaProbe?.ProbeAt(x, y);

            if (probed is null)
            {
                // UIA hit-test returned null. This is common for UWP apps that use
                // DirectComposition for rendering — their visual content isn't exposed
                // through the UIA tree at every pixel. If we already confirmed the click
                // is within the target app's window bounds, fall back to the window-level
                // element so the click is still recorded.
                Log($"[OnClick] ProbeAt({x},{y}) returned null, trying window-level fallback");
                probed = _mode == ProbeMode.FlaUI ? CreateWindowLevelElement() : null;
                if (probed is null)
                {
                    Log($"[OnClick] rejected: ProbeAt({x},{y}) returned null and no window fallback");
                    return;
                }
                Log($"[OnClick] using window-level fallback: {probed.ControlType} name={probed.Name}");
            }

            Log($"[OnClick] accepted: {probed.ControlType} name={probed.Name} automationId={probed.AutomationId}");

            string page = ResolvePage(x, y);
            string ancestorPath = BuildAncestorPath(probed);
            string idOrName = string.IsNullOrEmpty(probed.AutomationId) ? probed.Name : probed.AutomationId!;
            string alias = string.IsNullOrEmpty(ancestorPath)
                ? $"{page}.{idOrName}"
                : $"{page}.{ancestorPath}.{idOrName}";

            bool isTextEntry = probed.ControlType is "TextBox" or "Edit" or "ComboBox" or "PasswordBox" or "Document";
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
                AppId = _appId,
            };
            Log($"[OnClick] firing StepCaptured: alias={alias}, action={step.Action}, appId={_appId ?? "(none)"}");
            StepCaptured?.Invoke(step, entry);
        }

        private void CommitPendingValueIfAny()
        {
            if (_pendingFocusedInput is null || _pendingFocusedAlias is null) return;

            string? value = _mode == ProbeMode.WPFSpy
                ? _wpfProbe?.GetCurrentValue(_pendingFocusedInput)
                : _flaProbe?.GetCurrentValue(_pendingFocusedInput);

            var entry = BuildEntry(_pendingFocusedAlias, _pendingFocusedInput);
            var step = new RecordedStep
            {
                Kind = StepKind.Action,
                Alias = _pendingFocusedAlias,
                Action = ActionKind.SetValue,
                Value = value ?? "",
                NonStandard = _pendingFocusedInput.ResolvedVia == "WPFSpy",
                AppId = _appId,
            };
            StepCaptured?.Invoke(step, entry);
            _pendingFocusedInput = null;
            _pendingFocusedAlias = null;
        }

        /// <summary>
        /// Resolves the live process id for the target app. When the session
        /// was created with a known pid, returns that. When the pid was 0
        /// (not yet launched) and we know the exe name, falls back to
        /// process enumeration. Returns 0 if not found.
        /// </summary>
        private int ResolveTargetPid()
        {
            if (_targetProcessId > 0) return _targetProcessId;

            if (!string.IsNullOrEmpty(_appExeName))
            {
                try
                {
                    string exeName = System.IO.Path.GetFileNameWithoutExtension(_appExeName);
                    string fullExeName = System.IO.Path.GetFileName(_appExeName);

                    // Try several name variants since Windows process names can differ from exe names:
                    //   - "calc"        -> "calc" (Win32 stub)
                    //   - "calc.exe"    -> "calc" (no extension)
                    //   - "CalculatorApp" -> UWP app, different process name
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
                        Log($"[ResolveTargetPid] searching by name='{candidate}'");
                        var processes = Process.GetProcessesByName(candidate);
                        Log($"[ResolveTargetPid] found {processes.Length} process(es) for '{candidate}'");
                        if (processes.Length == 0) continue;

                        // Prefer a process whose main window is visible AND has a non-empty title.
                        foreach (var p in processes)
                        {
                            try
                            {
                                if (p.MainWindowHandle != IntPtr.Zero)
                                {
                                    string title = GetWindowTitle(p.MainWindowHandle);
                                    if (!string.IsNullOrEmpty(title))
                                    {
                                        Log($"[ResolveTargetPid] picked pid={p.Id} (visible, title='{title}')");
                                        return p.Id;
                                    }
                                }
                            }
                            catch { }
                        }

                        // Fallback: any process with a visible main window handle.
                        foreach (var p in processes)
                        {
                            try
                            {
                                if (p.MainWindowHandle != IntPtr.Zero)
                                {
                                    Log($"[ResolveTargetPid] picked pid={p.Id} (visible handle)");
                                    return p.Id;
                                }
                            }
                            catch { }
                        }

                        // Last resort: scan ALL running processes for one that owns a window whose
                        // title contains a known app title. This handles UWP/Desktop Bridge apps
                        // where the actual UI runs in a different process (RuntimeBroker, etc.).
                        // We try several title patterns derived from the exe name:
                        //   - "calc"     -> ["Calculator", "calc"]
                        //   - "notepad"  -> ["Notepad", "notepad"]
                        //   - "wordpad"  -> ["WordPad", "wordpad"]
                        List<string> titlePatterns = new List<string>();
                        if (!string.IsNullOrEmpty(exeName))
                        {
                            // Add the exe name as a lowercase pattern (most apps show the exe name in the title)
                            titlePatterns.Add(exeName);
                            // Add a capitalized version ("calc" -> "Calc")
                            if (exeName.Length > 0)
                                titlePatterns.Add(char.ToUpper(exeName[0]) + exeName.Substring(1));
                            // Special-case mappings for well-known UWP apps
                            if (exeName.Equals("calc", StringComparison.OrdinalIgnoreCase))
                                titlePatterns.Add("Calculator");
                            if (exeName.Equals("notepad", StringComparison.OrdinalIgnoreCase))
                                titlePatterns.Add("Notepad");
                        }

                        Log($"[ResolveTargetPid] trying title scan with patterns: [{string.Join(", ", titlePatterns)}]");
                        var allProcs = Process.GetProcesses();
                        foreach (var ap in allProcs)
                        {
                            try
                            {
                                if (ap.MainWindowHandle != IntPtr.Zero)
                                {
                                    string title = GetWindowTitle(ap.MainWindowHandle);
                                    foreach (string pattern in titlePatterns)
                                    {
                                        if (!string.IsNullOrEmpty(title) && title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                                        {
                                            Log($"[ResolveTargetPid] picked pid={ap.Id} via title scan (title='{title}', matched pattern='{pattern}')");
                                            return ap.Id;
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        Log($"[ResolveTargetPid] title scan found no match");

                        // Last resort: first process.
                        Log($"[ResolveTargetPid] picked pid={processes[0].Id} (no visible handle)");
                        return processes[0].Id;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[ResolveTargetPid] exception: {ex.GetType().Name}: {ex.Message}");
                }
            }
            else
            {
                Log($"[ResolveTargetPid] _appExeName is empty");
            }
            return 0;
        }

        /// <summary>
        /// Creates a window-level ProbedElement for the target app's main window.
        /// Used as a fallback when UIA's FromPoint returns null (e.g. UWP apps
        /// that render via DirectComposition and don't expose every pixel through UIA).
        /// </summary>
        private ProbedElement? CreateWindowLevelElement()
        {
            try
            {
                int livePid = ResolveTargetPid();
                if (livePid == 0) return null;

                ProbedElement? found = null;
                System.Text.StringBuilder titleLog = new System.Text.StringBuilder();
                EnumWindows((hWnd, lParam) =>
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    uint pid;
                    GetWindowThreadProcessId(hWnd, out pid);
                    if ((int)pid != livePid) return true;
                    string title = GetWindowTitle(hWnd);
                    if (string.IsNullOrEmpty(title)) return true;
                    titleLog.AppendLine($"  candidate: hwnd={hWnd} title='{title}'");
                    if (found == null || title.Length > found.Name.Length)
                    {
                        // Use the window itself as the "element" — we'll record a click
                        // on the window with its title.
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
                Log($"[CreateWindowLevelElement] candidates:{Environment.NewLine}{titleLog}");
                return found;
            }
            catch (Exception ex)
            {
                Log($"[CreateWindowLevelElement] exception: {ex.Message}");
                return null;
            }
        }

        private bool PointBelongsToTargetProcess(int x, int y)
        {
            if (_mode == ProbeMode.WPFSpy)
            {
                try
                {
                    var client = new SpyAgentClient(_pipeName);
                    var response = client.Send("GetMainWindowTitle");
                    if (response.Success && !string.IsNullOrEmpty(response.Data)) return true;
                }
                catch (Exception ex)
                {
                    Log($"[PointBelongsToTargetProcess] WPFSpy exception: {ex.GetType().Name}: {ex.Message}");
                }
                return false;
            }
            else
            {
                // FlaUI mode: trust the UIA hit-test + process-id filter inside the probe.
                // Resolve the live pid (handles the case where the session was created
                // before the app was launched — pid was 0 at that time).
                int livePid = ResolveTargetPid();
                if (livePid == 0)
                {
                    return false;
                }

                try
                {
                    Log($"[PointBelongsToTargetProcess] click at ({x},{y}) target_pid={livePid}");

                    // Strategy 1: WindowFromPoint — the most direct check.
                    IntPtr hwndAtPoint = WindowFromPoint(x, y);
                    if (hwndAtPoint != IntPtr.Zero)
                    {
                        uint pidAtPoint;
                        GetWindowThreadProcessId(hwndAtPoint, out pidAtPoint);
                        string titleAtPoint = GetWindowTitle(hwndAtPoint);
                        Log($"[PointBelongsToTargetProcess] WindowFromPoint hwnd={hwndAtPoint} pid={pidAtPoint} title='{titleAtPoint}'");
                        if ((int)pidAtPoint == livePid) return true;

                        // Walk up to the top-level window — the click may be on a child control.
                        IntPtr ownerHwnd = GetAncestor(hwndAtPoint, GA_ROOTOWNER);
                        if (ownerHwnd != IntPtr.Zero && ownerHwnd != hwndAtPoint)
                        {
                            uint pidOwner;
                            GetWindowThreadProcessId(ownerHwnd, out pidOwner);
                            string titleOwner = GetWindowTitle(ownerHwnd);
                            Log($"[PointBelongsToTargetProcess] Owner hwnd={ownerHwnd} pid={pidOwner} title='{titleOwner}'");
                            if ((int)pidOwner == livePid) return true;
                        }
                    }
                    else
                    {
                        Log($"[PointBelongsToTargetProcess] WindowFromPoint returned NULL");
                    }

                    // Strategy 2: Check if the click point is within the bounding rect of any
                    // top-level window owned by the target process. This catches clicks on
                    // title bars, non-client areas, and child windows that WindowFromPoint
                    // might not associate with the right HWND (e.g. UWP/Desktop Bridge apps).
                    if (IsPointInTargetProcessWindows(x, y, livePid))
                    {
                        Log($"[PointBelongsToTargetProcess] Strategy 2 (bounds) ACCEPTED click at ({x},{y})");
                        return true;
                    }
                    Log($"[PointBelongsToTargetProcess] Strategy 2 (bounds) rejected click at ({x},{y})");

                    // Strategy 3: Foreground window fallback.
                    IntPtr hwnd = GetForegroundWindow();
                    if (hwnd != IntPtr.Zero)
                    {
                        uint pid;
                        GetWindowThreadProcessId(hwnd, out pid);
                        string title = GetWindowTitle(hwnd);
                        Log($"[PointBelongsToTargetProcess] Foreground hwnd={hwnd} pid={pid} title='{title}'");
                        if ((int)pid == livePid) return true;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[PointBelongsToTargetProcess] FlaUI exception: {ex.GetType().Name}: {ex.Message}");
                }
                return false;
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
        /// Enumerates all top-level windows and returns true if (x,y) lies
        /// within the bounding rect of any visible window owned by targetPid.
        /// </summary>
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
                    uint pid;
                    GetWindowThreadProcessId(hWnd, out pid);
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
                            return false; // stop enumeration
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

        private string ResolvePage(int x, int y)
        {
            if (_mode == ProbeMode.WPFSpy)
            {
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
            }
            else
            {
                // FlaUI mode: read the foreground window's title.
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
                            foreach (var (titleContains, pageAlias) in _pageMap)
                            {
                                if (title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
                                {
                                    return pageAlias;
                                }
                            }
                            return title; // fallback to literal title
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

        private ElementEntry BuildEntry(string alias, ProbedElement probed) => new()
        {
            Alias = alias,
            DisplayName = probed.Name,
            ControlType = probed.ControlType,
            AutomationId = probed.AutomationId,
            Name = probed.Name,
            XPath = probed.XPath,
            // For FlaUI-recorded elements, only include FlaUI mode in the repository
            // strategies — WPFSpy strategies would fail at replay time because the
            // target app has no spy agent. For WPFSpy-recorded elements, include both
            // so the element is self-healing across both drivers.
            RecordingModes = _mode == ProbeMode.FlaUI
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
            _wpfProbe?.Dispose();
        }
    }
}
