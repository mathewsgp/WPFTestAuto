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
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IgnoreReadOnlyProperties = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public static string Serialize(IEnumerable<RecordedStep> steps, string? driver = null, string? mode = null, string? appId = null)
        {
            var document = new ScriptDocument
            {
                SchemaVersion = 1,
                Driver = driver,
                Mode = mode,
                AppId = appId,
                Steps = steps.ToList(),
            };
            return JsonSerializer.Serialize(document, Options);
        }

        public static List<RecordedStep> Deserialize(string json)
            => DeserializeDocument(json).Steps;

        public static ScriptImportResult DeserializeDocument(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (TryGetProperty(document.RootElement, "schemaVersion", out var schemaElement)
                && schemaElement.ValueKind == JsonValueKind.Number
                && schemaElement.TryGetInt32(out int schemaVersion)
                && schemaVersion > 1)
                throw new JsonException($"Script JSON schema version {schemaVersion} is newer than this IDE supports.");

            if (!TryGetProperty(document.RootElement, "steps", out var stepsElement)
                || stepsElement.ValueKind != JsonValueKind.Array)
                throw new JsonException("A script JSON document must contain a 'steps' array.");

            var steps = new List<RecordedStep>();
            int index = 0;
            foreach (var stepElement in stepsElement.EnumerateArray())
            {
                if (stepElement.ValueKind != JsonValueKind.Object
                    || !TryGetProperty(stepElement, "kind", out _)
                    || !TryGetProperty(stepElement, "action", out _)
                    || !TryGetProperty(stepElement, "alias", out _))
                    throw new JsonException($"Script step {index + 1} must contain 'kind', 'action', and 'alias'.");

                var step = JsonSerializer.Deserialize<RecordedStep>(stepElement.GetRawText(), Options);
                if (step == null)
                    throw new JsonException($"Script step {index + 1} could not be read.");
                if (!Enum.IsDefined(step.Kind) || !Enum.IsDefined(step.Action))
                    throw new JsonException($"Script step {index + 1} contains an unknown kind or action.");
                steps.Add(step);
                index++;
            }

            return new ScriptImportResult(
                steps,
                GetOptionalString(document.RootElement, "driver"),
                GetOptionalString(document.RootElement, "mode"),
                GetOptionalString(document.RootElement, "appId"));
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
            public List<RecordedStep> Steps { get; set; } = new();
        }
    }
}
