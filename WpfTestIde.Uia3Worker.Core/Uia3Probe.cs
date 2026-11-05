using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using WpfTestIde.Uia3Worker.Core.Protocols;

namespace WpfTestIde.Uia3Worker.Core;

/// <summary>
/// UIA3 element probe: resolves AutomationElements by AutomationId/Name/ControlType/XPath,
/// invokes UIA patterns, caches elements with staleness handling, captures screenshots
/// via PrintWindow/GDI, and extracts DataGrid content via TablePattern.
/// </summary>
public sealed class Uia3Probe : IDisposable
{
    private readonly Dictionary<int, ElementEntry> _cache = new();
    private int _nextElementId = 1;

    /// <summary>Find a single element by search criteria.</summary>
    public int FindElement(string searchBy, string value, int? index = null)
    {
        var element = ResolveElement(searchBy, value, index);
        if (element == null)
            throw new ElementNotFoundException($"Element not found: searchBy={searchBy}, value={value}");

        var entry = new ElementEntry
        {
            ElementId = _nextElementId++,
            Element = element,
            SearchBy = searchBy,
            SearchValue = value,
            Index = index,
            LastAccessed = DateTime.UtcNow,
        };
        _cache[entry.ElementId] = entry;
        return entry.ElementId;
    }

    /// <summary>Find all elements matching search criteria.</summary>
    public List<int> FindElements(string searchBy, string value)
    {
        var results = new List<int>();
        var elements = ResolveElements(searchBy, value);
        foreach (var element in elements)
        {
            var entry = new ElementEntry
            {
                ElementId = _nextElementId++,
                Element = element,
                SearchBy = searchBy,
                SearchValue = value,
                Index = null,
                LastAccessed = DateTime.UtcNow,
            };
            _cache[entry.ElementId] = entry;
            results.Add(entry.ElementId);
        }
        return results;
    }

    /// <summary>Invoke (click) a cached element, re-resolving if stale.</summary>
    public bool Invoke(int elementId)
    {
        var entry = GetOrResolve(elementId);
        try
        {
            if (entry.Element.TryGetCurrentPattern(InvokePattern.Pattern, out var patternObj) &&
                patternObj is InvokePattern ip)
            {
                ip.Invoke();
                return true;
            }
            // Fallback: click via the element's Click method if available
            return false;
        }
        catch (ElementNotAvailableException)
        {
            return RetryInvoke(elementId);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return RetryInvoke(elementId);
        }
    }

