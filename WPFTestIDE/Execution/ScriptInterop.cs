using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using WpfTestIde.Models;

namespace WpfTestIde.Execution
{
    public sealed record ScriptImportResult(
        List<RecordedStep> Steps,
        string? Driver = null,
        string? Mode = null,
        string? AppId = null);

    /// <summary>Parses the IDE's common step subset from its JSON, Python, and Robot views.</summary>
    public static class ScriptInterop
    {
        private static readonly Regex PythonCallPattern = new(
            @"^api\.(?<name>[A-Za-z_]\w*)\s*\((?<arguments>.*)\)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static ScriptImportResult Parse(ScriptFormat format, string text)
        {
            var imported = format switch
            {
                ScriptFormat.Json => JsonStepScript.DeserializeDocument(text),
                ScriptFormat.Python => ParsePython(text),
                ScriptFormat.Robot => ParseRobot(text),
                _ => throw new FormatException($"The {format} view cannot be imported as a script.")
            };
            ApplyDefaultAppId(imported.Steps, imported.AppId);
            return imported;
        }

        private static ScriptImportResult ParsePython(string text)
        {
            var steps = new List<RecordedStep>();
            string? driver = null;
            string? mode = null;
            string? appId = null;
            string? generatedFunction = null;
            bool insideGeneratedFunction = false;
            bool sawApiInitialization = false;
            bool sawFunction = false;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                if (line.StartsWith("import ", StringComparison.Ordinal)
                    || line.StartsWith("from ", StringComparison.Ordinal))
                    continue;
                if (line.StartsWith("api = DriverAgnosticApi", StringComparison.Ordinal))
                {
                    sawApiInitialization = true;
                    continue;
                }
                if (line.StartsWith("def ", StringComparison.Ordinal))
                {
                    var match = Regex.Match(line, @"^def\s+(?<name>[A-Za-z_]\w*)\s*\(");
                    if (match.Success)
                    {
                        generatedFunction = match.Groups["name"].Value;
                        insideGeneratedFunction = true;
                        sawFunction = true;
                        continue;
                    }
                }
                if (line.StartsWith("if __name__", StringComparison.Ordinal))
                    continue;
                if (generatedFunction != null && line == generatedFunction + "()")
                    continue;
                if (line.StartsWith("\"\"\"", StringComparison.Ordinal)
                    || line.StartsWith("'''", StringComparison.Ordinal))
                    continue;

                var callMatch = PythonCallPattern.Match(line);
                if (!callMatch.Success)
                {
                    if (line.StartsWith("api.", StringComparison.Ordinal) || insideGeneratedFunction)
                        throw new FormatException($"Unsupported Python statement on line {index + 1}: {line}");
                    throw new FormatException($"Unsupported Python statement on line {index + 1}: {line}");
                }

                var call = ParsePythonCall(callMatch.Groups["name"].Value, callMatch.Groups["arguments"].Value, index + 1);
                switch (call.Name)
                {
                    case "set_mode_and_driver":
                        mode = PositionalString(call, 0);
                        driver = PositionalString(call, 1);
                        continue;
                    case "set_mode":
                        mode = PositionalString(call, 0);
                        continue;
                    case "set_driver":
                        driver = PositionalString(call, 0);
                        continue;
                    case "attach_to_application":
                        appId = PositionalString(call, 0);
                        continue;
                }

                steps.Add(ParsePythonStep(call, index + 1));
            }

            if (steps.Count == 0 && !(sawApiInitialization && sawFunction))
                throw new FormatException("No supported DriverAgnosticApi step calls were found in the Python script.");

            return new ScriptImportResult(steps, driver, mode, appId);
        }

