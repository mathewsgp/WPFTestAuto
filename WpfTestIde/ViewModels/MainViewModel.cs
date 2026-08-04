using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using WpfTestIde.Execution;
using WpfTestIde.Models;
using WpfTestIde.Recording;

namespace WpfTestIde.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private RecordingSession? _session;
        private GlobalMouseHook? _pickMouseHook;

        public ObservableCollection<RecordedStep> Steps { get; } = new();
        public ObservableCollection<ElementEntry> Elements { get; } = new();
        public ObservableCollection<string> RunOutputLines { get; } = new();

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

        private bool _lastRunSuccess;
        public bool LastRunSuccess { get => _lastRunSuccess; set { _lastRunSuccess = value; OnPropertyChanged(); } }

        // Paths — defaults match this repo's layout when the IDE is run
        // from WpfTestIde/bin/.../ against the sibling WpfTestFramework checkout.
        public string FrameworkRoot { get; set; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        public string PipeName { get; set; } = "WPFSpyAgentPipe";

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
        public string OcrResultText { get => _ocrResultText; set { _ocrResultText = value; OnPropertyChanged(); } }

        public ICommand ToggleRecordingCommand { get; }
        public ICommand AttachCommand { get; }
        public ICommand AddVerificationCommand { get; }
        public ICommand DeleteStepCommand { get; }
        public ICommand RunCommand { get; }
        public ICommand ExportRepositoryCommand { get; }
        public ICommand ExportScriptCommand { get; }
        public ICommand SaveScriptCommand { get; }
        public ICommand LoadSampleCommand { get; }
        public ICommand ResetCommand { get; }
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

        public MainViewModel()
        {
            ToggleRecordingCommand = new RelayCommand(_ => ToggleRecording(), _ => IsAttached);
            AttachCommand = new RelayCommand(_ => Attach());
            AddVerificationCommand = new RelayCommand(param => AddVerification(param as RecordedStep));
            DeleteStepCommand = new RelayCommand(param => DeleteStep(param as RecordedStep));
            RunCommand = new RelayCommand(async _ => await RunAsync(), _ => Steps.Count > 0);
            ExportRepositoryCommand = new RelayCommand(_ => ExportRepository());
            ExportScriptCommand = new RelayCommand(_ => ExportScript());
            SaveScriptCommand = new RelayCommand(_ => SaveScript());
            LoadSampleCommand = new RelayCommand(_ => LoadSample());
            ResetCommand = new RelayCommand(_ => Reset());
            CheckPipeConnectionCommand = new RelayCommand(_ => CheckPipeConnection(), _ => IsAttached);
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

            Steps.CollectionChanged += (_, __) => RegenerateScript();
            Elements.CollectionChanged += (_, __) => RegenerateRepository();
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

            _session?.Dispose();
            _session = new RecordingSession(dialog.PipeName, dialog.SelectedProcessId.Value, dialog.PageMap);
            _session.StepCaptured += OnStepCaptured;

            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "repository", "attach_log.txt");
            try
            {
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] Attach: FrameworkRoot={FrameworkRoot}, PipeName={PipeName}{Environment.NewLine}");
            }
            catch { }

            RepositoryLookup.EnsureLoaded(FrameworkRoot);

            PipeName = dialog.PipeName;
            IsAttached = true;
            StatusText = $"Attached to process #{dialog.SelectedProcessId} — ready to record.";
        }

        private void CheckPipeConnection()
        {
            if (string.IsNullOrEmpty(PipeName))
            {
                PipeStatusText = "No pipe name configured.";
                return;
            }

            const int maxAttempts = 3;
            const int delayMs = 1000;
            string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pipe_check_log.txt");

            void Log(string msg)
            {
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
            }

            Log($"=== Pipe check started, pipe={PipeName} ===");

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Log($"Attempt {attempt}/{maxAttempts}: connecting...");
                    var client = new SpyAgentClient(PipeName);
                    var response = client.Send("GetMainWindowTitle");
                    Log($"Attempt {attempt}: response => Success={response.Success}, Data={(response.Data ?? "(null)")}, Error={(response.Error ?? "(null)")}");

                    if (response.Success && !string.IsNullOrEmpty(response.Data))
                    {
                        PipeStatusText = $"Pipe OK — attached app main window: {response.Data}";
                        Log($"Result: {PipeStatusText}");
                        return;
                    }
                    if (response.Success)
                    {
                        PipeStatusText = "Pipe OK — attached app has no main window title.";
                        Log($"Result: {PipeStatusText}");
                        return;
                    }
                    PipeStatusText = $"Pipe check failed (attempt {attempt}/{maxAttempts}): {response.Error ?? "unknown error"}";
                    Log($"Result: {PipeStatusText}");
                }
                catch (Exception ex)
                {
                    PipeStatusText = $"Pipe check failed (attempt {attempt}/{maxAttempts}): {ex.Message}";
                    Log($"Exception: {ex.GetType().Name}: {ex.Message}");
                }

                if (attempt < maxAttempts)
                {
                    Thread.Sleep(delayMs);
                }
            }

            Log("=== Pipe check finished ===");
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
                    NonStandard = step.NonStandard,
                };
                entry = new ElementEntry
                {
                    Alias = resolvedAlias,
                    DisplayName = entry.DisplayName,
                    ControlType = entry.ControlType,
                    AutomationId = entry.AutomationId,
                    Name = entry.Name,
                    XPath = entry.XPath,
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
            var dialog = new Dialogs.AddVerificationDialog(Elements.ToList(), PipeName);
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var verifyStep = new RecordedStep
            {
                Kind = StepKind.Verify,
                Alias = dialog.SelectedAlias,
                Value = dialog.ExpectedValue,
            };

            int index = afterStep is null ? Steps.Count : Steps.IndexOf(afterStep) + 1;
            Steps.Insert(index, verifyStep);
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

            var dialog = new Dialogs.SpyToolDialog(PipeName);
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
                        XPath = xpath ?? ""
                    };
                    Elements.Add(newElement);
                    StatusText = $"Added element: {alias}";
                }
            }
        }

        private void DeleteStep(RecordedStep? step)
        {
            if (step != null)
            {
                Steps.Remove(step);
            }
        }

        // ------------------------------------------------------------
        // Generation
        // ------------------------------------------------------------
        private void RegenerateScript() => GeneratedScript = ScriptGenerator.Generate(Steps);
        private void RegenerateRepository() => RepositoryYaml = RepositoryWriter.GenerateYaml(Elements);

        // ------------------------------------------------------------
        // Run
        // ------------------------------------------------------------
        private async System.Threading.Tasks.Task RunAsync()
        {
            RunOutputLines.Clear();
            RunSummaryText = "Running...";

            string testsDir = Path.Combine(FrameworkRoot, "tests");
            Directory.CreateDirectory(testsDir);
            string scriptPath = Path.Combine(testsDir, "ide_generated_test.robot");
            File.WriteAllText(scriptPath, GeneratedScript);

            string outputDir = Path.Combine(FrameworkRoot, "results", "ide_run");

            var summary = await RobotRunner.RunAsync(
                scriptPath,
                outputDir,
                FrameworkRoot,
                line => Application.Current.Dispatcher.Invoke(() => RunOutputLines.Add(line)),
                new System.Collections.Generic.Dictionary<string, string>
                {
                    ["WPFSPY_MODE"] = "real",
                    ["WPFSPY_PIPE_NAME"] = PipeName,
                    ["WPFSPY_IDE_RUN"] = "1",
                });

            LastRunSuccess = summary.Success;
            RunSummaryText = summary.Total > 0
                ? $"{summary.Total} tests, {summary.Passed} passed, {summary.Failed} failed"
                : "No summary line found — check the output above for errors.";
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
            }
        }

        // ------------------------------------------------------------
        // Sample data / reset (works without a live attach, for demoing)
        // ------------------------------------------------------------
        private void LoadSample()
        {
            Reset();
            var sampleElements = new[]
            {
                new ElementEntry { Alias = "LoginPage.txtUsername", DisplayName = "UsernameInput", ControlType = "TextBox", AutomationId = "txtUsername", Name = "UsernameInput" },
                new ElementEntry { Alias = "LoginPage.txtPassword", DisplayName = "PasswordInput", ControlType = "PasswordBox", AutomationId = "txtPassword", Name = "PasswordInput" },
                new ElementEntry { Alias = "LoginPage.btnSubmit", DisplayName = "SubmitBtn", ControlType = "Button", AutomationId = "btnSubmit", Name = "SubmitBtn" },
                new ElementEntry { Alias = "OrdersPage.cmbSku", DisplayName = "SkuCombo", ControlType = "ComboBox", AutomationId = "cmbSku", Name = "SkuCombo" },
                new ElementEntry { Alias = "OrdersPage.txtQty", DisplayName = "QtyInput", ControlType = "TextBox", AutomationId = "txtQty", Name = "QtyInput" },
                new ElementEntry { Alias = "OrdersPage.chkPriority", DisplayName = "PriorityToggle", ControlType = "PriorityToggleControl", AutomationId = null, Name = "PriorityToggle" },
                new ElementEntry { Alias = "OrdersPage.btnCreateOrder", DisplayName = "CreateOrderBtn", ControlType = "Button", AutomationId = "btnCreateOrder", Name = "CreateOrderBtn" },
                new ElementEntry { Alias = "OrdersPage.lblConfirmation", DisplayName = "ConfirmationLabel", ControlType = "Label", AutomationId = "lblConfirmation", Name = "ConfirmationLabel" },
            };
            foreach (var e in sampleElements) Elements.Add(e);

            var sampleSteps = new[]
            {
                new RecordedStep { Kind = StepKind.Action, Alias = "LoginPage.txtUsername", Action = ActionKind.SetValue, Value = "user1" },
                new RecordedStep { Kind = StepKind.Action, Alias = "LoginPage.txtPassword", Action = ActionKind.SetValue, Value = "Pass@123" },
                new RecordedStep { Kind = StepKind.Action, Alias = "LoginPage.btnSubmit", Action = ActionKind.Invoke },
                new RecordedStep { Kind = StepKind.Action, Alias = "OrdersPage.cmbSku", Action = ActionKind.SetValue, Value = "SKU-1001" },
                new RecordedStep { Kind = StepKind.Action, Alias = "OrdersPage.txtQty", Action = ActionKind.SetValue, Value = "2" },
                new RecordedStep { Kind = StepKind.Action, Alias = "OrdersPage.chkPriority", Action = ActionKind.Toggle, NonStandard = true },
                new RecordedStep { Kind = StepKind.Verify, Alias = "OrdersPage.chkPriority", Value = "On" },
                new RecordedStep { Kind = StepKind.Action, Alias = "OrdersPage.btnCreateOrder", Action = ActionKind.Invoke },
                new RecordedStep { Kind = StepKind.Verify, Alias = "OrdersPage.lblConfirmation", Value = "Order confirmed: SKU-1001 x2" },
                 new RecordedStep { Kind = StepKind.VerifyOcr, Alias = "OrdersPage.gridOrders" },
            };
            foreach (var s in sampleSteps) Steps.Add(s);

            StatusText = "Loaded sample recording (no live attach required).";
        }

        private void Reset()
        {
            Steps.Clear();
            Elements.Clear();
            RunOutputLines.Clear();
            RunSummaryText = "";
            EditingElement = null;
            SelectedElement = null;
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
                XPath = ""
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
                            XPath = probe.XPath
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

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

