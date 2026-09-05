using System;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

class Program
{
    static void Main()
    {
        string yamlPath = @"D:\testpgms\WPFTestAutoClaudeNew\WpfTestFramework\repository\elements\login_page.yaml";
        string yaml = File.ReadAllText(yamlPath);
        
        var deserializer = new DeserializerBuilder().Build();
        var root = deserializer.Deserialize<Dictionary<object, object>>(yaml);
        
        Console.WriteLine("Root keys: " + string.Join(", ", root.Keys));
        
        if (root.TryGetValue("elements", out var elementsObj))
        {
            var elementsDict = ConvertToDictionary(elementsObj);
            Console.WriteLine("Elements count: " + elementsDict.Count);
            
            foreach (var kv in elementsDict)
            {
                Console.WriteLine("Alias: " + kv.Key);
                var entry = ConvertToDictionary(kv.Value);
                if (entry != null)
                {
                    Console.WriteLine("  Entry keys: " + string.Join(", ", entry.Keys));
                    if (entry.TryGetValue("strategies", out var strategiesObj))
                    {
                        var strategies = ConvertToDictionary(strategiesObj);
                        Console.WriteLine("  Strategies: " + string.Join(", ", strategies.Keys));
                        
                        if (strategies.TryGetValue("FlaUI", out var flaObj))
                        {
                            var fla = ConvertToDictionary(flaObj);
                            Console.WriteLine("    FlaUI value: " + fla.GetValueOrDefault("value"));
                        }
                        
                        if (strategies.TryGetValue("WPFSpy", out var wpfObj))
                        {
                            var wpf = ConvertToDictionary(wpfObj);
                            Console.WriteLine("    WPFSpy value: " + wpf.GetValueOrDefault("value"));
                        }
                    }
                }
            }
        }
    }
    
    static Dictionary<string, object>? ConvertToDictionary(object obj)
    {
        if (obj is Dictionary<string, object> dict)
            return dict;
        
        if (obj is Dictionary<object, object> objDict)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
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
    
    static object ConvertValue(object value)
    {
        if (value is Dictionary<string, object> dict)
            return dict;
        
        if (value is Dictionary<object, object> objDict)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
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
        
        if (value is List<object> list)
        {
            var result = new List<object>();
            foreach (var item in list)
            {
                result.Add(ConvertValue(item));
            }
            return result;
        }
        
        return value;
    }
}
