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
        public static string GenerateYaml(IEnumerable<ElementEntry> entries)
        {
            var elements = new Dictionary<string, object>();

            foreach (var entry in entries)
            {
                var strategies = new Dictionary<string, object>();

                if (!string.IsNullOrEmpty(entry.AutomationId))
                {
                    strategies["FlaUI"] = new Dictionary<string, object>
                    {
                        ["searchBy"] = "AutomationId",
                        ["value"] = entry.AutomationId!,
                        ["scope"] = "Descendant",
                    };
                }

                strategies["WPFSpy"] = new Dictionary<string, object>
                {
                    ["searchBy"] = "Name",
                    ["value"] = entry.Name,
                };

                if (!string.IsNullOrEmpty(entry.XPath))
                {
                    strategies["XPath"] = new Dictionary<string, object>
                    {
                        ["value"] = entry.XPath!,
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
