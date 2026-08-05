using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using WpfTestIde.Models;

namespace WpfTestIde.ViewModels
{
    /// <summary>
    /// Represents a single step in the visual test flow.
    /// </summary>
    public class FlowStep : INotifyPropertyChanged
    {
        private int _stepNumber;
        private string _actionType = "Click";
        private string _elementAlias = "";
        private string _value = "";
        private string _checkpointType = "";
        private string _expectedValue = "";
        private bool _isSelected;
        private FlowStepStatus _status = FlowStepStatus.Pending;

        public int StepNumber
        {
            get => _stepNumber;
            set { _stepNumber = value; OnPropertyChanged(); }
        }

        public string ActionType
        {
            get => _actionType;
            set { _actionType = value; OnPropertyChanged(); GenerateDescription(); }
        }

        public string ElementAlias
        {
            get => _elementAlias;
            set { _elementAlias = value; OnPropertyChanged(); GenerateDescription(); }
        }

        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); GenerateDescription(); }
        }

        public string CheckpointType
        {
            get => _checkpointType;
            set { _checkpointType = value; OnPropertyChanged(); GenerateDescription(); }
        }

        public string ExpectedValue
        {
            get => _expectedValue;
            set { _expectedValue = value; OnPropertyChanged(); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public FlowStepStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusColor)); }
        }

        public string StatusColor => Status switch
        {
            FlowStepStatus.Pending => "#9E9E9E",
            FlowStepStatus.Running => "#2196F3",
            FlowStepStatus.Passed => "#4CAF50",
            FlowStepStatus.Failed => "#F44336",
            FlowStepStatus.Skipped => "#FF9800",
            _ => "#9E9E9E"
        };

        public string Description { get; private set; } = "";

        public string Icon => ActionType switch
        {
            "Click" => "🖱️",
            "DoubleClick" => "🖱️🖱️",
            "RightClick" => "🖱️☑️",
            "Hover" => "👆",
            "SetText" => "⌨️",
            "GetText" => "📝",
            "Select" => "☑️",
            "Check" => "✅",
            "Uncheck" => "⬜",
            "Verify" => "🔍",
            "Wait" => "⏳",
            "Screenshot" => "📷",
            "KeyPress" => "🎹",
            _ => "➡️"
        };

        void GenerateDescription()
        {
            Description = ActionType switch
            {
                "Click" or "DoubleClick" or "RightClick" or "Hover" =>
                    $"{Icon} {ActionType} [{ElementAlias}]",
                "SetText" =>
                    $"{Icon} Set text '{Value}' in [{ElementAlias}]",
                "GetText" =>
                    $"{Icon} Get text from [{ElementAlias}]",
                "Select" =>
                    $"{Icon} Select '{Value}' in [{ElementAlias}]",
                "Check" or "Uncheck" =>
                    $"{Icon} {ActionType} [{ElementAlias}]",
                "Verify" =>
                    $"{Icon} Verify {CheckpointType} '{ExpectedValue}' in [{ElementAlias}]",
                "Wait" =>
                    $"{Icon} Wait {Value}ms",
                "Screenshot" =>
                    $"{Icon} Take screenshot",
                "KeyPress" =>
                    $"{Icon} Press key '{Value}'",
                _ => $"{Icon} {ActionType} [{ElementAlias}]"
            };
            OnPropertyChanged(nameof(Description));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>
        /// Generates the Robot Framework code for this step.
        /// </summary>
        public string ToRobotCode()
        {
            return ActionType switch
            {
                "Click" => $"Click Element    alias={ElementAlias}",
                "DoubleClick" => $"Double Click Element    alias={ElementAlias}",
                "RightClick" => $"Click Element    alias={ElementAlias}    button=right",
                "Hover" => $"Mouse Over    alias={ElementAlias}",
                "SetText" => $"Input Text    alias={ElementAlias}    text={Value}",
                "GetText" => $"Get Text    alias={ElementAlias}",
                "Select" => $"Select From List By Label    alias={ElementAlias}    label={Value}",
                "Check" => $"Select Checkbox    alias={ElementAlias}",
                "Uncheck" => $"Unselect Checkbox    alias={ElementAlias}",
                "Verify" => CheckpointType switch
                {
                    "Text" => $"Verify Element Text    alias={ElementAlias}    expected={ExpectedValue}",
                    "Value" => $"Verify Element Value    alias={ElementAlias}    expected={ExpectedValue}",
                    "Property" => $"Verify Element Property    alias={ElementAlias}    property=IsEnabled    expected={ExpectedValue}",
                    _ => $"Verify Element Attribute    alias={ElementAlias}    attribute=IsVisible    expected={ExpectedValue}"
                },
                "Wait" => $"Sleep    {Value}",
                "Screenshot" => $"Capture Page Screenshot",
                "KeyPress" => $"Press Key    alias={ElementAlias}    key={Value}",
                _ => $"Log    Unknown action: {ActionType}"
            };
        }
    }

    public enum FlowStepStatus
    {
        Pending,
        Running,
        Passed,
        Failed,
        Skipped
    }

    /// <summary>
    /// Available action types for visual test building.
    /// </summary>
    public class ActionTypeInfo
    {
        public string Name { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// ViewModel for the Visual Test Builder.
    /// </summary>
    public class TestFlowViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<FlowStep> _steps = new();
        private FlowStep? _selectedStep;
        private string _testName = "New Test";
        private string _description = "";
        private bool _isRecording;
        private string _searchFilter = "";
        private ElementEntry? _selectedElement;

        public TestFlowViewModel()
        {
            Steps = new ObservableCollection<FlowStep>();
            
            // Initialize action types
            ActionTypes = new ObservableCollection<ActionTypeInfo>
            {
                // Mouse actions
                new() { Name = "Click", Icon = "🖱️", Category = "Mouse", Description = "Click on element" },
                new() { Name = "DoubleClick", Icon = "🖱️🖱️", Category = "Mouse", Description = "Double-click on element" },
                new() { Name = "RightClick", Icon = "🖱️☑️", Category = "Mouse", Description = "Right-click on element" },
                new() { Name = "Hover", Icon = "👆", Category = "Mouse", Description = "Hover over element" },
                
                // Input actions
                new() { Name = "SetText", Icon = "⌨️", Category = "Input", Description = "Enter text into element" },
                new() { Name = "GetText", Icon = "📝", Category = "Input", Description = "Get text from element" },
                new() { Name = "Select", Icon = "☑️", Category = "Input", Description = "Select from dropdown" },
                
                // Checkbox actions
                new() { Name = "Check", Icon = "✅", Category = "Checkbox", Description = "Check a checkbox" },
                new() { Name = "Uncheck", Icon = "⬜", Category = "Checkbox", Description = "Uncheck a checkbox" },
                
                // Verification
                new() { Name = "Verify", Icon = "🔍", Category = "Verification", Description = "Verify element property" },
                
                // Control flow
                new() { Name = "Wait", Icon = "⏳", Category = "Control", Description = "Wait for specified time" },
                new() { Name = "Screenshot", Icon = "📷", Category = "Control", Description = "Capture screenshot" },
                new() { Name = "KeyPress", Icon = "🎹", Category = "Control", Description = "Press keyboard key" },
            };

            // Initialize commands
            AddStepCommand = new RelayCommand(_ => AddStep());
            RemoveStepCommand = new RelayCommand(_ => RemoveSelectedStep(), _ => SelectedStep != null);
            MoveUpCommand = new RelayCommand(_ => MoveStep(-1), _ => CanMoveUp());
            MoveDownCommand = new RelayCommand(_ => MoveStep(1), _ => CanMoveDown());
            DuplicateStepCommand = new RelayCommand(_ => DuplicateStep(), _ => SelectedStep != null);
            InsertStepCommand = new RelayCommand(_ => InsertStep(), _ => _selectedElement != null);
            ClearAllCommand = new RelayCommand(_ => ClearSteps());
            SelectElementCommand = new RelayCommand(p => SelectElement(p as ElementEntry));

            // Initialize checkpoint types
            CheckpointTypes = new ObservableCollection<string>
            {
                "Text", "Value", "Property", "Exists", "IsVisible", "IsEnabled"
            };
        }

        public ObservableCollection<FlowStep> Steps
        {
            get => _steps;
            set { _steps = value; OnPropertyChanged(); }
        }

        public FlowStep? SelectedStep
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

        public bool IsRecording
        {
            get => _isRecording;
            set { _isRecording = value; OnPropertyChanged(); }
        }

        public string SearchFilter
        {
            get => _searchFilter;
            set { _searchFilter = value; OnPropertyChanged(); OnPropertyChanged(nameof(FilteredSteps)); }
        }

        public ElementEntry? SelectedElement
        {
            get => _selectedElement;
            set { _selectedElement = value; OnPropertyChanged(); }
        }

        public ObservableCollection<FlowStep> FilteredSteps
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SearchFilter))
                    return Steps;
                return new ObservableCollection<FlowStep>(
                    Steps.Where(s => 
                        s.Description.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase) ||
                        s.ElementAlias.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase)));
            }
        }

        public ObservableCollection<ActionTypeInfo> ActionTypes { get; }

        public ObservableCollection<string> CheckpointTypes { get; }

        public ObservableCollection<ElementEntry> AvailableElements { get; set; } = new();

        // Commands
        public ICommand AddStepCommand { get; }
        public ICommand RemoveStepCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand DuplicateStepCommand { get; }
        public ICommand InsertStepCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand SelectElementCommand { get; }

        public void AddStep()
        {
            var step = new FlowStep
            {
                StepNumber = Steps.Count + 1,
                ActionType = "Click",
                ElementAlias = SelectedElement?.Alias ?? "",
                Status = FlowStepStatus.Pending
            };
            Steps.Add(step);
            SelectedStep = step;
            RenumberSteps();
            OnPropertyChanged(nameof(FilteredSteps));
        }

        public void RemoveSelectedStep()
        {
            if (SelectedStep != null)
            {
                var index = Steps.IndexOf(SelectedStep);
                Steps.Remove(SelectedStep);
                if (Steps.Count > 0)
                {
                    SelectedStep = Steps[Math.Min(index, Steps.Count - 1)];
                }
                else
                {
                    SelectedStep = null;
                }
                RenumberSteps();
                OnPropertyChanged(nameof(FilteredSteps));
            }
        }

        public void InsertStep()
        {
            if (SelectedElement == null) return;
            
            var step = new FlowStep
            {
                StepNumber = SelectedStep != null ? SelectedStep.StepNumber + 1 : Steps.Count + 1,
                ActionType = "Click",
                ElementAlias = SelectedElement.Alias,
                Status = FlowStepStatus.Pending
            };

            if (SelectedStep != null)
            {
                var index = Steps.IndexOf(SelectedStep);
                Steps.Insert(index + 1, step);
            }
            else
            {
                Steps.Add(step);
            }

            SelectedStep = step;
            RenumberSteps();
            OnPropertyChanged(nameof(FilteredSteps));
        }

        private void DuplicateStep()
        {
            if (SelectedStep == null) return;

            var copy = new FlowStep
            {
                StepNumber = Steps.Count + 1,
                ActionType = SelectedStep.ActionType,
                ElementAlias = SelectedStep.ElementAlias,
                Value = SelectedStep.Value,
                CheckpointType = SelectedStep.CheckpointType,
                ExpectedValue = SelectedStep.ExpectedValue,
                Status = FlowStepStatus.Pending
            };

            Steps.Add(copy);
            SelectedStep = copy;
            RenumberSteps();
            OnPropertyChanged(nameof(FilteredSteps));
        }

        private bool CanMoveUp() => SelectedStep != null && Steps.IndexOf(SelectedStep) > 0;
        private bool CanMoveDown() => SelectedStep != null && Steps.IndexOf(SelectedStep) < Steps.Count - 1;

        public void MoveStep(int direction)
        {
            if (SelectedStep == null) return;

            var index = Steps.IndexOf(SelectedStep);
            var newIndex = index + direction;

            if (newIndex < 0 || newIndex >= Steps.Count) return;

            Steps.Move(index, newIndex);
            RenumberSteps();
            SelectedStep = Steps[newIndex];
            OnPropertyChanged(nameof(FilteredSteps));
        }

        public void ClearSteps()
        {
            Steps.Clear();
            SelectedStep = null;
            OnPropertyChanged(nameof(FilteredSteps));
        }

        public void LoadSteps(ObservableCollection<RecordedStep> recordedSteps)
        {
            Steps.Clear();
            int i = 1;
            foreach (var rs in recordedSteps)
            {
                var step = new FlowStep
                {
                    StepNumber = i++,
                    ActionType = MapActionType(rs.Kind, rs.Action),
                    ElementAlias = rs.Alias ?? "",
                    Value = rs.Value ?? "",
                    CheckpointType = rs.Kind == StepKind.Verify ? "Text" : "",
                    ExpectedValue = rs.Value ?? "",
                    Status = FlowStepStatus.Pending
                };
                Steps.Add(step);
            }
            OnPropertyChanged(nameof(FilteredSteps));
        }

        private string MapActionType(StepKind kind, ActionKind action)
        {
            if (kind == StepKind.Verify)
                return "Verify";
            if (kind == StepKind.VerifyOcr)
                return "GetText";
                
            return action switch
            {
                ActionKind.Invoke => "Click",
                ActionKind.SetValue => "SetText",
                ActionKind.Toggle => "Check",
                _ => "Click"
            };
        }

        private void RenumberSteps()
        {
            for (int i = 0; i < Steps.Count; i++)
            {
                Steps[i].StepNumber = i + 1;
            }
        }

        private void SelectElement(ElementEntry? element)
        {
            SelectedElement = element;
        }

        /// <summary>
        /// Generates the complete Robot Framework test from the flow.
        /// </summary>
        public string GenerateRobotTest()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("*** Settings ***");
            sb.AppendLine("Library    WpfTestLibrary");
            sb.AppendLine();
            sb.AppendLine("*** Test Cases ***");
            sb.AppendLine($"{TestName}");
            if (!string.IsNullOrEmpty(Description))
            {
                sb.AppendLine($"    [Documentation]    {Description}");
            }
            sb.AppendLine();

            foreach (var step in Steps)
            {
                sb.AppendLine($"    {step.ToRobotCode()}");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Generates a visual flow diagram description.
        /// </summary>
        public string GenerateFlowDiagram()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("┌─────────────────────────────────────────────────────────┐");
            sb.AppendLine($"│  📋 Test: {TestName}");
            sb.AppendLine("├─────────────────────────────────────────────────────────┤");

            foreach (var step in Steps)
            {
                var status = step.Status switch
                {
                    FlowStepStatus.Passed => "✅",
                    FlowStepStatus.Failed => "❌",
                    FlowStepStatus.Running => "🔄",
                    FlowStepStatus.Skipped => "⏭️",
                    _ => "⬜"
                };
                sb.AppendLine($"│ {step.StepNumber:2}. {status} {step.Description}");
                sb.AppendLine("│         │");
            }

            sb.AppendLine("└─────────┘");
            return sb.ToString();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Simple relay command implementation.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;

        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
    }
}
