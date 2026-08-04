using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;

namespace WpfSpyAgent
{
    /// <summary>
    /// UIA Event Recorder - hooks into UI Automation events to record
    /// user interactions with the WPF application in real-time.
    /// 
    /// Records:
    /// - Invoke events (clicks on buttons, menu items, etc.)
    /// - Text change events (text input in text boxes, etc.)
    /// - Focus change events (tab navigation, etc.)
    /// - Selection events (combo box selection, etc.)
    /// 
    /// Each recorded event includes:
    /// - Timestamp
    /// - Element properties (AutomationId, Name, ControlType)
    /// - XPath for reliable element identification
    /// - Event type and value (for text input)
    /// </summary>
    public class UiaEventRecorder
    {
        private readonly List<RecordedEvent> _events = new();
        private readonly object _lock = new();
        private bool _isRecording;
        private AutomationEventHandler? _invokeHandler;
        private AutomationEventHandler? _textChangedHandler;
        private AutomationFocusChangedEventHandler? _focusChangedHandler;
        private AutomationEventHandler? _selectionHandler;
        private readonly Dispatcher _dispatcher;

        public UiaEventRecorder()
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        /// <summary>
        /// Gets whether the recorder is currently capturing events.
        /// </summary>
        public bool IsRecording => _isRecording;

        /// <summary>
        /// Gets the number of events recorded so far.
        /// </summary>
        public int EventCount
        {
            get
            {
                lock (_lock)
                {
                    return _events.Count;
                }
            }
        }

        /// <summary>
        /// Starts recording UI Automation events from the application.
        /// Must be called from the UI thread.
        /// </summary>
        public void StartRecording()
        {
            if (_isRecording) return;

            _events.Clear();
            _isRecording = true;

            // Register event handlers
            _invokeHandler = OnAutomationEvent;
            _focusChangedHandler = OnFocusChanged;
            _textChangedHandler = OnAutomationEvent;
            _selectionHandler = OnAutomationEvent;

            // Invoke events (Button, MenuItem, etc.)
            Automation.AddAutomationEventHandler(
                InvokePattern.InvokedEvent,
                AutomationElement.RootElement,
                TreeScope.Descendants,
                _invokeHandler);

            // Text change events (TextBox)
            Automation.AddAutomationEventHandler(
                TextPattern.TextChangedEvent,
                AutomationElement.RootElement,
                TreeScope.Descendants,
                _textChangedHandler);

            // Selection events (ComboBox, ListBox)
            Automation.AddAutomationEventHandler(
                SelectionItemPattern.ElementSelectedEvent,
                AutomationElement.RootElement,
                TreeScope.Descendants,
                _selectionHandler);

            // Focus change events
            Automation.AddAutomationFocusChangedEventHandler(_focusChangedHandler);

            Log($"[UiaEventRecorder] Recording started. Events will be captured.");
        }

        /// <summary>
        /// Stops recording UI Automation events.
        /// </summary>
        public void StopRecording()
        {
            if (!_isRecording) return;

            _isRecording = false;

            // Unregister event handlers
            if (_invokeHandler != null)
            {
                Automation.RemoveAutomationEventHandler(
                    InvokePattern.InvokedEvent,
                    AutomationElement.RootElement,
                    _invokeHandler);
            }

            if (_textChangedHandler != null)
            {
                Automation.RemoveAutomationEventHandler(
                    TextPattern.TextChangedEvent,
                    AutomationElement.RootElement,
                    _textChangedHandler);
            }

            if (_selectionHandler != null)
            {
                Automation.RemoveAutomationEventHandler(
                    SelectionItemPattern.ElementSelectedEvent,
                    AutomationElement.RootElement,
                    _selectionHandler);
            }

            if (_focusChangedHandler != null)
            {
                Automation.RemoveAutomationFocusChangedEventHandler(_focusChangedHandler);
            }

            Log($"[UiaEventRecorder] Recording stopped. Captured {_events.Count} events.");
        }

        /// <summary>
        /// Gets all recorded events as a JSON-serializable list.
        /// </summary>
        public List<RecordedEvent> GetRecordedEvents()
        {
            lock (_lock)
            {
                return _events.ToList();
            }
        }

        /// <summary>
        /// Clears all recorded events.
        /// </summary>
        public void ClearEvents()
        {
            lock (_lock)
            {
                _events.Clear();
            }
            Log($"[UiaEventRecorder] Events cleared.");
        }