        private static ScriptImportResult ParseRobot(string text)
        {
            var steps = new List<RecordedStep>();
            string? driver = null;
            string? mode = null;
            string? appId = null;
            string section = "";
            bool sawTestCase = false;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string rawLine = lines[index];
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;
                if (line.StartsWith("***", StringComparison.Ordinal) && line.EndsWith("***", StringComparison.Ordinal))
                {
                    section = line.Trim('*', ' ').ToUpperInvariant();
                    continue;
                }
                if (section == "SETTINGS")
                {
                    ParseRobotSetting(line, ref driver, ref mode, ref appId);
                    continue;
                }
                if (section != "TEST CASES")
                    continue;
                if (line.StartsWith("[Documentation]", StringComparison.OrdinalIgnoreCase))
                    continue;

                var cells = SplitRobotCells(rawLine);
                if (cells.Length < 2)
                {
                    if (line.Length > 0)
                        sawTestCase = true;
                    continue; // Test case name or a free-standing metadata line.
                }

                var keyword = cells[0].Trim();
                if (TryParseRobotSetupKeyword(cells, ref driver, ref mode, ref appId))
                    continue;

                try
                {
                    steps.Add(ParseRobotStep(keyword, cells.Skip(1).ToArray(), index + 1));
                }
                catch (KeyNotFoundException)
                {
                    throw new FormatException($"Unsupported Robot keyword on line {index + 1}: {keyword}");
                }
            }

            if (steps.Count == 0 && !sawTestCase)
                throw new FormatException("No supported Robot Framework step keywords were found in the script.");

            return new ScriptImportResult(steps, driver, mode, appId);
        }

        private static void ParseRobotSetting(string line, ref string? driver, ref string? mode, ref string? appId)
        {
            var cells = SplitRobotCells(line);
            if (cells.Length < 2 || !cells[0].Equals("Test Setup", StringComparison.OrdinalIgnoreCase))
                return;

            var setup = cells[1].Trim();
            if (setup.Equals("Set Mode And Driver", StringComparison.OrdinalIgnoreCase) && cells.Length >= 4)
            {
                mode = cells[2];
                driver = cells[3];
            }
            else if (setup.Equals("Set Mode", StringComparison.OrdinalIgnoreCase) && cells.Length >= 3)
            {
                mode = cells[2];
            }
            else if (setup.Equals("Set Driver", StringComparison.OrdinalIgnoreCase) && cells.Length >= 3)
            {
                driver = cells[2];
            }
            else if (setup.Equals("Attach To Application", StringComparison.OrdinalIgnoreCase) && cells.Length >= 3)
            {
                appId = cells[2];
            }
        }

        private static bool TryParseRobotSetupKeyword(string[] cells, ref string? driver, ref string? mode, ref string? appId)
        {
            if (cells[0].Equals("Set Mode And Driver", StringComparison.OrdinalIgnoreCase) && cells.Length >= 3)
            {
                mode = cells[1];
                driver = cells[2];
                return true;
            }
            if (cells[0].Equals("Set Mode", StringComparison.OrdinalIgnoreCase) && cells.Length >= 2)
            {
                mode = cells[1];
                return true;
            }
            if (cells[0].Equals("Set Driver", StringComparison.OrdinalIgnoreCase) && cells.Length >= 2)
            {
                driver = cells[1];
                return true;
            }
            if (cells[0].Equals("Attach To Application", StringComparison.OrdinalIgnoreCase) && cells.Length >= 2)
            {
                appId = cells[1];
                return true;
            }
            return false;
        }

