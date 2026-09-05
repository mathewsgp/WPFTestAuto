using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WpfTestIde.Models;
using WpfTestIde.Recording;

namespace WpfTestIde.Dialogs
{
    public partial class AddStepWizardDialog : Window
    {
        private readonly List<ElementEntry> _elements;
        private readonly string _pipeName;

        public RecordedStep? CreatedStep { get; private set; }

        private bool _isLoaded;

        public AddStepWizardDialog(List<ElementEntry> elements, string pipeName = "WPFSpyAgentPipe")
        {
            InitializeComponent();
            _elements = elements;
            _pipeName = pipeName;

            AliasCombo.ItemsSource = elements;
            AliasCombo.DisplayMemberPath = nameof(ElementEntry.Alias);
            if (elements.Count > 0)
            {
                AliasCombo.SelectedIndex = 0;
            }

            Loaded += (_, __) => { _isLoaded = true; UpdateUIForStepType(); };
        }

        private void StepTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoaded) UpdateUIForStepType();
        }

        private void UpdateUIForStepType()
        {
            if (StepTypeCombo.SelectedItem is not ComboBoxItem selectedItem) return;
            if (ValueLabel == null || ValueBox == null || TimeoutLabel == null || TimeoutBox == null) return;

            var text = selectedItem.Content?.ToString() ?? "";

            bool showValue = text is "Set Element Value" or "Verify Element Text" or "Verify Element Contains Text"
                             or "Verify Element Text Matches Regex" or "Press Keys"
                             or "Scroll" or "Sikuli Click (Image)" or "Sikuli Type (Image)";
            bool showTimeout = text is "Wait Until Element Exists" or "Wait Until Element Visible"
                               or "Wait Until Element Enabled" or "Wait Until Text Contains";
            bool showAttributeName = text is "Verify Element Attribute";
            bool showTarget = text is "Drag And Drop";
            bool showPropertyPanel = text is "Property Checkpoint";
            bool showCountPanel = text is "Count Checkpoint";
            bool showLaunchAppPanel = text is "Launch Application";
            bool showTerminateAppPanel = text is "Terminate Application";

            // Launch / Terminate Application steps do NOT operate on a UI element
            // and have no timeout / value / attribute / target — hide those rows.
            bool isAppLifecycleStep = showLaunchAppPanel || showTerminateAppPanel;
            var elementRowVisibility = isAppLifecycleStep ? Visibility.Collapsed : Visibility.Visible;
            var descriptionRowVisibility = isAppLifecycleStep ? Visibility.Collapsed : Visibility.Visible;
            var valueRowVisibility = isAppLifecycleStep ? Visibility.Collapsed : (showValue ? Visibility.Visible : Visibility.Collapsed);

            // Use ElementLabel to keep the "Element" row easily togglable.
            if (ElementLabel != null) ElementLabel.Visibility = elementRowVisibility;
            AliasCombo.Visibility = elementRowVisibility;

            ValueLabel.Visibility = valueRowVisibility;
            ValueBox.Visibility = valueRowVisibility;
            TimeoutLabel.Visibility = isAppLifecycleStep ? Visibility.Collapsed : (showTimeout ? Visibility.Visible : Visibility.Collapsed);
            TimeoutBox.Visibility = isAppLifecycleStep ? Visibility.Collapsed : (showTimeout ? Visibility.Visible : Visibility.Collapsed);
            AttributeNameLabel.Visibility = isAppLifecycleStep ? Visibility.Collapsed : (showAttributeName ? Visibility.Visible : Visibility.Collapsed);
            AttributeNameBox.Visibility = isAppLifecycleStep ? Visibility.Collapsed : (showAttributeName ? Visibility.Visible : Visibility.Collapsed);
            TargetLabel.Visibility = isAppLifecycleStep ? Visibility.Collapsed : (showTarget ? Visibility.Visible : Visibility.Collapsed);
            TargetCombo.Visibility = isAppLifecycleStep ? Visibility.Collapsed : (showTarget ? Visibility.Visible : Visibility.Collapsed);
            PropertyPanel.Visibility = isAppLifecycleStep ? Visibility.Collapsed : (showPropertyPanel ? Visibility.Visible : Visibility.Collapsed);
            CountPanel.Visibility = isAppLifecycleStep ? Visibility.Collapsed : (showCountPanel ? Visibility.Visible : Visibility.Collapsed);
            LaunchAppPanel.Visibility = showLaunchAppPanel ? Visibility.Visible : Visibility.Collapsed;
            TerminateAppPanel.Visibility = showTerminateAppPanel ? Visibility.Visible : Visibility.Collapsed;

            // Description row: hide for launch/terminate (their own panels self-document).
            if (DescriptionLabel != null) DescriptionLabel.Visibility = descriptionRowVisibility;
            DescriptionBox.Visibility = descriptionRowVisibility;

            if (showTarget)
            {
                TargetCombo.ItemsSource = _elements;
                TargetCombo.DisplayMemberPath = nameof(ElementEntry.Alias);
                if (_elements.Count > 0)
                {
                    TargetCombo.SelectedIndex = 0;
                }
            }

            if (text == "Verify Element Text" && AliasCombo.SelectedItem is ElementEntry entry)
            {
                TryPrefillExpectedValue(entry);
            }
        }

        private void AliasCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AliasCombo.SelectedItem is not ElementEntry entry) return;

            var selectedItem = StepTypeCombo.SelectedItem as ComboBoxItem;
            if (selectedItem?.Content?.ToString() == "Verify Element Text")
            {
                TryPrefillExpectedValue(entry);
            }
        }

        private void TryPrefillExpectedValue(ElementEntry entry)
        {
            try
            {
                var client = new SpyAgentClient(_pipeName);
                var response = client.Send("GetText", name: entry.Name);
                ValueBox.Text = response.Success ? (response.Data ?? "") : "";
            }
            catch
            {
                ValueBox.Text = "";
            }
        }

        private void BrowseAppPath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select application executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(this) == true)
            {
                LaunchAppPathBox.Text = dlg.FileName;
                if (string.IsNullOrWhiteSpace(LaunchAppIdBox.Text))
                {
                    // Suggest a sensible app_id from the file name (no extension, lowercased).
                    var suggested = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                    LaunchAppIdBox.Text = string.IsNullOrEmpty(suggested) ? "app" : suggested.ToLowerInvariant();
                }
                if (string.IsNullOrWhiteSpace(LaunchStartInBox.Text))
                {
                    LaunchStartInBox.Text = System.IO.Path.GetDirectoryName(dlg.FileName);
                }
                // Suggest pipe name based on app id
                UpdatePipeNameSuggestion();
            }
        }

        private void LaunchAppIdBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isLoaded)
            {
                UpdatePipeNameSuggestion();
            }
        }

        private void UpdatePipeNameSuggestion()
        {
            // Only update pipe name if user hasn't manually entered one
            if (string.IsNullOrWhiteSpace(LaunchPipeNameBox.Text))
            {
                var appId = LaunchAppIdBox.Text?.Trim();
                if (!string.IsNullOrEmpty(appId))
                {
                    LaunchPipeNameBox.Text = $"WPFSpyAgentPipe_{appId}";
                }
                else
                {
                    LaunchPipeNameBox.Text = "WPFSpyAgentPipe";
                }
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedItem = StepTypeCombo.SelectedItem as ComboBoxItem;
                if (selectedItem == null) return;

                var stepType = selectedItem.Content?.ToString() ?? "";
                var step = new RecordedStep
                {
                    Alias = (AliasCombo.SelectedItem as ElementEntry)?.Alias ?? "",
                };

                switch (stepType)
                {
                    case "Click Element":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.Invoke;
                        break;
                    case "Set Element Value":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.SetValue;
                        step.Value = ValueBox.Text;
                        break;
                    case "Toggle Element":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.Toggle;
                        break;
                    case "Double Click Element":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.DoubleClick;
                        break;
                    case "Right Click Element":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.RightClick;
                        break;
                    case "Drag And Drop":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.DragDrop;
                        step.TargetAlias = (TargetCombo.SelectedItem as ElementEntry)?.Alias ?? "";
                        break;
                    case "Hover Over Element":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.Hover;
                        break;
                    case "Press Keys":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.PressKeys;
                        step.Value = ValueBox.Text;
                        break;
                    case "Scroll":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.Scroll;
                        step.Value = ValueBox.Text;
                        break;
                    case "Sikuli Click (Image)":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.SikuliClick;
                        step.Value = ValueBox.Text;
                        break;
                    case "Sikuli Type (Image)":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.SikuliType;
                        step.Value = ValueBox.Text;
                        break;
                    case "Verify Element Text":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.Verify;
                        step.Value = ValueBox.Text;
                        break;
                    case "Verify Element Enabled":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.VerifyEnabled;
                        step.Value = "true";
                        break;
                    case "Verify Element Visible":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.VerifyVisible;
                        step.Value = "true";
                        break;
                    case "Verify Element Contains Text":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.VerifyContains;
                        step.Value = ValueBox.Text;
                        break;
                    case "Verify Element Text Matches Regex":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.VerifyRegex;
                        step.Value = ValueBox.Text;
                        break;
                    case "Verify Element Attribute":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.VerifyAttribute;
                        step.AttributeName = AttributeNameBox.Text;
                        step.Value = ValueBox.Text;
                        break;
                    case "Get Data Grid Content Ocr":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.VerifyOcr;
                        break;
                    case "Wait Until Element Exists":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.WaitExists;
                        step.Value = TimeoutBox.Text;
                        break;
                    case "Wait Until Element Visible":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.WaitVisible;
                        step.Value = TimeoutBox.Text;
                        break;
                    case "Wait Until Element Enabled":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.WaitEnabled;
                        step.Value = TimeoutBox.Text;
                        break;
                    case "Wait Until Text Contains":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.WaitTextContains;
                        step.Value = ValueBox.Text;
                        break;
                    case "Property Checkpoint":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.CheckpointProperty;
                        step.PropertyName = (PropertyNameCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Text";
                        step.Value = PropertyExpectedBox.Text;
                        break;
                    case "DataGrid Checkpoint":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.CheckpointDataGrid;
                        step.Value = ValueBox.Text;
                        break;
                    case "Count Checkpoint":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.CheckpointCount;
                        step.ExpectedCount = CountBox.Text;
                        break;
                    case "Attribute Checkpoint":
                        if (string.IsNullOrEmpty(step.Alias)) { ShowElementRequired(); return; }
                        step.Kind = StepKind.CheckpointAttribute;
                        step.AttributeName = AttributeNameBox.Text;
                        step.Value = ValueBox.Text;
                        break;
                     case "Launch Application":
                     {
                         var path = LaunchAppPathBox.Text?.Trim() ?? "";
                         if (string.IsNullOrEmpty(path))
                         {
                             MessageBox.Show(this, "Please provide a path to the executable.",
                                 "Add Step", MessageBoxButton.OK, MessageBoxImage.Warning);
                             return;
                         }
                         step.Kind = StepKind.LaunchApplication;
                         step.AppPath = path;
                         step.AppId = string.IsNullOrWhiteSpace(LaunchAppIdBox.Text)
                             ? null : LaunchAppIdBox.Text.Trim();
                         step.StartIn = string.IsNullOrWhiteSpace(LaunchStartInBox.Text)
                             ? null : LaunchStartInBox.Text.Trim();
                         step.Args = string.IsNullOrWhiteSpace(LaunchArgsBox.Text)
                             ? null : LaunchArgsBox.Text.Trim();
                         step.AutoAttach = LaunchAutoAttachCheck.IsChecked == true;
                         step.LaunchDriver = "WPFSpy";
                         step.SpyAgentEnabled = LaunchSpyAgentCheck.IsChecked == true;
                         step.PipeName = string.IsNullOrWhiteSpace(LaunchPipeNameBox.Text)
                             ? null : LaunchPipeNameBox.Text.Trim();
                         break;
                     }
                    case "Terminate Application":
                    {
                        var appId = string.IsNullOrWhiteSpace(TerminateAppIdBox.Text)
                            ? null : TerminateAppIdBox.Text.Trim();
                        var title = string.IsNullOrWhiteSpace(TerminateWindowTitleBox.Text)
                            ? null : TerminateWindowTitleBox.Text.Trim();
                        var proc = string.IsNullOrWhiteSpace(TerminateProcessNameBox.Text)
                            ? null : TerminateProcessNameBox.Text.Trim();
                        if (appId == null && title == null && proc == null)
                        {
                            MessageBox.Show(this,
                                "Provide at least one of: Application ID, Window Title, or Process Name.",
                                "Add Step", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        step.Kind = StepKind.TerminateApplication;
                        step.AppId = appId;
                        step.WindowTitle = title;
                        step.ProcessName = proc;
                        step.ForceTerminate = TerminateForceCheck.IsChecked == true;
                        break;
                    }
                }

                CreatedStep = step;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Error adding step: {ex.Message}\n{ex.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowElementRequired()
        {
            MessageBox.Show(this, "Please select an element.", "Add Step",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CreatedStep = null;
            DialogResult = false;
        }
    }
}