        /// <summary>
        /// Exports recorded events to a format compatible with the recorder converter.
        /// </summary>
        public RecordingExport Export()
        {
            List<RecordedEvent> eventsCopy;
            lock (_lock)
            {
                eventsCopy = _events.ToList();
            }

            var elements = new Dictionary<string, ElementInfo>();
            var steps = new List<StepInfo>();

            foreach (var evt in eventsCopy)
            {
                // Build element info
                var alias = $"{evt.PageName}.{evt.AutomationId ?? evt.ControlType}";
                if (!elements.ContainsKey(alias))
                {
                    elements[alias] = new ElementInfo
                    {
                        AutomationId = evt.AutomationId,
                        Name = evt.Name,
                        ControlType = evt.ControlType,
                        Xpath = evt.Xpath
                    };
                }

                // Build step info
                var stepType = evt.EventType switch
                {
                    "Invoke" => "InvokeStep",
                    "TextChanged" => "ValueStep",
                    "Selection" => "SelectionStep",
                    _ => "GenericStep"
                };

                steps.Add(new StepInfo
                {
                    Alias = alias,
                    StepType = stepType,
                    Value = evt.Value,
                    Timestamp = evt.Timestamp
                });
            }

            return new RecordingExport
            {
                Elements = elements,
                Steps = steps,
                Sequence = eventsCopy
            };
        }

        private void OnAutomationEvent(object sender, AutomationEventArgs e)
        {
            if (!_isRecording) return;

            try
            {
                if (sender is not AutomationElement element) return;

                // Get element properties
                var props = GetElementProperties(element);
                if (props == null) return;

                // Determine event type and value
                string eventType;
                string? value = null;

                if (e.EventId == InvokePattern.InvokedEvent)
                {
                    eventType = "Invoke";
                }
                else if (e.EventId == TextPattern.TextChangedEvent)
                {
                    eventType = "TextChanged";
                    value = GetElementText(element);
                }
                else if (e.EventId == SelectionItemPattern.ElementSelectedEvent)
                {
                    eventType = "Selection";
                    value = GetSelectedItem(element);
                }
                else
                {
                    eventType = e.EventId.ProgrammaticName;
                }

                var recordedEvent = new RecordedEvent
                {
                    Timestamp = DateTime.Now.ToString("o"),
                    EventType = eventType,
                    AutomationId = props.AutomationId,
                    Name = props.Name,
                    ControlType = props.ControlType,
                    Xpath = props.Xpath,
                    Value = value,
                    PageName = InferPageName(element)
                };

                lock (_lock)
                {
                    _events.Add(recordedEvent);
                }

                Log($"[UiaEventRecorder] Event: {eventType} on {props.ControlType} '{props.AutomationId ?? props.Name}'");
            }
            catch (Exception ex)
            {
                Log($"[UiaEventRecorder] Error capturing event: {ex.Message}");
            }
        }

        private void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
        {
            if (!_isRecording) return;

            try
            {
                // Use AutomationElement.FocusedElement to get the focused element
                var element = AutomationElement.FocusedElement;
                if (element == null) return;

                var props = GetElementPropertiesFromElement(element);
                if (props == null) return;

                var recordedEvent = new RecordedEvent
                {
                    Timestamp = DateTime.Now.ToString("o"),
                    EventType = "FocusChanged",
                    AutomationId = props.AutomationId,
                    Name = props.Name,
                    ControlType = props.ControlType,
                    Xpath = props.Xpath,
                    PageName = InferPageNameFromElement(element)
                };

                lock (_lock)
                {
                    _events.Add(recordedEvent);
                }

                Log($"[UiaEventRecorder] Focus: {props.ControlType} '{props.AutomationId ?? props.Name}'");
            }
            catch (Exception ex)
            {
                Log($"[UiaEventRecorder] Error capturing focus: {ex.Message}");
            }
        }

        private ElementProperties? GetElementProperties(AutomationElement element)
        {
            try
            {
                string? automationId = null;
                string name = "";
                string controlType = "";

                // Get AutomationId
                try
                {
                    automationId = element.GetCurrentPropertyValue(AutomationElement.AutomationIdProperty) as string;
                }
                catch { }

                // Get Name
                try
                {
                    name = element.GetCurrentPropertyValue(AutomationElement.NameProperty) as string ?? "";
                }
                catch { }

                // Get ControlType
                try
                {
                    var ct = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;
                    controlType = ct?.ProgrammaticName ?? "Unknown";
                }
                catch { }

                // Skip if we don't have identifying information
                if (string.IsNullOrEmpty(automationId) && string.IsNullOrEmpty(name))
                    return null;

                // Note: XPath building requires FrameworkElement, not available for all AutomationElements
                // Use the helper method with FrameworkElement when available

                return new ElementProperties
                {
                    AutomationId = automationId,
                    Name = name,
                    ControlType = controlType,
                    Xpath = null  // XPath not available for generic AutomationElement
                };
            }
            catch
            {
                return null;
            }
        }

        private string? GetElementText(AutomationElement element)
        {
            try
            {
                // Try ValuePattern first
                if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern))
                {
                    return ((ValuePattern)valuePattern).Current.Value;
                }

