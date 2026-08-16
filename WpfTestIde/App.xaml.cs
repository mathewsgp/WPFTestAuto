using System.Windows;

namespace WpfTestIde
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Create theme brushes programmatically so they are NOT frozen.
            // Brushes created in code remain mutable, allowing ThemeManager
            // to replace them in place for live theme switching via DynamicResource.
            var lightBrushes = new (string Key, System.Windows.Media.Color Color)[]
            {
                ("BackgroundBrush", System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)),
                ("SidebarBrush", System.Windows.Media.Color.FromRgb(0xF3, 0xF3, 0xF3)),
                ("PanelBrush", System.Windows.Media.Color.FromRgb(0xE7, 0xE7, 0xE7)),
                ("BorderBrush", System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0)),
                ("InputBackgroundBrush", System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)),
                ("InputBorderBrush", System.Windows.Media.Color.FromRgb(0xC0, 0xC0, 0xC0)),
                ("TextPrimaryBrush", System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33)),
                ("TextSecondaryBrush", System.Windows.Media.Color.FromRgb(0x66, 0x66, 0x66)),
                ("TextDisabledBrush", System.Windows.Media.Color.FromRgb(0x99, 0x99, 0x99)),
                ("AccentBrush", System.Windows.Media.Color.FromRgb(0x00, 0x7A, 0xCC)),
                ("AccentHoverBrush", System.Windows.Media.Color.FromRgb(0x00, 0x98, 0xFF)),
                ("AccentPressedBrush", System.Windows.Media.Color.FromRgb(0x00, 0x62, 0xA3)),
                ("SuccessBrush", System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)),
                ("WarningBrush", System.Windows.Media.Color.FromRgb(0xFF, 0x98, 0x00)),
                ("ErrorBrush", System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36)),
                ("ToolbarBrush", System.Windows.Media.Color.FromRgb(0xF3, 0xF3, 0xF3)),
                ("StatusBarBrush", System.Windows.Media.Color.FromRgb(0x00, 0x7A, 0xCC)),
                ("StatusBarTextBrush", System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)),
                ("ButtonBackgroundBrush", System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8)),
                ("ButtonHoverBrush", System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0)),
                ("ButtonPressedBrush", System.Windows.Media.Color.FromRgb(0xB8, 0xB8, 0xB8)),
                ("ButtonBorderBrush", System.Windows.Media.Color.FromRgb(0xC0, 0xC0, 0xC0)),
                ("TabActiveBrush", System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF)),
                ("TabInactiveBrush", System.Windows.Media.Color.FromRgb(0xE7, 0xE7, 0xE7)),
                ("TabBorderBrush", System.Windows.Media.Color.FromRgb(0xD0, 0xD0, 0xD0)),
                ("ListBoxItemHoverBrush", System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8)),
                ("ListBoxItemSelectedBrush", System.Windows.Media.Color.FromRgb(0xCC, 0xE8, 0xFF)),
                ("ListBoxItemSelectedBorderBrush", System.Windows.Media.Color.FromRgb(0x00, 0x7A, 0xCC)),
            };

            foreach (var (key, color) in lightBrushes)
            {
                Resources[key] = new System.Windows.Media.SolidColorBrush(color);
            }

            base.OnStartup(e);
        }
    }
}
