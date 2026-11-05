using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WpfTestIde.Helpers;

/// <summary>Persisted window and AvalonDock state for the IDE.</summary>
public sealed class LayoutState
{
    public int WindowState { get; set; }
    public bool HasWindowBounds { get; set; }
    public double Top { get; set; }
    public double Left { get; set; }
    public double Width { get; set; } = 1400;
    public double Height { get; set; } = 760;

    public string Theme { get; set; } = "Light";
    public int SelectedTabIndex { get; set; }
    public string? ActivePaneId { get; set; }
    public string? DockLayoutJson { get; set; }

    // AvalonDock 5 can normalize absolute DockWidth values back to star sizing
    // during layout. These maps preserve the user's last pixel widths/heights.
    public Dictionary<string, double> PaneWidths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, double> PaneHeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // The Elements pane has an internal splitter outside AvalonDock's layout tree.
    public double TreeColumnWidth { get; set; }
    public double PropertiesColumnWidth { get; set; }

    public bool RepositoryPanelExpanded { get; set; }
    public bool RunOutputPanelExpanded { get; set; }
}

/// <summary>Best-effort persistence in %AppData%\WpfTestIde\layout.json.</summary>
public static class LayoutPersistence
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WpfTestIde");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "layout.json");
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static LayoutState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new LayoutState();
            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json)) return new LayoutState();
            var state = JsonSerializer.Deserialize<LayoutState>(json, Options) ?? new LayoutState();
            state.PaneWidths ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            state.PaneHeights ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            return state;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Layout state load failed; using defaults: {ex}");
            return new LayoutState();
        }
    }

    public static void Save(LayoutState state)
    {
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, Options));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Layout state save failed: {ex}");
            if (temporaryPath != null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception cleanupException) { Debug.WriteLine($"Temporary layout cleanup failed: {cleanupException.Message}"); }
            }
        }
    }
}