        private static RecordedStep ParseRobotStep(string keyword, string[] args, int lineNumber)
        {
            int requiredPositionals = RobotPositionalArgumentCount(keyword);
            var named = ParseRobotNamedArguments(args.Skip(requiredPositionals));
            // A value can legitimately look like "app_id=value". Preserve
            // required positional cells before recognizing named arguments.
            var positional = args.Take(requiredPositionals)
                .Concat(args.Skip(requiredPositionals).Where(arg => !IsRobotNamedArgument(arg)))
                .ToList();
            bool hasVariableArguments = keyword.Trim().Equals("LAUNCH APPLICATION", StringComparison.OrdinalIgnoreCase)
                || keyword.Trim().Equals("TERMINATE APPLICATION", StringComparison.OrdinalIgnoreCase);
            int maxPositionals = keyword.Trim().ToUpperInvariant() switch
            {
                "WAIT UNTIL ELEMENT EXISTS" or "WAIT UNTIL ELEMENT VISIBLE" or "WAIT UNTIL ELEMENT ENABLED" => 2,
                _ => requiredPositionals,
            };
            if (!hasVariableArguments && (positional.Count < requiredPositionals || positional.Count > maxPositionals))
                throw new FormatException($"Robot keyword '{keyword}' has an unsupported number of arguments on line {lineNumber}.");
            string alias = Positional(positional, 0);
            string? value = OptionalPositional(positional, 1);
            string? appId = Named(named, "app_id");

            RecordedStep Action(ActionKind action) => new()
            {
                Kind = StepKind.Action,
                Action = action,
                Alias = alias,
                Value = value,
                AppId = appId,
            };

            return keyword.Trim().ToUpperInvariant() switch
            {
                "CLICK ELEMENT" => Action(ActionKind.Invoke),
                "SET ELEMENT VALUE" => Action(ActionKind.SetValue),
                "TOGGLE ELEMENT" => Action(ActionKind.Toggle),
                "DOUBLE CLICK ELEMENT" => Action(ActionKind.DoubleClick),
                "RIGHT CLICK ELEMENT" => Action(ActionKind.RightClick),
                "DRAG AND DROP" => new RecordedStep { Kind = StepKind.Action, Action = ActionKind.DragDrop, Alias = alias, TargetAlias = value, AppId = appId },
                "HOVER OVER ELEMENT" => Action(ActionKind.Hover),
                "PRESS KEYS" => Action(ActionKind.PressKeys),
                "SCROLL" => Action(ActionKind.Scroll),
                "OCULIX CLICK" or "SIKULI CLICK" => Action(ActionKind.SikuliClick),
                "OCULIX TYPE" or "SIKULI TYPE" => Action(ActionKind.SikuliType),
                "VERIFY ELEMENT TEXT" => Verify(StepKind.Verify, alias, value, appId),
                "GET DATA GRID CONTENT OCR" => Verify(StepKind.VerifyOcr, alias, null, appId),
                "VERIFY ELEMENT ENABLED" => Verify(StepKind.VerifyEnabled, alias, null, appId),
                "VERIFY ELEMENT VISIBLE" => Verify(StepKind.VerifyVisible, alias, null, appId),
                "VERIFY ELEMENT CONTAINS TEXT" => Verify(StepKind.VerifyContains, alias, value, appId),
                "VERIFY ELEMENT TEXT MATCHES REGEX" => Verify(StepKind.VerifyRegex, alias, value, appId),
                "VERIFY ELEMENT ATTRIBUTE" => VerifyAttribute(StepKind.VerifyAttribute, positional, appId),
                "WAIT UNTIL ELEMENT EXISTS" => Wait(StepKind.WaitExists, alias, Named(named, "timeout") ?? OptionalPositional(positional, 1), appId),
                "WAIT UNTIL ELEMENT VISIBLE" => Wait(StepKind.WaitVisible, alias, Named(named, "timeout") ?? OptionalPositional(positional, 1), appId),
                "WAIT UNTIL ELEMENT ENABLED" => Wait(StepKind.WaitEnabled, alias, Named(named, "timeout") ?? OptionalPositional(positional, 1), appId),
                "WAIT UNTIL ELEMENT TEXT CONTAINS" or "WAIT UNTIL TEXT CONTAINS" => Wait(StepKind.WaitTextContains, alias, value, appId),
                "PROPERTY CHECKPOINT" => new RecordedStep { Kind = StepKind.CheckpointProperty, Alias = alias, PropertyName = value, Value = OptionalPositional(positional, 2), AppId = appId },
                "AREA CHECKPOINT" => Verify(StepKind.CheckpointArea, alias, value, appId),
                "IMAGE CHECKPOINT" => Verify(StepKind.CheckpointImage, alias, value, appId),
                "DATA GRID CHECKPOINT" => Verify(StepKind.CheckpointDataGrid, alias, value, appId),
                "COUNT CHECKPOINT" => new RecordedStep { Kind = StepKind.CheckpointCount, Alias = alias, ExpectedCount = value, AppId = appId },
                "ATTRIBUTE CHECKPOINT" => VerifyAttribute(StepKind.CheckpointAttribute, positional, appId),
                "LAUNCH APPLICATION" => ParseRobotLaunchApplication(positional, named),
                "TERMINATE APPLICATION" => ParseRobotTerminateApplication(positional, named),
                _ => throw new KeyNotFoundException($"Unknown keyword at line {lineNumber}.")
            };
        }

