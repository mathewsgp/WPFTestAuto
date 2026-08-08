using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace WpfTestIde.Recording
{
    /// <summary>
    /// Loads the Python framework's Element Repository YAML files and
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

            foreach (string yamlPath in Directory.EnumerateFiles(repoDir, "*.yaml"))
            {
                try
                {
                    string yaml = File.ReadAllText(yamlPath);
                    var deserializer = new DeserializerBuilder().Build();
                    
                    var root = deserializer.Deserialize<Dictionary<object, object>>(yaml);
                    if (root == null) continue;

                    try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Loaded YAML: {yamlPath}, keys: {string.Join(", ", root.Keys)}{Environment.NewLine}"); } catch { }

                    if (!root.TryGetValue("elements", out var elementsObj)) continue;

                    var elementsDict = ConvertToDictionary(elementsObj);
                    if (elementsDict == null) 
                    {
                        try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] elementsObj conversion returned null, type={elementsObj.GetType().FullName}{Environment.NewLine}"); } catch { }
                        continue;
                    }

                    try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] elements count: {elementsDict.Count}{Environment.NewLine}"); } catch { }

                    foreach (var kv in elementsDict)
                    {
                        string alias = kv.Key?.ToString() ?? "";
                        if (string.IsNullOrEmpty(alias)) continue;

                        var entry = ConvertToDictionary(kv.Value);
                        if (entry == null) 
                        {
                            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] entry conversion returned null for alias={alias}{Environment.NewLine}"); } catch { }
                            continue;
                        }

                        // Multi-app: check if element is scoped to a different app
                        if (appId != null && entry.TryGetValue("appId", out var elementAppIdObj) && elementAppIdObj is string elementAppId)
                        {
                            if (!string.Equals(elementAppId, appId, StringComparison.OrdinalIgnoreCase))
                            {
                                try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Skipping alias={alias} (appId={elementAppId} != {appId}){Environment.NewLine}"); } catch { }
                                continue;
                            }
                        }

                        try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Processing alias={alias}, entry keys={string.Join(",", entry.Keys)}{Environment.NewLine}"); } catch { }

                        // Extract AutomationId from FlaUI strategy
                        if (entry.TryGetValue("strategies", out var strategiesObj) &&
                            strategiesObj is System.Collections.Generic.IDictionary<string, object> strategies)
                        {
                            try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}]   Found strategies: {string.Join(",", strategies.Keys)}{Environment.NewLine}"); } catch { }
                            
                            string? automationId = null;
                            string? name = null;
                            
                            if (strategies.TryGetValue("FlaUI", out var flaObj) &&
                                flaObj is System.Collections.Generic.IDictionary<string, object> fla &&
                                fla.TryGetValue("value", out var flaValue) &&
                                flaValue is string flaStr &&
                                !string.IsNullOrEmpty(flaStr))
                            {
                                automationId = flaStr;
                                _byAutomationId[flaStr] = alias;
                                try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}]   Added AutomationId mapping: {flaStr} -> {alias}{Environment.NewLine}"); } catch { }
                            }

                            if (strategies.TryGetValue("WPFSpy", out var wpfObj) &&
                                wpfObj is System.Collections.Generic.IDictionary<string, object> wpf &&
                                wpf.TryGetValue("value", out var wpfValue) &&
                                wpfValue is string wpfStr &&
                                !string.IsNullOrEmpty(wpfStr))
                            {
                                name = wpfStr;
                                _byName[wpfStr] = alias;
                                try { File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}]   Added Name mapping: {wpfStr} -> {alias}{Environment.NewLine}"); } catch { }
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
                        File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] ERROR loading {yamlPath}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
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

        private static System.Collections.Generic.IDictionary<string, object>? ConvertToDictionary(object obj)
        {
            if (obj is System.Collections.Generic.IDictionary<string, object> dict)
                return dict;
            
            // Handle Dictionary<object, object> from YamlDotNet
            if (obj is System.Collections.Generic.IDictionary<object, object> objDict)
            {
                var result = new System.Collections.Generic.Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in objDict)
                {
                    string key = kv.Key?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(key))
                    {
                        result[key] = ConvertValue(kv.Value);
                    }
                }
                return result;
            }
            
            return null;
        }
        
        private static object ConvertValue(object value)
        {
            if (value is System.Collections.Generic.IDictionary<string, object> dict)
                return dict;
            
            if (value is System.Collections.Generic.IDictionary<object, object> objDict)
            {
                var result = new System.Collections.Generic.Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in objDict)
                {
                    string key = kv.Key?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(key))
                    {
                        result[key] = ConvertValue(kv.Value);
                    }
                }
                return result;
            }
            
            if (value is System.Collections.Generic.IList<object> list)
            {
                var result = new System.Collections.Generic.List<object>();
                foreach (var item in list)
                {
                    result.Add(ConvertValue(item));
                }
                return result;
            }
            
            return value;
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
