using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using WpfTestIde.Models;

namespace WpfTestIde.Execution
{
    public static class ScriptFlowValidator
    {
        public const int MaxLoopIterations = 1000;
        private const int MaxTreeDepth = 64;
        private const int MaxNodeCount = 10000;
        private static readonly Regex IdentifierPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static void Validate(
            IEnumerable<RecordedStep> nodes,
            IReadOnlyDictionary<string, List<string>>? datasets = null)
        {
            var normalizedDatasets = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (datasets != null)
            {
                foreach (var pair in datasets)
                {
                    if (!IdentifierPattern.IsMatch(pair.Key))
                        throw new FormatException($"Dataset name '{pair.Key}' must be a valid identifier.");
                    if (pair.Value == null || pair.Value.Any(value => value == null))
                        throw new FormatException($"Dataset '{pair.Key}' and its values must be string arrays without nulls.");
                    if (!normalizedDatasets.TryAdd(pair.Key, pair.Value))
                        throw new FormatException($"Dataset name '{pair.Key}' is duplicated (dataset names are case-insensitive).");
                    if (pair.Value.Count > MaxLoopIterations)
                        throw new FormatException($"Dataset '{pair.Key}' exceeds the {MaxLoopIterations} item limit.");
                }
            }

            int total = 0;
            ValidateNodes(nodes, normalizedDatasets, depth: 0, ref total);
        }

        private static void ValidateNodes(
            IEnumerable<RecordedStep> nodes,
            IReadOnlyDictionary<string, List<string>> datasets,
            int depth,
            ref int total)
        {
            if (depth > MaxTreeDepth)
                throw new FormatException($"Script control-flow nesting exceeds {MaxTreeDepth} levels.");

            foreach (var node in nodes)
            {
                if (node == null)
                    throw new FormatException("The script contains a null node.");
                if (++total > MaxNodeCount)
                    throw new FormatException($"A script cannot contain more than {MaxNodeCount} nodes.");

                switch (node.Kind)
                {
                    case StepKind.If:
                        ValidateCondition(node.Condition);
                        ValidateNodes(node.ThenSteps, datasets, depth + 1, ref total);
                        ValidateNodes(node.ElseSteps, datasets, depth + 1, ref total);
                        break;
                    case StepKind.Repeat:
                        if (node.RepeatCount < 0 || node.RepeatCount > MaxLoopIterations)
                            throw new FormatException($"Repeat count must be between 0 and {MaxLoopIterations}.");
                        ValidateNodes(node.BodySteps, datasets, depth + 1, ref total);
                        break;
                    case StepKind.ForEach:
                        if (!Enum.IsDefined(node.LoopSourceKind))
                            throw new FormatException("A ForEach node contains an unknown source kind.");
                        if (!IdentifierPattern.IsMatch(node.LoopVariable))
                            throw new FormatException($"Loop variable '{node.LoopVariable}' must be a valid identifier.");
                        if (node.LoopSourceKind == FlowLoopSourceKind.NamedDataset)
                        {
                            if (string.IsNullOrWhiteSpace(node.DatasetName)
                                || !datasets.ContainsKey(node.DatasetName))
                                throw new FormatException($"ForEach references missing dataset '{node.DatasetName}'.");
                        }
                        else if (node.LoopValues.Count > MaxLoopIterations)
                        {
                            throw new FormatException($"A ForEach loop cannot contain more than {MaxLoopIterations} literal values.");
                        }
                        else if (node.LoopValues.Any(value => value == null))
                        {
                            throw new FormatException("Literal ForEach values must be strings.");
                        }
                        ValidateNodes(node.BodySteps, datasets, depth + 1, ref total);
                        break;
                    default:
                        if (!Enum.IsDefined(node.Kind) || !Enum.IsDefined(node.Action))
                            throw new FormatException("The script contains an unknown node kind or action.");
                        break;
                }
            }
        }

        private static void ValidateCondition(FlowCondition? condition)
        {
            if (condition == null)
                throw new FormatException("If nodes need a condition.");
            if (!Enum.IsDefined(condition.Kind))
                throw new FormatException("An If node contains an unknown condition kind.");
            if (string.IsNullOrWhiteSpace(condition.Alias))
                throw new FormatException("If conditions need an element alias.");
            if ((condition.Kind is FlowConditionKind.TextEquals or FlowConditionKind.TextContains)
                && condition.ExpectedValue == null)
                throw new FormatException($"The {condition.Kind} condition needs an expected value.");
        }
    }
}
