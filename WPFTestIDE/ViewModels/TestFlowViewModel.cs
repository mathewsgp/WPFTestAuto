using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WpfTestIde.Execution;
using WpfTestIde.Models;
using WpfTestIde.Recording;

namespace WpfTestIde.ViewModels
{
    /// <summary>The visual builder edits the same recursive nodes as every script view.</summary>
    public sealed class TestFlowViewModel : INotifyPropertyChanged
    {
        private RecordedStep? _selectedStep;
        private string _testName = "New Test";
        private string _description = "";

        public ObservableCollection<RecordedStep> Steps { get; } = new();
        public ObservableCollection<ElementEntry> AvailableElements { get; } = new();
        public ObservableCollection<string> DatasetNames { get; } = new();
        public IReadOnlyList<FlowConditionKind> FlowConditionKinds { get; } = Enum.GetValues<FlowConditionKind>();
        public IReadOnlyList<FlowLoopSourceKind> FlowLoopSourceKinds { get; } = Enum.GetValues<FlowLoopSourceKind>();
        public IReadOnlyList<ActionKind> ActionKinds { get; } = Enum.GetValues<ActionKind>();
        public Dictionary<string, List<string>> Datasets { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        public RecordedStep? SelectedStep
        {
            get => _selectedStep;
            set { _selectedStep = value; OnPropertyChanged(); }
        }

        public string TestName
        {
            get => _testName;
            set { _testName = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        public ICommand AddActionCommand { get; }
        public ICommand AddIfCommand { get; }
        public ICommand AddRepeatCommand { get; }
        public ICommand AddForEachCommand { get; }
        public ICommand AddThenStepCommand { get; }
        public ICommand AddElseStepCommand { get; }
        public ICommand AddLoopStepCommand { get; }
        public ICommand AddNestedIfCommand { get; }
        public ICommand AddNestedRepeatCommand { get; }
        public ICommand AddNestedForEachCommand { get; }
        public ICommand AddElseIfCommand { get; }
        public ICommand AddElseRepeatCommand { get; }
        public ICommand AddElseForEachCommand { get; }
        public ICommand DeleteStepCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand ClearAllCommand { get; }

        public TestFlowViewModel()
        {
            AddActionCommand = new BuilderCommand(parameter => AddAction(parameter?.ToString() ?? "Click"));
            AddIfCommand = new BuilderCommand(_ => Steps.Add(new RecordedStep
            {
                Kind = StepKind.If,
                Condition = new FlowCondition { Alias = AvailableElements.FirstOrDefault()?.Alias ?? "" },
                HasElse = true,
            }));
            AddRepeatCommand = new BuilderCommand(_ => Steps.Add(new RecordedStep { Kind = StepKind.Repeat, RepeatCount = 2 }));
            AddForEachCommand = new BuilderCommand(_ => Steps.Add(new RecordedStep
            {
                Kind = StepKind.ForEach,
                LoopVariable = "item",
                LoopValues = new ObservableCollection<string> { "value1" },
            }));
            AddThenStepCommand = new BuilderCommand(parameter => AddChild(parameter as RecordedStep, false));
            AddElseStepCommand = new BuilderCommand(parameter => AddChild(parameter as RecordedStep, true));
            AddLoopStepCommand = new BuilderCommand(parameter => AddChild(parameter as RecordedStep, false));
            AddNestedIfCommand = new BuilderCommand(parameter => AddNestedFlow(parameter as RecordedStep, StepKind.If, false));
            AddNestedRepeatCommand = new BuilderCommand(parameter => AddNestedFlow(parameter as RecordedStep, StepKind.Repeat, false));
            AddNestedForEachCommand = new BuilderCommand(parameter => AddNestedFlow(parameter as RecordedStep, StepKind.ForEach, false));
            AddElseIfCommand = new BuilderCommand(parameter => AddNestedFlow(parameter as RecordedStep, StepKind.If, true));
            AddElseRepeatCommand = new BuilderCommand(parameter => AddNestedFlow(parameter as RecordedStep, StepKind.Repeat, true));
            AddElseForEachCommand = new BuilderCommand(parameter => AddNestedFlow(parameter as RecordedStep, StepKind.ForEach, true));
            DeleteStepCommand = new BuilderCommand(parameter => DeleteStep(parameter as RecordedStep));
            MoveUpCommand = new BuilderCommand(parameter => MoveStep(parameter as RecordedStep, -1));
            MoveDownCommand = new BuilderCommand(parameter => MoveStep(parameter as RecordedStep, 1));
            ClearAllCommand = new BuilderCommand(_ => { Steps.Clear(); SelectedStep = null; });
        }

        public void LoadSteps(IEnumerable<RecordedStep> nodes)
        {
            Steps.Clear();
            foreach (var node in nodes)
                Steps.Add(node.CloneDeep());
            SelectedStep = Steps.FirstOrDefault();
        }

        public void LoadDatasets(IReadOnlyDictionary<string, List<string>>? datasets)
        {
            Datasets = datasets == null
                ? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, List<string>>(datasets, StringComparer.OrdinalIgnoreCase);
            DatasetNames.Clear();
            foreach (var name in Datasets.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                DatasetNames.Add(name);
        }

        public string GenerateRobotTest()
            => ScriptGenerator.Generate(Steps, TestName, datasets: Datasets);

        public string GenerateFlowDiagram()
        {
            var lines = new List<string> { $"Test: {TestName}" };
            AppendNodes(lines, Steps, 0);
            return string.Join(Environment.NewLine, lines);
        }

        private static void AppendNodes(List<string> lines, IEnumerable<RecordedStep> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                string indent = new string(' ', depth * 2);
                lines.Add(indent + "• " + node.DisplayVerb + (string.IsNullOrWhiteSpace(node.Alias) ? "" : $" [{node.Alias}]"));
                if (node.Kind == StepKind.If)
                {
                    lines.Add(indent + "  THEN");
                    AppendNodes(lines, node.ThenSteps, depth + 2);
                    if (node.HasElse || node.ElseSteps.Count > 0)
                    {
                        lines.Add(indent + "  ELSE");
                        AppendNodes(lines, node.ElseSteps, depth + 2);
                    }
                }
                else if (node.Kind is StepKind.Repeat or StepKind.ForEach)
                {
                    lines.Add(indent + "  BODY");
                    AppendNodes(lines, node.BodySteps, depth + 2);
                }
            }
        }

        private void AddAction(string type)
        {
            var alias = AvailableElements.FirstOrDefault()?.Alias ?? "";
            var step = type switch
            {
                "Set Text" => new RecordedStep { Kind = StepKind.Action, Action = ActionKind.SetValue, Alias = alias, Value = "" },
                "Toggle" => new RecordedStep { Kind = StepKind.Action, Action = ActionKind.Toggle, Alias = alias },
                "Double Click" => new RecordedStep { Kind = StepKind.Action, Action = ActionKind.DoubleClick, Alias = alias },
                "Right Click" => new RecordedStep { Kind = StepKind.Action, Action = ActionKind.RightClick, Alias = alias },
                "Hover" => new RecordedStep { Kind = StepKind.Action, Action = ActionKind.Hover, Alias = alias },
                "Verify Text" => new RecordedStep { Kind = StepKind.Verify, Alias = alias, Value = "" },
                "Wait Visible" => new RecordedStep { Kind = StepKind.WaitVisible, Alias = alias, Value = "5" },
                _ => new RecordedStep { Kind = StepKind.Action, Action = ActionKind.Invoke, Alias = alias },
            };
            Steps.Add(step);
            SelectedStep = step;
        }

        private void AddChild(RecordedStep? parent, bool elseBranch)
        {
            if (parent == null) return;
            var child = new RecordedStep
            {
                Kind = StepKind.Action,
                Action = ActionKind.Invoke,
                Alias = AvailableElements.FirstOrDefault()?.Alias ?? "",
            };
            if (parent.Kind == StepKind.If)
            {
                if (elseBranch)
                {
                    parent.HasElse = true;
                    parent.ElseSteps.Add(child);
                }
                else parent.ThenSteps.Add(child);
            }
            else if (parent.Kind is StepKind.Repeat or StepKind.ForEach)
            {
                parent.BodySteps.Add(child);
            }
            SelectedStep = child;
        }

        private void AddNestedFlow(RecordedStep? parent, StepKind kind, bool elseBranch)
        {
            if (parent == null) return;
            RecordedStep node = kind switch
            {
                StepKind.If => new RecordedStep
                {
                    Kind = StepKind.If,
                Condition = new FlowCondition { Alias = AvailableElements.FirstOrDefault()?.Alias ?? "" },
                    HasElse = true,
                },
                StepKind.Repeat => new RecordedStep { Kind = StepKind.Repeat, RepeatCount = 2 },
                _ => new RecordedStep
                {
                    Kind = StepKind.ForEach,
                    LoopVariable = "item",
                    LoopValues = new ObservableCollection<string> { "value1" },
                },
            };
            if (parent.Kind == StepKind.If)
            {
                if (elseBranch)
                {
                    parent.HasElse = true;
                    parent.ElseSteps.Add(node);
                }
                else parent.ThenSteps.Add(node);
            }
            else if ((parent.Kind is StepKind.Repeat or StepKind.ForEach) && !elseBranch)
            {
                parent.BodySteps.Add(node);
            }
            SelectedStep = node;
        }

        private void DeleteStep(RecordedStep? target)
        {
            if (target == null) return;
            if (RemoveFrom(Steps, target))
                SelectedStep = Steps.FirstOrDefault();
        }

        private static bool RemoveFrom(ObservableCollection<RecordedStep> nodes, RecordedStep target)
        {
            if (nodes.Remove(target)) return true;
            foreach (var node in nodes)
            {
                if (RemoveFrom(node.ThenSteps, target) || RemoveFrom(node.ElseSteps, target) || RemoveFrom(node.BodySteps, target))
                    return true;
            }
            return false;
        }

        private void MoveStep(RecordedStep? target, int delta)
        {
            if (target == null) return;
            MoveRecursive(Steps, target, delta);
        }

        private static bool MoveRecursive(ObservableCollection<RecordedStep> nodes, RecordedStep target, int delta)
        {
            if (MoveIn(nodes, target, delta)) return true;
            foreach (var node in nodes)
            {
                if (MoveRecursive(node.ThenSteps, target, delta)
                    || MoveRecursive(node.ElseSteps, target, delta)
                    || MoveRecursive(node.BodySteps, target, delta)) return true;
            }
            return false;
        }

        private static bool MoveIn(ObservableCollection<RecordedStep> nodes, RecordedStep target, int delta)
        {
            int index = nodes.IndexOf(target);
            int next = index + delta;
            if (index < 0 || next < 0 || next >= nodes.Count) return false;
            nodes.Move(index, next);
            return true;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private sealed class BuilderCommand : ICommand
        {
            private readonly Action<object?> _execute;
            public BuilderCommand(Action<object?> execute) => _execute = execute;
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => _execute(parameter);
            public event EventHandler? CanExecuteChanged { add { } remove { } }
        }
    }
}
