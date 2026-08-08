using System.Collections.Generic;
using System.Linq;
using WpfTestIde.Models;
using YamlDotNet.Serialization;

namespace WpfTestIde.Recording
{
    /// <summary>
    /// Serializes discovered ElementEntry objects into the exact YAML
    /// schema used by repository/elements/*.yaml in the Python framework
    /// (see docs/ELEMENT_REPOSITORY_GUIDE.md) — the C# equivalent of
    /// recorder/converter.py's generate_element_repository. Built as
    /// plain Dictionary trees (rather than typed classes) so the emitted
    /// YAML keys are guaranteed to match the schema exactly, independent
    /// of YamlDotNet's naming-convention settings.
    /// </summary>
    public static class RepositoryWriter
    {
        /// <summary>
        /// Generates YAML for the element repository, including only
        /// strategies for the selected recording modes.
        /// </summary>
        /// <param name="entries">Discovered element entries.</param>
        /// <param name="recordingModes">Selected recording modes (FlaUI, WPFSpy, Sikuli).
        /// If empty or null, all strategies are included (backward compatible).</param>
        public static string GenerateYaml(IEnumerable<ElementEntry> entries, List<string>? recordingModes = null)
        {
            var elements = new Dictionary<string, object>();

            foreach (var entry in entries)
            {
                var strategies = new Dictionary<string, object>();

                // Use per-element recording modes if available, otherwise fall back to global modes
                var modes = entry.RecordingModes ?? recordingModes;

                // FlaUI strategy: AutomationId-based (most stable for standard controls)
                if ((modes == null || modes.Contains("FlaUI")) &&
                    !string.IsNullOrEmpty(entry.AutomationId))
                {
                    strategies["FlaUI"] = new Dictionary<string, object>
                    {
                        ["searchBy"] = "AutomationId",
                        ["value"] = entry.AutomationId!,
                        ["scope"] = "Descendant",
                    };
                }

                // WPFSpy strategy: XPath-based (visual tree path from root window)
                if (modes == null || modes.Contains("WPFSpy"))
                {
                    strategies["WPFSpy"] = new Dictionary<string, object>
                    {
                        ["searchBy"] = "XPath",
                        ["value"] = entry.XPath ?? $"{entry.ControlType}[@Name='{entry.Name}']",
                    };
                }

                // Sikuli strategy: image-based (placeholder — image capture not yet implemented)
                if (modes == null || modes.Contains("Sikuli"))
                {
                    strategies["Sikuli"] = new Dictionary<string, object>
                    {
                        ["searchBy"] = "Image",
                        ["value"] = $"{entry.Alias.Split('.').Last().ToLower()}.png",
                    };
                }

                var elementDef = new Dictionary<string, object>
                {
                    ["displayName"] = entry.DisplayName,
                    ["controlType"] = entry.ControlType,
                    ["parentAlias"] = entry.Alias.Split('.')[0] + ".MainWindow",
                    ["defaultTimeout"] = 10,
                    ["tags"] = entry.NonStandard
                        ? new List<string> { "recorded", "self-healing-demo" }
                        : new List<string> { "recorded" },
                    ["strategies"] = strategies,
                };

                elements[entry.Alias] = elementDef;
            }

            var root = new Dictionary<string, object> { ["elements"] = elements };

            var serializer = new SerializerBuilder().Build();
            return serializer.Serialize(root);
        }

        /// <summary>Deduplicates a raw step/entry stream by alias, keeping
        /// the first-seen entry for each (matches how the real recorder
        /// treats repeated interactions with the same control).</summary>
        public static List<ElementEntry> Deduplicate(IEnumerable<ElementEntry> entries) =>
            entries.GroupBy(e => e.Alias).Select(g => g.First()).ToList();
    }
}
