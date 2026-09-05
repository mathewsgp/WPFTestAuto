using System.Windows;

namespace WpfTestIde.Dialogs
{
    /// <summary>
    /// Settings dialog for the Sikuli image-based driver. Exposes the
    /// knobs that the framework reads from environment variables, so the
    /// operator can tune them from the IDE without editing shell profiles.
    /// </summary>
    public partial class SikuliSettingsDialog : Window
    {
        public double Similarity { get; set; } = 0.85;
        public int CapturePaddingPx { get; set; } = 4;
        public string Matcher { get; set; } = "multi";
        public string ScreenCapture { get; set; } = "mss";

        public SikuliSettingsDialog()
        {
            InitializeComponent();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            Similarity = sldSimilarity.Value;
            CapturePaddingPx = (int)System.Math.Round(sldPadding.Value);
            Matcher = (cbMatcher.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "multi";
            ScreenCapture = (cbCapture.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "mss";

            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
