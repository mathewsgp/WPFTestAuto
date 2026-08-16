using System;
using System.Windows;
using System.Windows.Media;

namespace WpfTestIde.Themes;

/// <summary>
/// Manages VS Code-like theme switching for the application.
/// Supports light and dark themes.
/// </summary>
public static class ThemeManager
{
    private const string ThemeMarkerKey = "CurrentThemeKey";
    
    // Light theme colors (VS Code Light+)
    private static readonly Color[] LightColors = new Color[]
    {
        Color.FromRgb(0xFF, 0xFF, 0xFF), // BackgroundBrush
        Color.FromRgb(0xF3, 0xF3, 0xF3), // SidebarBrush
        Color.FromRgb(0xE7, 0xE7, 0xE7), // PanelBrush
        Color.FromRgb(0xD0, 0xD0, 0xD0), // BorderBrush
        Color.FromRgb(0xFF, 0xFF, 0xFF), // InputBackgroundBrush
        Color.FromRgb(0xC0, 0xC0, 0xC0), // InputBorderBrush
        Color.FromRgb(0x33, 0x33, 0x33), // TextPrimaryBrush
        Color.FromRgb(0x66, 0x66, 0x66), // TextSecondaryBrush
        Color.FromRgb(0x99, 0x99, 0x99), // TextDisabledBrush
        Color.FromRgb(0x00, 0x7A, 0xCC), // AccentBrush
        Color.FromRgb(0x00, 0x98, 0xFF), // AccentHoverBrush
        Color.FromRgb(0x00, 0x62, 0xA3), // AccentPressedBrush
        Color.FromRgb(0x4C, 0xAF, 0x50), // SuccessBrush
        Color.FromRgb(0xFF, 0x98, 0x00), // WarningBrush
        Color.FromRgb(0xF4, 0x43, 0x36), // ErrorBrush
        Color.FromRgb(0xF3, 0xF3, 0xF3), // ToolbarBrush
        Color.FromRgb(0x00, 0x7A, 0xCC), // StatusBarBrush
        Color.FromRgb(0xFF, 0xFF, 0xFF), // StatusBarTextBrush
        Color.FromRgb(0xE8, 0xE8, 0xE8), // ButtonBackgroundBrush
        Color.FromRgb(0xD0, 0xD0, 0xD0), // ButtonHoverBrush
        Color.FromRgb(0xB8, 0xB8, 0xB8), // ButtonPressedBrush
        Color.FromRgb(0xC0, 0xC0, 0xC0), // ButtonBorderBrush
        Color.FromRgb(0xFF, 0xFF, 0xFF), // TabActiveBrush
        Color.FromRgb(0xE7, 0xE7, 0xE7), // TabInactiveBrush
        Color.FromRgb(0xD0, 0xD0, 0xD0), // TabBorderBrush
        Color.FromRgb(0xE8, 0xE8, 0xE8), // ListBoxItemHoverBrush
        Color.FromRgb(0xCC, 0xE8, 0xFF), // ListBoxItemSelectedBrush
        Color.FromRgb(0x00, 0x7A, 0xCC), // ListBoxItemSelectedBorderBrush
    };
    
    // Dark theme colors (VS Code Dark+)
    private static readonly Color[] DarkColors = new Color[]
    {
        Color.FromRgb(0x1E, 0x1E, 0x1E), // BackgroundBrush
        Color.FromRgb(0x25, 0x25, 0x26), // SidebarBrush
        Color.FromRgb(0x2D, 0x2D, 0x30), // PanelBrush
        Color.FromRgb(0x3E, 0x3E, 0x42), // BorderBrush
        Color.FromRgb(0x3C, 0x3C, 0x3C), // InputBackgroundBrush
        Color.FromRgb(0x55, 0x55, 0x55), // InputBorderBrush
        Color.FromRgb(0xCC, 0xCC, 0xCC), // TextPrimaryBrush
        Color.FromRgb(0x96, 0x96, 0x96), // TextSecondaryBrush
        Color.FromRgb(0x6D, 0x6D, 0x6D), // TextDisabledBrush
        Color.FromRgb(0x00, 0x7A, 0xCC), // AccentBrush
        Color.FromRgb(0x00, 0x98, 0xFF), // AccentHoverBrush
        Color.FromRgb(0x00, 0x62, 0xA3), // AccentPressedBrush
        Color.FromRgb(0x4C, 0xAF, 0x50), // SuccessBrush
        Color.FromRgb(0xFF, 0x98, 0x00), // WarningBrush
        Color.FromRgb(0xF4, 0x43, 0x36), // ErrorBrush
        Color.FromRgb(0x3C, 0x3C, 0x3C), // ToolbarBrush
        Color.FromRgb(0x00, 0x7A, 0xCC), // StatusBarBrush
        Color.FromRgb(0xFF, 0xFF, 0xFF), // StatusBarTextBrush
        Color.FromRgb(0x3C, 0x3C, 0x3C), // ButtonBackgroundBrush
        Color.FromRgb(0x4E, 0x4E, 0x4E), // ButtonHoverBrush
        Color.FromRgb(0x55, 0x55, 0x55), // ButtonPressedBrush
        Color.FromRgb(0x55, 0x55, 0x55), // ButtonBorderBrush
        Color.FromRgb(0x1E, 0x1E, 0x1E), // TabActiveBrush
        Color.FromRgb(0x2D, 0x2D, 0x30), // TabInactiveBrush
        Color.FromRgb(0x3E, 0x3E, 0x42), // TabBorderBrush
        Color.FromRgb(0x2A, 0x2D, 0x2E), // ListBoxItemHoverBrush
        Color.FromRgb(0x09, 0x47, 0x71), // ListBoxItemSelectedBrush
        Color.FromRgb(0x00, 0x7A, 0xCC), // ListBoxItemSelectedBorderBrush
    };
    
