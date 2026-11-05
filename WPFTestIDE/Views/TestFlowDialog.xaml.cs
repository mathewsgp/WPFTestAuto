using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WpfTestIde.Dialogs;
using WpfTestIde.Execution;
using WpfTestIde.Models;
using WpfTestIde.Recording;
using WpfTestIde.ViewModels;

namespace WpfTestIde.Views
{
    public partial class TestFlowDialog : Window
    {
        private readonly TestFlowViewModel _viewModel;

        public TestFlowDialog()
            : this(new ObservableCollection<ElementEntry>(), Array.Empty<RecordedStep>(), null)
        {
        }

        public TestFlowDialog(ObservableCollection<ElementEntry> elements)
            : this(elements, Array.Empty<RecordedStep>(), null)
        {
        }

        public TestFlowDialog(
            ObservableCollection<ElementEntry> elements,
            IEnumerable<RecordedStep> existingNodes,
            IReadOnlyDictionary<string, List<string>>? datasets)
        {
            InitializeComponent();
            _viewModel = new TestFlowViewModel();
            foreach (var element in elements)
                _viewModel.AvailableElements.Add(element);
            _viewModel.LoadSteps(existingNodes);
            _viewModel.LoadDatasets(datasets);
            DataContext = _viewModel;
        }

        public ObservableCollection<RecordedStep> Nodes => _viewModel.Steps;
        public IReadOnlyDictionary<string, List<string>> Datasets => _viewModel.Datasets;
        public string TestName => _viewModel.TestName;
        public string GeneratedCode => _viewModel.GenerateRobotTest();

        private void LoadSteps_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Interoperable scripts (*.json;*.py;*.robot)|*.json;*.py;*.robot|All files (*.*)|*.*",
                Title = "Import script",
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                var format = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
                {
                    ".json" => ScriptFormat.Json,
                    ".py" => ScriptFormat.Python,
                    ".robot" => ScriptFormat.Robot,
                    _ => throw new FormatException("Choose a .json, .py, or .robot script."),
                };
                var imported = ScriptInterop.Parse(format, File.ReadAllText(dialog.FileName));
                _viewModel.LoadSteps(imported.Steps);
                _viewModel.LoadDatasets(imported.Datasets);
                MessageBox.Show(this, $"Imported {imported.Steps.Count} top-level node(s).", "Import complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveRobot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var code = _viewModel.GenerateRobotTest();
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Robot Framework (*.robot)|*.robot|JSON script (*.json)|*.json|Python script (*.py)|*.py",
                    Title = "Export interoperable script",
                    FileName = $"{_viewModel.TestName}.robot",
                    DefaultExt = ".robot",
                };
                if (dialog.ShowDialog(this) != true) return;
                string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                if (extension == ".json")
                    code = JsonStepScript.Serialize(Nodes, datasets: _viewModel.Datasets);
                else if (extension == ".py")
                    code = ScriptGenerator.GeneratePython(Nodes, "test_" + SanitizeName(_viewModel.TestName), datasets: _viewModel.Datasets);
                File.WriteAllText(dialog.FileName, code);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EditDatasets_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ScriptDatasetsDialog(_viewModel.Datasets) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            try
            {
                ScriptFlowValidator.Validate(Nodes, dialog.Datasets);
                _viewModel.LoadDatasets(dialog.Datasets);
            }
            catch (FormatException ex)
            {
                MessageBox.Show(this, ex.Message, "Datasets are in use", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static string SanitizeName(string name)
        {
            var chars = name.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray();
            string result = new string(chars).Trim('_');
            return string.IsNullOrWhiteSpace(result) ? "flow" : result;
        }

        private void GenerateCode_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                new CodePreviewDialog(_viewModel.GenerateRobotTest()).ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Cannot generate code", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ShowFlowDiagram_Click(object sender, RoutedEventArgs e)
            => new CodePreviewDialog(_viewModel.GenerateFlowDiagram(), "Flow Diagram").ShowDialog();

        private void UseFlow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ScriptFlowValidator.Validate(Nodes, _viewModel.Datasets);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Invalid flow", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public sealed class CodePreviewDialog : Window
    {
        public CodePreviewDialog(string code, string title = "Generated Code")
        {
            Title = title;
            Width = 700;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var grid = new Grid { Margin = new Thickness(14) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var header = new TextBlock { Text = title, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10), FontSize = 14 };
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
                Padding = new Thickness(8),
            };
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);
            Content = grid;
        }
    }
}
