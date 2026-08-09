using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WpfTestIde.Models;
using WpfTestIde.Recording;

namespace WpfTestIde.Dialogs
{
    public partial class CheckpointWizardDialog : Window
    {
        private readonly List<ElementEntry> _elements;
        private readonly string _pipeName;
        private readonly CheckpointRecorder _checkpointRecorder;
        private CheckpointEntry? _createdCheckpoint;
        private int _currentStep = 1;

        public CheckpointEntry? CreatedCheckpoint => _createdCheckpoint;

        public CheckpointWizardDialog(
            List<ElementEntry> elements, 
            string pipeName = "WPFSpyAgentPipe",
            CheckpointRecorder? checkpointRecorder = null)
        {
            InitializeComponent();
            _elements = elements;
            _pipeName = pipeName;
            _checkpointRecorder = checkpointRecorder ?? new CheckpointRecorder(pipeName);

            ElementCombo.ItemsSource = elements;
            ElementCombo.DisplayMemberPath = nameof(ElementEntry.Alias);
            if (elements.Count > 0)
            {
                ElementCombo.SelectedIndex = 0;
            }

            UpdateStepIndicator();
        }

        private void UpdateStepIndicator()
        {
            var stepText = _currentStep switch
            {
                1 => "Step 1: Select Element & Checkpoint Type",
                2 => "Step 2: Configure Checkpoint",
                _ => "Checkpoint Configuration"
            };
             StepIndicator.Text = stepText;
             btnCheckpointBack.IsEnabled = _currentStep > 1;
        }

        private void ElementCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Could pre-populate values based on selected element
        }

        private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Show/hide appropriate panels based on checkpoint type
            var selectedIndex = TypeCombo.SelectedIndex;

            PropertyPanel.Visibility = selectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            AreaPanel.Visibility = selectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            ImagePanel.Visibility = selectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            DataGridPanel.Visibility = selectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            AttributePanel.Visibility = selectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
            CountPanel.Visibility = selectedIndex == 5 ? Visibility.Visible : Visibility.Collapsed;

