using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using WpfTestIde.Execution;
using WpfTestIde.Models;

namespace WpfTestIde.Dialogs
{
    public sealed class ScriptDatasetsDialog : Window
    {
        private readonly TextBox _editor;

        public Dictionary<string, List<string>> Datasets { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        public ScriptDatasetsDialog(IReadOnlyDictionary<string, List<string>> datasets)
        {
            Title = "Script datasets";
            Width = 620;
            Height = 480;
            MinWidth = 480;
            MinHeight = 320;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new DockPanel { Margin = new Thickness(12) };
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0),
            };
            DockPanel.SetDock(footer, Dock.Bottom);
            var cancel = new Button { Content = "Cancel", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            var save = new Button { Content = "Apply", MinWidth = 90, IsDefault = true };
            save.Click += Save_Click;
            footer.Children.Add(cancel);
            footer.Children.Add(save);
            root.Children.Add(footer);

            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(new TextBlock
            {
                Text = "Enter named datasets as JSON object properties containing string arrays. ForEach loops can reference these names.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
            _editor = new TextBox
            {
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Text = JsonSerializer.Serialize(datasets, new JsonSerializerOptions { WriteIndented = true }),
            };
            Grid.SetRow(_editor, 1);
            content.Children.Add(_editor);
            root.Children.Add(content);
            Content = root;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(
                    _editor.Text,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new Dictionary<string, List<string>>();
                ScriptFlowValidator.Validate(Array.Empty<RecordedStep>(), parsed);
                Datasets = new Dictionary<string, List<string>>(parsed, StringComparer.OrdinalIgnoreCase);
                DialogResult = true;
                Close();
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                MessageBox.Show(this, ex.Message, "Invalid datasets", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
