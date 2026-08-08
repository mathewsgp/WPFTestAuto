using System.Collections.Generic;

namespace WpfTestIde.Models
{
    /// <summary>
    /// One Element Repository entry, discovered automatically during
    /// recording. Mirrors repository/elements/*.yaml's schema in the
    /// Python framework (see RepositoryWriter for the exact YAML shape).
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

        /// <summary>Per-element recording modes (FlaUI, WPFSpy, Sikuli).
        /// If null or empty, the global recording modes from the ViewModel are used.</summary>
        public List<string>? RecordingModes { get; set; }

        /// <summary>Per-element driver priority order for element identification.
        /// Example: ["FlaUI", "WPFSpy", "Sikuli"]
        /// If null or empty, the global driver order from config is used.</summary>
        public List<string>? DriverPriority { get; set; }

        /// <summary>App context ID for multi-app support. Null/empty means global element.</summary>
        public string? AppId { get; set; }

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
                DriverPriority = DriverPriority != null ? new List<string>(DriverPriority) : null,
                AppId = AppId,
            };
        }
    }
}
