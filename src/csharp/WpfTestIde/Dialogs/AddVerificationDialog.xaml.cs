using System.Collections.Generic;
using System.Windows;
using WpfTestIde.Models;
using WpfTestIde.Recording;

namespace WpfTestIde.Dialogs
{
    public partial class AddVerificationDialog : Window
    {
        private readonly List<ElementEntry> _elements;
        private readonly string _pipeName;

        public string SelectedAlias { get; private set; } = "";
        public string ExpectedValue { get; private set; } = "";

        public AddVerificationDialog(List<ElementEntry> elements, string pipeName = "WPFSpyAgentPipe")
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
        }

        private void AliasCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (AliasCombo.SelectedItem is not ElementEntry entry)
            {
                return;
            }

            // Best-effort: if a live Spy Agent is reachable, pre-fill with
            // the element's CURRENT on-screen text — the common real-world
            // authoring move of "look at what's showing right now and
            // assert that". Silently falls back to empty if unreachable
            // (e.g. no live app attached, using sample/offline data).
            try
            {
                var client = new SpyAgentClient(_pipeName);
                var response = client.Send("GetText", name: entry.Name);
                ExpectedBox.Text = response.Success ? (response.Data ?? "") : "";
            }
            catch
            {
                ExpectedBox.Text = "";
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (AliasCombo.SelectedItem is not ElementEntry entry)
            {
                MessageBox.Show(this, "Select an element first.", "Add Verification",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SelectedAlias = entry.Alias;
            ExpectedValue = ExpectedBox.Text;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
