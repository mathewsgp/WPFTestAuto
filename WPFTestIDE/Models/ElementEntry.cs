using System.Collections.Generic;
using System.Linq;

namespace WpfTestIde.Models
{
    /// <summary>
    /// Locator/probe data captured for one driver during recording.
    /// Stored separately per driver so UIA3 and WPFSpy can keep their
    /// own XPath/properties even when both recording modes are selected.
    /// </summary>
    public class DriverProbeData
    {
        public string? AutomationId { get; set; }
        public string Name { get; set; } = "";
        public string ControlType { get; set; } = "";
        public string? Text { get; set; }
        public string? XPath { get; set; }

        public DriverProbeData Clone()
        {
            return new DriverProbeData
            {
                AutomationId = AutomationId,
                Name = Name,
                ControlType = ControlType,
                Text = Text,
                XPath = XPath,
            };
        }
    }

    /// <summary>
    /// One Element Repository entry, discovered automatically during
    /// recording. Mirrors repository/elements/*.json's schema in the
    /// Python framework (see RepositoryWriter for the exact JSON shape).
    /// </summary>
    public class ElementEntry
    {
        public string Alias { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string ControlType { get; set; } = "";

        /// <summary>Null/empty means "no reliable AutomationId" — a
        /// custom-rendered control that needs the WPFSpy fallback
        /// strategy. See docs/SELF_HEALING_LOCATORS.md.</summary>
        public string? AutomationId { get; set; }

        /// <summary>WPF Name — always present, used as the WPFSpy strategy's locator.</summary>
        public string Name { get; set; } = "";

        /// <summary>XPath from the root window to this element. Used when
        /// AutomationId/Name alone are not unique enough in a deep
        /// hierarchy. Populated during recording by WPFSpy's ProbeAt.</summary>
        public string? XPath { get; set; }

        public bool NonStandard => string.IsNullOrEmpty(AutomationId);

        /// <summary>Per-element recording modes (UIA3, WPFSpy, OculiX).
        /// If null or empty, the global recording modes from the ViewModel are used.</summary>
        public List<string>? RecordingModes { get; set; }

        /// <summary>Per-driver probe results captured during recording.
        /// Keys are driver names (UIA3, WPFSpy). When present, repository
        /// generation should prefer this data over the shared top-level fields.</summary>
        public Dictionary<string, DriverProbeData>? DriverProbes { get; set; }

        /// <summary>Order of driver strategy groups in the emitted repository
        /// JSON (e.g. ["UIA3","WPFSpy","OculiX"]). When null/empty, the
        /// default fixed order UIA3 -> WPFSpy -> OculiX is used.</summary>
        public List<string>? StrategyOrder { get; set; }

        /// <summary>Legacy per-element driver priority. Accepted on load for backward compatibility;
        /// ignored for runtime priority (fixed UIA3-&gt;WPFSpy-&gt;OculiX) and not emitted on save.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("driverPriority")]
        public List<string>? LegacyDriverPriority
        {
            get => null;
            set
            {
                if (value != null && value.Count > 0 && (StrategyOrder == null || StrategyOrder.Count == 0))
                    StrategyOrder = value.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            }
        }

        /// <summary>App context ID for multi-app support. Null/empty means global element.</summary>
        public string? AppId { get; set; }

        /// <summary>Path to a reference image under repository/okulix/
        /// captured by the WpfSpyAgent at recording time. When set, the
        /// emitted OculiX strategy uses this image instead of a derived
        /// file name. Stored as a repository-relative path so the JSON
        /// stays portable across machines.</summary>
        public string? ImagePath { get; set; }

        /// <summary>Editable strategy groups for the properties UI and canonical export.
        /// Not persisted directly by System.Text.Json in old loads; populated by IDE import/edit flow.</summary>
        public List<ElementStrategyGroup>? StrategyGroups { get; set; }

        public ElementEntry Clone()
        {
            return new ElementEntry
            {
                Alias = Alias,
                DisplayName = DisplayName,
                ControlType = ControlType,
                AutomationId = AutomationId,
                Name = Name,
                XPath = XPath,
                RecordingModes = RecordingModes != null ? new List<string>(RecordingModes) : null,
                DriverProbes = DriverProbes?.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
                StrategyOrder = StrategyOrder != null ? new List<string>(StrategyOrder) : null,
                StrategyGroups = StrategyGroups?.Select(g => g.Clone()).ToList(),
                AppId = AppId,
                ImagePath = ImagePath,
            };
        }
    }
}
