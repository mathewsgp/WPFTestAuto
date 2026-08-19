using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
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
                             or "Verify Element Text Matches Regex" or "Verify Element Attribute" or "Press Keys"
                             or "Drag And Drop" or "Scroll" or "Sikuli Click (Image)" or "Sikuli Type (Image)";
            bool showTimeout = text is "Wait Until Element Exists" or "Wait Until Element Visible" 
                               or "Wait Until Element Enabled" or "Wait Until Text Contains";

            ValueLabel.Visibility = showValue ? Visibility.Visible : Visibility.Collapsed;
            ValueBox.Visibility = showValue ? Visibility.Visible : Visibility.Collapsed;
            TimeoutLabel.Visibility = showTimeout ? Visibility.Visible : Visibility.Collapsed;
            TimeoutBox.Visibility = showTimeout ? Visibility.Visible : Visibility.Collapsed;

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

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AliasCombo.SelectedItem is not ElementEntry entry)
                {
                    MessageBox.Show(this, "Please select an element.", "Add Step",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var selectedItem = StepTypeCombo.SelectedItem as ComboBoxItem;
                if (selectedItem == null) return;

                var stepType = selectedItem.Content?.ToString() ?? "";
                var step = new RecordedStep
                {
                    Alias = entry.Alias
                };

                switch (stepType)
                {
                    case "Click Element":
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.Invoke;
                        break;
                    case "Set Element Value":
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.SetValue;
                        step.Value = ValueBox.Text;
                        break;
                    case "Toggle Element":
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.Toggle;
                        break;
                    case "Double Click Element":
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.DoubleClick;
                        break;
                    case "Right Click Element":
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.RightClick;
                        break;
                    case "Drag And Drop":
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.DragDrop;
                        step.Value = ValueBox.Text;
                        break;
                    case "Hover Over Element":
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.Hover;
                        break;
                    case "Press Keys":
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.PressKeys;
                        step.Value = ValueBox.Text;
                        break;
                    case "Scroll":
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.Scroll;
                        step.Value = ValueBox.Text;
                        break;
                    case "Sikuli Click (Image)":
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.SikuliClick;
                        step.Value = ValueBox.Text;
                        break;
                    case "Sikuli Type (Image)":
                        step.Kind = StepKind.Action;
                        step.Action = ActionKind.SikuliType;
                        step.Value = ValueBox.Text;
                        break;
                    case "Verify Element Text":
                        step.Kind = StepKind.Verify;
                        step.Value = ValueBox.Text;
                        break;
                    case "Verify Element Enabled":
                        step.Kind = StepKind.VerifyEnabled;
                        step.Value = "true";
                        break;
                    case "Verify Element Visible":
                        step.Kind = StepKind.VerifyVisible;
                        step.Value = "true";
                        break;
                    case "Verify Element Contains Text":
                        step.Kind = StepKind.VerifyContains;
                        step.Value = ValueBox.Text;
                        break;
                    case "Verify Element Text Matches Regex":
                        step.Kind = StepKind.VerifyRegex;
                        step.Value = ValueBox.Text;
                        break;
                    case "Verify Element Attribute":
                        step.Kind = StepKind.VerifyAttribute;
                        step.Value = ValueBox.Text;
                        break;
                    case "Get Data Grid Content Ocr":
                        step.Kind = StepKind.VerifyOcr;
                        break;
                    case "Wait Until Element Exists":
                        step.Kind = StepKind.WaitExists;
                        step.Value = TimeoutBox.Text;
                        break;
                    case "Wait Until Element Visible":
                        step.Kind = StepKind.WaitVisible;
                        step.Value = TimeoutBox.Text;
                        break;
                    case "Wait Until Element Enabled":
                        step.Kind = StepKind.WaitEnabled;
                        step.Value = TimeoutBox.Text;
                        break;
                    case "Wait Until Text Contains":
                        step.Kind = StepKind.WaitTextContains;
                        step.Value = ValueBox.Text;
                        break;
                    case "Property Checkpoint":
                        step.Kind = StepKind.CheckpointProperty;
                        break;
                    case "DataGrid Checkpoint":
                        step.Kind = StepKind.CheckpointDataGrid;
                        break;
                    case "Count Checkpoint":
                        step.Kind = StepKind.CheckpointCount;
                        break;
                    case "Attribute Checkpoint":
                        step.Kind = StepKind.CheckpointAttribute;
                        break;
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

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            CreatedStep = null;
            DialogResult = false;
        }
    }
}
