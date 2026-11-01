using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Text.Json;
using WpfTestIde.Execution;
using WpfTestIde.Helpers;
using WpfTestIde.Models;
using WpfTestIde.Recording;
using WpfTestIde.Themes;

namespace WpfTestIde.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private RecordingSession? _session;
        private GlobalMouseHook? _pickMouseHook;
        private readonly IScriptExecutionService _scriptExecutionService = new ScriptExecutionService();
        private CancellationTokenSource? _runCancellation;
        private string? _importedAppId;
        private readonly Dictionary<string, List<string>> _scriptDatasets = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<RecordedStep> _observedSteps = new();
        private readonly HashSet<ObservableCollection<RecordedStep>> _observedStepCollections = new();

        public ObservableCollection<RecordedStep> Steps { get; } = new();
        public ObservableCollection<string> ScriptDatasetNames { get; } = new();
        public IReadOnlyList<FlowConditionKind> FlowConditionKinds { get; } = Enum.GetValues<FlowConditionKind>();
        public IReadOnlyList<FlowLoopSourceKind> FlowLoopSourceKinds { get; } = Enum.GetValues<FlowLoopSourceKind>();
        public IReadOnlyList<ActionKind> ActionKinds { get; } = Enum.GetValues<ActionKind>();
        public ObservableCollection<ElementEntry> Elements { get; } = new();
        public ObservableCollection<string> RunOutputLines { get; } = new();
        public string RunOutputText { get => string.Join(Environment.NewLine, RunOutputLines); }

        // D4: structured mirror of RunOutputLines. The raw string collection above
        // is kept (A5 bottom tail binds to it) so existing Robot-line plumbing is
        // untouched; this second collection holds the parsed LogEntry that the
        // RESULTS-tab ListView binds to. Seeded in the ctor off the same
        // RunOutputLines.CollectionChanged signal so the two never get out of sync.
        public ObservableCollection<LogEntry> RunOutputLog { get; } = new();

        // E3: transient notification toasts. Single-slot queue (capped at 1
        // visible): a new arrival replaces any current toast. The XAML binds
        // <c>ActiveToasts[0].Text</c> / <c>ActiveToasts[0].Kind</c> and a single
        // INPC bool <see cref="IsToastVisible"/> gates the <c>ToastBar</c>
        // Border's <c>Visibility</c>. The two-phase removal (hide-then-dequeue
        // on the next dispatcher tick) keeps the <c>ActiveToasts[0]</c>
        // indexer binding from re-evaluating against an empty collection while
        // the Border is still in the visible tree � see the E3 risk register.
        public ObservableCollection<ToastMessage> ActiveToasts { get; } = new();

        private bool _isToastVisible;
        /// <summary>Single source of truth for the XAML's <c>ToastBar</c>
        /// <c>Visibility</c> binding (via <c>BoolToVisibilityConverter</c>).
        /// Set <see langword="false"/> before an active toast is removed from
        /// <see cref="ActiveToasts"/> so the indexer binding never sees the
        /// empty edge case while the Border is visible.</summary>
        public bool IsToastVisible
        {
            get => _isToastVisible;
            private set { _isToastVisible = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// E3: push a single transient toast, automatically removed
        /// after ~4 s. The queue is capped at one: a later arrival drops any
        /// existing toast (replace, not stack). All collection + visibility
        /// mutations marshal onto the dispatcher because this can be called
        /// from a ThreadPool thread (e.g. the
        /// <see cref="CheckPipeConnectionAsync"/> worker).</summary>
        public void EnqueueToast(string text, ToastKind kind)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                EnqueueToastCore(text, kind);
                return;
            }
            dispatcher.BeginInvoke(new Action(() => EnqueueToastCore(text, kind)));
        }

        // E3: the single outstanding toast-removal timer. Tracked as a field
        // (not a per-call local) so a replacement toast's Enqueue-toast can
        // Dispose the previous toast's still-pending timer � otherwise the
        // prior timer's 4-s callback fires against whatever toast is currently
        // shown and hides it early (the toast-overlap bug called out in the
        // E3 review).
        private System.Threading.Timer? _toastTimer;

        private void EnqueueToastCore(string text, ToastKind kind)
        {
            // Cancel any prior in-flight removal timer before starting a new
            // one � guarantees only one removal timer is outstanding at a
            // time, so it can never fire against a replacement toast.
            var oldTimer = _toastTimer;
            _toastTimer = null;
            oldTimer?.Dispose();

            // Two-phase hide-then-replace: collapse the Border first so the
            // ActiveToasts[0] indexer binding isn't re-evaluated against the
            // collection while we mutate it. The Visibility flip is flushed
            // via the deferred reveal below (a fresh dispatcher tick at
            // Render priority) so WPF actually sees a Collapsed?Visible
            // transition and the fade-in Storyboard re-fires per toast.
            IsToastVisible = false;

            // In-place swap when a toast is already showing � raises
            // NotifyCollectionChangedAction.Replace (not Reset) so the
            // ActiveToasts[0] indexer bindings never re-evaluate against a
            // 0-length collection (avoids the WPF trace binding errors that
            // Clear()+Add() would emit on the replace path).
            if (ActiveToasts.Count == 1)
            {
                ActiveToasts[0] = new ToastMessage(text, kind);
            }
            else
            {
                if (ActiveToasts.Count > 0) ActiveToasts.Clear();
                ActiveToasts.Add(new ToastMessage(text, kind));
            }

            // Defer the reveal one render tick so the Collapsed transition
            // flushes through the binding pipeline before Visibility flips
            // back to Visible (fixes both the fade-re-fire and the indexer-
            // binding-before-Visible-edge on the way out of a replacement).
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                IsToastVisible = true;
            }
            else
            {
                dispatcher.BeginInvoke(new Action(() => IsToastVisible = true),
                    System.Windows.Threading.DispatcherPriority.Render);
            }

            // One-shot 4-s removal. The Timer Disposes itself in a try/finally
            // that wraps the dispatcher hop (NOT inside the BeginInvoke
            // callback) so the native handle is always freed � even if the
            // Dispatcher is shutting down when the timer fires (window closing
            // mid-toast) and the queued callback is dropped/throws.
            System.Threading.Timer? timer = null;
            timer = new System.Threading.Timer(_ =>
            {
                try
                {
                    // Only act if this timer is still THE current one. A
                    // replacement toast Disposes this timer (above) and
                    // sets _toastTimer to its own � in that case this
                    // callback's captured `timer` ref equals the disposed
                    // old timer and we silently no-op.
                    if (timer != _toastTimer) return;

                    var d = Application.Current?.Dispatcher;
                    if (d is null)
                    {
                        IsToastVisible = false;
                        if (ActiveToasts.Count > 0) ActiveToasts.Clear();
                        return;
                    }
                    d.BeginInvoke(new Action(() =>
                    {
                        // Hide first; the Border collapses so the bound
                        // TextBlock + indexer go quiescent, then the ActiveToasts
                        // item is removed. Order matters � see the E3 risk
                        // register.
                        IsToastVisible = false;
                        if (ActiveToasts.Count > 0) ActiveToasts.Clear();
                    }));
                }
                finally
                {
                    // Dispose from the timer's own ThreadPool thread (NOT
                    // inside the BeginInvoke callback). try/finally guarantees
                    // the Dispose runs even if BeginInvoke throws because the
                    // Dispatcher is shutting down � no native-handle leak and
                    // the VM/ActiveToasts closure is release-able on window
                    // close.
                    timer?.Dispose();
                    if (_toastTimer == timer) _toastTimer = null;
                }
            }, null, TimeSpan.FromSeconds(4), TimeSpan.FromMilliseconds(-1));
            _toastTimer = timer;
        }

        /// <summary>Default view over <see cref="RunOutputLog"/> surfaced to the
        /// View. Filtered in-place by the Show* / search props below; re-evaluated
        /// whenever a filter input changes via <see cref="RefreshLogFilter"/>.
        /// Exposed as a property (not a method) so XAML can bind
        /// <c>ItemsSource="{Binding RunOutputFiltered}"</c> � a stable object ref
        /// through filter changes keeps the ListView from re-templating.</summary>
        public ICollectionView RunOutputFiltered { get; }

        // D4: filter-strip state. Three independent level toggles + a free-text
        // search. Each is the canonical INPC pattern already used by the other
        // VM props; setters call RefreshLogFilter so the live Robot stream filters
        // without re-running the collection source.
        private bool _showInfo = true;
        public bool ShowInfo { get => _showInfo; set { _showInfo = value; OnPropertyChanged(); RefreshLogFilter(); } }

        private bool _showWarn = true;
        public bool ShowWarn { get => _showWarn; set { _showWarn = value; OnPropertyChanged(); RefreshLogFilter(); } }

        private bool _showError = true;
        public bool ShowError { get => _showError; set { _showError = value; OnPropertyChanged(); RefreshLogFilter(); } }

        private string _logSearchText = "";
        public string LogSearchText
        {
            get => _logSearchText;
            set { _logSearchText = value; OnPropertyChanged(); RefreshLogFilter(); }
        }
        
        // Element Tree ViewModel
        public ElementTreeViewModel ElementTree { get; } = new();

        // Separate content per format � each tab edits its own text, Apply
        // syncs the active tab back into the Steps collection so all views
        // reflect the change when switching.
        private string _jsonScriptContent = "";
        public string JsonScriptContent
        {
            get => _jsonScriptContent;
            set { _jsonScriptContent = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        private string _pythonScriptContent = "";
        public string PythonScriptContent
        {
            get => _pythonScriptContent;
            set { _pythonScriptContent = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        private string _robotScriptContent = "";
        public string RobotScriptContent
        {
            get => _robotScriptContent;
            set { _robotScriptContent = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
        }

        private string _currentScriptFormat = "Steps";
        public string CurrentScriptFormat
        {
            get => _currentScriptFormat;
            set
            {
                if (_currentScriptFormat == value) return;
                _currentScriptFormat = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsScriptRunInProgress => _runCancellation != null;

        private string _repositoryJson = "{ \"elements\": {} }";
        public string RepositoryJson { get => _repositoryJson; set { _repositoryJson = value; OnPropertyChanged(); } }

        private bool _isRecording;
        public bool IsRecording { get => _isRecording; set { _isRecording = value; OnPropertyChanged(); OnPropertyChanged(nameof(RecordButtonLabel)); } }
        public string RecordButtonLabel => IsRecording ? "� Stop Recording" : "? Record";

        private bool _isAttached;
        public bool IsAttached { get => _isAttached; set { _isAttached = value; OnPropertyChanged(); } }

        private string _statusText = "Not attached � use \"Attach to Process...\" to begin.";
        public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

        private string _pipeStatusText = "";
        public string PipeStatusText { get => _pipeStatusText; set { _pipeStatusText = value; OnPropertyChanged(); } }

         private string _runSummaryText = "";
        public string RunSummaryText { get => _runSummaryText; set { _runSummaryText = value; OnPropertyChanged(); } }

         // Driver selection for test execution
        private string _selectedDriver = "Auto";
        public string SelectedDriver
        {
            get => _selectedDriver;
            set { _selectedDriver = value; OnPropertyChanged(); RegenerateScript(); }
        }
        public string[] AvailableDrivers { get; } = { "Auto", "FlaUI", "WPFSpy", "OculiX" };

         // Mode selection for test execution
        private string _selectedMode = "Auto";
        public string SelectedMode
        {
            get => _selectedMode;
            set { _selectedMode = value; OnPropertyChanged(); RegenerateScript(); }
        }
        public string[] AvailableModes { get; } = { "Auto", "mock", "real" };

        // ------------------------------------------------------------
        // Working Folder (B3): open a folder, browse its tree, and import
        // element JSON files into Elements or script files into Steps.
        // ------------------------------------------------------------
        private string _workingFolderPath = "";
        public string WorkingFolderPath
        {
            get => _workingFolderPath;
            set { _workingFolderPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasWorkingFolder)); }
        }
        public bool HasWorkingFolder => !string.IsNullOrWhiteSpace(WorkingFolderPath);

        private FolderTreeItem? _workingFolderRoot;
        public FolderTreeItem? WorkingFolderRoot
        {
            get => _workingFolderRoot;
            set { _workingFolderRoot = value; OnPropertyChanged(); }
        }

private FolderTreeItem? _workingFolderSelectedItem;
        public FolderTreeItem? WorkingFolderSelectedItem
        {
            get => _workingFolderSelectedItem;
            set
            {
                _workingFolderSelectedItem = value;
                // CanImportSelected is computed from this property, so raise
                // its change notification explicitly when the tree selection
                // changes (the XAML Import button's IsEnabled re-evaluates).
                foreach (var name in new[] { nameof(WorkingFolderSelectedItem), nameof(CanImportSelected) })
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
        public bool CanImportSelected => WorkingFolderSelectedItem != null && !WorkingFolderSelectedItem.IsDirectory;

        // Recording mode checkboxes (FlaUI, WPFSpy, Sikuli)
        private bool _recordFlaUI = true;
        public bool RecordFlaUI
        {
            get => _recordFlaUI;
            set { _recordFlaUI = value; OnPropertyChanged(); RegenerateScript(); }
        }

        private bool _recordWPFSpy = true;
        public bool RecordWPFSpy
        {
            get => _recordWPFSpy;
            set { _recordWPFSpy = value; OnPropertyChanged(); RegenerateScript(); }
        }

        private bool _recordSikuli = false;
        public bool RecordSikuli
        {
            get => _recordSikuli;
            set
            {
                _recordSikuli = value;
                OnPropertyChanged();
                RegenerateScript();
                if (_session != null)
                {
                    _session.RecordSikuli = _recordSikuli;
                }
            }
        }

        // Run mode checkboxes (FlaUI, WPFSpy, Sikuli)
        private bool _runFlaUI = true;
        public bool RunFlaUI
        {
            get => _runFlaUI;
            set { _runFlaUI = value; OnPropertyChanged(); RegenerateScript(); }
        }

        private bool _runWPFSpy = true;
        public bool RunWPFSpy
        {
            get => _runWPFSpy;
            set { _runWPFSpy = value; OnPropertyChanged(); RegenerateScript(); }
        }

        private bool _runSikuli = false;
        public bool RunSikuli
        {
            get => _runSikuli;
            set { _runSikuli = value; OnPropertyChanged(); RegenerateScript(); }
        }

        private double _sikuliSimilarity = 0.85;
        public double SikuliSimilarity
        {
            get => _sikuliSimilarity;
            set { _sikuliSimilarity = value; OnPropertyChanged(); }
        }

        private int _sikuliCapturePadding = 4;
        public int SikuliCapturePadding
        {
            get => _sikuliCapturePadding;
            set { _sikuliCapturePadding = value; OnPropertyChanged(); }
        }

        private string _sikuliMatcher = "multi";
        public string SikuliMatcher
        {
            get => _sikuliMatcher;
            set { _sikuliMatcher = value; OnPropertyChanged(); }
        }

        private string _sikuliScreenCapture = "mss";
        public string SikuliScreenCapture
        {
            get => _sikuliScreenCapture;
            set { _sikuliScreenCapture = value; OnPropertyChanged(); }
        }

        public ICommand MoveElementPriorityUpCommand { get; }
        public ICommand MoveElementPriorityDownCommand { get; }

        private bool _lastRunSuccess;
        public bool LastRunSuccess { get => _lastRunSuccess; set { _lastRunSuccess = value; OnPropertyChanged(); } }

        // Paths � defaults match this repo's layout when the IDE is run
        // from WpfTestIde/bin/.../ against the sibling WpfTestFramework checkout.
        public string FrameworkRoot { get; set; } = Path.GetFullPath(Path.Combine(System.AppContext.BaseDirectory, "..", "..", ".."));
        public string PipeName { get; set; } = "WPFSpyAgentPipe";
        public int SelectedProcessId { get; set; }

        // Multi-app support
        public ObservableCollection<WpfTestIde.Models.AppContext> AttachedApps { get; } = new();
        private WpfTestIde.Models.AppContext? _selectedApp;
        public WpfTestIde.Models.AppContext? SelectedApp
        {
            get => _selectedApp;
            set
            {
                _selectedApp = value;
                if (value != null && !string.IsNullOrEmpty(_importedAppId)
                    && !string.Equals(value.AppId, _importedAppId, StringComparison.OrdinalIgnoreCase))
                    _importedAppId = null;
                OnPropertyChanged();
                if (value != null)
                {
                    PipeName = value.PipeName;
                    SelectedProcessId = value.ProcessId;
                }
                RegenerateScript();
            }
        }

        private ElementEntry? _selectedElement;
        public ElementEntry? SelectedElement 
        { 
            get => _selectedElement; 
            set 
            { 
                _selectedElement = value; 
                OnPropertyChanged(); 
                if (value != null)
                {
                    EditingElement = value.Clone();
                }
                else
                {
                    EditingElement = null;
                }
            }
        }

        private ElementEntry? _editingElement;
        public ElementEntry? EditingElement
        {
            get => _editingElement;
            set
            {
                _editingElement = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEditingElement));
            }
        }
        public bool IsEditingElement => EditingElement != null;

        private bool _isPickModeActive;
        public bool IsPickModeActive { get => _isPickModeActive; set { _isPickModeActive = value; OnPropertyChanged(); OnPropertyChanged(nameof(PickButtonLabel)); } }
        public string PickButtonLabel => IsPickModeActive ? "� Cancel Pick" : "?? Pick Element";

        private string _previewText = "";
        public string PreviewText { get => _previewText; set { _previewText = value; OnPropertyChanged(); } }

private string _ocrResultText = "";
        public string OcrResultText
        {
            get => _ocrResultText;
            set
            {
                _ocrResultText = value;
                // The OCR Result expander was removed from the default layout (A3);
                // this property remains as a write-only sink so the OCR command can
                // still record its output without surfacing a UI element.
                OnPropertyChanged();
            }
        }

        // A4: Raw-JSON/repository panel collapsed by default so the ELEMENTS tab
        // tree+properties get maximum vertical space. Two-way so the user can
        // expand/collapse it at will. The YAML itself regenerates live regardless.
        private bool _repositoryPanelExpanded;
        public bool RepositoryPanelExpanded
        {
            get => _repositoryPanelExpanded;
            set
            {
                _repositoryPanelExpanded = value;
                OnPropertyChanged();
            }
        }

        // A5: bottom-docked Run Output tail panel, collapsed by default. Two-way so
        // the user can collapse it again. Auto-expanded in RunAsync when a run begins
        // (mirrors the OCR auto-expand on arrival pattern) � gives a glanceable live
        // tail across any tab without leaving the SCRIPTS/ELEMENTS view. This is a
        // duplicate of the RESULTS-tab txtRunOutput; that editor stays unchanged.
        private bool _runOutputPanelExpanded;
        public bool RunOutputPanelExpanded
        {
            get => _runOutputPanelExpanded;
            set
            {
                _runOutputPanelExpanded = value;
                OnPropertyChanged();
            }
        }

        public ICommand ToggleRecordingCommand { get; }
        public ICommand AttachCommand { get; }
        public ICommand AddVerificationCommand { get; }
        public ICommand AddStepCommand { get; }
        public ICommand DeleteStepCommand { get; }
        // D3: re-order recorded steps. Two explicit commands (up/down buttons) plus
        // a drag-drop path that calls MoveStep directly from MainWindow code-behind.
        public ICommand MoveStepUpCommand { get; }
        public ICommand MoveStepDownCommand { get; }
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
        public ICommand EditDatasetsCommand { get; }
        public ICommand RunCommand { get; }
        public ICommand CancelRunCommand { get; }
        public ICommand ExportRepositoryCommand { get; }
        public ICommand SaveExportScriptCommand { get; }
        public ICommand ApplyScriptCommand { get; }
        public ICommand ImportScriptCommand { get; }
        public ICommand LoadSampleCommand { get; }
        public ICommand ImportElementsCommand { get; }
        public ICommand ImportStepsCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand ResetLayoutCommand { get; }
        public ICommand CheckPipeConnectionCommand { get; }
        public ICommand AddElementCommand { get; }
        public ICommand EditElementCommand { get; }
        public ICommand SaveElementCommand { get; }
        public ICommand DeleteElementCommand { get; }
        public ICommand CancelEditElementCommand { get; }
	public ICommand PreviewElementCommand { get; }
        public ICommand PickElementCommand { get; }
        public ICommand GetDataGridContentOcrCommand { get; }   
        public ICommand OpenCheckpointWizardCommand { get; }    
        public ICommand OpenSpyToolCommand { get; }            
        public ICommand OpenVisualTestBuilderCommand { get; }  
        public ICommand OpenMultiAppDialogCommand { get; }
            public ICommand EnableLaunchAttachRecordingCommand { get; }

        // Working Folder commands
        public ICommand OpenWorkingFolderCommand { get; }
        public ICommand ImportWorkingFolderSelectedCommand { get; }
        public ICommand ShowWorkingFolderCommand { get; }
        public ICommand RefreshWorkingFolderCommand { get; }

        public MainViewModel()
        {
            ToggleRecordingCommand = new RelayCommand(_ => ToggleRecording(), _ => CanToggleRecording());
            AttachCommand = new RelayCommand(_ => Attach());
            AddVerificationCommand = new RelayCommand(param => AddVerification(param as RecordedStep));
            AddStepCommand = new RelayCommand(param => AddStep(param as RecordedStep));
            DeleteStepCommand = new RelayCommand(param => DeleteStep(param as RecordedStep));
            MoveStepUpCommand = new RelayCommand(param => MoveStep(param as RecordedStep, -1));
            MoveStepDownCommand = new RelayCommand(param => MoveStep(param as RecordedStep, +1));
            AddIfCommand = new RelayCommand(_ => AddFlowNode(new RecordedStep
            {
                Kind = StepKind.If,
                Condition = new FlowCondition { Alias = Elements.FirstOrDefault()?.Alias ?? "" },
                HasElse = true,
            }));
            AddRepeatCommand = new RelayCommand(_ => AddFlowNode(new RecordedStep { Kind = StepKind.Repeat, RepeatCount = 2 }));
            AddForEachCommand = new RelayCommand(_ => AddFlowNode(new RecordedStep
            {
                Kind = StepKind.ForEach,
                LoopVariable = "item",
                LoopValues = new ObservableCollection<string> { "value1" },
            }));
            AddThenStepCommand = new RelayCommand(param => AddChildStep(param as RecordedStep, elseBranch: false), param => param is RecordedStep { Kind: StepKind.If });
            AddElseStepCommand = new RelayCommand(param => AddChildStep(param as RecordedStep, elseBranch: true), param => param is RecordedStep { Kind: StepKind.If });
            AddLoopStepCommand = new RelayCommand(param => AddChildStep(param as RecordedStep, elseBranch: false), param => param is RecordedStep { Kind: StepKind.Repeat or StepKind.ForEach });
            AddNestedIfCommand = new RelayCommand(param => AddNestedFlowNode(param as RecordedStep, StepKind.If, elseBranch: false));
            AddNestedRepeatCommand = new RelayCommand(param => AddNestedFlowNode(param as RecordedStep, StepKind.Repeat, elseBranch: false));
            AddNestedForEachCommand = new RelayCommand(param => AddNestedFlowNode(param as RecordedStep, StepKind.ForEach, elseBranch: false));
            AddElseIfCommand = new RelayCommand(param => AddNestedFlowNode(param as RecordedStep, StepKind.If, elseBranch: true));
            AddElseRepeatCommand = new RelayCommand(param => AddNestedFlowNode(param as RecordedStep, StepKind.Repeat, elseBranch: true));
            AddElseForEachCommand = new RelayCommand(param => AddNestedFlowNode(param as RecordedStep, StepKind.ForEach, elseBranch: true));
            EditDatasetsCommand = new RelayCommand(_ => EditScriptDatasets());
            RunCommand = new AsyncRelayCommand(_ => RunAsync(), _ => CanRunCurrentScript());
            CancelRunCommand = new RelayCommand(_ => _runCancellation?.Cancel(), _ => _runCancellation != null);
            ExportRepositoryCommand = new RelayCommand(_ => ExportRepository());
            SaveExportScriptCommand = new RelayCommand(_ => SaveExportScript());
            ApplyScriptCommand = new RelayCommand(_ => ApplyScript());
            ImportScriptCommand = new RelayCommand(_ => ImportScript());
            LoadSampleCommand = new RelayCommand(_ => LoadSample());
            ImportElementsCommand = new RelayCommand(_ => ImportElements());
            ImportStepsCommand = new RelayCommand(_ => ImportSteps());
            ResetCommand = new RelayCommand(_ => Reset());
            ResetLayoutCommand = new RelayCommand(_ => ResetLayout());
            CheckPipeConnectionCommand = new AsyncRelayCommand(_ => CheckPipeConnectionAsync(), _ => IsAttached);
            AddElementCommand = new RelayCommand(_ => AddElement());
            EditElementCommand = new RelayCommand(param => EditElement(param as ElementEntry));
            SaveElementCommand = new RelayCommand(_ => SaveElement());
            DeleteElementCommand = new RelayCommand(param => DeleteElement(param as ElementEntry));
            CancelEditElementCommand = new RelayCommand(_ => CancelEditElement());
            PreviewElementCommand = new RelayCommand(_ => PreviewElement());
            PickElementCommand = new RelayCommand(_ => TogglePickMode(), _ => IsAttached);
            GetDataGridContentOcrCommand = new RelayCommand(async _ => await GetDataGridContentOcr(), _ => IsAttached);
            OpenCheckpointWizardCommand = new RelayCommand(_ => OpenCheckpointWizard());
            OpenSpyToolCommand = new RelayCommand(_ => OpenSpyTool());
            OpenVisualTestBuilderCommand = new RelayCommand(_ => OpenVisualTestBuilder());
            OpenMultiAppDialogCommand = new RelayCommand(_ => OpenMultiAppDialog(), _ => AttachedApps.Count > 0);
            EnableLaunchAttachRecordingCommand = new RelayCommand(_ => EnableRecordingForLaunchedApps(), _ => EnumerateSteps(Steps).Any(s => s.Kind == StepKind.LaunchApplication && s.AutoAttach));
            OpenWorkingFolderCommand = new RelayCommand(_ => OpenWorkingFolder());
            ImportWorkingFolderSelectedCommand = new RelayCommand(_ => ImportWorkingFolderSelected(), _ => CanImportSelected);
            ShowWorkingFolderCommand = new RelayCommand(_ => ShowWorkingFolder());
            RefreshWorkingFolderCommand = new RelayCommand(_ => RefreshWorkingFolder());
            MoveElementPriorityUpCommand = new RelayCommand(_ => MoveElementPriorityUp(), _ => EditingElement != null && EditingElement.DriverPriority != null && EditingElement.DriverPriority.Any());
            MoveElementPriorityDownCommand = new RelayCommand(_ => MoveElementPriorityDown(), _ => EditingElement != null && EditingElement.DriverPriority != null && EditingElement.DriverPriority.Any());

            // Menu commands
            ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
            ShowElementsCommand = new RelayCommand(_ => SelectedTabIndex = 0);
            ShowScriptsCommand = new RelayCommand(_ => SelectedTabIndex = 1);
            ShowResultsCommand = new RelayCommand(_ => SelectedTabIndex = 2);
            ShowSearchCommand = new RelayCommand(_ => { /* TODO: implement search */ });
            OpenSettingsCommand = new RelayCommand(_ =>
            {
                var dlg = new Dialogs.SikuliSettingsDialog
                {
                    Owner = Application.Current.MainWindow,
                    Similarity = SikuliSimilarity,
                    CapturePaddingPx = SikuliCapturePadding,
                    Matcher = SikuliMatcher,
                    ScreenCapture = SikuliScreenCapture,
                };
                if (dlg.ShowDialog() == true)
                {
                    SikuliSimilarity = dlg.Similarity;
                    SikuliCapturePadding = dlg.CapturePaddingPx;
                    SikuliMatcher = dlg.Matcher;
                    SikuliScreenCapture = dlg.ScreenCapture;
                }
            });
            ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());
            AboutCommand = new RelayCommand(_ => ShowAbout());
            DocumentationCommand = new RelayCommand(_ => { /* TODO: open docs */ });
            OpenScriptCommand = new RelayCommand(_ => { /* TODO: open script */ });
            UndoCommand = new RelayCommand(_ => { /* TODO: implement undo */ });
            RedoCommand = new RelayCommand(_ => { /* TODO: implement redo */ });
            StopRecordingCommand = new RelayCommand(_ => ToggleRecording(), _ => IsAttached && IsRecording);

            Steps.CollectionChanged += Steps_CollectionChanged;
            RefreshStepSubscriptions();
            Elements.CollectionChanged += (_, __) => { RegenerateRepository(); RefreshElementTree(); };
            RunOutputLines.CollectionChanged += (_, __) => OnPropertyChanged(nameof(RunOutputText));
            // D4: also parse each new raw line into RunOutputLog. Reset tag handler
            // (sender == Collection clearing) replays nothing here because we
            // separately clear RunOutputLog in Reset()/RunAsync below. Insert of
            // a single line at the end ? LogLineParser.Parse + add to RunOutputLog;
            // batch scenarios are not produced by the current Robot runner.
            RunOutputLines.CollectionChanged += (sender, args) =>
            {
                if (sender != RunOutputLines) return;
                if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) return;
                if (args.NewItems != null)
                {
                    foreach (string line in args.NewItems)
                    {
                        RunOutputLog.Add(LogLineParser.Parse(line));
                    }
                }
            };

            // D4: filtered view wired once in the ctor. Predicate uses the current
            // state of the three Show* props + LogSearchText; RefreshLogFilter
            // re-pumps the view whenever any of those change.
            RunOutputFiltered = CollectionViewSource.GetDefaultView(RunOutputLog);
            RunOutputFiltered.Filter = FilterLogEntry;
            
            // Initialize element tree
            RefreshElementTree();
        }
        
        private void RefreshElementTree()
        {
            ElementTree.LoadFromElements(Elements);
        }

        /// <summary>D4 filter predicate applied by RunOutputFiltered. exhibited
        /// when Level matches an enabled checkbox AND LogSearchText (if any) is
        /// contained in Raw. Raw rather than Message because the search strip is
        /// used to find timestamps/source control-characters too.</summary>
        private bool FilterLogEntry(object obj)
        {
            if (obj is not LogEntry e) return false;
            if (!LevelVisible(e.Level)) return false;
            if (string.IsNullOrWhiteSpace(LogSearchText)) return true;
            return (e.Raw ?? "").IndexOf(LogSearchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Maps the three Show* checkboxes to LogLevels. TRACE/DEBUG are
        /// rolled under ShowInfo on purpose � Robot's quiet trace/debug lines are
        /// noise most of the time and the DM filter strip only exposes Info/Warn/
        /// Error. Raw lines (unstructured Robot output: separator bars, blank
        /// lines, ASCII boxes) are shown when ShowInfo is on, hidden otherwise;
        /// that matches the RESULTS tab's pre-D4 behavior where they appeared
        /// unconditionally.</summary>
        private bool LevelVisible(LogLevel level) => level switch
        {
            LogLevel.Info => ShowInfo,
            LogLevel.Raw => ShowInfo,
            LogLevel.Trace => ShowInfo,
            LogLevel.Debug => ShowInfo,
            LogLevel.Warn => ShowWarn,
            LogLevel.Error => ShowError,
            LogLevel.Fail => ShowError,
            _ => true,
        };

        /// <summary>Re-evaluates the RunOutputFiltered predicate. Called on every
        /// Show*/LogSearchText setter. CollectionViewSource default view re-raises
        /// Filter on every item on Refresh, but log counts are small (Robot runs
        /// are capped ~hundreds of lines), so the cost is negligible.</summary>
        private void RefreshLogFilter()
        {
            RunOutputFiltered?.Refresh();
        }

        // ------------------------------------------------------------
        // Attach / Record
        // ------------------------------------------------------------
        private void Attach()
        {
            var dialog = new Dialogs.AttachToProcessDialog();
            if (dialog.ShowDialog() != true || dialog.SelectedProcessId is null)
            {
                return;
            }

            // Create app context for multi-app support
            // Default app_id is the process name. If another attached app
            // already has the same name, append the window title (with
            // whitespace stripped) so the disambiguated id is still stable
            // for the lifetime of the recording.
            string baseAppId = ComputeBaseAppId(dialog.SelectedProcessId.Value, dialog.ApplicationPath);
            string appId = string.IsNullOrWhiteSpace(dialog.AppId)
                ? ResolveUniqueAppId(baseAppId, dialog.SelectedProcessId.Value)
                : dialog.AppId.Trim();

            string appName = string.IsNullOrWhiteSpace(dialog.ApplicationPath)
                ? $"Process-{dialog.SelectedProcessId.Value}"
                : Path.GetFileNameWithoutExtension(dialog.ApplicationPath);

            var appContext = new WpfTestIde.Models.AppContext
            {
                AppId = appId,
                AppName = appName,
                Driver = dialog.DriverList != null && dialog.DriverList.Any() ? dialog.DriverList[0] : "WPFSpy",
                DriverList = dialog.DriverList,
                ProcessId = dialog.SelectedProcessId.Value,
                PipeName = dialog.PipeName,
                AppPath = dialog.ApplicationPath ?? "",
                IsAttached = true,
                IsDefault = AttachedApps.Count == 0,
            };

            // Dispose existing session for this app if re-attaching
            var existingApp = AttachedApps.FirstOrDefault(a => a.AppId == appId);
            if (existingApp != null)
            {
                AttachedApps.Remove(existingApp);
            }

            AttachedApps.Add(appContext);
            SelectedApp = appContext;

            // Multi-app session: keep the same session and add this app as a target.
            if (_session is null)
            {
                _session = new RecordingSession();
                _session.StepCaptured += OnStepCaptured;
                _session.RecordSikuli = RecordSikuli;
            }
            _session.RemoveTarget(appId);
            var attachMode = dialog.SpyAgentEnabled ? ProbeMode.WPFSpy : ProbeMode.FlaUI;
            _session.AddTarget(new RecordingTarget(
                appId: appId,
                pipeName: dialog.PipeName,
                processId: dialog.SelectedProcessId.Value,
                exeName: dialog.ApplicationPath,
                mode: attachMode));

            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "repository", "attach_log.txt");
            try
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Attach: FrameworkRoot={FrameworkRoot}, PipeName={PipeName}, AppId={appId}{Environment.NewLine}");
            }
            catch { }

            RepositoryLookup.EnsureLoaded(FrameworkRoot);

            IsAttached = true;
            StatusText = $"Attached to {appName} (PID {dialog.SelectedProcessId}) � ready to record.";
        }

        public void DetachApplication(string appId)
        {
            var app = AttachedApps.FirstOrDefault(a => a.AppId == appId);
            if (app == null) return;

            AttachedApps.Remove(app);
            _session?.RemoveTarget(appId);

            if (SelectedApp?.AppId == appId)
            {
                SelectedApp = AttachedApps.FirstOrDefault();
                if (SelectedApp != null)
                {
                    PipeName = SelectedApp.PipeName;
                    SelectedProcessId = SelectedApp.ProcessId;
                }
                else
                {
                    IsAttached = false;
                    StatusText = "No applications attached.";
                }
            }

            StatusText = $"Detached from {app.AppName} (PID {app.ProcessId}).";
        }

        /// <summary>
        /// Parse the script and find Launch Application steps with attach=Yes.
        /// For each such step, create an AppContext and enable recording.
        /// This allows recording multiple application scenarios when attach is chosen
        /// in the launch application step.
        /// </summary>
        public void EnableRecordingForLaunchedApps()
        {
            // Find all Launch Application steps with AutoAttach=true
            var launchSteps = EnumerateSteps(Steps).Where(s => s.Kind == StepKind.LaunchApplication && s.AutoAttach).ToList();

            if (!launchSteps.Any())
            {
                StatusText = "No Launch Application steps with attach=Yes found. Record button requires at least one attached app.";
                return;
            }

            // Clear existing launched apps (keep manually attached ones)
            var launchedApps = AttachedApps.Where(a => a.AppPath != null && !string.IsNullOrEmpty(a.AppPath)).ToList();
            foreach (var app in launchedApps)
            {
                AttachedApps.Remove(app);
            }

            // Create AppContext for each launched application
            foreach (var step in launchSteps)
            {
                if (string.IsNullOrWhiteSpace(step.AppPath))
                    continue;

                string appId = step.AppId ?? ComputeBaseAppId(0, step.AppPath);
                string appName = System.IO.Path.GetFileNameWithoutExtension(step.AppPath);
                string pipeName = step.PipeName ?? $"WPFSpyAgentPipe_{appId}";

                // Use FlaUI driver when spy agent is disabled (e.g. Notepad, Calc),
                // WPFSpy when the agent is enabled. Prefer LaunchDriverList if set.
                string driver = step.SpyAgentEnabled
                    ? (step.LaunchDriverList != null && step.LaunchDriverList.Any() ? step.LaunchDriverList[0] : (step.LaunchDriver ?? "WPFSpy"))
                    : "FlaUI";

                var appContext = new WpfTestIde.Models.AppContext
                {
                    AppId = appId,
                    AppName = appName,
                    Driver = driver,
                    DriverList = step.LaunchDriverList,
                    ProcessId = 0, // Will be set after launch
                    PipeName = pipeName,
                    AppPath = step.AppPath,
                    IsAttached = true,
                    IsDefault = AttachedApps.Count == 0,
                };

                // Remove existing app with same ID
                var existingApp = AttachedApps.FirstOrDefault(a => a.AppId == appId);
                if (existingApp != null)
                {
                    AttachedApps.Remove(existingApp);
                }

                AttachedApps.Add(appContext);
            }

            // Select the first launched app
            SelectedApp = AttachedApps.FirstOrDefault();
            if (SelectedApp != null)
            {
                PipeName = SelectedApp.PipeName;
            }

            // Multi-app recording session: one session, one target per attached app.
            // Use WPFSpy probe when spy agent is enabled, else FlaUI/UIA.
            if (_session is null)
            {
                _session = new RecordingSession();
                _session.StepCaptured += OnStepCaptured;
                _session.RecordSikuli = RecordSikuli;
            }
            else
            {
                _session.ClearTargets();
            }

            int priority = 0;
            foreach (var app in AttachedApps)
            {
                var matchingStep = launchSteps.FirstOrDefault(s =>
                    (s.AppId ?? System.IO.Path.GetFileNameWithoutExtension(s.AppPath ?? "").ToLowerInvariant()) == app.AppId);
                bool useFlaUI = matchingStep != null && !matchingStep.SpyAgentEnabled;
                
                _session.AddTarget(new RecordingTarget(
                    appId: app.AppId,
                    pipeName: app.PipeName,
                    processId: app.ProcessId,
                    exeName: app.AppPath,
                    mode: useFlaUI ? ProbeMode.FlaUI : ProbeMode.WPFSpy,
                    priority: priority++));
            }

            IsAttached = true;
            int wpfCount = _session.Targets.Count(t => t.Mode == ProbeMode.WPFSpy);
            int flaCount = _session.Targets.Count(t => t.Mode == ProbeMode.FlaUI);
            string modeLabel = (wpfCount > 0 && flaCount > 0)
                ? $"{wpfCount} WPFSpy + {flaCount} FlaUI"
                : (flaCount > 0 ? "FlaUI/UIA" : "WPFSpy");
            StatusText = $"Ready to record ({modeLabel} mode). {AttachedApps.Count} application(s) configured. Run the script to launch and attach.";
        }

        private void LogDiagnostic(string message)
        {
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "repository", "attach_log.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { }
        }

        public void SetDefaultApplication(string appId)
        {
            foreach (var app in AttachedApps)
            {
                app.IsDefault = app.AppId == appId;
            }

            var selected = AttachedApps.FirstOrDefault(a => a.AppId == appId);
            if (selected != null)
            {
                SelectedApp = selected;
                PipeName = selected.PipeName;
                SelectedProcessId = selected.ProcessId;
            }
        }

        private void OpenMultiAppDialog()
        {
            var dialog = new Dialogs.MultiAppDialog(this);
            dialog.ShowDialog();
        }

        /// <summary>
        /// Returns the canonical "base" app id for a process: the executable
        /// name without extension (e.g. "notepad", "SampleWpfApp"). For
        /// runtime attach (no appPath), derives the name from the live
        /// process via <see cref="TryGetProcessExeStem"/>; only falls back
        /// to "app_{pid}" when no name is resolvable (e.g. protected
        /// process, access denied, or process already exited). This is the
        /// stable id used by the user; uniqueness across multiple attached
        /// processes with the same name is resolved by
        /// <see cref="ResolveUniqueAppId"/>.
        /// </summary>
        private static string ComputeBaseAppId(int processId, string? appPath)
        {
            if (!string.IsNullOrWhiteSpace(appPath))
            {
                string name = Path.GetFileNameWithoutExtension(appPath);
                if (!string.IsNullOrEmpty(name)) return name;
            }
            string liveStem = TryGetProcessExeStem(processId);
            if (!string.IsNullOrEmpty(liveStem)) return liveStem;
            return $"app_{processId}";
        }

        /// <summary>
        /// Returns the executable name (without extension) of the given
        /// process. Prefers <c>MainModule.FileName</c> (full path) and
        /// falls back to <c>ProcessName</c> when the module is not
        /// accessible (e.g. elevated / system processes). Returns an
        /// empty string if the process is no longer running or the info
        /// can't be read.
        /// </summary>
        private static string TryGetProcessExeStem(int processId)
        {
            try
            {
                using var p = Process.GetProcessById(processId);
                try
                {
                    string? modulePath = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(modulePath))
                    {
                        string stem = Path.GetFileNameWithoutExtension(modulePath);
                        if (!string.IsNullOrEmpty(stem)) return stem;
                    }
                }
                catch { /* MainModule can throw for protected processes */ }

                string procName = p.ProcessName;
                return string.IsNullOrEmpty(procName) ? "" : procName;
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Returns an app_id that is unique across the currently attached
        /// apps. Starts from <paramref name="baseAppId"/>; if a sibling
        /// attached app already uses that id, appends the target process's
        /// main window title (with whitespace stripped) so the new id is
        /// both unique and stable. The user-supplied dialog.AppId (when
        /// non-empty) bypasses this resolution.
        /// </summary>
        private string ResolveUniqueAppId(string baseAppId, int processId)
        {
            if (!AttachedApps.Any(a => string.Equals(a.AppId, baseAppId, StringComparison.OrdinalIgnoreCase)))
            {
                return baseAppId;
            }
            string title = TryGetProcessMainWindowTitle(processId);
            string titleSlug = SanitizeForAppId(title);
            if (string.IsNullOrEmpty(titleSlug))
            {
                return $"{baseAppId}_{processId}";
            }
            return $"{baseAppId}_{titleSlug}";
        }

        /// <summary>
        /// Returns the main window title of the given process, or an empty
        /// string if the process is no longer running or has no main window.
        /// </summary>
        private static string TryGetProcessMainWindowTitle(int processId)
        {
            try
            {
                using var p = Process.GetProcessById(processId);
                return p.MainWindowTitle ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Strips whitespace and characters that are illegal in a Robot
        /// Framework variable / argument so the title can be safely used
        /// as part of an app_id (and as a directory name downstream).
        /// </summary>
        private static string SanitizeForAppId(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var sb = new System.Text.StringBuilder(value.Length);
            foreach (char ch in value)
            {
                if (char.IsWhiteSpace(ch)) continue;
                if (ch is '_' or '-') { sb.Append(ch); continue; }
                if (char.IsLetterOrDigit(ch)) { sb.Append(ch); continue; }
                // Collapse everything else to '_' so titles like
                // "Sample WPF App - Login" become "SampleWPFApp-Login"
                // rather than dropping characters entirely.
                sb.Append('_');
            }
            return sb.ToString().Trim('_');
        }

        /// <summary>
        /// E3: pipe-connection probe offloaded onto a ThreadPool thread so the
        /// UI thread stays responsive for the ~3 s the 3-attempt loop takes
        /// (the only behaviour change vs. the previous sync probe is that the
        /// status bar no longer freezes; the operator-observable per-attempt
        /// <see cref="PipeStatusText"/> messages are identical). Runs the
        /// 3-attempt loop + the synchronous <see cref="SpyAgentClient"/>
        /// <c>Send</c> + the 1-second back-off sleeps on a ThreadPool thread.
        /// All <see cref="PipeStatusText"/> setter writes (and the
        /// <see cref="EnqueueToast"/> calls) marshal back onto the dispatcher �
        /// the worker thread must not touch the bound string property directly.
        /// </summary>
        private async System.Threading.Tasks.Task CheckPipeConnectionAsync()
        {
            if (string.IsNullOrEmpty(PipeName))
            {
                Application.Current.Dispatcher.Invoke(() => PipeStatusText = "No pipe name configured.");
                return;
            }

            const int maxAttempts = 3;
            const int delayMs = 1000;
            string pipeName = PipeName; // capture before crossing thread boundary
            string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pipe_check_log.txt");

            (string message, bool ok, string? data) result =
                await System.Threading.Tasks.Task.Run<(string message, bool ok, string? data)>(async () =>
            {
                void Log(string msg)
                {
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
                }

                // The pipe responses (response.Data, response.Error, the
                // message of any exception thrown by SpyAgentClient) come from
                // the SpyAgent server which runs in the *attached target
                // process* � a separate trust boundary. They are
                // operator-readable here but their CRLF contents are not � a
                // hostile or compromised attached process could otherwise
                // forge arbitrary lines into the audit log
                // (pipe_check_log.txt) by embedding \r\n. Strip control
                // characters before interpolating into any log line so each
                // untrusted segment renders on a single physical line.
                static string SanitizeForLog(string? s) =>
                    (s ?? string.Empty)
                        .Replace("\r", "\\r")
                        .Replace("\n", "\\n")
                        .Replace("\0", "\\0");

                Log($"=== Pipe check started, pipe={pipeName} ===");

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        Log($"Attempt {attempt}/{maxAttempts}: connecting...");
                        var client = new SpyAgentClient(pipeName);
                        var response = client.Send("GetMainWindowTitle");
                        Log($"Attempt {attempt}: response => Success={response.Success}, Data={SanitizeForLog(response.Data ?? "(null)")}, Error={SanitizeForLog(response.Error ?? "(null)")}");

                        if (response.Success && !string.IsNullOrEmpty(response.Data))
                        {
                            Log($"Result: Pipe OK � attached app main window: {SanitizeForLog(response.Data)}");
                            return ($"Pipe OK � attached app main window: {response.Data}", true, response.Data);
                        }
                        if (response.Success)
                        {
                            // Log parity with the original sync
                            // CheckPipeConnection: the "Result:" line was
                            // emitted on both the data and no-data success
                            // branches; restore it here.
                            Log("Result: Pipe OK � attached app has no main window title.");
                            return ("Pipe OK � attached app has no main window title.", true, null);
                        }
                        Log($"Result: Pipe check failed (attempt {attempt}/{maxAttempts}): {SanitizeForLog(response.Error ?? "unknown error")}");
                        Application.Current.Dispatcher.Invoke(() =>
                            PipeStatusText = $"Pipe check failed (attempt {attempt}/{maxAttempts}): {response.Error ?? "unknown error"}");
                    }
                    catch (Exception ex)
                    {
                        Log($"Exception: {ex.GetType().Name}: {SanitizeForLog(ex.Message)}");
                        Application.Current.Dispatcher.Invoke(() =>
                            PipeStatusText = $"Pipe check failed (attempt {attempt}/{maxAttempts}): {ex.Message}");
                    }

                    if (attempt < maxAttempts)
                    {
                        await System.Threading.Tasks.Task.Delay(delayMs);
                    }
                }

                Log("=== Pipe check finished ===");
                return ($"Pipe check failed (attempt {maxAttempts}/{maxAttempts})", false, null);
            });

            // Single dispatcher hop to commit the final status text + toast.
            // Both the success and failure paths funnel through here so the
            // worker thread's only UI-thread touches are these marshaled writes.
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (result.ok)
                {
                    PipeStatusText = result.message;
                    // Mirror the status text exactly: on the empty-data
                    // success branch result.message is "Pipe OK � attached
                    // app has no main window title." (NOT a "connected"
                    // fallback) so the toast and the status bar don't
                    // contradict each other.
                    EnqueueToast(result.message, ToastKind.Success);
                }
                else
                {
                    // The per-attempt failure string was already written inside
                    // the loop above; leave PipeStatusText on the last attempt.
                    EnqueueToast("Pipe check failed", ToastKind.Warning);
                }
            });
        }

        private bool CanToggleRecording() => true;

        private void ToggleRecording()
        {
            if (_session is null)
            {
                _session = new RecordingSession();
                _session.StepCaptured += OnStepCaptured;
                _session.RecordSikuli = RecordSikuli;
            }

            if (IsRecording)
            {
                _session.Stop();
                IsRecording = false;
                StatusText = "Recording stopped.";
            }
            else
            {
                // Auto-register any running target apps if none are attached
                // yet. This supports "launch the app manually, open the IDE,
                // press Record" without a prior explicit Attach step.
                if (_session.Targets.Count == 0 && !IsAttached)
                {
                    var registered = _session.TryAutoRegisterRunningApps();
                    if (registered.Count > 0)
                    {
                        foreach (var target in registered)
                        {
                            var appContext = new WpfTestIde.Models.AppContext
                            {
                                AppId = target.AppId,
                                AppName = Path.GetFileNameWithoutExtension(target.ExeName) ?? target.AppId,
                                Driver = target.Mode == ProbeMode.WPFSpy ? "WPFSpy" : "FlaUI",
                                DriverList = target.Mode == ProbeMode.WPFSpy
                                    ? new List<string> { "WPFSpy", "FlaUI" }
                                    : new List<string> { "FlaUI" },
                                ProcessId = target.ProcessId,
                                PipeName = target.PipeName,
                                AppPath = target.ExeName ?? "",
                                IsAttached = true,
                                IsDefault = AttachedApps.Count == 0,
                            };
                            AttachedApps.Add(appContext);
                        }

                        IsAttached = true;
                        SelectedApp = AttachedApps.FirstOrDefault();
                        if (SelectedApp != null)
                        {
                            PipeName = SelectedApp.PipeName;
                            SelectedProcessId = SelectedApp.ProcessId;
                        }

                        bool allSpy = registered.All(t => t.Mode == ProbeMode.WPFSpy);
                        string modeLabel = allSpy ? "WPFSpy"
                            : registered.Any(t => t.Mode == ProbeMode.WPFSpy)
                                ? "WPFSpy + FlaUI"
                                : "FlaUI/UIA";
                        StatusText = $"Auto-attached to {registered.Count} application(s) ({modeLabel}). Ready to record.";
                    }
                }

                if (_session.Targets.Count == 0)
                {
                    StatusText = "No running WPF application detected. Launch Sample WPF first, then press Record.";
                    return;
                }

                // Ensure IsAttached reflects the session state when targets
                // were auto-registered via a click before Record was pressed.
                if (!IsAttached)
                {
                    IsAttached = true;
                    SelectedApp = AttachedApps.FirstOrDefault();
                    if (SelectedApp != null)
                    {
                        PipeName = SelectedApp.PipeName;
                        SelectedProcessId = SelectedApp.ProcessId;
                    }
                }

                _session.Start();
                IsRecording = true;
                StatusText = "Recording — interact with the application now.";
            }
        }

        private void OnStepCaptured(RecordedStep step, ElementEntry entry)
        {
            // Marshal back to the UI thread � the mouse hook and FlaUI
            // focus events fire on non-UI threads.
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Try to resolve the recorded alias to a canonical repository
                // alias so generated scripts play back against the existing
                // Element Repository without manual reconciliation.
                string resolvedAlias = RepositoryLookup.ResolveAlias(entry.AutomationId, entry.Name) ?? step.Alias;
                
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "repository", "attach_log.txt");
                try
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] OnStepCaptured: step.Alias={step.Alias}, entry.AutomationId={entry.AutomationId}, entry.Name={entry.Name}, resolvedAlias={resolvedAlias}{Environment.NewLine}");
                }
                catch { }

                step = new RecordedStep
                {
                    Kind = step.Kind,
                    Alias = resolvedAlias,
                    Action = step.Action,
                    Value = step.Value,
                    AttributeName = step.AttributeName,
                    TargetAlias = step.TargetAlias,
                    PropertyName = step.PropertyName,
                    ExpectedCount = step.ExpectedCount,
                    NonStandard = step.NonStandard,
                    AppId = step.AppId,
                };

                // Choose driver priority based on which probe mode the session is using.
                // For FlaUI-resolved elements, WPFSpy is meaningless (no spy agent in the app).
                // For WPFSpy-resolved elements, the full [FlaUI, WPFSpy, Sikuli] priority applies.
                List<string> driverPriority;
                List<string>? recordingModes = entry.RecordingModes;
                if (recordingModes != null && recordingModes.Count > 0)
                {
                    driverPriority = new List<string>(recordingModes);
                }
                else
                {
                    // Default to whatever the session is currently using.
                    driverPriority = new List<string>(GetSelectedRecordingModes());
                    if (driverPriority.Count == 0)
                    {
                        driverPriority = new List<string> { "FlaUI", "WPFSpy", "Sikuli" };
                    }
                }

                entry = new ElementEntry
                {
                    Alias = resolvedAlias,
                    DisplayName = entry.DisplayName,
                    ControlType = entry.ControlType,
                    AutomationId = entry.AutomationId,
                    Name = entry.Name,
                    XPath = entry.XPath,
                    RecordingModes = recordingModes,
                    DriverPriority = driverPriority,
                    ImagePath = entry.ImagePath,
                };

                Steps.Add(step);
                if (!Elements.Any(e => e.Alias == entry.Alias))
                {
                    Elements.Add(entry);
                }
            });
        }

        // ------------------------------------------------------------
        // Verification / step management
        // ------------------------------------------------------------
        private void AddVerification(RecordedStep? afterStep)
        {
            try
            {
                var dialog = new Dialogs.AddStepWizardDialog(Elements.ToList(), PipeName);
                dialog.StepTypeCombo.SelectedIndex = 3; // Verify Element Text
                if (dialog.ShowDialog() != true || dialog.CreatedStep == null)
                {
                    return;
                }

                InsertStepAfter(afterStep, dialog.CreatedStep);
            }
            catch (Exception ex)
            {
                StatusText = $"Add verification error: {ex.Message}";
            }
        }

        private void AddStep(RecordedStep? afterStep)
        {
            try
            {
                var dialog = new Dialogs.AddStepWizardDialog(Elements.ToList(), PipeName);
                if (dialog.ShowDialog() != true || dialog.CreatedStep == null)
                {
                    return;
                }

                InsertStepAfter(afterStep, dialog.CreatedStep);
            }
            catch (Exception ex)
            {
                StatusText = $"Add step error: {ex.Message}";
            }
        }

        private void InsertStepAfter(RecordedStep? afterStep, RecordedStep step)
        {
            if (afterStep is { Kind: StepKind.If })
            {
                afterStep.ThenSteps.Add(step);
                return;
            }
            if (afterStep is { Kind: StepKind.Repeat or StepKind.ForEach })
            {
                afterStep.BodySteps.Add(step);
                return;
            }

            if (afterStep != null && TryFindStep(afterStep, out var collection, out int index))
            {
                collection!.Insert(index + 1, step);
                return;
            }
            Steps.Add(step);
        }

        private void AddFlowNode(RecordedStep node)
        {
            Steps.Add(node);
            CurrentScriptFormat = "Steps";
        }

        private void AddNestedFlowNode(RecordedStep? parent, StepKind kind, bool elseBranch)
        {
            if (parent == null) return;
            var node = CreateFlowNode(kind);
            if (parent.Kind == StepKind.If)
            {
                if (elseBranch)
                {
                    parent.HasElse = true;
                    parent.ElseSteps.Add(node);
                }
                else parent.ThenSteps.Add(node);
            }
            else if (parent.Kind is StepKind.Repeat or StepKind.ForEach)
            {
                if (!elseBranch) parent.BodySteps.Add(node);
            }
        }

        private RecordedStep CreateFlowNode(StepKind kind) => kind switch
        {
            StepKind.If => new RecordedStep
            {
                Kind = StepKind.If,
                Condition = new FlowCondition { Alias = Elements.FirstOrDefault()?.Alias ?? "" },
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

        private void AddChildStep(RecordedStep? parent, bool elseBranch)
        {
            if (parent == null) return;
            try
            {
                var dialog = new Dialogs.AddStepWizardDialog(Elements.ToList(), PipeName);
                if (dialog.ShowDialog() != true || dialog.CreatedStep == null)
                    return;

                if (parent.Kind == StepKind.If)
                {
                    if (elseBranch)
                    {
                        parent.HasElse = true;
                        parent.ElseSteps.Add(dialog.CreatedStep);
                    }
                    else
                    {
                        parent.ThenSteps.Add(dialog.CreatedStep);
                    }
                }
                else if (parent.Kind is StepKind.Repeat or StepKind.ForEach)
                {
                    parent.BodySteps.Add(dialog.CreatedStep);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Add nested step error: {ex.Message}";
            }
        }

        private void EditScriptDatasets()
        {
            var dialog = new Dialogs.ScriptDatasetsDialog(_scriptDatasets)
            {
                Owner = Application.Current?.MainWindow,
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                ScriptFlowValidator.Validate(Steps, dialog.Datasets);
            }
            catch (FormatException ex)
            {
                MessageBox.Show(Application.Current?.MainWindow, ex.Message, "Datasets are in use", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _scriptDatasets.Clear();
            foreach (var dataset in dialog.Datasets)
                _scriptDatasets.Add(dataset.Key, dataset.Value);
            SyncScriptDatasetNames();
            RegenerateScript();
        }

        private void SyncScriptDatasetNames()
        {
            ScriptDatasetNames.Clear();
            foreach (var name in _scriptDatasets.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                ScriptDatasetNames.Add(name);
        }

        private bool TryFindStep(RecordedStep target, out ObservableCollection<RecordedStep>? owner, out int index)
        {
            ObservableCollection<RecordedStep>? foundOwner = null;
            int foundIndex = -1;
            bool Find(ObservableCollection<RecordedStep> collection)
            {
                for (int i = 0; i < collection.Count; i++)
                {
                    var node = collection[i];
                    if (ReferenceEquals(node, target))
                    {
                        foundOwner = collection;
                        foundIndex = i;
                        return true;
                    }
                    if (Find(node.ThenSteps) || Find(node.ElseSteps) || Find(node.BodySteps))
                        return true;
                }
                return false;
            }

            bool found = Find(Steps);
            owner = foundOwner;
            index = foundIndex;
            return found;
        }

        private static IEnumerable<RecordedStep> EnumerateSteps(IEnumerable<RecordedStep> roots)
        {
            foreach (var node in roots)
            {
                yield return node;
                foreach (var child in EnumerateSteps(node.ThenSteps)) yield return child;
                foreach (var child in EnumerateSteps(node.ElseSteps)) yield return child;
                foreach (var child in EnumerateSteps(node.BodySteps)) yield return child;
            }
        }

        private void Steps_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RefreshStepSubscriptions();
            RegenerateScript();
            CommandManager.InvalidateRequerySuggested();
        }

        private void NestedSteps_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RefreshStepSubscriptions();
            RegenerateScript();
            CommandManager.InvalidateRequerySuggested();
        }

        private void RefreshStepSubscriptions()
        {
            foreach (var step in _observedSteps)
                step.PropertyChanged -= Step_PropertyChanged;
            foreach (var collection in _observedStepCollections)
                collection.CollectionChanged -= NestedSteps_CollectionChanged;
            _observedSteps.Clear();
            _observedStepCollections.Clear();

            void Observe(ObservableCollection<RecordedStep> collection)
            {
                if (!_observedStepCollections.Add(collection)) return;
                collection.CollectionChanged += NestedSteps_CollectionChanged;
                foreach (var node in collection)
                {
                    if (_observedSteps.Add(node)) node.PropertyChanged += Step_PropertyChanged;
                    Observe(node.ThenSteps);
                    Observe(node.ElseSteps);
                    Observe(node.BodySteps);
                }
            }

            foreach (var node in Steps)
            {
                if (_observedSteps.Add(node)) node.PropertyChanged += Step_PropertyChanged;
                Observe(node.ThenSteps);
                Observe(node.ElseSteps);
                Observe(node.BodySteps);
            }
        }

        private void Step_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(RecordedStep.ThenSteps) or nameof(RecordedStep.ElseSteps) or nameof(RecordedStep.BodySteps))
                RefreshStepSubscriptions();
            RegenerateScript();
            CommandManager.InvalidateRequerySuggested();
        }

        private void OpenCheckpointWizard()
        {
            if (!IsAttached)
            {
                MessageBox.Show("Please attach to a process first.", "Checkpoint Wizard",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Dialogs.CheckpointWizardDialog(Elements.ToList(), PipeName);
            if (dialog.ShowDialog() == true && dialog.CreatedCheckpoint != null)
            {
                var checkpoint = dialog.CreatedCheckpoint;
                
                // Add as a verification step
                var verifyStep = new RecordedStep
                {
                    Kind = StepKind.Verify,
                    Alias = checkpoint.ElementAlias ?? "",
                    Value = checkpoint.ExpectedValue,
                    AppId = SelectedApp?.AppId,
                };
                Steps.Add(verifyStep);
                
                StatusText = $"Checkpoint created: {checkpoint.Id} - {checkpoint.Type}";
            }
        }

        private void OpenSpyTool()
        {
            if (!IsAttached)
            {
                MessageBox.Show("Please attach to a process first.", "Spy Tool",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var appId = SelectedApp?.AppId;
            var dialog = new Dialogs.SpyToolDialog(PipeName, GetSelectedRecordingModes(), SelectedMode, SelectedProcessId, appId, AttachedApps.ToList());
            if (dialog.ShowDialog() == true)
            {
                // Add selected element to repository
                var props = dialog.SelectedProperties;
                if (props != null && props.Count > 0)
                {
                    var alias = dialog.SelectedAlias ?? "NewElement";
                    props.TryGetValue("Name", out var name);
                    props.TryGetValue("ControlType", out var controlType);
                    props.TryGetValue("AutomationId", out var automationId);
                    props.TryGetValue("XPath", out var xpath);
                    
                    var newElement = new ElementEntry
                    {
                        Alias = alias,
                        DisplayName = name ?? alias,
                        ControlType = controlType ?? "Unknown",
                        AutomationId = automationId ?? "",
                        Name = name ?? "",
                        XPath = xpath ?? "",
                        RecordingModes = dialog.SelectedRecordingModes,
                        DriverPriority = new List<string> { "FlaUI", "WPFSpy", "Sikuli" }
                    };
                    Elements.Add(newElement);
                    StatusText = $"Added element: {alias}";
                }
            }
        }

        private void OpenVisualTestBuilder()
        {
            var dialog = new Views.TestFlowDialog(Elements, Steps, _scriptDatasets)
            {
                Owner = Application.Current?.MainWindow,
            };
            if (dialog.ShowDialog() == true)
            {
                Steps.Clear();
                foreach (var node in dialog.Nodes)
                    Steps.Add(node.CloneDeep());
                _scriptDatasets.Clear();
                foreach (var dataset in dialog.Datasets)
                    _scriptDatasets.Add(dataset.Key, dataset.Value);
                SyncScriptDatasetNames();
                StatusText = $"Imported {Steps.Count} steps from Visual Test Builder";
            }
        }

        private void DeleteStep(RecordedStep? step)
        {
            if (step != null && TryFindStep(step, out var collection, out int index))
                collection!.RemoveAt(index);
        }

        /// <summary>D3: move a step by a relative delta (�1) from the up/down buttons.</summary>
        private void MoveStep(RecordedStep? step, int delta)
        {
            if (step == null) return;
            if (!TryFindStep(step, out var collection, out int index)) return;
            MoveStepInCollection(collection!, index, index + delta);
        }

        /// <summary>D3: move a step to an absolute index. Called by the relative
        /// overload (buttons) and by the drag-drop handler in MainWindow. Clamps to
        /// valid bounds and regenerates the Raw script so the order change is visible.</summary>
        public void MoveStepTo(RecordedStep step, int newIndex)
        {
            if (!TryFindStep(step, out var collection, out int currentIndex)) return;
            MoveStepInCollection(collection!, currentIndex, newIndex);
        }

        private void MoveStepInCollection(ObservableCollection<RecordedStep> collection, int currentIndex, int newIndex)
        {
            if (currentIndex < 0 || currentIndex >= collection.Count) return;
            newIndex = Math.Clamp(newIndex, 0, collection.Count - 1);
            if (newIndex == currentIndex) return;
            collection.Move(currentIndex, newIndex);
            RegenerateScript();
        }

        // ------------------------------------------------------------
        // Generation
        // ------------------------------------------------------------
        private void RegenerateScript()
        {
            UpdateScriptContent();
        }

        /// <summary>Regenerate all three script formats (JSON, Python, Robot)
        /// from the current Steps collection and refresh each format's content.</summary>
        private void UpdateScriptContent()
        {
            var driver = _selectedDriver != "Auto" ? _selectedDriver : null;
            var mode = _selectedMode != "Auto" ? _selectedMode : null;
            var recordingModes = GetSelectedRecordingModes();
            var appId = _importedAppId ?? SelectedApp?.AppId;
            try
            {
                RobotScriptContent = ScriptGenerator.Generate(Steps, testCaseName: "Recorded Session Playback", driver: driver, mode: mode, recordingModes: recordingModes, appId: appId, datasets: _scriptDatasets);
                PythonScriptContent = ScriptGenerator.GeneratePython(Steps, testCaseName: "test_recorded_session_playback", driver: driver, mode: mode, recordingModes: recordingModes, appId: appId, datasets: _scriptDatasets);
                JsonScriptContent = JsonStepScript.Serialize(Steps, driver, mode, appId, _scriptDatasets);
                if (StatusText.StartsWith("Script flow:", StringComparison.Ordinal))
                    StatusText = "Ready";
            }
            catch (FormatException ex)
            {
                StatusText = $"Script flow: {ex.Message}";
            }
        }

        private List<string> GetSelectedRecordingModes()
        {
            var modes = new List<string>();
            if (RecordFlaUI) modes.Add("FlaUI");
            if (RecordWPFSpy) modes.Add("WPFSpy");
            if (RecordSikuli) modes.Add("Sikuli");
            return modes;
        }

        private List<string> GetSelectedRunModes()
        {
            var modes = new List<string>();
            if (RunFlaUI) modes.Add("FlaUI");
            if (RunWPFSpy) modes.Add("WPFSpy");
            if (RunSikuli) modes.Add("Sikuli");
            return modes;
        }

        private void MoveElementPriorityUp()
        {
            if (EditingElement?.DriverPriority == null || EditingElement.DriverPriority.Count < 2) return;
            int index = EditingElement.DriverPriority.Count - 1; // Move last selected or just move up if there's a selection
            // For simplicity, move the first item up if no specific selection
            if (index > 0)
            {
                var item = EditingElement.DriverPriority[0];
                EditingElement.DriverPriority.RemoveAt(0);
                EditingElement.DriverPriority.Insert(1, item);
                OnPropertyChanged(nameof(EditingElement));
            }
        }

        private void MoveElementPriorityDown()
        {
            if (EditingElement?.DriverPriority == null || EditingElement.DriverPriority.Count < 2) return;
            int lastIndex = EditingElement.DriverPriority.Count - 1;
            if (lastIndex > 0)
            {
                var item = EditingElement.DriverPriority[lastIndex];
                EditingElement.DriverPriority.RemoveAt(lastIndex);
                EditingElement.DriverPriority.Insert(lastIndex - 1, item);
                OnPropertyChanged(nameof(EditingElement));
            }
        }
         private void RegenerateRepository() => RepositoryJson = RepositoryWriter.GenerateJson(Elements, GetSelectedRecordingModes());

        // ------------------------------------------------------------
        // Run
        // ------------------------------------------------------------
        private bool CanRunCurrentScript() => CurrentScriptFormat switch
        {
            "Steps" => Steps.Count > 0,
            "Json" => !string.IsNullOrWhiteSpace(JsonScriptContent),
            "Python" => !string.IsNullOrWhiteSpace(PythonScriptContent),
            "Robot" => !string.IsNullOrWhiteSpace(RobotScriptContent),
            _ => false,
        };

        private async System.Threading.Tasks.Task RunAsync()
        {
            var cancellation = new CancellationTokenSource();
            _runCancellation = cancellation;
            OnPropertyChanged(nameof(IsScriptRunInProgress));
            CommandManager.InvalidateRequerySuggested();

            try
            {
                RunOutputLines.Clear();
                RunOutputLog.Clear();
                RunSummaryText = "Running...";
                RunOutputPanelExpanded = true;

                string outputDir = Path.Combine(FrameworkRoot, "results", "ide_run");
                string elementsDir = Path.Combine(FrameworkRoot, "repository", "elements");
                Directory.CreateDirectory(elementsDir);
                File.WriteAllText(Path.Combine(elementsDir, "_ide_recorded.json"), RepositoryJson);

                var environment = new Dictionary<string, string>
                {
                    ["WPFSPY_MODE"] = "real",
                    ["WPFSPY_IDE_RUN"] = "1",
                    ["WPFSPY_RUN_MODES"] = string.Join(",", GetSelectedRunModes()),
                    ["WPFSPY_LOG_FILE"] = Path.Combine(outputDir, "framework_persistent_log.txt"),
                };

                string? runAppId = _importedAppId ?? SelectedApp?.AppId;
                if (CurrentScriptFormat == "Json")
                {
                    var jsonDocument = JsonStepScript.DeserializeDocument(JsonScriptContent);
                    runAppId = jsonDocument.AppId
                        ?? EnumerateSteps(jsonDocument.Steps).Select(step => step.AppId).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
                        ?? runAppId;
                }

                var runApp = !string.IsNullOrEmpty(runAppId)
                    ? AttachedApps.FirstOrDefault(app => string.Equals(app.AppId, runAppId, StringComparison.OrdinalIgnoreCase))
                    : SelectedApp;
                if (runApp != null && !string.IsNullOrEmpty(runApp.AppId))
                {
                    environment["WPFSPY_APP_ID"] = runApp.AppId;
                    environment["WPFSPY_APP_NAME"] = runApp.AppName;
                    environment["WPFSPY_PIPE_NAME"] = runApp.PipeName;
                    environment["WPFSPY_PROCESS_ID"] = runApp.ProcessId.ToString();
                }
                else if (!string.IsNullOrEmpty(PipeName) && SelectedProcessId > 0)
                {
                    environment["WPFSPY_PIPE_NAME"] = PipeName;
                    environment["WPFSPY_PROCESS_ID"] = SelectedProcessId.ToString();
                }

                if (!Enum.TryParse<ScriptFormat>(CurrentScriptFormat, ignoreCase: true, out var format))
                    throw new InvalidOperationException($"Unsupported script tab '{CurrentScriptFormat}'.");

                var request = new ScriptExecutionRequest
                {
                    Format = format,
                    SourceText = CurrentScriptFormat switch
                    {
                        "Json" => JsonScriptContent,
                        "Python" => PythonScriptContent,
                        "Robot" => RobotScriptContent,
                        _ => "",
                    },
                    SourcePath = null,
                    Steps = Steps.ToArray(),
                    Datasets = _scriptDatasets,
                    FrameworkRoot = FrameworkRoot,
                    WorkingDirectory = FrameworkRoot,
                    OutputDirectory = outputDir,
                    Driver = _selectedDriver != "Auto" ? _selectedDriver : null,
                    Mode = _selectedMode != "Auto" ? _selectedMode : null,
                    AppId = CurrentScriptFormat == "Json" ? null : runAppId,
                    RecordingModes = GetSelectedRecordingModes(),
                    Environment = environment,
                };

                var result = await _scriptExecutionService.RunAsync(
                    request,
                    line => Application.Current?.Dispatcher.BeginInvoke(new Action(() => RunOutputLines.Add(line))),
                    PrepareRobotScriptAsync,
                    cancellation.Token);

                LastRunSuccess = result.Success;
                RunSummaryText = result.TotalTests > 0
                    ? $"{result.TotalTests} tests, {result.PassedTests} passed, {result.FailedTests} failed"
                    : result.Format == ScriptFormat.Python
                        ? result.ExitCode == 0
                            ? $"Python script completed successfully in {result.Duration.TotalSeconds:F1}s."
                            : $"Python script failed with exit code {result.ExitCode}."
                        : $"Robot exited with code {result.ExitCode}; no test summary was found. Check the output above.";
                EnqueueToast(
                    result.Success ? "Script run completed successfully." : "Script run failed. See Run Output for details.",
                    result.Success ? ToastKind.Success : ToastKind.Error);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                LastRunSuccess = false;
                RunSummaryText = "Run cancelled.";
                EnqueueToast("Script run cancelled.", ToastKind.Warning);
            }
            catch (Exception ex)
            {
                LastRunSuccess = false;
                RunSummaryText = $"Run failed: {ex.Message}";
                StatusText = RunSummaryText;
                EnqueueToast(RunSummaryText, ToastKind.Error);
            }
            finally
            {
                if (ReferenceEquals(_runCancellation, cancellation))
                    _runCancellation = null;
                cancellation.Dispose();
                OnPropertyChanged(nameof(IsScriptRunInProgress));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async Task<RobotScriptPreparation> PrepareRobotScriptAsync(string scriptContent, CancellationToken cancellationToken)
        {
            var processIds = new List<int>();
            if (scriptContent.Contains("Launch Application", StringComparison.Ordinal))
                scriptContent = await PreLaunchFrameworkAppsAsync(scriptContent, processIds);
            cancellationToken.ThrowIfCancellationRequested();
            return new RobotScriptPreparation(scriptContent, processIds);
        }
        // Pre-launch / inject .NET Framework apps before script run
        // ------------------------------------------------------------
        private async System.Threading.Tasks.Task<string> PreLaunchFrameworkAppsAsync(string scriptContent, List<int> fwProcessIds)
        {
            var lines = scriptContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            var modifiedLines = new List<string>();
            bool inTestCases = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("*** Test Cases ***"))
                {
                    inTestCases = true;
                }

                if (inTestCases && line.StartsWith("    Launch Application"))
                {
                    var parts = line.Split(new[] { "    " }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        var appPath = parts[1].Trim();
                        if (IsNetFrameworkApp(appPath))
                        {
                            LogDiagnostic($"Pre-launching .NET Framework app: {appPath}");
                            StatusText = $"Pre-launching {Path.GetFileName(appPath)}...";
                            
                            var appId = ExtractRobotArg(parts, "app_id") ?? Path.GetFileNameWithoutExtension(appPath).ToLowerInvariant();
                            var expectedPipeName = ExtractRobotArg(parts, "pipe_name") ?? $"WPFSpyAgentPipe_{appId}";
                            
                            var pid = await LaunchAndInjectFrameworkAppAsync(appPath, expectedPipeName);
                            if (pid > 0)
                            {
                                fwProcessIds.Add(pid);
                                var attachLine = $"    Attach To Application    {appId}    {pid}    WPFSpy    {expectedPipeName}";
                                modifiedLines.Add(attachLine);
                                LogDiagnostic($"Replaced Launch Application with Attach To Application for PID {pid}, pipe={expectedPipeName}");
                                continue;
                            }
                            else
                            {
                                LogDiagnostic($"Failed to pre-launch {appPath}, keeping original Launch Application line");
                            }
                        }
                    }
                }

                modifiedLines.Add(line);
            }

            return string.Join("\r\n", modifiedLines);
        }

        private bool IsNetFrameworkApp(string appPath)
        {
            try
            {
                var targetDir = Path.GetDirectoryName(appPath);
                if (string.IsNullOrEmpty(targetDir))
                    return false;

                var exeName = Path.GetFileNameWithoutExtension(appPath);
                var runtimeConfigPath = Path.Combine(targetDir, $"{exeName}.runtimeconfig.json");
                return !File.Exists(runtimeConfigPath);
            }
            catch
            {
                return false;
            }
        }

        private string? ExtractRobotArg(string[] parts, string argName)
        {
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith($"{argName}=", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(argName.Length + 1);
                }
            }
            return null;
        }

        private async System.Threading.Tasks.Task<int> LaunchAndInjectFrameworkAppAsync(string appPath, string pipeName)
        {
            try
            {
                var targetDir = Path.GetDirectoryName(appPath);
                if (string.IsNullOrEmpty(targetDir))
                {
                    LogDiagnostic("Cannot resolve target directory");
                    return -1;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = appPath,
                    Arguments = "",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = false,
                    WorkingDirectory = targetDir
                };

                LogDiagnostic($"Launching Framework app: {appPath}");
                var process = Process.Start(psi);
                if (process == null)
                {
                    LogDiagnostic("Process.Start returned null");
                    return -1;
                }

                LogDiagnostic($"Process started: PID={process.Id}");
                StatusText = $"Launched PID {process.Id}, waiting for initialization...";

                await Task.Delay(2000);

                LogDiagnostic($"Staging agent DLLs for PID {process.Id}");
                try
                {
                    RuntimeInjector.StageAgentDllsForTarget(process.Id);
                }
                catch (Exception stageEx)
                {
                    LogDiagnostic($"Stage warning: {stageEx.Message}");
                }

                var nativeInjectDll = RuntimeInjector.FindNativeInjectDll();
                if (string.IsNullOrEmpty(nativeInjectDll))
                {
                    LogDiagnostic("NativeInject DLL not found - cannot inject");
                    return process.Id;
                }

                LogDiagnostic($"Injecting NativeInject DLL: {nativeInjectDll}");
                StatusText = "Injecting Spy Agent...";

                bool injected = await RuntimeInjector.InjectAsync(process.Id, nativeInjectDll, pipeName);
                LogDiagnostic($"Injection result: {injected}");

                if (injected)
                {
                    StatusText = "Waiting for Spy Agent pipe...";
                    bool pipeReady = await WaitForSpyAgentAsync(pipeName, timeoutSeconds: 15);
                    if (pipeReady)
                    {
                        StatusText = $"Injected successfully, PID {process.Id}";
                        LogDiagnostic($"Spy Agent ready on pipe {pipeName}");
                    }
                    else
                    {
                        StatusText = $"Launched PID {process.Id} (agent initializing...)";
                        LogDiagnostic($"Spy Agent pipe not ready after 15s");
                    }
                }
                else
                {
                    StatusText = $"Launched PID {process.Id} (injection failed)";
                    LogDiagnostic($"Injection failed for PID {process.Id}");
                }

                return process.Id;
            }
            catch (Exception ex)
            {
                StatusText = $"Launch/inject error: {ex.Message}";
                LogDiagnostic($"LaunchAndInjectFrameworkAppAsync exception: {ex.GetType().Name}: {ex.Message}");
                return -1;
            }
        }

        private async System.Threading.Tasks.Task<bool> WaitForSpyAgentAsync(string pipeName, int timeoutSeconds = 10)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < timeoutSeconds)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                    client.Connect(2000);
                    if (client.IsConnected)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Pipe not ready yet
                }
                await System.Threading.Tasks.Task.Delay(500);
            }
            return false;
        }

        // ------------------------------------------------------------
        // Export
        // ------------------------------------------------------------
        private void ExportRepository()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "recorded_draft_elements.json",
                Filter = "JSON files (*.json)|*.json",
                InitialDirectory = Path.Combine(FrameworkRoot, "repository", "elements"),
            };
            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, RepositoryJson);
            }
        }

        /// <summary>Save or export the current script. Opens a SaveFileDialog
        /// with filters for JSON, Python, and Robot Framework formats so the
        /// user can choose the target format regardless of the active view tab.</summary>
        private void SaveExportScript()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "recorded_test",
                Filter = "Script files (*.json;*.py;*.robot)|*.json;*.py;*.robot|JSON files (*.json)|*.json|Python files (*.py)|*.py|Robot files (*.robot)|*.robot",
                InitialDirectory = Path.Combine(FrameworkRoot, "tests"),
            };
            if (dialog.ShowDialog() == true)
            {
                string ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
                string content = ext switch
                {
                    ".json" => JsonScriptContent,
                    ".py" => PythonScriptContent,
                    _ => RobotScriptContent
                };
                File.WriteAllText(dialog.FileName, content);
                EnqueueToast($"Saved/exported script: {dialog.FileName}", ToastKind.Info);
            }
        }

        /// <summary>Apply edits from the current view back to the Steps
        /// collection so all views reflect the change when switching tabs.</summary>
        private void ApplyScript()
        {
            if (CurrentScriptFormat == "Steps")
            {
                EnqueueToast("The Visual tab already edits the recorded steps.", ToastKind.Info);
                return;
            }

            string text = CurrentScriptFormat switch
            {
                "Json" => JsonScriptContent,
                "Python" => PythonScriptContent,
                "Robot" => RobotScriptContent,
                _ => "",
            };
            if (string.IsNullOrWhiteSpace(text))
            {
                EnqueueToast("Nothing to apply.", ToastKind.Warning);
                return;
            }

            try
            {
                var format = Enum.Parse<ScriptFormat>(CurrentScriptFormat, ignoreCase: true);
                var imported = ScriptInterop.Parse(format, text);
                ApplyImportedScript(imported);
                EnqueueToast($"Applied {CurrentScriptFormat} script ({Steps.Count} steps) to all views.", ToastKind.Success);
            }
            catch (Exception ex)
            {
                EnqueueToast($"Apply failed: {ex.Message}", ToastKind.Error);
            }
        }

        private void ApplyImportedScript(ScriptImportResult imported)
        {
            _importedAppId = imported.AppId;
            _selectedDriver = string.IsNullOrWhiteSpace(imported.Driver) ? "Auto" : imported.Driver;
            _selectedMode = string.IsNullOrWhiteSpace(imported.Mode) ? "Auto" : imported.Mode;
            OnPropertyChanged(nameof(SelectedDriver));
            OnPropertyChanged(nameof(SelectedMode));

            Steps.Clear();
            foreach (var step in imported.Steps)
                Steps.Add(step);
            _scriptDatasets.Clear();
            if (imported.Datasets != null)
            {
                foreach (var dataset in imported.Datasets)
                    _scriptDatasets.Add(dataset.Key, dataset.Value);
            }
            SyncScriptDatasetNames();
            RegenerateScript();
        }

        private void ImportScript()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Script files (*.json;*.py;*.robot)|*.json;*.py;*.robot|JSON files (*.json)|*.json|Python files (*.py)|*.py|Robot files (*.robot)|*.robot",
                InitialDirectory = Path.Combine(FrameworkRoot, "tests"),
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
                ImportScriptFile(dialog.FileName);
        }
        // Working Folder (B3)
        // ------------------------------------------------------------
        private void OpenWorkingFolder()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select a folder (then press Open)",
                Filter = "Folders|",
                InitialDirectory = string.IsNullOrWhiteSpace(WorkingFolderPath)
                    ? Path.Combine(FrameworkRoot, "repository")
                    : WorkingFolderPath,
                Multiselect = false,
                Title = "Open Working Folder"
            };
            // OpenFileDialog can't pick a folder directly; use FolderBrowserDialog via
            // the Windows Forms-free FolderBrowser dialog wrapped below.
            var folderDlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select the working folder to browse",
                InitialDirectory = string.IsNullOrWhiteSpace(WorkingFolderPath)
                    ? Path.Combine(FrameworkRoot, "repository")
                    : WorkingFolderPath,
                ShowNewFolderButton = true,
            };
            if (folderDlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            WorkingFolderPath = folderDlg.SelectedPath;
            LoadWorkingFolder();
            StatusText = $"Working folder opened: {WorkingFolderPath}";
            EnqueueToast($"Working folder: {Path.GetFileName(WorkingFolderPath)}", ToastKind.Info);
        }

        private void LoadWorkingFolder()
        {
            if (string.IsNullOrWhiteSpace(WorkingFolderPath) || !Directory.Exists(WorkingFolderPath)) return;
            WorkingFolderRoot = FolderTreeItem.CreateRoot(WorkingFolderPath);
            WorkingFolderRoot.IsExpanded = true;
            WorkingFolderRoot.LoadChildren();
            WorkingFolderSelectedItem = null;
        }

        private void RefreshWorkingFolder()
        {
            if (string.IsNullOrEmpty(WorkingFolderPath)) return;
            // Preserve the user's current selection so the tree re-expands
            // around it after a refresh (FolderTreeItem caches FileType and
            // ChildrenLoaded, so re-creating the root drops that state).
            string? selectedPath = WorkingFolderSelectedItem?.FullPath;
            LoadWorkingFolder();
            StatusText = $"Refreshed: {Path.GetFileName(WorkingFolderPath)}";
            EnqueueToast("Folder tree refreshed", ToastKind.Info);
        }

        private void ImportWorkingFolderSelected()
        {
            if (WorkingFolderSelectedItem == null || WorkingFolderSelectedItem.IsDirectory) return;
            string path = WorkingFolderSelectedItem.FullPath;
            string ext = Path.GetExtension(path).ToLowerInvariant();

            // .py and .robot are unambiguously scripts.
            if (ext == ".py" || ext == ".robot")
            {
                ImportScriptFile(path);
                return;
            }

            // .json files are ambiguous: both element repositories and script
            // JSONs use the .json extension. Disambiguate by inspecting the
            // file's root JSON key ("elements" ? Elements pane, "steps" ?
            // Scripts pane). Fall back to the parent-directory name heuristic
            // (FolderTreeItem.IsScriptJson) when the file can't be parsed.
            if (ext == ".json")
            {
                try
                {
                    string json = File.ReadAllText(path);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    bool hasElements = root.TryGetProperty("elements", out _);
                    bool hasSteps = root.TryGetProperty("steps", out _);

                    if (hasSteps && !hasElements)
                    {
                        ImportScriptFile(path);
                        return;
                    }

                    if (hasElements && !hasSteps)
                    {
                        ImportElementsFromJson(path);
                        return;
                    }

                    // Both present or neither present � fall back to the
                    // parent-directory heuristic used by FolderTreeItem.FileType.
                    if (WorkingFolderSelectedItem.IsScriptJson())
                    {
                        ImportScriptFile(path);
                        return;
                    }
                    ImportElementsFromJson(path);
                    return;
                }
                catch
                {
                    // Unparseable � fall back to the directory heuristic.
                    if (WorkingFolderSelectedItem.IsScriptJson())
                    {
                        ImportScriptFile(path);
                        return;
                    }
                    ImportElementsFromJson(path);
                    return;
                }
            }

            EnqueueToast($"Unsupported file type: {ext}", ToastKind.Warning);
        }

        private void ImportScriptFile(string path)
        {
            try
            {
                string extension = Path.GetExtension(path).ToLowerInvariant();
                ScriptFormat format = extension switch
                {
                    ".json" => ScriptFormat.Json,
                    ".py" => ScriptFormat.Python,
                    ".robot" => ScriptFormat.Robot,
                    _ => throw new FormatException($"Unsupported script type: {extension}"),
                };

                string text = File.ReadAllText(path);
                var imported = ScriptInterop.Parse(format, text);
                ApplyImportedScript(imported);
                CurrentScriptFormat = format.ToString();
                StatusText = $"Imported {Steps.Count} step(s) from {Path.GetFileName(path)} and refreshed all script views.";
                EnqueueToast($"Imported {Path.GetFileName(path)} as {Steps.Count} step(s).", ToastKind.Success);
            }
            catch (Exception ex)
            {
                StatusText = $"Script import failed: {ex.Message}";
                EnqueueToast(StatusText, ToastKind.Error);
            }
        }
        private void ImportElementsFromJson(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var rootElement = doc.RootElement;

                if (!rootElement.TryGetProperty("elements", out var elementsObj))
                {
                    EnqueueToast($"Not an element JSON file: missing 'elements' root.", ToastKind.Warning);
                    return;
                }

                int imported = 0;
                foreach (var kv in elementsObj.EnumerateObject())
                {
                    string alias = kv.Name;
                    if (string.IsNullOrEmpty(alias)) continue;
                    if (Elements.Any(e => e.Alias == alias)) continue;

                    var entry = kv.Value;
                    var element = new ElementEntry
                    {
                        Alias = alias,
                        DisplayName = entry.TryGetProperty("displayName", out var dnProp) ? dnProp.GetString() ?? "" : "",
                        ControlType = entry.TryGetProperty("controlType", out var ctProp) ? ctProp.GetString() ?? "" : "",
                        AutomationId = entry.TryGetProperty("automationId", out var aiProp) ? aiProp.GetString() : null,
                        Name = entry.TryGetProperty("name", out var nmProp) ? nmProp.GetString() ?? "" : "",
                        XPath = entry.TryGetProperty("relativeXPath", out var xpProp) ? xpProp.GetString() : null,
                    };
                    Elements.Add(element);
                    imported++;
                }

                RefreshElementTree();
                StatusText = $"Imported {imported} element(s) from {Path.GetFileName(path)}.";
                EnqueueToast($"Imported {imported} elements", ToastKind.Success);
            }
            catch (Exception ex)
            {
                EnqueueToast($"Element import failed: {ex.Message}", ToastKind.Error);
            }
        }

        private void ImportElements()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                InitialDirectory = Path.Combine(FrameworkRoot, "repository", "elements"),
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                string json = File.ReadAllText(dialog.FileName);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var rootElement = doc.RootElement;

                if (!rootElement.TryGetProperty("elements", out var elementsObj)) return;

                int imported = 0;
                foreach (var kv in elementsObj.EnumerateObject())
                {
                    string alias = kv.Name;
                    if (string.IsNullOrEmpty(alias)) continue;

                    if (Elements.Any(e => e.Alias == alias)) continue;

                    var entry = kv.Value;
                    var element = new ElementEntry
                    {
                        Alias = alias,
                        DisplayName = entry.TryGetProperty("displayName", out var dnProp) ? dnProp.GetString() ?? "" : "",
                        ControlType = entry.TryGetProperty("controlType", out var ctProp) ? ctProp.GetString() ?? "" : "",
                        AutomationId = entry.TryGetProperty("automationId", out var aiProp) ? aiProp.GetString() : null,
                        Name = entry.TryGetProperty("name", out var nmProp) ? nmProp.GetString() ?? "" : "",
                        XPath = entry.TryGetProperty("relativeXPath", out var xpProp) ? xpProp.GetString() : null,
                    };

                    Elements.Add(element);
                    imported++;
                }

                StatusText = $"Imported {imported} element(s) from {Path.GetFileName(dialog.FileName)}.";
                EnqueueToast($"Imported {imported} elements", ToastKind.Success);
            }
            catch (Exception ex)
            {
                StatusText = $"Import failed: {ex.Message}";
                EnqueueToast($"Import failed: {ex.Message}", ToastKind.Error);
            }
        }

        private void ImportSteps()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json",
                InitialDirectory = Path.Combine(FrameworkRoot, "repository", "steps"),
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                string json = File.ReadAllText(dialog.FileName);
                using var doc = JsonDocument.Parse(json);
                var rootElement = doc.RootElement;

                if (!rootElement.TryGetProperty("steps", out var stepsObj)) return;

                int imported = 0;
                foreach (var stepObj in stepsObj.EnumerateArray())
                {
                    if (!stepObj.TryGetProperty("alias", out var aliasProp) || string.IsNullOrEmpty(aliasProp.GetString()))
                        continue;

                    if (!stepObj.TryGetProperty("kind", out var kindProp) || string.IsNullOrEmpty(kindProp.GetString()))
                        continue;
                    if (!Enum.TryParse<StepKind>(kindProp.GetString(), out var kind)) continue;

                    if (!stepObj.TryGetProperty("action", out var actionProp) || string.IsNullOrEmpty(actionProp.GetString()))
                        continue;
                    if (!Enum.TryParse<ActionKind>(actionProp.GetString(), out var action)) continue;

                    var step = new RecordedStep
                    {
                        Kind = kind,
                        Alias = aliasProp.GetString()!,
                        Action = action,
                        NonStandard = stepObj.TryGetProperty("nonStandard", out var nsProp) && nsProp.GetBoolean()
                    };

                    if (stepObj.TryGetProperty("value", out var valueProp))
                        step.Value = valueProp.GetString() ?? "";
                    if (stepObj.TryGetProperty("attributeName", out var attrProp))
                        step.AttributeName = attrProp.GetString() ?? "";
                    if (stepObj.TryGetProperty("targetAlias", out var targetProp))
                        step.TargetAlias = targetProp.GetString() ?? "";
                    if (stepObj.TryGetProperty("propertyName", out var propProp))
                        step.PropertyName = propProp.GetString() ?? "";
                    if (stepObj.TryGetProperty("expectedCount", out var countProp))
                        step.ExpectedCount = countProp.GetString() ?? "";
                    if (stepObj.TryGetProperty("appId", out var appIdProp))
                        step.AppId = appIdProp.GetString() ?? "";

                    Steps.Add(step);
                    imported++;
                }

                StatusText = $"Imported {imported} step(s) from {Path.GetFileName(dialog.FileName)}.";
                EnqueueToast($"Imported {imported} steps", ToastKind.Success);
            }
            catch (Exception ex)
            {
                StatusText = $"Import failed: {ex.Message}";
                EnqueueToast($"Import failed: {ex.Message}", ToastKind.Error);
            }
        }

        // ------------------------------------------------------------
        // Sample data / reset (works without a live attach, for demoing)
        // ------------------------------------------------------------
        private void LoadSample()
        {
            Reset();

            string loginPath = Path.Combine(FrameworkRoot, "repository", "elements", "login_page.json");
            string ordersPath = Path.Combine(FrameworkRoot, "repository", "elements", "orders_page.json");

            var loadedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (string jsonPath in new[] { loginPath, ordersPath })
                {
                    if (!File.Exists(jsonPath)) continue;

                    string json = File.ReadAllText(jsonPath);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var rootElement = doc.RootElement;

                    if (!rootElement.TryGetProperty("elements", out var elementsObj)) continue;

                    foreach (var kv in elementsObj.EnumerateObject())
                    {
                        string alias = kv.Name;
                        if (string.IsNullOrEmpty(alias)) continue;

                        var entry = kv.Value;
                        var element = new ElementEntry
                        {
                            Alias = alias,
                            DisplayName = entry.TryGetProperty("displayName", out var dnProp) ? dnProp.GetString() ?? "" : "",
                            ControlType = entry.TryGetProperty("controlType", out var ctProp) ? ctProp.GetString() ?? "" : "",
                            AutomationId = entry.TryGetProperty("automationId", out var aiProp) ? aiProp.GetString() : null,
                            Name = entry.TryGetProperty("name", out var nmProp) ? nmProp.GetString() ?? "" : "",
                            DriverPriority = new List<string> { "FlaUI", "WPFSpy", "Sikuli" }
                        };

                        loadedAliases.Add(alias);
                        Elements.Add(element);
                    }
                }
            }
            catch
            {
            }

            BuildSampleSteps(loadedAliases);

            StatusText = "Loaded sample recording from repository JSON.";
        }

        private void BuildSampleSteps(HashSet<string> loadedAliases)
        {
            var loginAliases = loadedAliases.Where(a => a.StartsWith("LoginPage.", StringComparison.OrdinalIgnoreCase)).ToList();
            var ordersAliases = loadedAliases.Where(a => a.StartsWith("OrdersPage.", StringComparison.OrdinalIgnoreCase)).ToList();

            string? usernameAlias = loginAliases.FirstOrDefault(a => a.Contains("Username", StringComparison.OrdinalIgnoreCase));
            string? passwordAlias = loginAliases.FirstOrDefault(a => a.Contains("Password", StringComparison.OrdinalIgnoreCase));
            string? submitAlias = loginAliases.FirstOrDefault(a => a.Contains("Submit", StringComparison.OrdinalIgnoreCase));
            string? errorAlias = loginAliases.FirstOrDefault(a => a.Contains("Error", StringComparison.OrdinalIgnoreCase) || a.Contains("lblError", StringComparison.OrdinalIgnoreCase));

            string? skuAlias = ordersAliases.FirstOrDefault(a => a.Contains("Sku", StringComparison.OrdinalIgnoreCase) || a.Contains("cmb", StringComparison.OrdinalIgnoreCase));
            string? qtyAlias = ordersAliases.FirstOrDefault(a => a.Contains("Qty", StringComparison.OrdinalIgnoreCase) || a.Contains("Quantity", StringComparison.OrdinalIgnoreCase));
            string? priorityAlias = ordersAliases.FirstOrDefault(a => a.Contains("Priority", StringComparison.OrdinalIgnoreCase) || a.Contains("chk", StringComparison.OrdinalIgnoreCase));
            string? createAlias = ordersAliases.FirstOrDefault(a => a.Contains("Create", StringComparison.OrdinalIgnoreCase) || a.Contains("Submit", StringComparison.OrdinalIgnoreCase));
            string? confirmationAlias = ordersAliases.FirstOrDefault(a => a.Contains("Confirmation", StringComparison.OrdinalIgnoreCase) || a.Contains("lbl", StringComparison.OrdinalIgnoreCase));
            string? gridAlias = ordersAliases.FirstOrDefault(a => a.Contains("Grid", StringComparison.OrdinalIgnoreCase) || a.Contains("Orders", StringComparison.OrdinalIgnoreCase));
            string? logoutAlias = ordersAliases.FirstOrDefault(a => a.Contains("Logout", StringComparison.OrdinalIgnoreCase));

            if (usernameAlias != null)
                Steps.Add(new RecordedStep { Kind = StepKind.Action, Alias = usernameAlias, Action = ActionKind.SetValue, Value = "user1" });

            if (passwordAlias != null)
                Steps.Add(new RecordedStep { Kind = StepKind.Action, Alias = passwordAlias, Action = ActionKind.SetValue, Value = "Pass@123" });

            if (submitAlias != null)
                Steps.Add(new RecordedStep { Kind = StepKind.Action, Alias = submitAlias, Action = ActionKind.Invoke });

            if (skuAlias != null)
                Steps.Add(new RecordedStep { Kind = StepKind.Action, Alias = skuAlias, Action = ActionKind.SetValue, Value = "SKU-1001" });

            if (qtyAlias != null)
                Steps.Add(new RecordedStep { Kind = StepKind.Action, Alias = qtyAlias, Action = ActionKind.SetValue, Value = "2" });

            if (priorityAlias != null)
                Steps.Add(new RecordedStep { Kind = StepKind.Action, Alias = priorityAlias, Action = ActionKind.Toggle, NonStandard = true });

            if (createAlias != null)
                Steps.Add(new RecordedStep { Kind = StepKind.Action, Alias = createAlias, Action = ActionKind.Invoke });

            if (confirmationAlias != null)
                Steps.Add(new RecordedStep { Kind = StepKind.Verify, Alias = confirmationAlias, Value = "Order confirmed: SKU-1001 x2" });

            if (gridAlias != null)
                Steps.Add(new RecordedStep { Kind = StepKind.VerifyOcr, Alias = gridAlias });

            if (logoutAlias != null)
                Steps.Add(new RecordedStep { Kind = StepKind.Action, Alias = logoutAlias, Action = ActionKind.Invoke });
        }

        private void Reset()
        {
            Steps.Clear();
            _scriptDatasets.Clear();
            SyncScriptDatasetNames();
            _importedAppId = null;
            _selectedDriver = "Auto";
            _selectedMode = "Auto";
            OnPropertyChanged(nameof(SelectedDriver));
            OnPropertyChanged(nameof(SelectedMode));
            CurrentScriptFormat = "Steps";
            RegenerateScript();
            Elements.Clear();
            RunOutputLines.Clear();
            RunOutputLog.Clear();
            RunSummaryText = "";
            RunOutputPanelExpanded = false;
            EditingElement = null;
            SelectedElement = null;
        }

        private void ResetLayout()
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.ResetLayout();
            }
        }

        // ------------------------------------------------------------
        // Element Editor
        // ------------------------------------------------------------
        private void AddElement()
        {
            var newElement = new ElementEntry
            {
                Alias = "NewPage.newElement",
                DisplayName = "New Element",
                ControlType = "Text",
                AutomationId = "",
                Name = "",
                XPath = "",
                DriverPriority = new List<string> { "FlaUI", "WPFSpy", "Sikuli" }
            };
            Elements.Add(newElement);
            SelectedElement = newElement;
            EditingElement = newElement.Clone();
        }

        private void EditElement(ElementEntry? element)
        {
            if (element == null) return;
            try
            {
                EditingElement = element.Clone();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EditElement error: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task PreviewElement()
        {
            if (SelectedElement == null || string.IsNullOrEmpty(PipeName))
            {
                PreviewText = "No element selected or not attached.";
                return;
            }

            try
            {
                PreviewText = "Highlighting element in target app...";
                var client = new SpyAgentClient(PipeName);
                
                // Send Highlight command to flash the target app's main window
                var response = await System.Threading.Tasks.Task.Run(() => 
                    client.Send("Highlight", 
                               name: string.IsNullOrEmpty(SelectedElement.Name) ? null : SelectedElement.Name,
                               xpath: string.IsNullOrEmpty(SelectedElement.XPath) ? null : SelectedElement.XPath));
                
                if (response.Success)
                {
                    PreviewText = $"Preview: {response.Data}\n" +
                                  $"The target app window has been flashed.";
                }
                else
                {
                    PreviewText = $"Highlight failed: {response.Error}";
                }
            }
            catch (Exception ex)
            {
                PreviewText = $"Preview error: {ex.Message}";
            }
        }

private void SaveElement()
        {
            if (EditingElement == null || SelectedElement == null) return;

            try
            {
                int index = Elements.IndexOf(SelectedElement);
                if (index >= 0)
                {
                    Elements[index] = EditingElement.Clone();
                    SelectedElement = Elements[index];
                }
                EditingElement = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveElement error: {ex.GetType().Name}: {ex.Message}");
                MessageBox.Show($"Save failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

private async System.Threading.Tasks.Task GetDataGridContentOcr()
        {
            if (SelectedElement == null || string.IsNullOrEmpty(PipeName))
            {
                OcrResultText = "No element selected or not attached.";
                return;
            }

            try
            {
                OcrResultText = "Running OCR on DataGrid element...";
                var client = new SpyAgentClient(PipeName);

                var response = await System.Threading.Tasks.Task.Run(() =>
                    client.Send("GetDataGridContentOcr",
                               name: string.IsNullOrEmpty(SelectedElement.Name) ? null : SelectedElement.Name,
                               xpath: string.IsNullOrEmpty(SelectedElement.XPath) ? null : SelectedElement.XPath));

                if (response.Success)
                {
                    OcrResultText = string.IsNullOrEmpty(response.Data)
                        ? "OCR returned no text."
                        : response.Data;
                }
                else
                {
                    OcrResultText = $"OCR failed: {response.Error}";
                }
            }
            catch (Exception ex)
            {
                OcrResultText = $"OCR error: {ex.Message}";
            }
        }

        private void CancelEditElement()
        {
            EditingElement = null;
        }

        private void DeleteElement(ElementEntry? element)
        {
            if (element == null) return;
            if (Elements.Contains(element))
            {
                Elements.Remove(element);
            }
            if (SelectedElement == element)
            {
                SelectedElement = null;
                EditingElement = null;
            }
        }

        // ------------------------------------------------------------
        // Pick Element from Target App
        // ------------------------------------------------------------
        private void TogglePickMode()
        {
            if (IsPickModeActive)
            {
                StopPickMode();
            }
            else
            {
                StartPickMode();
            }
        }

        private void StartPickMode()
        {
            if (!IsAttached || string.IsNullOrEmpty(PipeName))
            {
                MessageBox.Show("Attach to a process first.", "Pick Element", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _pickMouseHook = new GlobalMouseHook();
                _pickMouseHook.LeftButtonDown += OnPickClick;
                _pickMouseHook.Start();
                IsPickModeActive = true;
                StatusText = "Pick mode active � click an element in the target app...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start pick mode: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StopPickMode();
            }
        }

        private void StopPickMode()
        {
            if (_pickMouseHook != null)
            {
                _pickMouseHook.LeftButtonDown -= OnPickClick;
                _pickMouseHook.Dispose();
                _pickMouseHook = null;
            }
            
            // Marshaling to UI thread because mouse hook raises events on a background thread
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() =>
                {
                    IsPickModeActive = false;
                    StatusText = IsAttached ? "Attached � ready." : "Not attached.";
                });
            }
            else
            {
                IsPickModeActive = false;
                StatusText = IsAttached ? "Attached � ready." : "Not attached.";
            }
        }

        private static string BuildAncestorPath(ProbedElement probed)
        {
            if (string.IsNullOrEmpty(probed.XPath))
            {
                return "";
            }

            var parts = new System.Collections.Generic.List<string>();
            var segments = probed.XPath.Split('/');
            for (int i = 1; i < segments.Length -1; i++)
            {
                string segment = segments[i];
                if (string.IsNullOrEmpty(segment))
                {
                    continue;
                }

                string? identifier = ExtractIdentifier(segment);
                if (!string.IsNullOrEmpty(identifier))
                {
                    parts.Add(identifier);
                }
            }
            return string.Join(".", parts);
        }

        private static string? ExtractIdentifier(string segment)
        {
            int bracketIndex = segment.IndexOf('[');
            if (bracketIndex >= 0)
            {
                string predicate = segment.Substring(bracketIndex + 1);
                if (predicate.StartsWith("@AutomationId='"))
                {
                    int start = "@AutomationId='".Length;
                    int end = predicate.IndexOf('\'', start);
                    if (end > start)
                    {
                        return predicate.Substring(start, end - start);
                    }
                }
                else if (predicate.StartsWith("@Name='"))
                {
                    int start = "@Name='".Length;
                    int end = predicate.IndexOf('\'', start);
                    if (end > start)
                    {
                        string name = predicate.Substring(start, end - start);
                        // Skip WPF template parts (PART_*) and Adorner layers �                        // they are internal visuals, not user-facing elements.
                        if (name.StartsWith("PART_") || name == "AdornerLayer")
                            return null;
                        return name;
                    }
                }
            }
            return null;
        }


        private async void OnPickClick(int x, int y)
        {
            try
            {
                var client = new SpyAgentClient(PipeName);
                var response = await System.Threading.Tasks.Task.Run(() => client.Send("ProbeAt", x: x, y: y));
                
                if (response.Success && response.Data != null)
                {
                    var probe = System.Text.Json.JsonSerializer.Deserialize<ProbeResultDto>(response.Data, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (probe != null)
                    {
                        string page = "PickedPage";
                        string ancestorPath = BuildAncestorPath(new ProbedElement
                        {
                            AutomationId = probe.AutomationId,
                            Name = probe.Name,
                            ControlType = probe.ControlType,
                            Text = probe.Text,
                            ResolvedVia = "WPFSpy",
                            XPath = probe.XPath,
                        });
                        string idOrName = string.IsNullOrEmpty(probe.AutomationId) ? probe.Name : probe.AutomationId!;
                        string alias = string.IsNullOrEmpty(ancestorPath)
                            ? $"{page}.{idOrName}"
                            : $"{page}.{ancestorPath}.{idOrName}";
                        
                        var newElement = new ElementEntry
                        {
                            Alias = alias,
                            DisplayName = probe.Name,
                            ControlType = probe.ControlType,
                            AutomationId = probe.AutomationId,
                            Name = probe.Name,
                            XPath = probe.XPath,
                            DriverPriority = new List<string> { "FlaUI", "WPFSpy", "Sikuli" }
                        };

                        // Marshaling to UI thread because mouse hook raises events on a background thread
                        var dispatcher = System.Windows.Application.Current?.Dispatcher;
                        if (dispatcher != null && !dispatcher.CheckAccess())
                        {
                            await dispatcher.InvokeAsync(() =>
                            {
                                Elements.Add(newElement);
                                SelectedElement = newElement;
                                EditingElement = newElement.Clone();
                                StatusText = $"Picked: {alias}";
                            });
                        }
                        else
                        {
                            Elements.Add(newElement);
                            SelectedElement = newElement;
                            EditingElement = newElement.Clone();
                            StatusText = $"Picked: {alias}";
                        }
                    }
                }
                else
                {
                    StatusText = $"Pick failed: {response.Error}";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Pick error: {ex.Message}";
            }
            finally
            {
                StopPickMode();
            }
        }

        private class ProbeResultDto
        {
            public string Name { get; set; } = "";
            public string? AutomationId { get; set; }
            public string ControlType { get; set; } = "";
            public string? Text { get; set; }
            public string? XPath { get; set; }
        }

        public ICommand ToggleThemeCommand { get; }
        public ICommand ShowElementsCommand { get; }
        public ICommand ShowScriptsCommand { get; }
        public ICommand ShowResultsCommand { get; }
        public ICommand ShowSearchCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand AboutCommand { get; }
        public ICommand DocumentationCommand { get; }
        public ICommand OpenScriptCommand { get; }
        public ICommand UndoCommand { get; }
        public ICommand RedoCommand { get; }
        public ICommand StopRecordingCommand { get; }

        public int SelectedTabIndex
        {
            get => ActivePaneId switch { "Scripts" => 1, "Results" => 2, "Working Folder" => 3, _ => 0 };
            set => ActivePaneId = value switch { 1 => "Scripts", 2 => "Results", 3 => "Working Folder", _ => "Elements" };
        }

        private string? _activePaneId;
        public string? ActivePaneId
        {
            get => _activePaneId;
            set { _activePaneId = value; OnPropertyChanged(); }
        }

        private void ShowWorkingFolder()
        {
            ActivePaneId = "Working Folder";
        }

        private void ToggleTheme()
        {
            Themes.ThemeManager.ToggleTheme();
            StatusText = ThemeManager.CurrentTheme == "Dark" ? "Dark theme applied" : "Light theme applied";
        }

        private void ShowAbout()
        {
            MessageBox.Show("WPF Test IDE\nVersion 1.0\n\nA VS Code-like IDE for WPF test automation.", "About WPF Test IDE", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