            if (selectedIndex == 0)
            {
                // Auto-populate current value for property checkpoints
                GetCurrentValue_Click(null, new RoutedEventArgs());
            }
        }

        private void GetCurrentValue_Click(object sender, RoutedEventArgs e)
        {
            if (ElementCombo.SelectedItem is not ElementEntry element)
            {
                return;
            }

            try
            {
                var client = new SpyAgentClient(_pipeName);
                var propertyName = (PropertyCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Text";

                string currentValue = propertyName.ToLower() switch
                {
                    "text" => GetElementText(client, element.Name),
                    "isenabled" => GetElementEnabled(client, element.Name),
                    "isvisible" => GetElementVisible(client, element.Name),
                    _ => ""
                };

                ExpectedValueBox.Text = currentValue;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to get current value: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string GetElementText(SpyAgentClient client, string name)
        {
            var response = client.Send("GetText", name: name);
            return response.Success ? (response.Data ?? "") : "";
        }

        private string GetElementEnabled(SpyAgentClient client, string name)
        {
            var response = client.Send("IsEnabled", name: name);
            return response.Success ? (response.Data ?? "false") : "false";
        }

        private string GetElementVisible(SpyAgentClient client, string name)
        {
            var response = client.Send("IsVisible", name: name);
            return response.Success ? (response.Data ?? "false") : "false";
        }

        private void SelectArea_Click(object sender, RoutedEventArgs e)
        {
            // Close this dialog and let user select area on screen
            // This would typically launch a fullscreen overlay for area selection
            MessageBox.Show(this, 
                "Area selection will open a transparent overlay.\nClick and drag to select the area.\nPress Escape to cancel.",
                "Select Area", MessageBoxButton.OK, MessageBoxImage.Information);

            // For now, use current values or prompt
            if (double.TryParse(XBox.Text, out var x) &&
                double.TryParse(YBox.Text, out var y) &&
                double.TryParse(WidthBox.Text, out var width) &&
                double.TryParse(HeightBox.Text, out var height))
            {
                // Area is configured
                OcrTextBox.Text = $"[Would capture OCR from area ({x},{y},{width},{height})]";
            }
        }

        private void CaptureArea_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(ImageXBox.Text, out var x) &&
                double.TryParse(ImageYBox.Text, out var y))
            {
                var baselinePath = $"baseline_images/area_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                BaselinePathBox.Text = baselinePath;
            }
        }

        private void GetDataGridContent_Click(object sender, RoutedEventArgs e)
        {
            if (ElementCombo.SelectedItem is not ElementEntry element)
            {
                return;
            }

            try
            {
                var client = new SpyAgentClient(_pipeName);
                var response = client.Send("GetDataGridContent", name: element.Name);
                if (response.Success)
                {
                    DataGridContentBox.Text = response.Data ?? "";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to get DataGrid content: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                UpdateStepIndicator();
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (!ValidateInput())
            {
                return;
            }

            CreateCheckpoint();
            DialogResult = true;
        }

        private bool ValidateInput()
        {
            if (ElementCombo.SelectedItem == null)
            {
                MessageBox.Show(this, "Please select an element.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            var selectedType = TypeCombo.SelectedIndex;
            
            // Validate based on checkpoint type
            switch (selectedType)
            {
                case 0: // Property
                    if (string.IsNullOrWhiteSpace(ExpectedValueBox.Text))
                    {
                        MessageBox.Show(this, "Please enter an expected value.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    break;

                case 1: // Area
                    if (!double.TryParse(XBox.Text, out _) ||
                        !double.TryParse(YBox.Text, out _) ||
                        !double.TryParse(WidthBox.Text, out _) ||
                        !double.TryParse(HeightBox.Text, out _))
                    {
                        MessageBox.Show(this, "Please enter valid area coordinates.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    break;

                case 5: // Count
                    if (!int.TryParse(CountBox.Text, out _))
                    {
                        MessageBox.Show(this, "Please enter a valid count.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    break;
            }

            return true;
        }

        private void CreateCheckpoint()
        {
            if (ElementCombo.SelectedItem is not ElementEntry element)
            {
                return;
            }

            var selectedType = TypeCombo.SelectedIndex;
            var description = DescriptionBox.Text;

            switch (selectedType)
            {
                case 0: // Property
                    var propertyName = (PropertyCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Text";
                    _createdCheckpoint = _checkpointRecorder.CreatePropertyCheckpoint(
                        element.Alias, propertyName, ExpectedValueBox.Text, description);
                    break;

                case 1: // Area
                    _createdCheckpoint = _checkpointRecorder.CreateAreaCheckpoint(
                        double.Parse(XBox.Text),
                        double.Parse(YBox.Text),
                        double.Parse(WidthBox.Text),
                        double.Parse(HeightBox.Text),
                        OcrTextBox.Text,
                        description);
                    break;

                case 2: // Image
                    double imgX = 0, imgY = 0;
                    double.TryParse(ImageXBox.Text, out imgX);
                    double.TryParse(ImageYBox.Text, out imgY);
                    _createdCheckpoint = _checkpointRecorder.CreateImageCheckpoint(
                        imgX, imgY, 100, 100, BaselinePathBox.Text, description);
                    break;

                case 3: // DataGrid
                    _createdCheckpoint = _checkpointRecorder.CreateDataGridCheckpoint(
                        element.Alias, DataGridContentBox.Text, description);
                    break;

                case 4: // Attribute
                    _createdCheckpoint = _checkpointRecorder.CreateAttributeCheckpoint(
                        element.Alias, AttributeNameBox.Text, AttributeValueBox.Text, description);
                    break;

                case 5: // Count
                    _createdCheckpoint = _checkpointRecorder.CreateCountCheckpoint(
                        element.Alias, int.Parse(CountBox.Text), description);
                    break;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _createdCheckpoint = null;
            DialogResult = false;
        }
    }
}
