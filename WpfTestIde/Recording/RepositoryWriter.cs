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
                var strategies = new Dictionary<string, List<object>>();

                // Use per-element recording modes if available, otherwise fall back to global modes.
                // If the element was resolved via FlaUI probe, always include a FlaUI strategy
                // (even if global recording modes don't include FlaUI) — otherwise the recorded
                // step will fail to find the element at replay time.
                var modes = entry.RecordingModes ?? recordingModes;
                bool resolvedViaFlaUI = entry.NonStandard == false && !string.IsNullOrEmpty(entry.XPath) == false;
                bool resolvedViaWPFSpy = !string.IsNullOrEmpty(entry.XPath) && entry.XPath!.Contains("[@AutomationId=");
                // Heuristic: elements with an XPath always get an XPath-based FlaUI strategy
                // as the default (mirrors the WPFSpy default), so the recorded step is
                // symmetric across drivers and stable when AutomationId is missing or
                // collides. AutomationId/Name are kept as fallbacks.
                bool includeFlaUI = (modes == null || modes.Contains("FlaUI"))
                    || !string.IsNullOrEmpty(entry.AutomationId)
                    || entry.ControlType == "Window"
                    || (!string.IsNullOrEmpty(entry.Name) && entry.Name.Length > 0);

                if (includeFlaUI && !string.IsNullOrEmpty(entry.XPath))
                {
                    // Default FlaUI strategy: XPath (UIA control types, built by
                    // FlaUIElementProbe.BuildXPath — already compatible with FlaUI).
                    var flauiStrategies = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["searchBy"] = "XPath",
                            ["value"] = entry.XPath!,
                        }
                    };
                    if (!string.IsNullOrEmpty(entry.AutomationId))
                    {
                        flauiStrategies.Add(new Dictionary<string, object>
                        {
                            ["searchBy"] = "AutomationId",
                            ["value"] = entry.AutomationId!,
                            ["scope"] = "Descendant",
                        });
                    }
                    if (!string.IsNullOrEmpty(entry.Name) && entry.Name != entry.AutomationId)
                    {
                        flauiStrategies.Add(new Dictionary<string, object>
                        {
                            ["searchBy"] = "Name",
                            ["value"] = entry.Name,
                            ["scope"] = "Descendant",
                        });
                    }
                    strategies["FlaUI"] = flauiStrategies;
                }
                else if (includeFlaUI && !string.IsNullOrEmpty(entry.AutomationId))
                {
                    strategies["FlaUI"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["searchBy"] = "AutomationId",
                            ["value"] = entry.AutomationId!,
                            ["scope"] = "Descendant",
                        }
                    };
                }
                else if (includeFlaUI && !string.IsNullOrEmpty(entry.Name))
                {
                    strategies["FlaUI"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["searchBy"] = "Name",
                            ["value"] = entry.Name,
                            ["scope"] = "Descendant",
                        }
                    };
                }

                // WPFSpy strategy: XPath-based (visual tree path from root window)
                if (modes == null || modes.Contains("WPFSpy"))
                {
                    strategies["WPFSpy"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["searchBy"] = "XPath",
                            ["value"] = entry.XPath ?? $"{entry.ControlType}[@Name='{entry.Name}']",
                        }
                    };
                }

                // Sikuli strategy: image-based. When a reference image was
                // captured during recording (entry.ImagePath), use that
                // path as imagePath and the alias as a stable value tag.
                // Otherwise fall back to the placeholder name that was
                // emitted before the recorder could capture.
                if (modes == null || modes.Contains("Sikuli"))
                {
                    var sikuliStrategy = new Dictionary<string, object>
                    {
                        ["searchBy"] = "Image",
                        ["value"] = entry.ImagePath
                            ?? $"sikuli/{entry.Alias.Split('.').Last().ToLower()}.png",
                    };
                    if (!string.IsNullOrEmpty(entry.ImagePath))
                    {
                        sikuliStrategy["imagePath"] = entry.ImagePath!;
                    }
                    sikuliStrategy["similarity"] = 0.85;
                    strategies["Sikuli"] = new List<object> { sikuliStrategy };
                }

                var aliasParts = entry.Alias.Split('.');
                string parentAlias = aliasParts.Length >= 2
                    ? string.Join(".", aliasParts.Take(aliasParts.Length - 1))
                    : (aliasParts[0] + ".MainWindow");

                var elementDef = new Dictionary<string, object>
                {
                    ["displayName"] = entry.DisplayName,
                    ["controlType"] = entry.ControlType,
                    ["parentAlias"] = parentAlias,
                    ["defaultTimeout"] = 10,
                    ["tags"] = entry.NonStandard
                        ? new List<string> { "recorded", "self-healing-demo" }
                        : new List<string> { "recorded" },
                    ["strategies"] = strategies,
                };

                // Include per-element driver priority if set
                if (entry.DriverPriority != null && entry.DriverPriority.Any())
                {
                    elementDef["driverPriority"] = entry.DriverPriority;
                }

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
