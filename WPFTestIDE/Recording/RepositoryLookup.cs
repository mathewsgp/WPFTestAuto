using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WpfTestIde.Recording
{
    /// <summary>
    /// Loads the Python framework's Element Repository JSON files and
    /// builds a lookup so the IDE can map a probed element's
    /// AutomationId / Name to the canonical repository alias.
    /// This is what makes recorded scripts play back correctly against
    /// the existing repository without manual alias reconciliation.
    /// </summary>
    public static class RepositoryLookup
    {
        private static readonly Dictionary<string, string> _byAutomationId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _byName = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, (string AutomationId, string Name, string Alias)> _allEntries = new(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        public static void EnsureLoaded(string frameworkRoot, string? appId = null)
        {
            if (_loaded) return;

            string repoDir = Path.Combine(frameworkRoot, "repository", "elements");
            string logPath = Path.Combine(frameworkRoot, "repository", "repo_lookup.log");
            try
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] EnsureLoaded: frameworkRoot={frameworkRoot}, repoDir={repoDir}, appId={appId ?? "(global)"}, exists={Directory.Exists(repoDir)}{Environment.NewLine}");
            }
            catch { }
            if (!Directory.Exists(repoDir)) return;

            foreach (string jsonPath in Directory.EnumerateFiles(repoDir, "*.json"))
            {
                try
                {
                    string json = File.ReadAllText(jsonPath);
                    using var doc = JsonDocument.Parse(json);
                    var rootElement = doc.RootElement;

                    try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Loaded JSON: {jsonPath}{Environment.NewLine}"); } catch { }

                    if (!rootElement.TryGetProperty("elements", out var elementsObj)) continue;

                    try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] elements count: {elementsObj.EnumerateObject().Count()}{Environment.NewLine}"); } catch { }

                    foreach (var kv in elementsObj.EnumerateObject())
                    {
                        string alias = kv.Name;
                        if (string.IsNullOrEmpty(alias)) continue;

                        var entry = kv.Value;

                        // Multi-app: check if element is scoped to a different app
                        if (appId != null && entry.TryGetProperty("appId", out var elementAppIdProp) && elementAppIdProp.ValueKind == JsonValueKind.String)
                        {
                            string elementAppId = elementAppIdProp.GetString()!;
                            if (!string.Equals(elementAppId, appId, StringComparison.OrdinalIgnoreCase))
                            {
                                try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Skipping alias={alias} (appId={elementAppId} != {appId}){Environment.NewLine}"); } catch { }
                                continue;
                            }
                        }

                        try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Processing alias={alias}{Environment.NewLine}"); } catch { }

                        // Extract AutomationId from FlaUI strategy
                        if (entry.TryGetProperty("strategies", out var strategiesObj) && strategiesObj.ValueKind == JsonValueKind.Object)
                        {
                            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}]   Found strategies{Environment.NewLine}"); } catch { }

                            string? automationId = null;
                            string? name = null;

                            if (strategiesObj.TryGetProperty("FlaUI", out var flaObj) && flaObj.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var strategy in flaObj.EnumerateArray())
                                {
                                    if (strategy.TryGetProperty("searchBy", out var sbProp) && sbProp.GetString() == "AutomationId"
                                        && strategy.TryGetProperty("value", out var valProp) && valProp.ValueKind == JsonValueKind.String)
                                    {
                                        automationId = valProp.GetString();
                                        if (!string.IsNullOrEmpty(automationId))
                                        {
                                            _byAutomationId[automationId] = alias;
                                            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}]   Added AutomationId mapping: {automationId} -> {alias}{Environment.NewLine}"); } catch { }
                                        }
                                        break;
                                    }
                                }
                            }

                            if (strategiesObj.TryGetProperty("WPFSpy", out var wpfObj) && wpfObj.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var strategy in wpfObj.EnumerateArray())
                                {
                                    if (strategy.TryGetProperty("searchBy", out var sbProp) && sbProp.GetString() == "XPath"
                                        && strategy.TryGetProperty("value", out var valProp) && valProp.ValueKind == JsonValueKind.String)
                                    {
                                        name = valProp.GetString();
                                        if (!string.IsNullOrEmpty(name))
                                        {
                                            _byName[name] = alias;
                                            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}]   Added Name mapping: {name} -> {alias}{Environment.NewLine}"); } catch { }
                                        }
                                        break;
                                    }
                                }
                            }

                            if (automationId != null || name != null)
                            {
                                _allEntries[alias] = (automationId ?? "", name ?? "", alias);
                            }
                        }
                        else
                        {
                            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}]   NO strategies found for alias={alias}{Environment.NewLine}"); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] ERROR loading {jsonPath}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
                    }
                    catch { }
                }
            }

            _loaded = true;
            try
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Loaded {_byAutomationId.Count} AutomationId mappings, {_byName.Count} Name mappings{Environment.NewLine}");
            }
            catch { }
        }

        /// <summary>
        /// Returns the repository alias for the given AutomationId or Name,
        /// or null if no matching repository entry exists.
        /// </summary>
        public static string? ResolveAlias(string? automationId, string? name)
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "repository", "repo_lookup.log");
            if (!_loaded)
            {
                try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] ResolveAlias called but not loaded!{Environment.NewLine}"); } catch { }
                return null;
            }

            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] ResolveAlias(automationId={automationId}, name={name}){Environment.NewLine}"); } catch { }

            if (!string.IsNullOrEmpty(automationId) && _byAutomationId.TryGetValue(automationId, out var byAuto))
            {
                try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}]   -> matched by AutomationId: {byAuto}{Environment.NewLine}"); } catch { }
                return byAuto;
            }

            if (!string.IsNullOrEmpty(name) && _byName.TryGetValue(name, out var byName))
            {
                try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}]   -> matched by Name: {byName}{Environment.NewLine}"); } catch { }
                return byName;
            }

            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}]   -> NO MATCH{Environment.NewLine}"); } catch { }
            return null;
        }
    }
}