        private static RecordedStep ParseRobotLaunchApplication(IReadOnlyList<string> positional, IReadOnlyDictionary<string, string> named)
        {
            var driverList = ParseDriverList(Named(named, "drivers") ?? OptionalPositional(positional, 4));
            return new RecordedStep
            {
                Kind = StepKind.LaunchApplication,
                AppPath = Positional(positional, 0),
                AppId = Named(named, "app_id") ?? OptionalPositional(positional, 1),
                StartIn = Named(named, "start_in") ?? OptionalPositional(positional, 3),
                Args = Named(named, "args") ?? OptionalPositional(positional, 2),
                LaunchTimeout = Named(named, "timeout") ?? OptionalPositional(positional, 6),
                AutoAttach = IsTrue(Named(named, "attach") ?? OptionalPositional(positional, 5)),
                LaunchDriver = Named(named, "driver") ?? driverList?.FirstOrDefault() ?? "WPFSpy",
                LaunchDriverList = driverList,
                SpyAgentEnabled = !string.Equals(Named(named, "spy_agent_enabled") ?? Named(named, "spy_agent") ?? OptionalPositional(positional, 7), "No", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Named(named, "spy_agent_enabled") ?? Named(named, "spy_agent") ?? OptionalPositional(positional, 7), "False", StringComparison.OrdinalIgnoreCase),
                PipeName = Named(named, "pipe_name") ?? OptionalPositional(positional, 8),
            };
        }

        private static RecordedStep ParseRobotTerminateApplication(IReadOnlyList<string> positional, IReadOnlyDictionary<string, string> named)
        {
            return new RecordedStep
            {
                Kind = StepKind.TerminateApplication,
                AppId = Named(named, "app_id") ?? OptionalPositional(positional, 0),
                WindowTitle = Named(named, "window_title") ?? OptionalPositional(positional, 1),
                ProcessName = Named(named, "process_name") ?? OptionalPositional(positional, 2),
                ForceTerminate = IsTrue(Named(named, "force") ?? OptionalPositional(positional, 3)),
            };
        }

