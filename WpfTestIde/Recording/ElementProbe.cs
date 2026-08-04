using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfTestIde.Recording
{
    public class ProbeResultDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("automationId")]
        public string? AutomationId { get; set; }
        [JsonPropertyName("controlType")]
        public string ControlType { get; set; } = "";
        [JsonPropertyName("text")]
        public string? Text { get; set; }
        [JsonPropertyName("xpath")]
        public string? XPath { get; set; }
    }

    public class ProbedElement
    {
        public string? AutomationId { get; set; }
        public string Name { get; set; } = "";
        public string ControlType { get; set; } = "";
        public string? Text { get; set; }

        /// <summary>Always "WPFSpy" in WPFSpy-only mode.</summary>
        public string ResolvedVia { get; set; } = "WPFSpy";

        /// <summary>XPath from the root window to this element, generated
        /// by WPFSpy's ProbeAt response. Used when AutomationId/Name alone
        /// are not unique enough in a deep hierarchy.</summary>
        public string? XPath { get; set; }
    }

    /// <summary>
    /// Identifies the WPF element at a screen point using WPFSpy only
    /// (temporary WPFSpy-only mode). Asks the target app's in-process
    /// WpfSpyAgent directly via Named Pipe ProbeAt.
    /// </summary>
    public class ElementProbe : IDisposable
    {
        private readonly string _pipeName;

        public ElementProbe(string pipeName)
        {
            _pipeName = pipeName;
        }

        public ProbedElement? ProbeAt(int screenX, int screenY)
        {
            return TryWpfSpy(screenX, screenY);
        }

        private ProbedElement? TryWpfSpy(int x, int y)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var client = new SpyAgentClient(_pipeName);
                var response = client.Send("ProbeAt", x: x, y: y);
                sw.Stop();
                Recording.RecordingSession.Log($"[ProbeAt] ({x},{y}) completed in {sw.ElapsedMilliseconds}ms, success={response.Success}, error='{response.Error}'");
                if (!response.Success || response.Data is null)
                {
                    return null;
                }

                var probe = JsonSerializer.Deserialize<ProbeResultDto>(response.Data, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (probe is null)
                {
                    return null;
                }

                return new ProbedElement
                {
                    AutomationId = probe.AutomationId,
                    Name = probe.Name,
                    ControlType = probe.ControlType,
                    Text = probe.Text,
                    ResolvedVia = "WPFSpy",
                    XPath = probe.XPath,
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                Recording.RecordingSession.Log($"[ProbeAt] ({x},{y}) exception after {sw.ElapsedMilliseconds}ms: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>Re-reads an element's current text — used when
        /// committing a SetValue step at focus-lost time, so the
        /// recorded value is whatever the user finished typing, not what
        /// was there at the moment of the initial click/focus.</summary>
        public string? GetCurrentValue(ProbedElement element)
        {
            try
            {
                var client = new SpyAgentClient(_pipeName);
                var response = client.Send("GetText", name: element.Name);
                return response.Success ? response.Data : element.Text;
            }
            catch
            {
                return element.Text;
            }
        }

        public void Dispose() { }
    }
}

