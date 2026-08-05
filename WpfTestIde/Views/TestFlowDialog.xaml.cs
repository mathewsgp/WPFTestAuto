using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfTestIde.ViewModels;

namespace WpfTestIde.Views
{
    public partial class TestFlowDialog : Window
    {
        private TestFlowViewModel _viewModel;

        public TestFlowDialog()
        {
            InitializeComponent();
            _viewModel = new TestFlowViewModel();
            DataContext = _viewModel;
        }

        public TestFlowDialog(ObservableCollection<ElementEntry> elements)
        {
            InitializeComponent();
            _viewModel = new TestFlowViewModel();
            _viewModel.AvailableElements = elements;
            DataContext = _viewModel;
        }

        public TestFlowDialog(ObservableCollection<RecordedStep> existingSteps)
        {
            InitializeComponent();
            _viewModel = new TestFlowViewModel();
            _viewModel.LoadSteps(existingSteps);
            DataContext = _viewModel;
        }

        public FlowStepCollection Steps => new FlowStepCollection(_viewModel.Steps);
        public string TestName => _viewModel.TestName;
        public string GeneratedCode => _viewModel.GenerateRobotTest();

        private void AddAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string actionType)
            {
                _viewModel.SelectedElement = _viewModel.AvailableElements.FirstOrDefault();
                
                var step = new FlowStep
                {
                    StepNumber = _viewModel.Steps.Count + 1,
                    ActionType = actionType,
                    ElementAlias = _viewModel.SelectedElement?.Alias ?? "",
                    Status = FlowStepStatus.Pending
                };
                
                _viewModel.Steps.Add(step);
                _viewModel.SelectedStep = step;
            }
        }

        private void AddStep_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AddStep();
        }

        private void LoadSteps_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Robot Framework Tests (*.robot)|*.robot|All Files (*.*)|*.*",
                Title = "Load Test Steps"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var content = System.IO.File.ReadAllText(dialog.FileName);
                    ParseRobotFile(content);
                    MessageBox.Show("Test loaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to load test: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ParseRobotFile(string content)
        {
            _viewModel.Steps.Clear();
            
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool inTestCase = false;
            string currentTestName = "Imported Test";
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                if (trimmed.StartsWith("*** Test Cases ***"))
                {
                    inTestCase = true;
                    continue;
                }
                
                if (trimmed.StartsWith("***"))
                {
                    inTestCase = false;
                    continue;
                }
                
                if (inTestCase && !trimmed.StartsWith("[") && !trimmed.StartsWith("    "))
                {
                    currentTestName = trimmed;
                    _viewModel.TestName = currentTestName;
                    continue;
                }
                
                if (inTestCase && trimmed.StartsWith("[Documentation]"))
                {
                    _viewModel.Description = trimmed.Replace("[Documentation]", "").Trim();
                    continue;
                }
                
                if (inTestCase && trimmed.StartsWith("    "))
                {
                    var keyword = trimmed.Trim();
                    var step = ParseKeyword(keyword);
                    if (step != null)
                    {
                        step.StepNumber = _viewModel.Steps.Count + 1;
                        _viewModel.Steps.Add(step);
                    }
                }
            }
        }

        private FlowStep? ParseKeyword(string keyword)
        {
            var parts = keyword.Split(new[] { "    ", "\t" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;

            var keywordName = parts[0];
            
            var step = new FlowStep { Status = FlowStepStatus.Pending };

            if (keywordName == "Click Element" || keywordName == "click element")
            {
                step.ActionType = "Click";
                step.ElementAlias = ExtractArg(parts, "alias") ?? "";
            }
            else if (keywordName == "Double Click Element" || keywordName == "double click element")
            {
                step.ActionType = "DoubleClick";
                step.ElementAlias = ExtractArg(parts, "alias") ?? "";
            }
            else if (keywordName == "Input Text" || keywordName == "input text")
            {
                step.ActionType = "SetText";
                step.ElementAlias = ExtractArg(parts, "alias") ?? "";
                step.Value = ExtractArg(parts, "text") ?? "";
            }
            else if (keywordName == "Get Text" || keywordName == "get text")
            {
                step.ActionType = "GetText";
                step.ElementAlias = ExtractArg(parts, "alias") ?? "";
            }
            else if (keywordName == "Select From List By Label" || keywordName == "select from list by label")
            {
                step.ActionType = "Select";
                step.ElementAlias = ExtractArg(parts, "alias") ?? "";
                step.Value = ExtractArg(parts, "label") ?? "";
            }
            else if (keywordName == "Select Checkbox" || keywordName == "select checkbox")
            {
                step.ActionType = "Check";
                step.ElementAlias = ExtractArg(parts, "alias") ?? "";
            }
            else if (keywordName == "Unselect Checkbox" || keywordName == "unselect checkbox")
            {
                step.ActionType = "Uncheck";
                step.ElementAlias = ExtractArg(parts, "alias") ?? "";
            }
            else if (keywordName == "Sleep" || keywordName == "sleep")
            {
                step.ActionType = "Wait";
                step.Value = ExtractArg(parts, "") ?? "1000";
            }
            else if (keywordName == "Capture Page Screenshot" || keywordName == "capture page screenshot")
            {
                step.ActionType = "Screenshot";
            }
            else if (keywordName == "Verify Element Text" || keywordName == "verify element text")
            {
                step.ActionType = "Verify";
                step.CheckpointType = "Text";
                step.ElementAlias = ExtractArg(parts, "alias") ?? "";
                step.ExpectedValue = ExtractArg(parts, "expected") ?? "";
            }
            else
            {
                // Generic unknown keyword
                step.ActionType = "Click";
                step.ElementAlias = keywordName;
            }

            return step;
        }

        private string? ExtractArg(string[] parts, string argName)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (argName != "" && parts[i].Equals(argName, StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < parts.Length)
                        return parts[i + 1];
                }
                else if (argName == "" && i > 0)
                {
                    return parts[i];
                }
            }
            return null;
        }

        private void SaveTest_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Robot Framework Tests (*.robot)|*.robot|All Files (*.*)|*.*",
                Title = "Save Test",
                FileName = $"{_viewModel.TestName}.robot"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var code = _viewModel.GenerateRobotTest();
                    System.IO.File.WriteAllText(dialog.FileName, code);
                    MessageBox.Show("Test saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save test: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void GenerateCode_Click(object sender, RoutedEventArgs e)
        {
            var code = _viewModel.GenerateRobotTest();
            var dialog = new CodePreviewDialog(code);
            dialog.ShowDialog();
        }

        private void ShowFlowDiagram_Click(object sender, RoutedEventArgs e)
        {
            var diagram = _viewModel.GenerateFlowDiagram();
            var dialog = new CodePreviewDialog(diagram, "Flow Diagram");
            dialog.ShowDialog();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ActionSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Filter actions based on search text
            var filter = ActionSearchBox.Text.ToLower();
            // This would filter the action palette in a full implementation
        }

        private void StepsListBox_Drop(object sender, DragEventArgs e)
        {
            // Handle drag and drop for reordering steps
            if (e.Data.GetDataPresent(typeof(FlowStep)))
            {
                var droppedData = e.Data.GetData(typeof(FlowStep)) as FlowStep;
                // Implement reordering logic
            }
        }

        private void StepsListBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(FlowStep)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// Wrapper class to expose Steps collection from ViewModel.
    /// </summary>
    public class FlowStepCollection
    {
        private readonly ObservableCollection<FlowStep> _steps;

        public FlowStepCollection(ObservableCollection<FlowStep> steps)
        {
            _steps = steps;
        }

        public ObservableCollection<FlowStep> Steps => _steps;
    }

    /// <summary>
    /// Simple code preview dialog.
    /// </summary>
    public class CodePreviewDialog : Window
    {
        public CodePreviewDialog(string code, string title = "Generated Code")
        {
            Title = title;
            Width = 600;
            Height = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var header = new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(16),
                FontSize = 14
            };
            Grid.SetRow(header, 0);
            grid.Children.Add(header);

            var textBox = new TextBox
            {
                Text = code,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                IsReadOnly = true,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(16, 0, 16, 16),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 50, 56)),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 230, 118)),
                Padding = new Thickness(8)
            };
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            Content = grid;
        }
    }
}
