using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using WpfTestIde.Models;

namespace WpfTestIde.Execution
{
    public static class JsonStepScript
    {
        public const int CurrentSchemaVersion = 2;

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IgnoreReadOnlyProperties = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static string Serialize(
            IEnumerable<RecordedStep> nodes,
            string? driver = null,
            string? mode = null,
            string? appId = null,
            IReadOnlyDictionary<string, List<string>>? datasets = null)
        {
            var nodeList = nodes.ToList();
            ScriptFlowValidator.Validate(nodeList, datasets);
            var document = new ScriptDocument
            {
                SchemaVersion = CurrentSchemaVersion,
                Driver = driver,
                Mode = mode,
                AppId = appId,
                Nodes = nodeList,
                Datasets = datasets == null
                    ? new Dictionary<string, List<string>>()
                    : datasets.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            };
            return JsonSerializer.Serialize(document, Options);
        }

        public static List<RecordedStep> Deserialize(string json)
            => DeserializeDocument(json).Steps;

        public static ScriptImportResult DeserializeDocument(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("A script JSON document must be an object.");

            int schemaVersion = 1;
            if (TryGetProperty(document.RootElement, "schemaVersion", out var schemaElement))
            {
                if (schemaElement.ValueKind != JsonValueKind.Number || !schemaElement.TryGetInt32(out schemaVersion))
                    throw new JsonException("'schemaVersion' must be an integer.");
            }
            if (schemaVersion < 1 || schemaVersion > CurrentSchemaVersion)
                throw new JsonException($"Script JSON schema version {schemaVersion} is not supported.");

            bool hasNodes = TryGetProperty(document.RootElement, "nodes", out var nodesElement);
            bool hasLegacySteps = TryGetProperty(document.RootElement, "steps", out var legacyStepsElement);
            if (!hasNodes && !hasLegacySteps)
                throw new JsonException("A script JSON document must contain a 'nodes' array or a legacy 'steps' array.");

            var sourceNodes = hasNodes ? nodesElement : legacyStepsElement;
            if (sourceNodes.ValueKind != JsonValueKind.Array)
                throw new JsonException("Script 'nodes' or 'steps' must be an array.");

            var nodes = new List<RecordedStep>();
            int index = 0;
            foreach (var nodeElement in sourceNodes.EnumerateArray())
            {
                if (nodeElement.ValueKind != JsonValueKind.Object
                    || !TryGetProperty(nodeElement, "kind", out _))
                    throw new JsonException($"Script node {index + 1} must contain a 'kind'.");
                if (!hasNodes
                    && (!TryGetProperty(nodeElement, "action", out _)
                        || !TryGetProperty(nodeElement, "alias", out _)))
                    throw new JsonException($"Legacy script step {index + 1} must contain 'action' and 'alias'.");

                var node = JsonSerializer.Deserialize<RecordedStep>(nodeElement.GetRawText(), Options);
                if (node == null || !Enum.IsDefined(node.Kind))
                    throw new JsonException($"Script node {index + 1} contains an unknown kind.");
                if (!hasNodes && !Enum.IsDefined(node.Action))
                    throw new JsonException($"Legacy script step {index + 1} contains an unknown action.");
                nodes.Add(node);
                index++;
            }

            var datasets = ReadDatasets(document.RootElement);
            try
            {
                ScriptFlowValidator.Validate(nodes, datasets);
            }
            catch (FormatException ex)
            {
                throw new JsonException($"Invalid script flow: {ex.Message}", ex);
            }

            return new ScriptImportResult(
                nodes,
                GetOptionalString(document.RootElement, "driver"),
                GetOptionalString(document.RootElement, "mode"),
                GetOptionalString(document.RootElement, "appId"),
                datasets);
        }

        private static Dictionary<string, List<string>> ReadDatasets(JsonElement root)
        {
            var datasets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!TryGetProperty(root, "datasets", out var datasetsElement))
                return datasets;
            if (datasetsElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("'datasets' must be an object containing string arrays.");

            foreach (var property in datasetsElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                    throw new JsonException($"Dataset '{property.Name}' must be an array.");
                var values = new List<string>();
                foreach (var value in property.Value.EnumerateArray())
                {
                    if (value.ValueKind != JsonValueKind.String)
                        throw new JsonException($"Dataset '{property.Name}' values must be strings.");
                    values.Add(value.GetString() ?? "");
                }
                if (!datasets.TryAdd(property.Name, values))
                    throw new JsonException($"Dataset '{property.Name}' is defined more than once.");
            }
            return datasets;
        }

        private static string? GetOptionalString(JsonElement element, string name) =>
            TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = property.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        private sealed class ScriptDocument
        {
            public int SchemaVersion { get; set; }
            public string? Driver { get; set; }
            public string? Mode { get; set; }
            public string? AppId { get; set; }
            public Dictionary<string, List<string>> Datasets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<RecordedStep> Nodes { get; set; } = new();
        }
    }
}
