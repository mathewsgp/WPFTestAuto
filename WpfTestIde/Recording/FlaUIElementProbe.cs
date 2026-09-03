using System;
using System.Windows.Automation;

namespace WpfTestIde.Recording
{
    /// <summary>
    /// Identifies the UI Automation element at a screen point using the
    /// system-wide UIA tree (no in-process agent required). Used when the
    /// target process does NOT have the WPFSpy agent loaded (e.g. Notepad,
    /// third-party Win32 apps).
    ///
    /// Uses System.Windows.Automation (UIA3) directly — no FlaUI dependency.
    /// </summary>
    public class FlaUIElementProbe
    {
        private int _targetProcessId;
        private readonly System.Func<int>? _pidResolver;

        /// <summary>
        /// Creates a probe that filters elements by process id. When the pid
        /// is 0 (not yet known) and a resolver is supplied, the resolver is
        /// called at each ProbeAt to find the live pid dynamically.
        /// </summary>
        public FlaUIElementProbe(int targetProcessId, System.Func<int>? pidResolver = null)
        {
            _targetProcessId = targetProcessId;
            _pidResolver = pidResolver;
        }

        /// <summary>Updates the process id filter at runtime.</summary>
        public void SetTargetProcessId(int pid) => _targetProcessId = pid;

        public ProbedElement? ProbeAt(int x, int y)
        {
            try
            {
                AutomationElement? element = AutomationElement.FromPoint(new System.Windows.Point(x, y));
                if (element == null) return null;

                int livePid = _targetProcessId > 0 ? _targetProcessId : (_pidResolver?.Invoke() ?? 0);
                if (livePid > 0)
                {
                    try
                    {
                        if (element.Current.ProcessId != livePid) return null;
                    }
                    catch { }
                }
                // If pid is unknown, accept the element (the FindOwningTarget
                // check in RecordingSession is the primary guard against
                // recording against the wrong app).

                return BuildProbedElement(element);
            }
            catch (Exception ex)
            {
                RecordingSession.Log($"[FlaUIElementProbe.ProbeAt] exception: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        public string? GetCurrentValue(ProbedElement probed)
        {
            try
            {
                AutomationElement? element = ResolveElement(probed);
                if (element == null) return null;

                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? patternObj)
                    && patternObj is ValuePattern vp)
                {
                    return vp.Current.Value;
                }
                return element.Current.Name;
            }
            catch (Exception ex)
            {
                RecordingSession.Log($"[FlaUIElementProbe.GetCurrentValue] exception: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private ProbedElement BuildProbedElement(AutomationElement element)
        {
            string controlType = "Unknown";
            try
            {
                var ct = element.Current.ControlType;
                if (ct != null && !string.IsNullOrEmpty(ct.ProgrammaticName))
                {
                    string prog = ct.ProgrammaticName;
                    int dotIndex = prog.LastIndexOf('.');
                    controlType = dotIndex >= 0 ? prog.Substring(dotIndex + 1) : prog;
                }
            }
            catch { }

            string? name = null;
            try { name = element.Current.Name; } catch { }
            string? automationId = null;
            try { automationId = element.Current.AutomationId; } catch { }

            string? text = null;
            try
            {
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? vpObj)
                    && vpObj is ValuePattern vp)
                {
                    text = vp.Current.Value;
                }
                else
                {
                    text = name;
                }
            }
            catch { }

            string xpath = BuildXPath(element, controlType, automationId, name);

            return new ProbedElement
            {
                AutomationId = string.IsNullOrEmpty(automationId) ? null : automationId,
                Name = name ?? "",
                ControlType = controlType,
                Text = text,
                ResolvedVia = "FlaUI",
                XPath = xpath,
            };
        }

        private static string BuildXPath(AutomationElement element, string controlType, string? automationId, string? name)
        {
            var path = new System.Collections.Generic.List<string>();
            AutomationElement? current = element;
            int depth = 0;
            const int maxDepth = 20;

            while (current != null && depth < maxDepth)
            {
                try
                {
                    string segmentControlType = "Element";
                    try
                    {
                        var ct = current.Current.ControlType;
                        if (ct != null && !string.IsNullOrEmpty(ct.ProgrammaticName))
                        {
                            string prog = ct.ProgrammaticName;
                            int dotIndex = prog.LastIndexOf('.');
                            segmentControlType = dotIndex >= 0 ? prog.Substring(dotIndex + 1) : prog;
                        }
                    }
                    catch { }

                    string? segmentAutomationId = null;
                    try { segmentAutomationId = current.Current.AutomationId; } catch { }
                    string? segmentName = null;
                    try { segmentName = current.Current.Name; } catch { }

                    string segment;
                    if (!string.IsNullOrEmpty(segmentAutomationId))
                    {
                        segment = $"{segmentControlType}[@AutomationId='{segmentAutomationId}']";
                    }
                    else if (!string.IsNullOrEmpty(segmentName))
                    {
                        segment = $"{segmentControlType}[@Name='{segmentName}']";
                    }
                    else
                    {
                        segment = segmentControlType;
                    }
                    path.Insert(0, "/" + segment);

                    AutomationElement? parent = null;
                    try { parent = TreeWalker.RawViewWalker.GetParent(current); } catch { }
                    if (parent == null || parent == AutomationElement.RootElement) break;
                    current = parent;
                    depth++;
                }
                catch { break; }
            }

            return string.Concat(path);
        }

        private AutomationElement? ResolveElement(ProbedElement probed)
        {
            try
            {
                int livePid = _targetProcessId > 0 ? _targetProcessId : (_pidResolver?.Invoke() ?? 0);
                AutomationElement? rootWindow = null;
                if (livePid > 0)
                {
                    var condition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window);
                    var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, condition);
                    foreach (AutomationElement window in windows)
                    {
                        try
                        {
                            if (window.Current.ProcessId == _targetProcessId)
                            {
                                rootWindow = window;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                AutomationElement searchRoot = rootWindow ?? AutomationElement.RootElement;

                if (!string.IsNullOrEmpty(probed.AutomationId))
                {
                    var idCondition = new PropertyCondition(AutomationElement.AutomationIdProperty, probed.AutomationId);
                    return searchRoot.FindFirst(TreeScope.Descendants, idCondition);
                }
                else if (!string.IsNullOrEmpty(probed.Name))
                {
                    var nameCondition = new PropertyCondition(AutomationElement.NameProperty, probed.Name);
                    return searchRoot.FindFirst(TreeScope.Descendants, nameCondition);
                }
            }
            catch (Exception ex)
            {
                RecordingSession.Log($"[FlaUIElementProbe.ResolveElement] exception: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }
    }
}