    /// <summary>Set value on a cached element.</summary>
    public bool SetValue(int elementId, string value)
    {
        var entry = GetOrResolve(elementId);
        try
        {
            if (entry.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj) &&
                patternObj is ValuePattern vp && !vp.Current.IsReadOnly)
            {
                vp.SetValue(value);
                return true;
            }
            return false;
        }
        catch (ElementNotAvailableException)
        {
            return RetrySetValue(elementId, value);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return RetrySetValue(elementId, value);
        }
    }

    /// <summary>Get text from a cached element.</summary>
    public string GetText(int elementId)
    {
        var entry = GetOrResolve(elementId);
        try
        {
            if (entry.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj) &&
                patternObj is ValuePattern vp)
            {
                return vp.Current.Value ?? entry.Element.Current.Name ?? "";
            }
            return entry.Element.Current.Name ?? "";
        }
        catch (ElementNotAvailableException)
        {
            var retryEntry = RetryResolve(entry);
            if (retryEntry == null) return "";
            var retryElement = retryEntry.Element;
            if (retryElement.TryGetCurrentPattern(ValuePattern.Pattern, out var vpObj) &&
                vpObj is ValuePattern rvp)
            {
                return rvp.Current.Value ?? retryElement.Current.Name ?? "";
            }
            return retryElement.Current.Name ?? "";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            var retryEntry = RetryResolve(entry);
            if (retryEntry == null) return "";
            var retryElement = retryEntry.Element;
            return retryElement.Current.Name ?? "";
        }
    }

    /// <summary>Toggle a checkbox/toggle button.</summary>
    public bool Toggle(int elementId, bool? state = null)
    {
        var entry = GetOrResolve(elementId);
        try
        {
            if (entry.Element.TryGetCurrentPattern(TogglePattern.Pattern, out var patternObj) &&
                patternObj is TogglePattern tp)
            {
                if (state.HasValue)
                {
                    if (tp.Current.ToggleState != (state.Value ? ToggleState.On : ToggleState.Off))
                        tp.Toggle();
                }
                else
                {
                    tp.Toggle();
                }
                return true;
            }
            return false;
        }
        catch (ElementNotAvailableException)
        {
            return RetryToggle(elementId, state);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return RetryToggle(elementId, state);
        }
    }

    /// <summary>Check if element is visible.</summary>
    public bool IsVisible(int elementId)
    {
        var entry = GetOrResolve(elementId);
        try
        {
            return !entry.Element.Current.IsOffscreen;
        }
        catch (ElementNotAvailableException)
        {
            var retryEntry = RetryResolve(entry);
            return retryEntry != null && !retryEntry.Element.Current.IsOffscreen;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            var retryEntry = RetryResolve(entry);
            return retryEntry != null && !retryEntry.Element.Current.IsOffscreen;
        }
    }

    /// <summary>Check if element is enabled.</summary>
    public bool IsEnabled(int elementId)
    {
        var entry = GetOrResolve(elementId);
        try
        {
            return entry.Element.Current.IsEnabled;
        }
        catch (ElementNotAvailableException)
        {
            var retryEntry = RetryResolve(entry);
            return retryEntry != null && retryEntry.Element.Current.IsEnabled;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            var retryEntry = RetryResolve(entry);
            return retryEntry != null && retryEntry.Element.Current.IsEnabled;
        }
    }

    /// <summary>Get an attribute value.</summary>
    public string? GetAttribute(int elementId, string attributeName)
    {
        var entry = GetOrResolve(elementId);
        try
        {
            switch (attributeName.ToLowerInvariant())
            {
                case "automationid": return entry.Element.Current.AutomationId;
                case "name": return entry.Element.Current.Name;
                case "classname": return entry.Element.Current.ClassName;
                case "controltype": return entry.Element.Current.ControlType.ProgrammaticName;
                case "helptext": return entry.Element.Current.HelpText;
                case "acceleratorkey": return entry.Element.Current.AcceleratorKey;
                case "accesskey": return entry.Element.Current.AccessKey;
            }
            return null;
        }
        catch (ElementNotAvailableException)
        {
            var retryEntry = RetryResolve(entry);
            if (retryEntry == null) return null;
            var el = retryEntry.Element;
            return attributeName.ToLowerInvariant() switch
            {
                "automationid" => el.Current.AutomationId,
                "name" => el.Current.Name,
                "classname" => el.Current.ClassName,
                "controltype" => el.Current.ControlType.ProgrammaticName,
                "helptext" => el.Current.HelpText,
                "acceleratorkey" => el.Current.AcceleratorKey,
                "accesskey" => el.Current.AccessKey,
                _ => null
            };
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            var retryEntry = RetryResolve(entry);
            if (retryEntry == null) return null;
            var el = retryEntry.Element;
            return attributeName.ToLowerInvariant() switch
            {
                "automationid" => el.Current.AutomationId,
                "name" => el.Current.Name,
                "classname" => el.Current.ClassName,
                "controltype" => el.Current.ControlType.ProgrammaticName,
                "helptext" => el.Current.HelpText,
                "acceleratorkey" => el.Current.AcceleratorKey,
                "accesskey" => el.Current.AccessKey,
                _ => null
            };
        }
    }

    /// <summary>Capture a screenshot of the element's window via PrintWindow/GDI.</summary>
    public string? CaptureScreenshot(int? elementId = null)
    {
        try
        {
            IntPtr hwnd = IntPtr.Zero;
            if (elementId.HasValue)
            {
                var entry = GetOrResolve(elementId.Value);
                try { hwnd = new IntPtr(entry.Element.Current.NativeWindowHandle); }
                catch { hwnd = IntPtr.Zero; }
            }
            return ScreenshotCapture.CaptureWindow(hwnd);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Extract DataGrid content via TablePattern (no OCR needed).</summary>
    public string GetDataGridContent(int elementId)
    {
        var entry = GetOrResolve(elementId);
        try
        {
            if (!entry.Element.TryGetCurrentPattern(TablePattern.Pattern, out var patternObj) ||
                patternObj is not TablePattern tp)
            {
                return "";
            }

            var rowCount = tp.Current.RowCount;
            var columnCount = tp.Current.ColumnCount;
            if (rowCount == 0 || columnCount == 0)
                return "";

            using var writer = new StringWriter();
            for (int r = 0; r < rowCount; r++)
            {
                var cells = new List<string>();
                for (int c = 0; c < columnCount; c++)
                {
                    try
                    {
                        var item = tp.GetItem(r, c);
                        cells.Add(item.Current.Name ?? item.Current.AutomationId ?? "");
                    }
                    catch
                    {
                        cells.Add("");
                    }
                }
                writer.WriteLine(string.Join("\t", cells));
            }
            return writer.ToString();
        }
        catch (ElementNotAvailableException)
        {
            var retryEntry = RetryResolve(entry);
            if (retryEntry == null) return "";
            if (!retryEntry.Element.TryGetCurrentPattern(TablePattern.Pattern, out var tpObj) ||
                tpObj is not TablePattern rtp)
            {
                return "";
            }
            using var writer2 = new StringWriter();
            for (int r = 0; r < rtp.Current.RowCount; r++)
            {
                var cells = new List<string>();
                for (int c = 0; c < rtp.Current.ColumnCount; c++)
                {
                    try
                    {
                        var item = rtp.GetItem(r, c);
                        cells.Add(item.Current.Name ?? item.Current.AutomationId ?? "");
                    }
                    catch { cells.Add(""); }
                }
                writer2.WriteLine(string.Join("\t", cells));
            }
            return writer2.ToString();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            var retryEntry = RetryResolve(entry);
            if (retryEntry == null) return "";
            if (!retryEntry.Element.TryGetCurrentPattern(TablePattern.Pattern, out var tpObj2) ||
                tpObj2 is not TablePattern rtp2)
            {
                return "";
            }
            using var writer3 = new StringWriter();
            for (int r = 0; r < rtp2.Current.RowCount; r++)
            {
                var cells = new List<string>();
                for (int c = 0; c < rtp2.Current.ColumnCount; c++)
                {
                    try
                    {
                        var item = rtp2.GetItem(r, c);
                        cells.Add(item.Current.Name ?? item.Current.AutomationId ?? "");
                    }
                    catch { cells.Add(""); }
                }
                writer3.WriteLine(string.Join("\t", cells));
            }
            return writer3.ToString();
        }
    }

    /// <summary>Scroll an element in the given direction.</summary>
    public bool Scroll(int elementId, string direction)
    {
        var entry = GetOrResolve(elementId);
        try
        {
            if (!entry.Element.TryGetCurrentPattern(ScrollPattern.Pattern, out var patternObj) ||
                patternObj is not ScrollPattern sp)
            {
                return false;
            }
            var dir = direction.ToLowerInvariant();
            if (dir == "down" || dir == "pagedown")
                sp.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            else if (dir == "up" || dir == "pageup")
                sp.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeDecrement);
            else if (dir == "left")
                sp.Scroll(ScrollAmount.LargeDecrement, ScrollAmount.NoAmount);
            else if (dir == "right")
                sp.Scroll(ScrollAmount.LargeIncrement, ScrollAmount.NoAmount);
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return RetryScroll(elementId, direction);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return RetryScroll(elementId, direction);
        }
    }

    /// <summary>Double-click an element.</summary>
    public bool DoubleClick(int elementId)
    {
        var entry = GetOrResolve(elementId);
        try
        {
            if (entry.Element.TryGetCurrentPattern(InvokePattern.Pattern, out var patternObj) &&
                patternObj is InvokePattern ip)
            {
                ip.Invoke();
                return true;
            }
            return false;
        }
        catch (ElementNotAvailableException)
        {
            return RetryInvoke(elementId);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return RetryInvoke(elementId);
        }
    }

    /// <summary>Right-click an element (UIA3 has no direct right-click; fallback to invoke).</summary>
    public bool RightClick(int elementId)
    {
        return Invoke(elementId);
    }

    /// <summary>Press keys into an element.</summary>
    public bool PressKeys(int elementId, string keys)
    {
        var entry = GetOrResolve(elementId);
        try
        {
            if (entry.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj) &&
                patternObj is ValuePattern vp && !vp.Current.IsReadOnly)
            {
                vp.SetValue(keys);
                return true;
            }
            return false;
        }
        catch (ElementNotAvailableException)
        {
            return RetryPressKeys(elementId, keys);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return RetryPressKeys(elementId, keys);
        }
    }

    /// <summary>Drag and drop between two elements.</summary>
    public bool DragDrop(int srcElementId, int dstElementId)
    {
        // UIA3 does not expose drag-drop natively; invoke both as best-effort.
        return Invoke(srcElementId) && Invoke(dstElementId);
    }

    /// <summary>Hover over an element (set focus).</summary>
    public bool Hover(int elementId)
    {
        var entry = GetOrResolve(elementId);
        try
        {
            entry.Element.SetFocus();
            return true;
        }
        catch (ElementNotAvailableException)
        {
            var retryEntry = RetryResolve(entry);
            if (retryEntry == null) return false;
            try { retryEntry.Element.SetFocus(); return true; } catch { return false; }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            var retryEntry = RetryResolve(entry);
            if (retryEntry == null) return false;
            try { retryEntry.Element.SetFocus(); return true; } catch { return false; }
        }
    }

    /// <summary>Close/dispose all cached elements.</summary>
    public void Close()
    {
        _cache.Clear();
        _nextElementId = 1;
    }

    // ------------------------------------------------------------------
    // Internal resolution helpers
    // ------------------------------------------------------------------

    private ElementEntry GetOrResolve(int elementId)
    {
        if (_cache.TryGetValue(elementId, out var entry))
        {
            entry.LastAccessed = DateTime.UtcNow;
            return entry;
        }
        throw new KeyNotFoundException($"Element ID {elementId} not in cache");
    }

    private ElementEntry? RetryResolve(ElementEntry staleEntry)
    {
        try
        {
            var element = ResolveElement(staleEntry.SearchBy, staleEntry.SearchValue, staleEntry.Index);
            if (element == null) return null;
            var newEntry = new ElementEntry
            {
                ElementId = staleEntry.ElementId,
                Element = element,
                SearchBy = staleEntry.SearchBy,
                SearchValue = staleEntry.SearchValue,
                Index = staleEntry.Index,
                LastAccessed = DateTime.UtcNow,
            };
            _cache[staleEntry.ElementId] = newEntry;
            return newEntry;
        }
        catch
        {
            return null;
        }
    }

    private bool RetryInvoke(int elementId)
    {
        if (!_cache.TryGetValue(elementId, out var entry)) return false;
        var retryEntry = RetryResolve(entry);
        if (retryEntry == null) return false;
        try
        {
            if (retryEntry.Element.TryGetCurrentPattern(InvokePattern.Pattern, out var patternObj) &&
                patternObj is InvokePattern ip)
            {
                ip.Invoke();
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    private bool RetrySetValue(int elementId, string value)
    {
        if (!_cache.TryGetValue(elementId, out var entry)) return false;
        var retryEntry = RetryResolve(entry);
        if (retryEntry == null) return false;
        try
        {
            if (retryEntry.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj) &&
                patternObj is ValuePattern vp && !vp.Current.IsReadOnly)
            {
                vp.SetValue(value);
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    private bool RetryToggle(int elementId, bool? state)
    {
        if (!_cache.TryGetValue(elementId, out var entry)) return false;
        var retryEntry = RetryResolve(entry);
        if (retryEntry == null) return false;
        try
        {
            if (retryEntry.Element.TryGetCurrentPattern(TogglePattern.Pattern, out var patternObj) &&
                patternObj is TogglePattern tp)
            {
                if (state.HasValue)
                {
                    if (tp.Current.ToggleState != (state.Value ? ToggleState.On : ToggleState.Off))
                        tp.Toggle();
                }
                else
                {
                    tp.Toggle();
                }
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    private bool RetryScroll(int elementId, string direction)
    {
        if (!_cache.TryGetValue(elementId, out var entry)) return false;
        var retryEntry = RetryResolve(entry);
        if (retryEntry == null) return false;
        try
        {
            if (!retryEntry.Element.TryGetCurrentPattern(ScrollPattern.Pattern, out var patternObj) ||
                patternObj is not ScrollPattern sp)
            {
                return false;
            }
            var dir = direction.ToLowerInvariant();
            if (dir == "down" || dir == "pagedown")
                sp.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            else if (dir == "up" || dir == "pageup")
                sp.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeDecrement);
            else if (dir == "left")
                sp.Scroll(ScrollAmount.LargeDecrement, ScrollAmount.NoAmount);
            else if (dir == "right")
                sp.Scroll(ScrollAmount.LargeIncrement, ScrollAmount.NoAmount);
            return true;
        }
        catch { return false; }
    }

    private bool RetryPressKeys(int elementId, string keys)
    {
        if (!_cache.TryGetValue(elementId, out var entry)) return false;
        var retryEntry = RetryResolve(entry);
        if (retryEntry == null) return false;
        try
        {
            if (retryEntry.Element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObj) &&
                patternObj is ValuePattern vp && !vp.Current.IsReadOnly)
            {
                vp.SetValue(keys);
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    // ------------------------------------------------------------------
    // UIA3 resolution
    // ------------------------------------------------------------------

    private AutomationElement? ResolveElement(string searchBy, string value, int? index)
    {
        AutomationElement start = AutomationElement.RootElement;
        Condition? condition = BuildCondition(searchBy, value);

        try
        {
            if (searchBy.Equals("XPath", StringComparison.OrdinalIgnoreCase))
            {
                return ResolveXPath(value, index);
            }

            var found = start.FindFirst(TreeScope.Descendants, condition!);
            if (found == null) return null;
            if (index.HasValue)
            {
                var all = start.FindAll(TreeScope.Descendants, condition!);
                if (index.Value >= 0 && index.Value < all.Count)
                    return all[index.Value];
                return null;
            }
            return found;
        }
        catch
        {
            return null;
        }
    }

    private List<AutomationElement> ResolveElements(string searchBy, string value)
    {
        var results = new List<AutomationElement>();
        AutomationElement start = AutomationElement.RootElement;
        Condition? condition = BuildCondition(searchBy, value);

        try
        {
            if (searchBy.Equals("XPath", StringComparison.OrdinalIgnoreCase))
            {
                var single = ResolveXPath(value, null);
                if (single != null) results.Add(single);
                return results;
            }

            var all = start.FindAll(TreeScope.Descendants, condition!);
            foreach (AutomationElement el in all)
                results.Add(el);
        }
        catch
        {
        }
        return results;
    }

    private Condition? BuildCondition(string searchBy, string value)
    {
        return searchBy.ToLowerInvariant() switch
        {
            "automationid" => new PropertyCondition(AutomationElement.AutomationIdProperty, value),
            "name" => new PropertyCondition(AutomationElement.NameProperty, value),
            "controltype" => new PropertyCondition(AutomationElement.ControlTypeProperty, ControlTypeFromName(value)),
            "classname" => new PropertyCondition(AutomationElement.ClassNameProperty, value),
            _ => new PropertyCondition(AutomationElement.NameProperty, value)
        };
    }

    private AutomationElement? ResolveXPath(string xpath, int? index)
    {
        // Simple XPath parsing: /Window[@AutomationId='x']/Button[@AutomationId='y']
        var path = xpath.Trim();
        if (path.StartsWith("/")) path = path[1..];
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        AutomationElement current = AutomationElement.RootElement;
        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i].Trim();
            if (seg.Length == 0) continue;

            var (controlType, attr, attrValue, segIndex) = ParseSegment(seg);
            Condition cond = BuildSegmentCondition(controlType, attr, attrValue);

            try
            {
                AutomationElement found;
                if (i == 0)
                    found = current.FindFirst(TreeScope.Children, cond);
                else
                    found = current.FindFirst(TreeScope.Children, cond);

                if (found == null) return null;

                if (segIndex.HasValue)
                {
                    var siblings = current.FindAll(TreeScope.Children, cond);
                    if (segIndex.Value >= 0 && segIndex.Value < siblings.Count)
                        found = siblings[segIndex.Value];
                    else
                        return null;
                }
                current = found;
            }
            catch
            {
                return null;
            }
        }
        return current;
    }

    private (string? controlType, string? attr, string? value, int? index) ParseSegment(string segment)
    {
        string? controlType = null;
        string? attr = null;
        string? value = null;
        int? index = null;

        // Match tag[@attr='val'][N] or tag[N] or tag
        int bracketIdx = segment.IndexOf('[');
        if (bracketIdx >= 0)
        {
            controlType = segment[..bracketIdx];
            var rest = segment[(bracketIdx + 1)..];
            int closeIdx = rest.IndexOf(']');
            if (closeIdx >= 0)
            {
                var predicate = rest[..closeIdx];
                var after = rest[(closeIdx + 1)..];
                // Check for index after predicate
                int idxBracket = after.IndexOf('[');
                if (idxBracket >= 0)
                {
                    int idxClose = after.IndexOf(']', idxBracket);
                    if (idxClose >= 0 && int.TryParse(after[(idxBracket + 1)..idxClose], out var idx))
                        index = idx - 1; // XPath 1-based -> 0-based
                }

                // Parse @attr='value'
                int eqIdx = predicate.IndexOf('=');
                if (eqIdx >= 0 && predicate.StartsWith("@"))
                {
                    attr = predicate[1..eqIdx];
                    var valPart = predicate[(eqIdx + 1)..].Trim();
                    if (valPart.Length >= 2 && valPart.StartsWith("'") && valPart.EndsWith("'"))
                        value = valPart[1..^1];
                    else if (valPart.Length >= 2 && valPart.StartsWith("\"") && valPart.EndsWith("\""))
                        value = valPart[1..^1];
                    else
                        value = valPart;
                }
                else if (int.TryParse(predicate, out var numericIdx))
                {
                    index = numericIdx - 1;
                }
            }
        }
        else
        {
            controlType = segment;
        }

        return (controlType, attr, value, index);
    }

    private Condition BuildSegmentCondition(string? controlType, string? attr, string? value)
    {
        var conditions = new List<Condition>();
        if (!string.IsNullOrEmpty(controlType) && controlType != "*" && controlType != "Element")
        {
            try
            {
                var ct = ControlTypeFromName(controlType);
                conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, ct));
            }
            catch { }
        }
        if (!string.IsNullOrEmpty(attr) && value != null)
        {
            conditions.Add(attr.ToLowerInvariant() switch
            {
                "automationid" => new PropertyCondition(AutomationElement.AutomationIdProperty, value),
                "name" => new PropertyCondition(AutomationElement.NameProperty, value),
                "classname" => new PropertyCondition(AutomationElement.ClassNameProperty, value),
                _ => new PropertyCondition(AutomationElement.NameProperty, value)
            });
        }
        if (conditions.Count == 0)
            return Condition.TrueCondition;
        if (conditions.Count == 1)
            return conditions[0];
        var and = new AndCondition(conditions[0], conditions[1]);
        for (int i = 2; i < conditions.Count; i++)
            and = new AndCondition(and, conditions[i]);
        return and;
    }

    private static ControlType ControlTypeFromName(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "window" => ControlType.Window,
            "button" => ControlType.Button,
            "textbox" or "edit" => ControlType.Edit,
            "checkbox" => ControlType.CheckBox,
            "radiobutton" => ControlType.RadioButton,
            "combobox" => ControlType.ComboBox,
            "listbox" or "list" => ControlType.List,
            "datagrid" => ControlType.DataGrid,
            "tabcontrol" => ControlType.Tab,
            "tabitem" => ControlType.TabItem,
            "label" or "textblock" => ControlType.Text,
            "progressbar" => ControlType.ProgressBar,
            "slider" => ControlType.Slider,
            "menu" => ControlType.Menu,
            "menuitem" => ControlType.MenuItem,
            "treeview" => ControlType.Tree,
            "treeviewitem" => ControlType.TreeItem,
            "listview" => ControlType.List,
            "statusbar" => ControlType.StatusBar,
            "toolbar" => ControlType.ToolBar,
            "groupbox" or "expander" => ControlType.Group,
            "scrollviewer" => ControlType.ScrollBar,
            "border" or "grid" or "stackpanel" or "canvas" => ControlType.Pane,
            "image" or "mediaelement" => ControlType.Image,
            _ => ControlType.Window
        };
    }

    public void Dispose()
    {
        Close();
    }
}

/// <summary>Thrown when a UIA element cannot be found.</summary>
public sealed class ElementNotFoundException : Exception
{
    public ElementNotFoundException(string message) : base(message) { }
}
