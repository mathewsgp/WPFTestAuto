#if NET461
using Newtonsoft.Json;
using JsonPropertyNameAttribute = Newtonsoft.Json.JsonPropertyAttribute;
#else
using System.Text.Json.Serialization;
#endif

namespace WpfSpyAgent.Protocol
{
    /// <summary>
    /// Wire format for one request from the WPFSpy driver (test-runner
    /// side, Python) to the in-process Spy Agent (this assembly), sent as
    /// a single line of JSON over the Named Pipe. See docs/PROTOCOL.md
    /// for the authoritative reference.
    /// </summary>
    public class SpyRequest
    {
        [JsonPropertyName("command")]
        public string Command { get; set; } = "";

        /// <summary>The element's WPF Name (FrameworkElement.Name) — the
        /// agent re-resolves the element fresh from the live visual tree
        /// on every call rather than caching a handle, so navigating to a
        /// new page/window between calls never produces a stale
        /// reference.</summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Used by SetValue only.</summary>
        [JsonPropertyName("value")]
        public string? Value { get; set; }

        /// <summary>Used by ProbeAt only — screen coordinates to hit-test
        /// against this process's windows. Used by WpfTestIde's recorder
        /// to identify the element under the cursor at click time,
        /// including custom-rendered controls with no AutomationId.</summary>
        [JsonPropertyName("x")]
        public double? X { get; set; }

        [JsonPropertyName("y")]
        public double? Y { get; set; }

        /// <summary>Used by CaptureArea — width of capture region.</summary>
        [JsonPropertyName("width")]
        public double? Width { get; set; }

        /// <summary>Used by CaptureArea — height of capture region.</summary>
        [JsonPropertyName("height")]
        public double? Height { get; set; }

        /// <summary>Used by FindByXPath only — an XPath expression locating
        /// the element from the root window.</summary>
        [JsonPropertyName("xpath")]
        public string? XPath { get; set; }

        /// <summary>Used by FindByAutomationId — the AutomationId to search for.</summary>
        [JsonPropertyName("automationId")]
        public string? AutomationId { get; set; }

        /// <summary>Used by GetAttribute — the attribute name to retrieve.</summary>
        [JsonPropertyName("attributeName")]
        public string? AttributeName { get; set; }

        /// <summary>Used by DragDrop — the target element name.</summary>
        [JsonPropertyName("targetName")]
        public string? TargetName { get; set; }

        /// <summary>Used by DragDrop — the target element XPath.</summary>
        [JsonPropertyName("targetXPath")]
        public string? TargetXPath { get; set; }
    }

    /// <summary>
    /// Wire format for the Spy Agent's response, one line of JSON.
    /// `data` carries the command-specific payload: GetText -> the text
    /// string, IsVisible -> "true"/"false", everything else -> null.
    /// </summary>
    public class SpyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public string? Data { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        public static SpyResponse Ok(string? data = null) => new() { Success = true, Data = data };
        public static SpyResponse Fail(string error) => new() { Success = false, Error = error };
    }

    /// <summary>
    /// Payload carried inside SpyResponse.Data (as a nested JSON string)
    /// for the ProbeAt command — everything WpfTestIde's recorder needs
    /// to build one Element Repository entry from a single click.
    /// </summary>
    public class ProbeResult
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("automationId")]
        public string? AutomationId { get; set; }

        [JsonPropertyName("controlType")]
        public string ControlType { get; set; } = "";

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        /// <summary>XPath from the root window to this element, built by
        /// <see cref="VisualTreeInspector.BuildXPath(FrameworkElement)"/>.
        /// Used by the IDE recorder when AutomationId/Name alone are not
        /// unique enough in a deep hierarchy.</summary>
        [JsonPropertyName("xpath")]
        public string? XPath { get; set; }
    }
}
