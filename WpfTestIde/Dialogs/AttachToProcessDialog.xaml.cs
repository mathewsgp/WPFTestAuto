using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;

namespace WpfTestIde.Dialogs
{
    public class PageMapRow
    {
        public string TitleContains { get; set; } = "";
        public string PageAlias { get; set; } = "";
    }

    public partial class AttachToProcessDialog : Window
    {
        public int? SelectedProcessId { get; private set; }
        public string PipeName { get; private set; } = "WPFSpyAgentPipe";
        public List<(string, string)> PageMap { get; private set; } = new();

        private readonly ObservableCollection<PageMapRow> _pageMapRows = new()
        {
            new PageMapRow { TitleContains = "Login", PageAlias = "LoginPage" },
            new PageMapRow { TitleContains = "Orders", PageAlias = "OrdersPage" },
        };

        public AttachToProcessDialog()
        {
            InitializeComponent();
            PageMapGrid.ItemsSource = _pageMapRows;

            var candidates = Process.GetProcesses()
                .Where(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle))
                .OrderBy(p => p.MainWindowTitle)
                .ToList();
            ProcessListView.ItemsSource = candidates;
        }

        private void Attach_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessListView.SelectedItem is Process proc)
            {
                SelectedProcessId = proc.Id;
            }
            PipeName = string.IsNullOrWhiteSpace(PipeNameBox.Text) ? "WPFSpyAgentPipe" : PipeNameBox.Text.Trim();
            PageMap = _pageMapRows
                .Where(r => !string.IsNullOrWhiteSpace(r.TitleContains) && !string.IsNullOrWhiteSpace(r.PageAlias))
                .Select(r => (r.TitleContains, r.PageAlias))
                .ToList();

            if (SelectedProcessId is null)
            {
                MessageBox.Show(this, "Select a process first.", "Attach to Process",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
