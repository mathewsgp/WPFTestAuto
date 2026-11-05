using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using WpfTestIde.Models;

namespace WpfTestIde.Recording
{
    /// <summary>
    /// Serializes discovered ElementEntry objects into the exact JSON
    /// schema used by repository/elements/*.json in the Python framework
    /// (see docs/ELEMENT_REPOSITORY_GUIDE.md) — the C# equivalent of
    /// recorder/converter.py's generate_element_repository. Built as
    /// plain Dictionary trees so the emitted JSON keys match the schema exactly.
    /// </summary>
    public static class RepositoryWriter
    {
        /// <summary>
        /// Generates JSON for the element repository, including only
        /// strategies for the selected recording modes.
        /// </summary>
        /// <param name="entries">Discovered element entries.</param>
        /// <param name="recordingModes">Selected recording modes (UIA3, WPFSpy, Sikuli).
        /// If empty or null, all strategies are included (backward compatible).</param>
        public static string GenerateJson(IEnumerable<ElementEntry> entries, List<string>? recordingModes = null)
        {
            var elements = new Dictionary<string, object>();

            foreach (var entry in entries)
            {
                var strategies = new Dictionary<string, List<object>>();

                // Use per-element recording modes if available, otherwise fall back to global modes.
                // If the element was resolved via UIA3 probe, always include a UIA3 strategy
                // (even if global recording modes don't include UIA3) — otherwise the recorded
                // step will fail to find the element at replay time.
                var modes = entry.RecordingModes ?? recordingModes;
                bool resolvedViaUIA3 = entry.NonStandard == false && !string.IsNullOrEmpty(entry.XPath) == false;
                bool resolvedViaWPFSpy = !string.IsNullOrEmpty(entry.XPath) && entry.XPath!.Contains("[@AutomationId=");
                // Heuristic: elements with an XPath always get an XPath-based UIA3 strategy
                // as the default (mirrors the WPFSpy default), so the recorded step is
                // symmetric across drivers and stable when AutomationId is missing or
                // collides. AutomationId/Name are kept as fallbacks.
                bool includeUIA3 = (modes == null || modes.Contains("UIA3"))
                    || !string.IsNullOrEmpty(entry.AutomationId)
                    || entry.ControlType == "Window"
                    || (!string.IsNullOrEmpty(entry.Name) && entry.Name.Length > 0);

                if (includeUIA3 && !string.IsNullOrEmpty(entry.XPath))
                {
                    // Default UIA3 strategy: XPath (UIA control types, built by
                    // UIA3ElementProbe.BuildXPath — already compatible with UIA3).
                    var uia3Strategies = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["searchBy"] = "XPath",
                            ["value"] = entry.XPath!,
                        }
                    };
                    if (!string.IsNullOrEmpty(entry.AutomationId))
                    {
                        uia3Strategies.Add(new Dictionary<string, object>
                        {
                            ["searchBy"] = "AutomationId",
                            ["value"] = entry.AutomationId!,
                            ["scope"] = "Descendant",
                        });
                    }
                    if (!string.IsNullOrEmpty(entry.Name) && entry.Name != entry.AutomationId)
                    {
                        uia3Strategies.Add(new Dictionary<string, object>
                        {
                            ["searchBy"] = "Name",
                            ["value"] = entry.Name,
                            ["scope"] = "Descendant",
                        });
                    }
                    strategies["UIA3"] = uia3Strategies;
                }
                else if (includeUIA3 && !string.IsNullOrEmpty(entry.AutomationId))
                {
                    strategies["UIA3"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["searchBy"] = "AutomationId",
                            ["value"] = entry.AutomationId!,
                            ["scope"] = "Descendant",
                        }
                    };
                }
                else if (includeUIA3 && !string.IsNullOrEmpty(entry.Name))
                {
                    strategies["UIA3"] = new List<object>
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

                // OculiX strategy: image-based. When a reference image was
                // captured during recording (entry.ImagePath), use that
                // path as imagePath and the alias as a stable value tag.
                // Otherwise fall back to the placeholder name that was
                // emitted before the recorder could capture.
                if (modes == null || modes.Contains("OculiX"))
                {
                    var oculixStrategy = new Dictionary<string, object>
                    {
                        ["searchBy"] = "Image",
                        ["value"] = entry.ImagePath
                            ?? $"okulix/{entry.Alias.Split('.').Last().ToLower()}.png",
                    };
                    if (!string.IsNullOrEmpty(entry.ImagePath))
                    {
                        oculixStrategy["imagePath"] = entry.ImagePath!;
                    }
                    oculixStrategy["similarity"] = 0.85;
                    strategies["OculiX"] = new List<object> { oculixStrategy };
                }

                var aliasParts = entry.Alias.Split('.');
                string parentAlias = aliasParts.Length >= 2
                    ? string.Join(".", aliasParts.Take(aliasParts.Length - 1))
                    : (aliasParts[0] + ".MainWindow");

                var elementDef = new Dictionary<string, object>
                {
                    ["displayName"] = entry.DisplayName,
                    ["controlType"] = entry.ControlType,
                };

                if (!string.IsNullOrEmpty(entry.AutomationId))
                {
                    elementDef["automationId"] = entry.AutomationId!;
                }
                if (!string.IsNullOrEmpty(entry.Name))
                {
                    elementDef["name"] = entry.Name;
                }
                if (!string.IsNullOrEmpty(entry.XPath))
                {
                    elementDef["relativeXPath"] = entry.XPath!;
                }

                elementDef["parentAlias"] = parentAlias;
                elementDef["defaultTimeout"] = 10;
                elementDef["tags"] = entry.NonStandard
                    ? new List<string> { "recorded", "self-healing-demo" }
                    : new List<string> { "recorded" };
                elementDef["strategies"] = strategies;

                // Include per-element driver priority if set
                if (entry.DriverPriority != null && entry.DriverPriority.Any())
                {
                    elementDef["driverPriority"] = entry.DriverPriority;
                }

                elements[entry.Alias] = elementDef;
            }

            var root = new Dictionary<string, object> { ["elements"] = elements };

            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(root, options);
        }

        /// <summary>Deduplicates a raw step/entry stream by alias, keeping
        /// the first-seen entry for each (matches how the real recorder
        /// treats repeated interactions with the same control).</summary>
        public static List<ElementEntry> Deduplicate(IEnumerable<ElementEntry> entries) =>
            entries.GroupBy(e => e.Alias).Select(g => g.First()).ToList();
    }
}
