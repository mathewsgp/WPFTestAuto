using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using WpfTestIde.Execution;
using WpfTestIde.Models;
using WpfTestIde.Recording;
using WpfTestIde.Themes;

namespace WpfTestIde.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private RecordingSession? _session;
        private GlobalMouseHook? _pickMouseHook;

        public ObservableCollection<RecordedStep> Steps { get; } = new();
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
        // the Border is still in the visible tree — see the E3 risk register.
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
        // Dispose the previous toast's still-pending timer — otherwise the
        // prior timer's 4-s callback fires against whatever toast is currently
        // shown and hides it early (the toast-overlap bug called out in the
        // E3 review).
        private System.Threading.Timer? _toastTimer;

        private void EnqueueToastCore(string text, ToastKind kind)
        {
            // Cancel any prior in-flight removal timer before starting a new
            // one — guarantees only one removal timer is outstanding at a
            // time, so it can never fire against a replacement toast.
            var oldTimer = _toastTimer;
            _toastTimer = null;
            oldTimer?.Dispose();

            // Two-phase hide-then-replace: collapse the Border first so the
            // ActiveToasts[0] indexer binding isn't re-evaluated against the
            // collection while we mutate it. The Visibility flip is flushed
            // via the deferred reveal below (a fresh dispatcher tick at
            // Render priority) so WPF actually sees a Collapsed→Visible
            // transition and the fade-in Storyboard re-fires per toast.
            IsToastVisible = false;

            // In-place swap when a toast is already showing — raises
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
            // callback) so the native handle is always freed — even if the
            // Dispatcher is shutting down when the timer fires (window closing
            // mid-toast) and the queued callback is dropped/throws.
            System.Threading.Timer? timer = null;
            timer = new System.Threading.Timer(_ =>
            {
                try
                {
                    // Only act if this timer is still THE current one. A
                    // replacement toast Disposes this timer (above) and
                    // sets _toastTimer to its own — in that case this
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
                        // item is removed. Order matters — see the E3 risk
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
                    // Dispatcher is shutting down — no native-handle leak and
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
        /// <c>ItemsSource="{Binding RunOutputFiltered}"</c> — a stable object ref
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

        private string _generatedScript = "";
        public string GeneratedScript { get => _generatedScript; set { _generatedScript = value; OnPropertyChanged(); } }

        private string _repositoryYaml = "elements: {}\n";
        public string RepositoryYaml { get => _repositoryYaml; set { _repositoryYaml = value; OnPropertyChanged(); } }

        private bool _isRecording;
        public bool IsRecording { get => _isRecording; set { _isRecording = value; OnPropertyChanged(); OnPropertyChanged(nameof(RecordButtonLabel)); } }
        public string RecordButtonLabel => IsRecording ? "■ Stop Recording" : "● Record";

        private bool _isAttached;
        public bool IsAttached { get => _isAttached; set { _isAttached = value; OnPropertyChanged(); } }

        private string _statusText = "Not attached — use \"Attach to Process...\" to begin.";
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
        public string[] AvailableDrivers { get; } = { "Auto", "FlaUI", "WPFSpy", "Sikuli" };

         // Mode selection for test execution
        private string _selectedMode = "Auto";
        public string SelectedMode
        {
            get => _selectedMode;
            set { _selectedMode = value; OnPropertyChanged(); RegenerateScript(); }
        }
        public string[] AvailableModes { get; } = { "Auto", "mock", "real" };

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
            set { _recordSikuli = value; OnPropertyChanged(); RegenerateScript(); }
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

        public ICommand MoveElementPriorityUpCommand { get; }
        public ICommand MoveElementPriorityDownCommand { get; }

        private bool _lastRunSuccess;
        public bool LastRunSuccess { get => _lastRunSuccess; set { _lastRunSuccess = value; OnPropertyChanged(); } }

        // Paths — defaults match this repo's layout when the IDE is run
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
                OnPropertyChanged();
                if (value != null)
                {
                    PipeName = value.PipeName;
                    SelectedProcessId = value.ProcessId;
                }
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
        public string PickButtonLabel => IsPickModeActive ? "■ Cancel Pick" : "🔍 Pick Element";

        private string _previewText = "";
        public string PreviewText { get => _previewText; set { _previewText = value; OnPropertyChanged(); } }

        private string _ocrResultText = "";
        public string OcrResultText
        {
            get => _ocrResultText;
            set
            {
                _ocrResultText = value;
                OnPropertyChanged();
                // Only raise HasOcrResult on a real state change to avoid spamming
                // change notifications for identical content.
                var hasResult = !string.IsNullOrEmpty(value);
                if (hasResult != _hasOcrResult)
                {
                    HasOcrResult = hasResult;
                }
                // Auto-open the OCR panel whenever fresh OCR content arrives so the
                // user sees it without hunting for the collapsed expander. Manual
                // collapse is still honored afterwards.
                if (hasResult)
                {
                    OcrPanelExpanded = true;
                }
            }
        }

        // True when there is OCR text to show; drives the Expander's caret/empty hint
        // and is computed from OcrResultText so consumers don't have to re-test it.
        private bool _hasOcrResult;
        public bool HasOcrResult
        {
            get => _hasOcrResult;
            set { _hasOcrResult = value; OnPropertyChanged(); }
        }

        // Two-way: user can collapse/expand manually, OCR arrival auto-expands.
        private bool _ocrPanelExpanded;
        public bool OcrPanelExpanded
        {
            get => _ocrPanelExpanded;
            set
            {
                _ocrPanelExpanded = value;
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
        // (mirrors the OCR auto-expand on arrival pattern) — gives a glanceable live
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
        public ICommand RunCommand { get; }
        public ICommand ExportRepositoryCommand { get; }
        public ICommand ExportScriptCommand { get; }
        public ICommand SaveScriptCommand { get; }
        public ICommand LoadSampleCommand { get; }
        public ICommand ImportElementsCommand { get; }
        public ICommand ExportStepsAsYamlCommand { get; }
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

        public MainViewModel()
        {
            ToggleRecordingCommand = new RelayCommand(_ => ToggleRecording(), _ => IsAttached);
            AttachCommand = new RelayCommand(_ => Attach());
            AddVerificationCommand = new RelayCommand(param => AddVerification(param as RecordedStep));
            AddStepCommand = new RelayCommand(param => AddStep(param as RecordedStep));
            DeleteStepCommand = new RelayCommand(param => DeleteStep(param as RecordedStep));
            MoveStepUpCommand = new RelayCommand(param => MoveStep(param as RecordedStep, -1));
            MoveStepDownCommand = new RelayCommand(param => MoveStep(param as RecordedStep, +1));
            RunCommand = new AsyncRelayCommand(_ => RunAsync(), _ => Steps.Count > 0);
            ExportRepositoryCommand = new RelayCommand(_ => ExportRepository());
            ExportScriptCommand = new RelayCommand(_ => ExportScript());
            SaveScriptCommand = new RelayCommand(_ => SaveScript());
            LoadSampleCommand = new RelayCommand(_ => LoadSample());
            ImportElementsCommand = new RelayCommand(_ => ImportElements());
            ExportStepsAsYamlCommand = new RelayCommand(_ => ExportStepsAsYaml());
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
            EnableLaunchAttachRecordingCommand = new RelayCommand(_ => EnableRecordingForLaunchedApps(), _ => Steps.Any(s => s.Kind == StepKind.LaunchApplication && s.AutoAttach));
            MoveElementPriorityUpCommand = new RelayCommand(_ => MoveElementPriorityUp(), _ => EditingElement != null && EditingElement.DriverPriority != null && EditingElement.DriverPriority.Any());
            MoveElementPriorityDownCommand = new RelayCommand(_ => MoveElementPriorityDown(), _ => EditingElement != null && EditingElement.DriverPriority != null && EditingElement.DriverPriority.Any());

            // Menu commands
            ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
            ShowElementsCommand = new RelayCommand(_ => SelectedTabIndex = 0);
            ShowScriptsCommand = new RelayCommand(_ => SelectedTabIndex = 1);
            ShowResultsCommand = new RelayCommand(_ => SelectedTabIndex = 2);
            ShowSearchCommand = new RelayCommand(_ => { /* TODO: implement search */ });
            OpenSettingsCommand = new RelayCommand(_ => { /* TODO: implement settings */ });
            ExitCommand = new RelayCommand(_ => Application.Current.Shutdown());
            AboutCommand = new RelayCommand(_ => ShowAbout());
            DocumentationCommand = new RelayCommand(_ => { /* TODO: open docs */ });
            OpenScriptCommand = new RelayCommand(_ => { /* TODO: open script */ });
            UndoCommand = new RelayCommand(_ => { /* TODO: implement undo */ });
            RedoCommand = new RelayCommand(_ => { /* TODO: implement redo */ });
            StopRecordingCommand = new RelayCommand(_ => ToggleRecording(), _ => IsAttached && IsRecording);

            Steps.CollectionChanged += (_, __) => RegenerateScript();
            Elements.CollectionChanged += (_, __) => { RegenerateRepository(); RefreshElementTree(); };
            RunOutputLines.CollectionChanged += (_, __) => OnPropertyChanged(nameof(RunOutputText));
            // D4: also parse each new raw line into RunOutputLog. Reset tag handler
            // (sender == Collection clearing) replays nothing here because we
            // separately clear RunOutputLog in Reset()/RunAsync below. Insert of
            // a single line at the end → LogLineParser.Parse + add to RunOutputLog;
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
        /// rolled under ShowInfo on purpose — Robot's quiet trace/debug lines are
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
            string appId = string.IsNullOrWhiteSpace(dialog.AppId)
                ? GenerateAppId(dialog.SelectedProcessId.Value, dialog.ApplicationPath)
                : dialog.AppId.Trim();

            string appName = string.IsNullOrWhiteSpace(dialog.ApplicationPath)
                ? $"Process-{dialog.SelectedProcessId.Value}"
                : Path.GetFileNameWithoutExtension(dialog.ApplicationPath);

            var appContext = new WpfTestIde.Models.AppContext
            {
                AppId = appId,
                AppName = appName,
                Driver = "WPFSpy",
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

            _session?.Dispose();
            _session = new RecordingSession(dialog.PipeName, dialog.SelectedProcessId.Value, dialog.PageMap, appId);
            _session.StepCaptured += OnStepCaptured;

            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "repository", "attach_log.txt");
            try
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Attach: FrameworkRoot={FrameworkRoot}, PipeName={PipeName}, AppId={appId}{Environment.NewLine}");
            }
            catch { }

            RepositoryLookup.EnsureLoaded(FrameworkRoot);

            IsAttached = true;
            StatusText = $"Attached to {appName} (PID {dialog.SelectedProcessId}) — ready to record.";
        }

        public void DetachApplication(string appId)
        {
            var app = AttachedApps.FirstOrDefault(a => a.AppId == appId);
            if (app == null) return;

            AttachedApps.Remove(app);
            
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
            var launchSteps = Steps.Where(s => s.Kind == StepKind.LaunchApplication && s.AutoAttach).ToList();

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

                string appId = step.AppId ?? System.IO.Path.GetFileNameWithoutExtension(step.AppPath).ToLowerInvariant();
                string appName = System.IO.Path.GetFileNameWithoutExtension(step.AppPath);
                string pipeName = step.PipeName ?? $"WPFSpyAgentPipe_{appId}";

                // Use FlaUI driver when spy agent is disabled (e.g. Notepad, Calc),
                // WPFSpy when the agent is enabled.
                string driver = step.SpyAgentEnabled
                    ? (step.LaunchDriver ?? "WPFSpy")
                    : "FlaUI";

                var appContext = new WpfTestIde.Models.AppContext
                {
                    AppId = appId,
                    AppName = appName,
                    Driver = driver,
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

            // Create recording session for the first launched app.
            // Use WPFSpy probe mode when spy agent is enabled; otherwise fall
            // back to the FlaUI/UIA probe so apps like Notepad can be recorded.
            var firstApp = AttachedApps.FirstOrDefault();
            if (firstApp != null)
            {
                var firstStep = launchSteps.FirstOrDefault(s =>
                    (s.AppId ?? System.IO.Path.GetFileNameWithoutExtension(s.AppPath ?? "").ToLowerInvariant()) == firstApp.AppId);

                bool useFlaUI = firstStep != null && !firstStep.SpyAgentEnabled;

                _session?.Dispose();
                _session = new RecordingSession(
                    firstApp.PipeName,
                    firstApp.ProcessId,
                    new List<(string, string)>(),
                    firstApp.AppId,
                    useFlaUI ? RecordingSession.ProbeMode.FlaUI : RecordingSession.ProbeMode.WPFSpy,
                    firstApp.AppPath);
                _session.StepCaptured += OnStepCaptured;

                IsAttached = true;
                string modeLabel = useFlaUI ? "FlaUI/UIA" : "WPFSpy";
                StatusText = $"Ready to record ({modeLabel} mode). {AttachedApps.Count} application(s) configured. Run the script to launch and attach.";
            }
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

        private static string GenerateAppId(int processId, string? appPath)
        {
            if (!string.IsNullOrWhiteSpace(appPath))
            {
                string name = Path.GetFileNameWithoutExtension(appPath);
                return $"{name}_{processId}";
            }
            return $"app_{processId}";
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
        /// <see cref="EnqueueToast"/> calls) marshal back onto the dispatcher —
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
                // process* — a separate trust boundary. They are
                // operator-readable here but their CRLF contents are not — a
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
                            Log($"Result: Pipe OK — attached app main window: {SanitizeForLog(response.Data)}");
                            return ($"Pipe OK — attached app main window: {response.Data}", true, response.Data);
                        }
                        if (response.Success)
                        {
                            // Log parity with the original sync
                            // CheckPipeConnection: the "Result:" line was
                            // emitted on both the data and no-data success
                            // branches; restore it here.
                            Log("Result: Pipe OK — attached app has no main window title.");
                            return ("Pipe OK — attached app has no main window title.", true, null);
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
                    // success branch result.message is "Pipe OK — attached
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

        private void ToggleRecording()
        {
            if (_session is null)
            {
                return;
            }

            if (IsRecording)
            {
                _session.Stop();
                IsRecording = false;
                StatusText = "Recording stopped.";
            }
            else
            {
                _session.Start();
                IsRecording = true;
                StatusText = "Recording — interact with the attached application now.";
            }
        }

        private void OnStepCaptured(RecordedStep step, ElementEntry entry)
        {
            // Marshal back to the UI thread — the mouse hook and FlaUI
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

                int index = afterStep is null ? Steps.Count : Steps.IndexOf(afterStep) + 1;
                Steps.Insert(index, dialog.CreatedStep);
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

                int index = afterStep is null ? Steps.Count : Steps.IndexOf(afterStep) + 1;
                Steps.Insert(index, dialog.CreatedStep);
            }
            catch (Exception ex)
            {
                StatusText = $"Add step error: {ex.Message}";
            }
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
            // Create visual test builder with current elements and steps
            var dialog = new Views.TestFlowDialog(Elements);
            
            // Load existing steps if any
            foreach (var step in Steps)
            {
                var flowStep = new ViewModels.FlowStep
                {
                    StepNumber = dialog.Steps.Steps.Count + 1,
                    ActionType = MapFlowActionToString(step),
                    ElementAlias = step.Alias ?? "",
                    Value = step.Value ?? ""
                };
                dialog.Steps.Steps.Add(flowStep);
            }
            
            if (dialog.ShowDialog() == true)
            {
                // Import steps back from visual builder
                Steps.Clear();
                foreach (var flowStep in dialog.Steps.Steps)
                {
                    var (kind, action) = MapFlowActionToRecorded(flowStep.ActionType);
                    var recordedStep = new RecordedStep
                    {
                        Kind = kind,
                        Action = action,
                        Alias = flowStep.ElementAlias,
                        Value = flowStep.Value,
                        AppId = flowStep.AppId,
                    };
                    Steps.Add(recordedStep);
                }
                StatusText = $"Imported {Steps.Count} steps from Visual Test Builder";
            }
        }

        private string MapFlowActionToString(RecordedStep step)
        {
            if (step.Kind == Models.StepKind.Verify)
                return "Verify";
            if (step.Kind == Models.StepKind.VerifyOcr)
                return "GetText";
                
            return step.Action switch
            {
                Models.ActionKind.Invoke => "Click",
                Models.ActionKind.SetValue => "SetText",
                Models.ActionKind.Toggle => "Check",
                _ => "Click"
            };
        }

        private (Models.StepKind kind, Models.ActionKind action) MapFlowActionToRecorded(string flowActionType)
        {
            return flowActionType switch
            {
                "Click" or "DoubleClick" or "RightClick" or "Hover" => 
                    (Models.StepKind.Action, Models.ActionKind.Invoke),
                "SetText" or "Select" => 
                    (Models.StepKind.Action, Models.ActionKind.SetValue),
                "Check" or "Uncheck" => 
                    (Models.StepKind.Action, Models.ActionKind.Toggle),
                "Verify" => 
                    (Models.StepKind.Verify, Models.ActionKind.Invoke),
                "GetText" => 
                    (Models.StepKind.VerifyOcr, Models.ActionKind.Invoke),
                _ => 
                    (Models.StepKind.Action, Models.ActionKind.Invoke)
            };
        }

        private void DeleteStep(RecordedStep? step)
        {
            if (step != null)
            {
                Steps.Remove(step);
            }
        }

        /// <summary>D3: move a step by a relative delta (±1) from the up/down buttons.</summary>
        private void MoveStep(RecordedStep? step, int delta)
        {
            if (step == null) return;
            var index = Steps.IndexOf(step);
            if (index < 0) return;
            MoveStepTo(step, index + delta);
        }

        /// <summary>D3: move a step to an absolute index. Called by the relative
        /// overload (buttons) and by the drag-drop handler in MainWindow. Clamps to
        /// valid bounds and regenerates the Raw script so the order change is visible.</summary>
        public void MoveStepTo(RecordedStep step, int newIndex)
        {
            var currentIndex = Steps.IndexOf(step);
            if (currentIndex < 0) return;
            newIndex = Math.Clamp(newIndex, 0, Steps.Count - 1);
            if (newIndex == currentIndex) return;
            Steps.Move(currentIndex, newIndex);
            RegenerateScript();
        }

        // ------------------------------------------------------------
        // Generation
        // ------------------------------------------------------------
         private void RegenerateScript()
         {
             var driver = _selectedDriver != "Auto" ? _selectedDriver : null;
             var mode = _selectedMode != "Auto" ? _selectedMode : null;
             var recordingModes = GetSelectedRecordingModes();
             var appId = SelectedApp?.AppId;
             GeneratedScript = ScriptGenerator.Generate(Steps, testCaseName: "Recorded Session Playback", driver: driver, mode: mode, recordingModes: recordingModes, appId: appId);
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
         private void RegenerateRepository() => RepositoryYaml = RepositoryWriter.GenerateYaml(Elements, GetSelectedRecordingModes());

        // ------------------------------------------------------------
        // Run
        // ------------------------------------------------------------
         private async System.Threading.Tasks.Task RunAsync()
         {
             RunOutputLines.Clear();
             RunOutputLog.Clear();
             RunSummaryText = "Running...";
             // A5: pop open the bottom tail so the user sees the live stream as the
             // run begins. Two-way binding honors manual collapse afterwards.
             RunOutputPanelExpanded = true;

             string testsDir = Path.Combine(FrameworkRoot, "tests");
             Directory.CreateDirectory(testsDir);
             string scriptPath = Path.Combine(testsDir, "ide_generated_test.robot");
             File.WriteAllText(scriptPath, GeneratedScript);

             // Write in-memory elements to repository so DriverAgnosticApi
             // can resolve aliases for elements recorded in this session
             // that may not yet exist in the static YAML files.
             string elementsDir = Path.Combine(FrameworkRoot, "repository", "elements");
             Directory.CreateDirectory(elementsDir);
             string ideElementsPath = Path.Combine(elementsDir, "_ide_recorded.yaml");
             File.WriteAllText(ideElementsPath, RepositoryYaml);

             string outputDir = Path.Combine(FrameworkRoot, "results", "ide_run");

             var env = new System.Collections.Generic.Dictionary<string, string>
             {
                 ["WPFSPY_MODE"] = "real",
                 ["WPFSPY_IDE_RUN"] = "1",
                 ["WPFSPY_RUN_MODES"] = string.Join(",", GetSelectedRunModes()),
             };

             // Register the selected app with the Python framework for multi-app support
             if (SelectedApp != null && !string.IsNullOrEmpty(SelectedApp.AppId))
             {
                 env["WPFSPY_APP_ID"] = SelectedApp.AppId;
                 env["WPFSPY_APP_NAME"] = SelectedApp.AppName;
                 env["WPFSPY_PIPE_NAME"] = SelectedApp.PipeName;
                 env["WPFSPY_PROCESS_ID"] = SelectedApp.ProcessId.ToString();
             }
             else if (!string.IsNullOrEmpty(PipeName) && SelectedProcessId > 0)
             {
                 env["WPFSPY_PIPE_NAME"] = PipeName;
                 env["WPFSPY_PROCESS_ID"] = SelectedProcessId.ToString();
             }

             var summary = await RobotRunner.RunAsync(
                 scriptPath,
                 outputDir,
                 FrameworkRoot,
                 line => Application.Current.Dispatcher.Invoke(() => RunOutputLines.Add(line)),
                 env);

             LastRunSuccess = summary.Success;
             RunSummaryText = summary.Total > 0
                 ? $"{summary.Total} tests, {summary.Passed} passed, {summary.Failed} failed"
                 : "No summary line found — check the output above for errors.";

             // E3: amplify the run result with a transient toast. Suppress on
             // the "no test summary line" branch (the operator needs to read the
             // log to diagnose those) — the banner RunSummaryText above stays
             // the authoritative read for everyone, the toast only mirrors the
             // pass/fail outcome for glancability when the operator is on a
             // different tab.
             if (summary.Total > 0)
             {
                 EnqueueToast(
                     $"Run finished — {summary.Passed} passed, {summary.Failed} failed",
                     summary.Success ? ToastKind.Success : ToastKind.Error);
             }
         }

        // ------------------------------------------------------------
        // Export
        // ------------------------------------------------------------
        private void ExportRepository()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "recorded_draft_elements.yaml",
                Filter = "YAML files (*.yaml)|*.yaml",
                InitialDirectory = Path.Combine(FrameworkRoot, "repository", "elements"),
            };
            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, RepositoryYaml);
            }
        }

        private void ExportScript()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "draft_recorded_test.robot",
                Filter = "Robot files (*.robot)|*.robot",
                InitialDirectory = Path.Combine(FrameworkRoot, "tests"),
            };
            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, GeneratedScript);
                // E3: announce the export with a transient Info toast; the user
                // may be on a different tab when the Save dialog closes, and the
                // toast is independent of the active tab.
                EnqueueToast($"Exported script: {dialog.FileName}", ToastKind.Info);
            }
        }

        private void SaveScript()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "recorded_test.robot",
                Filter = "Robot files (*.robot)|*.robot",
                InitialDirectory = Path.Combine(FrameworkRoot, "tests"),
            };
            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, GeneratedScript);
                StatusText = $"Script saved to {dialog.FileName}";
                // E3: mirror the StatusText line with a green Success toast so
                // a save from a different tab is still noticed at a glance.
                EnqueueToast($"Saved: {dialog.FileName}", ToastKind.Success);
            }
        }

        private void ImportElements()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "YAML files (*.yaml)|*.yaml",
                InitialDirectory = Path.Combine(FrameworkRoot, "repository", "elements"),
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            try
            {
                string yaml = File.ReadAllText(dialog.FileName);
                var root = deserializer.Deserialize<Dictionary<object, object>>(yaml);
                if (root == null || !root.TryGetValue("elements", out var elementsObj)) return;

                var elementsDict = ConvertToDictionary(elementsObj);
                if (elementsDict == null) return;

                int imported = 0;
                foreach (var kv in elementsDict)
                {
                    string alias = kv.Key?.ToString() ?? "";
                    if (string.IsNullOrEmpty(alias)) continue;

                    if (Elements.Any(e => e.Alias == alias)) continue;

                    var entry = ConvertToDictionary(kv.Value);
                    if (entry == null) continue;

                    var element = new ElementEntry
                    {
                        Alias = alias,
                        DisplayName = entry.ContainsKey("displayName") ? (entry["displayName"] as string ?? "") : "",
                        ControlType = entry.ContainsKey("controlType") ? (entry["controlType"] as string ?? "") : "",
                        AutomationId = entry.ContainsKey("automationId") ? (entry["automationId"] as string ?? null) : null,
                        Name = entry.ContainsKey("name") ? (entry["name"] as string ?? "") : "",
                        XPath = entry.ContainsKey("relativeXPath") ? (entry["relativeXPath"] as string ?? null) : null,
                        DriverPriority = entry.ContainsKey("driverPriority") ? (entry["driverPriority"] as List<object> ?? new List<object>()).Cast<string>().ToList() : null,
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

        private void ExportStepsAsYaml()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = "recorded_steps.yaml",
                Filter = "YAML files (*.yaml)|*.yaml",
                InitialDirectory = Path.Combine(FrameworkRoot, "repository", "steps")
            };
            if (dialog.ShowDialog() != true) return;

            var stepsList = new List<object>();
            foreach (var step in Steps)
            {
                var stepDict = new Dictionary<string, object>
                {
                    ["kind"] = step.Kind.ToString(),
                    ["alias"] = step.Alias,
                    ["action"] = step.Action.ToString(),
                    ["nonStandard"] = step.NonStandard
                };

                if (!string.IsNullOrEmpty(step.Value))
                    stepDict["value"] = step.Value;
                if (!string.IsNullOrEmpty(step.AttributeName))
                    stepDict["attributeName"] = step.AttributeName;
                if (!string.IsNullOrEmpty(step.TargetAlias))
                    stepDict["targetAlias"] = step.TargetAlias;
                if (!string.IsNullOrEmpty(step.PropertyName))
                    stepDict["propertyName"] = step.PropertyName;
                if (!string.IsNullOrEmpty(step.ExpectedCount))
                    stepDict["expectedCount"] = step.ExpectedCount;
                if (!string.IsNullOrEmpty(step.AppId))
                    stepDict["appId"] = step.AppId;

                stepsList.Add(stepDict);
            }

            var root = new Dictionary<string, object> { ["steps"] = stepsList };
            var serializer = new SerializerBuilder().Build();
            File.WriteAllText(dialog.FileName, serializer.Serialize(root));
            StatusText = $"Steps exported to {dialog.FileName}";
            EnqueueToast($"Exported steps: {dialog.FileName}", ToastKind.Success);
        }

        private void ImportSteps()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "YAML files (*.yaml)|*.yaml",
                InitialDirectory = Path.Combine(FrameworkRoot, "repository", "steps"),
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            try
            {
                string yaml = File.ReadAllText(dialog.FileName);
                var root = deserializer.Deserialize<Dictionary<object, object>>(yaml);
                if (root == null || !root.TryGetValue("steps", out var stepsObj)) return;

                var stepsList = stepsObj as List<object>;
                if (stepsList == null) return;

                int imported = 0;
                foreach (var stepObj in stepsList)
                {
                    var stepDict = ConvertToDictionary(stepObj);
                    if (stepDict == null) continue;

                    if (!stepDict.TryGetValue("alias", out var aliasObj) || aliasObj?.ToString() is not string alias || string.IsNullOrEmpty(alias))
                        continue;

                    if (!stepDict.TryGetValue("kind", out var kindObj) || kindObj?.ToString() is not string kindStr)
                        continue;
                    if (!Enum.TryParse<StepKind>(kindStr, out var kind)) continue;

                    if (!stepDict.TryGetValue("action", out var actionObj) || actionObj?.ToString() is not string actionStr)
                        continue;
                    if (!Enum.TryParse<ActionKind>(actionStr, out var action)) continue;

                    var step = new RecordedStep
                    {
                        Kind = kind,
                        Alias = alias,
                        Action = action,
                        NonStandard = stepDict.TryGetValue("nonStandard", out var nsObj) && nsObj is bool ns && ns
                    };

                    if (stepDict.TryGetValue("value", out var valueObj) && valueObj != null)
                        step.Value = valueObj.ToString();
                    if (stepDict.TryGetValue("attributeName", out var attrObj) && attrObj != null)
                        step.AttributeName = attrObj.ToString();
                    if (stepDict.TryGetValue("targetAlias", out var targetObj) && targetObj != null)
                        step.TargetAlias = targetObj.ToString();
                    if (stepDict.TryGetValue("propertyName", out var propObj) && propObj != null)
                        step.PropertyName = propObj.ToString();
                    if (stepDict.TryGetValue("expectedCount", out var countObj) && countObj != null)
                        step.ExpectedCount = countObj.ToString();
                    if (stepDict.TryGetValue("appId", out var appIdObj) && appIdObj != null)
                        step.AppId = appIdObj.ToString();

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

            string loginPath = Path.Combine(FrameworkRoot, "repository", "elements", "login_page.yaml");
            string ordersPath = Path.Combine(FrameworkRoot, "repository", "elements", "orders_page.yaml");

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            var loadedAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (string yamlPath in new[] { loginPath, ordersPath })
                {
                    if (!File.Exists(yamlPath)) continue;

                    string yaml = File.ReadAllText(yamlPath);
                    var root = deserializer.Deserialize<Dictionary<object, object>>(yaml);
                    if (root == null || !root.TryGetValue("elements", out var elementsObj)) continue;

                    var elementsDict = ConvertToDictionary(elementsObj);
                    if (elementsDict == null) continue;

                    foreach (var kv in elementsDict)
                    {
                        string alias = kv.Key?.ToString() ?? "";
                        if (string.IsNullOrEmpty(alias)) continue;

                        var entry = ConvertToDictionary(kv.Value);
                        if (entry == null) continue;

                        loadedAliases.Add(alias);

                        var element = new ElementEntry
                        {
                            Alias = alias,
                            DisplayName = entry.ContainsKey("displayName") ? (entry["displayName"] as string ?? "") : "",
                            ControlType = entry.ContainsKey("controlType") ? (entry["controlType"] as string ?? "") : "",
                            AutomationId = entry.ContainsKey("automationId") ? (entry["automationId"] as string ?? null) : null,
                            Name = entry.ContainsKey("name") ? (entry["name"] as string ?? "") : "",
                            DriverPriority = new List<string> { "FlaUI", "WPFSpy", "Sikuli" }
                        };

                        Elements.Add(element);
                    }
                }
            }
            catch
            {
            }

            BuildSampleSteps(loadedAliases);

            StatusText = "Loaded sample recording from repository YAML.";
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

        private static System.Collections.Generic.IDictionary<string, object>? ConvertToDictionary(object obj)
        {
            if (obj is System.Collections.Generic.IDictionary<string, object> dict)
                return dict;

            if (obj is System.Collections.Generic.IDictionary<object, object> objDict)
            {
                var result = new System.Collections.Generic.Dictionary<string, object>(System.StringComparer.OrdinalIgnoreCase);
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
            if (value is System.Collections.Generic.IDictionary<object, object> dict)
            {
                var result = new System.Collections.Generic.Dictionary<string, object>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var kv in dict)
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

        private void Reset()
        {
            Steps.Clear();
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
                StatusText = "Pick mode active — click an element in the target app...";
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
                    StatusText = IsAttached ? "Attached — ready." : "Not attached.";
                });
            }
            else
            {
                IsPickModeActive = false;
                StatusText = IsAttached ? "Attached — ready." : "Not attached.";
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
                        // Skip WPF template parts (PART_*) and Adorner layers ・                        // they are internal visuals, not user-facing elements.
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
            get => ActivePaneId switch { "Scripts" => 1, "Results" => 2, _ => 0 };
            set => ActivePaneId = value switch { 1 => "Scripts", 2 => "Results", _ => "Elements" };
        }

        private string? _activePaneId;
        public string? ActivePaneId
        {
            get => _activePaneId;
            set { _activePaneId = value; OnPropertyChanged(); }
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