    private static readonly string[] BrushKeys = new string[]
    {
        "BackgroundBrush", "SidebarBrush", "PanelBrush", "BorderBrush",
        "InputBackgroundBrush", "InputBorderBrush", "TextPrimaryBrush",
        "TextSecondaryBrush", "TextDisabledBrush", "AccentBrush",
        "AccentHoverBrush", "AccentPressedBrush", "SuccessBrush",
        "WarningBrush", "ErrorBrush", "ToolbarBrush", "StatusBarBrush",
        "StatusBarTextBrush", "ButtonBackgroundBrush", "ButtonHoverBrush",
        "ButtonPressedBrush", "ButtonBorderBrush", "TabActiveBrush",
        "TabInactiveBrush", "TabBorderBrush", "ListBoxItemHoverBrush",
        "ListBoxItemSelectedBrush", "ListBoxItemSelectedBorderBrush"
    };
    
    /// <summary>
    /// Gets or sets the current theme. Use "Light" or "Dark".
    /// </summary>
    public static string CurrentTheme
    {
        get
        {
            var app = Application.Current;
            if (app == null) return "Light";
            
            if (app.Resources.Contains(ThemeMarkerKey))
                return app.Resources[ThemeMarkerKey] as string ?? "Light";
            return "Light";
        }
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                value = "Light";
            
            ApplyTheme(value);
        }
    }
    
    /// <summary>
    /// Applies the specified theme by replacing brush instances in resource dictionaries.
    /// DynamicResource references will automatically pick up the new brushes.
    /// </summary>
    public static void ApplyTheme(string theme)
    {
        var app = Application.Current;
        if (app == null) return;
        
        bool isDark = theme.Equals("Dark", StringComparison.OrdinalIgnoreCase);
        var colors = isDark ? DarkColors : LightColors;
        
        // Replace brushes so DynamicResource references update automatically
        for (int i = 0; i < BrushKeys.Length && i < colors.Length; i++)
        {
            var newBrush = new SolidColorBrush(colors[i]);
            ReplaceBrush(app, BrushKeys[i], newBrush);
        }
        
        // Store current theme
        app.Resources[ThemeMarkerKey] = theme;
    }
    
    private static void ReplaceBrush(Application app, string key, SolidColorBrush newBrush)
    {
        // Replace in main resources
        if (app.Resources.Contains(key))
        {
            app.Resources[key] = newBrush;
            return;
        }
        
        // Replace in merged dictionaries
        foreach (ResourceDictionary dict in app.Resources.MergedDictionaries)
        {
            if (dict.Contains(key))
            {
                dict[key] = newBrush;
                return;
            }
        }
        
        // If not found anywhere, add to main resources
        app.Resources[key] = newBrush;
    }
    
    private static SolidColorBrush? FindBrush(Application app, string key)
    {
        // Check main resources first
        if (app.Resources.Contains(key))
            return app.Resources[key] as SolidColorBrush;
        
        // Check merged dictionaries
        foreach (ResourceDictionary dict in app.Resources.MergedDictionaries)
        {
            if (dict.Contains(key))
                return dict[key] as SolidColorBrush;
        }
        
        return null;
    }
    
    /// <summary>
    /// Toggles between light and dark themes.
    /// </summary>
    public static void ToggleTheme()
    {
        var newTheme = CurrentTheme == "Dark" ? "Light" : "Dark";
        ApplyTheme(newTheme);
    }
}