        private static RecordedStep ParsePythonStep(PythonCall call, int lineNumber)
        {
            string alias = PositionalString(call, 0) ?? "";
            string? value = OptionalPositionalString(call, 1);
            string? appId = KeywordString(call, "app_id");
            RecordedStep Action(ActionKind action) => new() { Kind = StepKind.Action, Action = action, Alias = alias, Value = value, AppId = appId };
            RecordedStep Verify(StepKind kind) => new() { Kind = kind, Alias = alias, Value = value, AppId = appId };

            return call.Name switch
            {
                "click_element" => Action(ActionKind.Invoke),
                "set_element_value" => Action(ActionKind.SetValue),
                "toggle_element" => Action(ActionKind.Toggle),
                "double_click_element" => Action(ActionKind.DoubleClick),
                "right_click_element" => Action(ActionKind.RightClick),
                "drag_and_drop" => new RecordedStep { Kind = StepKind.Action, Action = ActionKind.DragDrop, Alias = alias, TargetAlias = value, AppId = appId },
                "hover_over_element" => Action(ActionKind.Hover),
                "press_keys" => Action(ActionKind.PressKeys),
                "scroll" => Action(ActionKind.Scroll),
                "sikuli_click" => Action(ActionKind.SikuliClick),
                "sikuli_type" => Action(ActionKind.SikuliType),
                "verify_element_text" => Verify(StepKind.Verify),
                "get_data_grid_content_ocr" => Verify(StepKind.VerifyOcr),
                "verify_element_enabled" => Verify(StepKind.VerifyEnabled),
                "verify_element_visible" => Verify(StepKind.VerifyVisible),
                "verify_element_contains_text" => Verify(StepKind.VerifyContains),
                "verify_element_text_matches_regex" => Verify(StepKind.VerifyRegex),
                "verify_element_attribute" => new RecordedStep { Kind = StepKind.VerifyAttribute, Alias = alias, AttributeName = value, Value = OptionalPositionalString(call, 2), AppId = appId },
                "wait_until_element_exists" => Wait(StepKind.WaitExists, alias, KeywordString(call, "timeout"), appId),
                "wait_until_element_visible" => Wait(StepKind.WaitVisible, alias, KeywordString(call, "timeout"), appId),
                "wait_until_element_enabled" => Wait(StepKind.WaitEnabled, alias, KeywordString(call, "timeout"), appId),
                "wait_until_element_text_contains" => Verify(StepKind.WaitTextContains),
                "property_checkpoint" => new RecordedStep { Kind = StepKind.CheckpointProperty, Alias = alias, PropertyName = value, Value = OptionalPositionalString(call, 2), AppId = appId },
                "area_checkpoint" => Verify(StepKind.CheckpointArea),
                "image_checkpoint" => Verify(StepKind.CheckpointImage),
                "data_grid_checkpoint" => Verify(StepKind.CheckpointDataGrid),
                "count_checkpoint" => new RecordedStep { Kind = StepKind.CheckpointCount, Alias = alias, ExpectedCount = value, AppId = appId },
                "attribute_checkpoint" => new RecordedStep { Kind = StepKind.CheckpointAttribute, Alias = alias, AttributeName = value, Value = OptionalPositionalString(call, 2), AppId = appId },
                "launch_application" => ParsePythonLaunchApplication(call),
                "terminate_application" => ParsePythonTerminateApplication(call),
                _ => throw new FormatException($"Unsupported DriverAgnosticApi call '{call.Name}' on Python line {lineNumber}.")
            };
        }

        private static RecordedStep ParsePythonLaunchApplication(PythonCall call)
        {
            return new RecordedStep
            {
                Kind = StepKind.LaunchApplication,
                AppPath = PositionalString(call, 0),
                AppId = KeywordString(call, "app_id"),
                StartIn = KeywordString(call, "start_in"),
                Args = KeywordString(call, "args"),
                LaunchTimeout = KeywordRaw(call, "timeout"),
                AutoAttach = IsTrue(KeywordRaw(call, "attach")),
                LaunchDriver = ParseDriverList(KeywordRaw(call, "drivers"))?.FirstOrDefault() ?? "WPFSpy",
                LaunchDriverList = ParsePythonDriverList(KeywordRaw(call, "drivers")),
                SpyAgentEnabled = !string.Equals(KeywordRaw(call, "spy_agent_enabled"), "False", StringComparison.OrdinalIgnoreCase),
                PipeName = KeywordString(call, "pipe_name"),
            };
        }

        private static RecordedStep ParsePythonTerminateApplication(PythonCall call)
        {
            return new RecordedStep
            {
                Kind = StepKind.TerminateApplication,
                AppId = KeywordString(call, "app_id"),
                WindowTitle = KeywordString(call, "window_title"),
                ProcessName = KeywordString(call, "process_name"),
                ForceTerminate = IsTrue(KeywordRaw(call, "force")),
            };
        }

