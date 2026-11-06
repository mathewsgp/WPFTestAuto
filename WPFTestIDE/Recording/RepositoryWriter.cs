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
        /// <param name="recordingModes">Selected recording modes (UIA3, WPFSpy, OculiX).
        /// If empty or null, all strategies are included (backward compatible).</param>
        public static string GenerateJson(IEnumerable<ElementEntry> entries, List<string>? recordingModes = null)
        {
            var elements = new Dictionary<string, object>();

            foreach (var entry in entries)
            {
                var strategies = new Dictionary<string, List<object>>();

                // Use per-element recording modes if available, otherwise fall back to global modes.
                // Prefer per-driver captured probe data so each driver gets its own XPath/properties.
                var modes = entry.RecordingModes ?? recordingModes;
                var driverProbes = entry.DriverProbes ?? new Dictionary<string, DriverProbeData>();
                bool includeUIA3 = modes == null || modes.Contains("UIA3") || driverProbes.ContainsKey("UIA3");
                bool includeWPFSpy = modes == null || modes.Contains("WPFSpy") || driverProbes.ContainsKey("WPFSpy");

                if (includeUIA3)
                {
                    if (driverProbes.TryGetValue("UIA3", out var uiaData))
                    {
                        var uia = uiaData;
                        var uia3Strategies = new List<object>();
                        if (!string.IsNullOrEmpty(uia.XPath))
                        {
                            uia3Strategies.Add(new Dictionary<string, object>
                            {
                                ["searchBy"] = "XPath",
                                ["value"] = uia.XPath!,
                            });
                        }
                        if (!string.IsNullOrEmpty(uia.AutomationId))
                        {
                            uia3Strategies.Add(new Dictionary<string, object>
                            {
                                ["searchBy"] = "AutomationId",
                                ["value"] = uia.AutomationId!,
                                ["scope"] = "Descendant",
                            });
                        }
                        if (!string.IsNullOrEmpty(uia.Name) && uia.Name != uia.AutomationId)
                        {
                            uia3Strategies.Add(new Dictionary<string, object>
                            {
                                ["searchBy"] = "Name",
                                ["value"] = uia.Name,
                                ["scope"] = "Descendant",
                            });
                        }
                        if (uia3Strategies.Count > 0)
                            strategies["UIA3"] = uia3Strategies;
                    }
                    else if (modes == null || modes.Contains("UIA3"))
                    {
                        // Legacy/shared fallback only when UIA3 is selected and no
                        // per-driver probe data exists (older recordings / non-split path).
                        var uia = new DriverProbeData
                        {
                            AutomationId = entry.AutomationId,
                            Name = entry.Name,
                            ControlType = entry.ControlType,
                            XPath = entry.XPath,
                        };
                        if (!string.IsNullOrEmpty(uia.XPath) || !string.IsNullOrEmpty(uia.AutomationId) || !string.IsNullOrEmpty(uia.Name))
                        {
                            var uia3Strategies = new List<object>();
                            if (!string.IsNullOrEmpty(uia.XPath))
                                uia3Strategies.Add(new Dictionary<string, object> { ["searchBy"] = "XPath", ["value"] = uia.XPath! });
                            if (!string.IsNullOrEmpty(uia.AutomationId))
                                uia3Strategies.Add(new Dictionary<string, object> { ["searchBy"] = "AutomationId", ["value"] = uia.AutomationId!, ["scope"] = "Descendant" });
                            if (!string.IsNullOrEmpty(uia.Name) && uia.Name != uia.AutomationId)
                                uia3Strategies.Add(new Dictionary<string, object> { ["searchBy"] = "Name", ["value"] = uia.Name, ["scope"] = "Descendant" });
                            if (uia3Strategies.Count > 0)
                                strategies["UIA3"] = uia3Strategies;
                        }
                    }
                }

                // WPFSpy strategy: prefer separately captured WPFSpy probe data.
                // Do not fabricate WPFSpy XPath from UIA3/shared data when both
                // drivers were selected but only UIA3 probe data was captured.
                if (includeWPFSpy)
                {
                    if (driverProbes.TryGetValue("WPFSpy", out var wpfData))
                    {
                        var wpf = wpfData;
                        string? wpfValue = !string.IsNullOrEmpty(wpf.XPath)
                            ? wpf.XPath
                            : $"{(string.IsNullOrEmpty(wpf.ControlType) ? entry.ControlType : wpf.ControlType)}[@Name='{(string.IsNullOrEmpty(wpf.Name) ? entry.Name : wpf.Name)}']";
                        strategies["WPFSpy"] = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                ["searchBy"] = "XPath",
                                ["value"] = wpfValue!,
                            }
                        };
                    }
                    else if ((modes == null || modes.Contains("WPFSpy")) && !driverProbes.ContainsKey("UIA3"))
                    {
                        // Legacy/shared fallback only when no split probe data exists.
                        string? wpfValue = !string.IsNullOrEmpty(entry.XPath)
                            ? entry.XPath
                            : $"{entry.ControlType}[@Name='{entry.Name}']";
                        strategies["WPFSpy"] = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                ["searchBy"] = "XPath",
                                ["value"] = wpfValue!,
                            }
                        };
                    }
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
                if (entry.AppId != null) elementDef["appId"] = entry.AppId;
                if (entry.ImagePath != null) elementDef["imagePath"] = entry.ImagePath;
                // relativeXPath is redundant with strategy locators; omit from canonical JSON. Legacy files/import remain tolerant.

                    // Prefer explicit editable strategy groups when present; otherwise derive from probes/fields.
                    if (entry.StrategyGroups != null && entry.StrategyGroups.Count > 0)
                    {
                        strategies.Clear();
                        foreach (var g in entry.StrategyGroups.Where(g => g.IsEnabled && !string.IsNullOrWhiteSpace(g.Driver)))
                        {
                            var rows = g.Items
                                .Where(i => !string.IsNullOrWhiteSpace(i.SearchBy) && !string.IsNullOrWhiteSpace(i.Value))
                                .Select(i =>
                                {
                                    var o = new Dictionary<string, object> { ["searchBy"] = i.SearchBy, ["value"] = i.Value };
                                    if (!string.IsNullOrWhiteSpace(i.Scope)) o["scope"] = i.Scope!;
                                    if (!string.IsNullOrWhiteSpace(i.ImagePath)) o["imagePath"] = i.ImagePath!;
                                    if (i.Similarity.HasValue) o["similarity"] = i.Similarity.Value;
                                    return (object)o;
                                }).ToList();
                            if (rows.Count > 0) strategies[g.Driver] = rows;
                        }
                    }

                    // driverProbes is IDE capture/debug metadata (per-driver raw probe snapshots).
                    // Runtime consumers use strategies; do not emit driverProbes to avoid duplication/drift.
                    // Legacy files containing driverProbes remain tolerated by readers that ignore unknown fields.

                    elementDef["parentAlias"] = parentAlias;
                    elementDef["defaultTimeout"] = 10;
                    elementDef["tags"] = entry.NonStandard
                        ? new List<string> { "recorded", "self-healing-demo" }
                        : new List<string> { "recorded" };
                    // Reorder strategies according to entry.StrategyOrder (if provided).
                    var orderedStrategies = new Dictionary<string, List<object>>();
                    var orderSource = entry.StrategyOrder != null && entry.StrategyOrder.Any()
                        ? entry.StrategyOrder
                        : (entry.StrategyGroups != null && entry.StrategyGroups.Any(g => g.IsEnabled)
                            ? entry.StrategyGroups.Where(g => g.IsEnabled).Select(g => g.Driver).ToList()
                            : new List<string> { "UIA3", "WPFSpy", "OculiX" });
                    foreach (var key in orderSource)
                    {
                        if (strategies.TryGetValue(key, out var val))
                            orderedStrategies[key] = val;
                    }
                    foreach (var kv in strategies)
                    {
                        if (!orderedStrategies.ContainsKey(kv.Key))
                            orderedStrategies[kv.Key] = kv.Value;
                    }
                    elementDef["strategies"] = orderedStrategies;

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
