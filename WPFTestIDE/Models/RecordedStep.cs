using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace WpfTestIde.Models
{
    public enum StepKind
    {
        Action, Verify, VerifyOcr, VerifyEnabled, VerifyVisible, VerifyContains,
        VerifyRegex, VerifyAttribute, WaitExists, WaitVisible, WaitEnabled, WaitTextContains,
        CheckpointProperty, CheckpointArea, CheckpointImage, CheckpointDataGrid,
        CheckpointCount, CheckpointAttribute,
        LaunchApplication, TerminateApplication,
        If, Repeat, ForEach,
    }
    public enum ActionKind { Invoke, SetValue, Toggle, DoubleClick, RightClick, DragDrop, Hover, PressKeys, Scroll, SikuliClick, SikuliType }
    public enum FlowConditionKind { ElementExists, ElementVisible, ElementEnabled, TextEquals, TextContains }
    public enum FlowLoopSourceKind { LiteralValues, NamedDataset }

    public sealed class FlowCondition : INotifyPropertyChanged
    {
        private FlowConditionKind _kind = FlowConditionKind.ElementExists;
        private string _alias = "";
        private string? _expectedValue;
        private string? _appId;

        public FlowConditionKind Kind { get => _kind; set { _kind = value; OnPropertyChanged(); } }
        public string Alias { get => _alias; set { _alias = value ?? ""; OnPropertyChanged(); } }
        public string? ExpectedValue { get => _expectedValue; set { _expectedValue = value; OnPropertyChanged(); } }
        public string? AppId { get => _appId; set { _appId = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// One node in the authored script tree. Ordinary nodes represent a
    /// recorded action or verification; If, Repeat, and ForEach nodes own
    /// nested child collections. Mirrors the shape of
    /// recorder/recorded_sequence.json in the Python framework, so the
    /// same generator logic (ScriptGenerator) produces output consistent
    /// with recorder/converter.py.
    /// </summary>
    public class RecordedStep : INotifyPropertyChanged
    {
        private StepKind _kind;
        private string _alias = "";
        private ActionKind _action;
        private string? _value;
        private FlowCondition? _condition;
        private int _repeatCount = 1;
        private string _loopVariable = "item";
        private FlowLoopSourceKind _loopSourceKind;
        private string? _datasetName;
        private bool _hasElse;
        private ObservableCollection<string> _loopValues = new();
        private ObservableCollection<RecordedStep> _thenSteps = new();
        private ObservableCollection<RecordedStep> _elseSteps = new();
        private ObservableCollection<RecordedStep> _bodySteps = new();

        public RecordedStep()
        {
            _loopValues.CollectionChanged += LoopValues_CollectionChanged;
            _thenSteps.CollectionChanged += Children_CollectionChanged;
            _elseSteps.CollectionChanged += Children_CollectionChanged;
            _bodySteps.CollectionChanged += Children_CollectionChanged;
        }

        public StepKind Kind { get => _kind; set { _kind = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayVerb)); } }
        public string Alias { get => _alias; set { _alias = value ?? ""; OnPropertyChanged(); } }
        public ActionKind Action { get => _action; set { _action = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayVerb)); } }

        /// <summary>SetValue's value, or a Verify step's expected text. Null for Invoke/Toggle.</summary>
        public string? Value { get => _value; set { _value = value; OnPropertyChanged(); } }

        /// <summary>Attribute name for VerifyAttribute/CheckpointAttribute steps.</summary>
        public string? AttributeName { get; set; }

        /// <summary>Target alias for DragDrop steps.</summary>
        public string? TargetAlias { get; set; }

        /// <summary>Property name for Property Checkpoint steps.</summary>
        public string? PropertyName { get; set; }

        /// <summary>Expected count for Count Checkpoint steps.</summary>
        public string? ExpectedCount { get; set; }

        // ----- Launch Application parameters (StepKind.LaunchApplication) -----
        /// <summary>Path to the executable to launch. Required for LaunchApplication.</summary>
        public string? AppPath { get; set; }

        /// <summary>Working directory for the launched process. Optional.</summary>
        public string? StartIn { get; set; }

        /// <summary>Command-line arguments for the launched process. Optional.</summary>
        public string? Args { get; set; }

        /// <summary>Launch wait timeout. Optional; null uses the framework default.</summary>
        public string? LaunchTimeout { get; set; }

        /// <summary>True to automatically attach the framework to the spawned process.</summary>
        public bool AutoAttach { get; set; }

        /// <summary>Driver to use for the launched process. Default "WPFSpy".</summary>
        public string LaunchDriver { get; set; } = "WPFSpy";

        /// <summary>Ordered driver priority list for the launched process. When set, takes precedence over LaunchDriver.</summary>
        public List<string>? LaunchDriverList { get; set; }

        /// <summary>Whether to enable Spy Agent for the launched process. Default true.</summary>
        public bool SpyAgentEnabled { get; set; } = true;

        /// <summary>Named pipe name for the Spy Agent. If null, derived from app id.</summary>
        public string? PipeName { get; set; }

        // ----- Terminate Application parameters (StepKind.TerminateApplication) -----
        /// <summary>Substring match against the main window title. Optional.</summary>
        public string? WindowTitle { get; set; }

        /// <summary>Executable image name (e.g. "SampleWpfApp" or "SampleWpfApp.exe"). Optional.</summary>
        public string? ProcessName { get; set; }

        /// <summary>True to force-kill the process (taskkill /F).</summary>
        public bool ForceTerminate { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>True if this step targets a control with no reliable
        /// AutomationId (resolved via WPFSpy at record time) — surfaced in
        /// the UI as a small badge, same signal as the real framework's
        /// self-healing locator demo.</summary>
        public bool NonStandard { get; set; }

        /// <summary>App context ID for multi-app recording. Null for single-app recordings.</summary>
        public string? AppId { get; set; }

        // Structured control-flow fields. They are empty for action nodes.
        public FlowCondition? Condition
        {
            get => _condition;
            set
            {
                if (_condition != null) _condition.PropertyChanged -= Condition_PropertyChanged;
                _condition = value;
                if (_condition != null) _condition.PropertyChanged += Condition_PropertyChanged;
                OnPropertyChanged();
            }
        }

        public int RepeatCount { get => _repeatCount; set { _repeatCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayVerb)); } }
        public string LoopVariable { get => _loopVariable; set { _loopVariable = value ?? "item"; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayVerb)); } }
        public FlowLoopSourceKind LoopSourceKind
        {
            get => _loopSourceKind;
            set { _loopSourceKind = value; OnPropertyChanged(); OnPropertyChanged(nameof(UsesNamedDataset)); OnPropertyChanged(nameof(DisplayVerb)); }
        }
        [JsonIgnore]
        public bool UsesNamedDataset
        {
            get => LoopSourceKind == FlowLoopSourceKind.NamedDataset;
            set => LoopSourceKind = value ? FlowLoopSourceKind.NamedDataset : FlowLoopSourceKind.LiteralValues;
        }
        public string? DatasetName { get => _datasetName; set { _datasetName = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayVerb)); } }
        public bool HasElse
        {
            get => _hasElse || ElseSteps.Count > 0;
            set
            {
                _hasElse = value;
                if (!value && ElseSteps.Count > 0)
                    ElseSteps.Clear();
                OnPropertyChanged();
            }
        }

        public ObservableCollection<string> LoopValues
        {
            get => _loopValues;
            set
            {
                if (_loopValues != null) _loopValues.CollectionChanged -= LoopValues_CollectionChanged;
                _loopValues = value ?? new ObservableCollection<string>();
                _loopValues.CollectionChanged += LoopValues_CollectionChanged;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LoopValuesText));
            }
        }

        [JsonIgnore]
        public string LoopValuesText
        {
            get => string.Join(Environment.NewLine, LoopValues);
            set
            {
                LoopValues.Clear();
                foreach (var item in (value ?? "").Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    LoopValues.Add(item);
                OnPropertyChanged();
            }
        }

        public ObservableCollection<RecordedStep> ThenSteps
        {
            get => _thenSteps;
            set { ReplaceChildren(ref _thenSteps, value); OnPropertyChanged(); }
        }

        public ObservableCollection<RecordedStep> ElseSteps
        {
            get => _elseSteps;
            set { ReplaceChildren(ref _elseSteps, value); OnPropertyChanged(); }
        }

        public ObservableCollection<RecordedStep> BodySteps
        {
            get => _bodySteps;
            set { ReplaceChildren(ref _bodySteps, value); OnPropertyChanged(); }
        }

        public string DisplayVerb => Kind switch
        {
            StepKind.Verify => "Verify Element Text",
            StepKind.VerifyOcr => "Get Data Grid Content Ocr",
            StepKind.VerifyEnabled => "Verify Element Enabled",
            StepKind.VerifyVisible => "Verify Element Visible",
            StepKind.VerifyContains => "Verify Element Contains Text",
            StepKind.VerifyRegex => "Verify Element Text Matches Regex",
            StepKind.VerifyAttribute => "Verify Element Attribute",
            StepKind.WaitExists => "Wait Until Element Exists",
            StepKind.WaitVisible => "Wait Until Element Visible",
            StepKind.WaitEnabled => "Wait Until Element Enabled",
            StepKind.WaitTextContains => "Wait Until Text Contains",
            StepKind.CheckpointProperty => "Property Checkpoint",
            StepKind.CheckpointArea => "Area Checkpoint (OCR)",
            StepKind.CheckpointImage => "Image Checkpoint",
            StepKind.CheckpointDataGrid => "DataGrid Checkpoint",
            StepKind.CheckpointCount => "Count Checkpoint",
            StepKind.CheckpointAttribute => "Attribute Checkpoint",
            StepKind.LaunchApplication => "Launch Application",
            StepKind.TerminateApplication => "Terminate Application",
            StepKind.If => "If / Else",
            StepKind.Repeat => $"Repeat {RepeatCount} time(s)",
            StepKind.ForEach => LoopSourceKind == FlowLoopSourceKind.NamedDataset
                ? $"For each {LoopVariable} in dataset '{DatasetName}'"
                : $"For each {LoopVariable} in {LoopValues.Count} value(s)",
            _ => Action switch
            {
                ActionKind.Invoke => "Click Element",
                ActionKind.SetValue => "Set Element Value",
                ActionKind.Toggle => "Toggle Element",
                ActionKind.DoubleClick => "Double Click Element",
                ActionKind.RightClick => "Right Click Element",
                ActionKind.DragDrop => "Drag And Drop",
                ActionKind.Hover => "Hover Over Element",
                ActionKind.PressKeys => "Press Keys",
                ActionKind.Scroll => "Scroll",
                ActionKind.SikuliClick => "OculiX Click (Image)",
                ActionKind.SikuliType => "OculiX Type (Image)",
                _ => "Unknown",
            },
        };

        /// <summary>Create a deep copy of this step for editing.</summary>
        public RecordedStep Clone()
        {
            return new RecordedStep
            {
                Kind = Kind,
                Alias = Alias,
                Action = Action,
                Value = Value,
                AttributeName = AttributeName,
                TargetAlias = TargetAlias,
                PropertyName = PropertyName,
                ExpectedCount = ExpectedCount,
                AppPath = AppPath,
                StartIn = StartIn,
                Args = Args,
                LaunchTimeout = LaunchTimeout,
                AutoAttach = AutoAttach,
                LaunchDriver = LaunchDriver,
                LaunchDriverList = LaunchDriverList != null ? new List<string>(LaunchDriverList) : null,
                SpyAgentEnabled = SpyAgentEnabled,
                PipeName = PipeName,
                WindowTitle = WindowTitle,
                ProcessName = ProcessName,
                ForceTerminate = ForceTerminate,
                Timestamp = Timestamp,
                NonStandard = NonStandard,
                AppId = AppId,
                Condition = Condition == null ? null : new FlowCondition
                {
                    Kind = Condition.Kind,
                    Alias = Condition.Alias,
                    ExpectedValue = Condition.ExpectedValue,
                    AppId = Condition.AppId,
                },
                RepeatCount = RepeatCount,
                LoopVariable = LoopVariable,
                LoopSourceKind = LoopSourceKind,
                DatasetName = DatasetName,
                HasElse = HasElse,
                LoopValues = new ObservableCollection<string>(LoopValues),
                ThenSteps = new ObservableCollection<RecordedStep>(ThenSteps.Select(step => step.CloneDeep())),
                ElseSteps = new ObservableCollection<RecordedStep>(ElseSteps.Select(step => step.CloneDeep())),
                BodySteps = new ObservableCollection<RecordedStep>(BodySteps.Select(step => step.CloneDeep())),
            };
        }

        public RecordedStep CloneDeep() => Clone();

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Condition_PropertyChanged(object? sender, PropertyChangedEventArgs e)
            => OnPropertyChanged(nameof(Condition));

        private void LoopValues_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(LoopValuesText));
            OnPropertyChanged(nameof(DisplayVerb));
        }

        private void ReplaceChildren(ref ObservableCollection<RecordedStep> target, ObservableCollection<RecordedStep>? value)
        {
            if (target != null) target.CollectionChanged -= Children_CollectionChanged;
            target = value ?? new ObservableCollection<RecordedStep>();
            target.CollectionChanged += Children_CollectionChanged;
            OnPropertyChanged(nameof(DisplayVerb));
        }

        private void Children_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(DisplayVerb));
            if (ReferenceEquals(sender, ElseSteps))
                OnPropertyChanged(nameof(HasElse));
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