        private static PythonCall ParsePythonCall(string name, string argumentText, int lineNumber)
        {
            var positional = new List<string>();
            var named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in SplitPythonArguments(argumentText))
            {
                int equals = FindTopLevelEquals(part);
                if (equals > 0)
                    named[part[..equals].Trim()] = part[(equals + 1)..].Trim();
                else if (!string.IsNullOrWhiteSpace(part))
                    positional.Add(part.Trim());
            }
            return new PythonCall(name, positional, named, lineNumber);
        }

        private static List<string> SplitPythonArguments(string source)
        {
            var parts = new List<string>();
            int start = 0;
            int nesting = 0;
            char quote = '\0';
            bool escaped = false;
            for (int i = 0; i < source.Length; i++)
            {
                char current = source[i];
                if (escaped) { escaped = false; continue; }
                if (current == '\\' && quote != '\0') { escaped = true; continue; }
                if (quote != '\0')
                {
                    if (current == quote) quote = '\0';
                    continue;
                }
                if (current is '\'' or '"') { quote = current; continue; }
                if (current is '(' or '[' or '{') { nesting++; continue; }
                if (current is ')' or ']' or '}') { nesting--; continue; }
                if (current == ',' && nesting == 0)
                {
                    parts.Add(source[start..i].Trim());
                    start = i + 1;
                }
            }
            if (start < source.Length)
                parts.Add(source[start..].Trim());
            return parts;
        }

        private static int FindTopLevelEquals(string source)
        {
            char quote = '\0';
            bool escaped = false;
            int nesting = 0;
            for (int i = 0; i < source.Length; i++)
            {
                char current = source[i];
                if (escaped) { escaped = false; continue; }
                if (current == '\\' && quote != '\0') { escaped = true; continue; }
                if (quote != '\0')
                {
                    if (current == quote) quote = '\0';
                    continue;
                }
                if (current is '\'' or '"') { quote = current; continue; }
                if (current is '(' or '[' or '{') { nesting++; continue; }
                if (current is ')' or ']' or '}') { nesting--; continue; }
                if (current == '=' && nesting == 0) return i;
            }
            return -1;
        }

        private static string[] SplitRobotCells(string line)
        {
            string trimmed = line.Trim();
            var tabCells = Regex.Split(trimmed, @"\t+").Where(cell => cell.Length > 0).ToArray();
            if (tabCells.Length > 1)
                return tabCells;
            var cells = Regex.Split(trimmed, @" {4,}").Where(cell => cell.Length > 0).ToArray();
            if (cells.Length > 1)
                return cells;
            return Regex.Split(trimmed, @" {2,}").Where(cell => cell.Length > 0).ToArray();
        }

        private static int RobotPositionalArgumentCount(string keyword) => keyword.Trim().ToUpperInvariant() switch
        {
            "CLICK ELEMENT" or "TOGGLE ELEMENT" or "DOUBLE CLICK ELEMENT" or "RIGHT CLICK ELEMENT"
                or "HOVER OVER ELEMENT" or "GET DATA GRID CONTENT OCR" or "VERIFY ELEMENT ENABLED"
                or "VERIFY ELEMENT VISIBLE" or "WAIT UNTIL ELEMENT EXISTS" or "WAIT UNTIL ELEMENT VISIBLE"
                or "WAIT UNTIL ELEMENT ENABLED" => 1,
            "SET ELEMENT VALUE" or "DRAG AND DROP" or "PRESS KEYS" or "SCROLL" or "OCULIX CLICK"
                or "OCULIX TYPE" or "SIKULI CLICK" or "SIKULI TYPE" or "VERIFY ELEMENT TEXT" or "VERIFY ELEMENT CONTAINS TEXT"
                or "VERIFY ELEMENT TEXT MATCHES REGEX" or "WAIT UNTIL TEXT CONTAINS"
                or "WAIT UNTIL ELEMENT TEXT CONTAINS" or "AREA CHECKPOINT" or "IMAGE CHECKPOINT"
                or "DATA GRID CHECKPOINT" or "COUNT CHECKPOINT" => 2,
            "VERIFY ELEMENT ATTRIBUTE" or "PROPERTY CHECKPOINT" or "ATTRIBUTE CHECKPOINT" => 3,
            "LAUNCH APPLICATION" => 1,
            _ => 0,
        };

        private static readonly HashSet<string> RobotNamedArguments = new(StringComparer.OrdinalIgnoreCase)
        {
            "app_id", "timeout", "start_in", "args", "attach", "driver", "drivers",
            "spy_agent", "spy_agent_enabled", "pipe_name", "window_title", "process_name", "force",
        };

        private static Dictionary<string, string> ParseRobotNamedArguments(IEnumerable<string> args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string arg in args)
            {
                int equals = arg.IndexOf('=');
                if (equals > 0 && RobotNamedArguments.Contains(arg[..equals].Trim()))
                    result[arg[..equals].Trim()] = arg[(equals + 1)..].Trim();
            }
            return result;
        }

        private static bool IsRobotNamedArgument(string arg)
        {
            int equals = arg.IndexOf('=');
            return equals > 0 && RobotNamedArguments.Contains(arg[..equals].Trim());
        }

        private static RecordedStep Verify(StepKind kind, string alias, string? value, string? appId) =>
            new() { Kind = kind, Alias = alias, Value = value, AppId = appId };

        private static RecordedStep VerifyAttribute(StepKind kind, IReadOnlyList<string> args, string? appId) =>
            new() { Kind = kind, Alias = Positional(args, 0), AttributeName = OptionalPositional(args, 1), Value = OptionalPositional(args, 2), AppId = appId };

        private static RecordedStep Wait(StepKind kind, string alias, string? value, string? appId) =>
            new() { Kind = kind, Alias = alias, Value = value, AppId = appId };

        private static void ApplyDefaultAppId(IEnumerable<RecordedStep> steps, string? appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return;
            foreach (var step in steps)
                step.AppId ??= appId;
        }

        private static string Positional(IReadOnlyList<string> values, int index) =>
            index >= 0 && index < values.Count ? values[index] : "";

        private static string? OptionalPositional(IReadOnlyList<string> values, int index) =>
            index >= 0 && index < values.Count ? values[index] : null;

        private static string? Named(IReadOnlyDictionary<string, string> values, string name) =>
            values.TryGetValue(name, out var value) ? value : null;

        private static bool IsTrue(string? value) => value != null
            && (value.Equals("Yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("True", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.OrdinalIgnoreCase));

        private static List<string>? ParseDriverList(string? value) => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim('[', ']', '"', '\'').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private sealed record PythonCall(string Name, List<string> Positional, Dictionary<string, string> Named, int LineNumber)
        {
            public string? PositionalString(int index) => index >= 0 && index < Positional.Count ? ParsePythonString(Positional[index]) : null;
            public string? KeywordRaw(string name) => Named.TryGetValue(name, out var raw) ? raw : null;
            public string? KeywordString(string name) => KeywordRaw(name) is { } raw ? ParsePythonString(raw) : null;
        }

        private static string? PositionalString(PythonCall call, int index) => call.PositionalString(index);
        private static string? OptionalPositionalString(PythonCall call, int index) => call.PositionalString(index);
        private static string? KeywordString(PythonCall call, string name) => call.KeywordString(name);
        private static string? KeywordRaw(PythonCall call, string name) => call.KeywordRaw(name);

        private static string ParsePythonString(string value)
        {
            value = value.Trim();
            if (value.Length < 2 || value[0] is not ('\'' or '"') || value[^1] != value[0])
                return value;
            var result = new System.Text.StringBuilder(value.Length - 2);
            for (int i = 1; i < value.Length - 1; i++)
            {
                char current = value[i];
                if (current == '\\' && i + 1 < value.Length - 1)
                {
                    char escaped = value[++i];
                    if (escaped == 'u' && i + 4 < value.Length - 1
                        && ushort.TryParse(value.AsSpan(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out ushort unicode))
                    {
                        result.Append((char)unicode);
                        i += 4;
                        continue;
                    }

                    result.Append(escaped switch
                    {
                        'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t',
                        '\\' => '\\', '\'' => '\'', '"' => '"',
                        _ => escaped,
                    });
                }
                else result.Append(current);
            }
            return result.ToString();
        }

        private static List<string>? ParsePythonDriverList(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
                return SplitPythonArguments(value[1..^1]).Select(ParsePythonString).ToList();
            return ParseDriverList(ParsePythonString(value));
        }
    }
}