                // Try TextPattern
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPattern))
                {
                    return ((TextPattern)textPattern).DocumentRange.GetText(-1);
                }

                // Fall back to Name
                return element.GetCurrentPropertyValue(AutomationElement.NameProperty) as string;
            }
            catch
            {
                return null;
            }
        }

        private string? GetSelectedItem(AutomationElement element)
        {
            try
            {
                // Try SelectionItemPattern first (for single selection)
                if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionItemPattern))
                {
                    var selectionItem = (SelectionItemPattern)selectionItemPattern;
                    // Check if this item is selected
                    if (selectionItem.Current.IsSelected)
                    {
                        // Try to get the name from the element itself
                        return element.GetCurrentPropertyValue(AutomationElement.NameProperty) as string;
                    }
                }

                // Try SelectionPattern (for containers)
                if (element.TryGetCurrentPattern(SelectionPattern.Pattern, out var selectionPatternObj))
                {
                    var selection = (SelectionPattern)selectionPatternObj;
                    var selected = selection.Current.GetSelection();
                    if (selected.Length > 0)
                    {
                        return selected[0].GetCurrentPropertyValue(AutomationElement.NameProperty) as string;
                    }
                }
            }
            catch { }

            return null;
        }

        private string InferPageName(AutomationElement element)
        {
            // Try to infer page name from window or container
            try
            {
                var window = TreeWalker.ControlViewWalker.GetParent(element);
                while (window != null)
                {
                    var ct = window.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;
                    if (ct == ControlType.Window)
                    {
                        var name = window.GetCurrentPropertyValue(AutomationElement.NameProperty) as string;
                        if (!string.IsNullOrEmpty(name))
                        {
                            // Convert window name to page name format
                            return name.Replace(" ", "").Replace("-", "") + "Page";
                        }
                    }
                    window = TreeWalker.ControlViewWalker.GetParent(window);
                }
            }
            catch { }

            return "RecordedPage";
        }

        /// <summary>
        /// Helper method to get element properties from AutomationElement.
        /// This works for all elements including those without FrameworkElement backing.
        /// </summary>
        private ElementProperties? GetElementPropertiesFromElement(AutomationElement element)
        {
            try
            {
                string? automationId = null;
                string name = "";
                string controlType = "";

                // Get AutomationId
                automationId = element.GetCurrentPropertyValue(AutomationElement.AutomationIdProperty) as string;

                // Get Name
                name = element.GetCurrentPropertyValue(AutomationElement.NameProperty) as string ?? "";

                // Get ControlType
                var ct = element.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;
                controlType = ct?.ProgrammaticName ?? "Unknown";

                // Skip if we don't have identifying information
                if (string.IsNullOrEmpty(automationId) && string.IsNullOrEmpty(name))
                    return null;

                return new ElementProperties
                {
                    AutomationId = automationId,
                    Name = name,
                    ControlType = controlType,
                    Xpath = null  // XPath not available for non-FrameworkElement
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Helper method to infer page name from AutomationElement.
        /// </summary>
        private string InferPageNameFromElement(AutomationElement element)
        {
            try
            {
                var window = TreeWalker.ControlViewWalker.GetParent(element);
                while (window != null)
                {
                    var ct = window.GetCurrentPropertyValue(AutomationElement.ControlTypeProperty) as ControlType;
                    if (ct == ControlType.Window)
                    {
                        var name = window.GetCurrentPropertyValue(AutomationElement.NameProperty) as string;
                        if (!string.IsNullOrEmpty(name))
                        {
                            return name.Replace(" ", "").Replace("-", "") + "Page";
                        }
                    }
                    window = TreeWalker.ControlViewWalker.GetParent(window);
                }
            }
            catch { }

            return "RecordedPage";
        }

        private void Log(string message)
        {
            try
            {
                System.IO.File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        private static readonly string _logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "agent_probe_log.txt");
    }

    public class RecordedEvent
    {
        public string Timestamp { get; set; } = "";
        public string EventType { get; set; } = "";
        public string? AutomationId { get; set; }
        public string Name { get; set; } = "";
        public string ControlType { get; set; } = "";
        public string? Xpath { get; set; }
        public string? Value { get; set; }
        public string PageName { get; set; } = "";
    }

    public class ElementProperties
    {
        public string? AutomationId { get; set; }
        public string Name { get; set; } = "";
        public string ControlType { get; set; } = "";
        public string? Xpath { get; set; }
    }

    public class StepInfo
    {
        public string Alias { get; set; } = "";
        public string StepType { get; set; } = "";
        public string? Value { get; set; }
        public string Timestamp { get; set; } = "";
    }

    public class RecordingExport
    {
        public Dictionary<string, ElementInfo> Elements { get; set; } = new();
        public List<StepInfo> Steps { get; set; } = new();
        public List<RecordedEvent> Sequence { get; set; } = new();
    }

    public class ElementInfo
    {
        public string? AutomationId { get; set; }
        public string Name { get; set; } = "";
        public string ControlType { get; set; } = "";
        public string? Xpath { get; set; }
    }
}
