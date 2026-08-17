using System;
using System.IO;
using System.Text.Json;

namespace WpfTestIde.Helpers
{
    // ------------------------------------------------------------
    // E1: layout + theme persistence to %AppData%\WpfTestIde\layout.json.
    //
    // The main window snapshot lives here as a plain POCO. System.Text.Json
    // serializes it without requiring attributes (defaults are reasonable:
    // public fields with camelCase via JsonNamingPolicy.CamelCase, no BOM,
    // indented for human readability under source control or support dumps).
    //
    // Persisted:
    //   - Window state/geometry (so the app reopens where the user left it)
    //   - Theme key (round-trips through ThemeManager.CurrentTheme)
    //   - Last active main tab (binding to MainViewModel.SelectedTabIndex)
    //   - A1 Element-Tree <-> Properties splitter column widths in pixels
    //   - A3/A4/A5 collapsible bottom-panel expanded booleans
    //
    // NOT persisted (kept as defaults):
    //   - B3 toolbar mode (not shipped), A2 Element-Tree pinned (reverted).
    // ------------------------------------------------------------

    /// <summary>POCO snapshot of the user's window + panel layout. All fields
    /// default to framework defaults / panel-collapsed so a missing or partial
    /// file always loads something sane.</summary>
    public class LayoutState
    {
        // WindowState as int: 0=Normal, 1=Minimized, 2=Maximized. Persisted as int
        // (rather than the enum) so System.Text.Json doesn't pull in the Windows
        // Base assembly's enum serializer; the converter is symmetric here.
        public int WindowState { get; set; } = 0;

        public double Top { get; set; }
        public double Left { get; set; }
        public double Width { get; set; } = 1000;
        public double Height { get; set; } = 700;

        public string Theme { get; set; } = "Light";
        public int SelectedTabIndex { get; set; } = 0;

        // A1 splitter: column 0 (Element Tree) and column 2 (Properties) widths
        // in device-independent pixels. Persisted as pixel values rather than star
        // ratios because column 1 (the GridSplitter) uses Width="Auto"; restoring
        // pixel widths keeps the user's spatial memory intact across DPI changes
        // reasonably well, and one-off fallback to the XAML star defaults happens
        // when both are <= 0 (no persisted state on first run).
        public double TreeColumnWidth { get; set; }
        public double PropertiesColumnWidth { get; set; }

        // A3 / A4 / A5 collapsible bottom-panel expanded states.
        public bool OcrPanelExpanded { get; set; }
        public bool RepositoryPanelExpanded { get; set; }
        public bool RunOutputPanelExpanded { get; set; }

        // A6+E4: persisted dock state — Exercise Step 1 introduced the DockingManager
        // host; Steps 3-6 promote tabs/dialogs into dockable panes whose arrangement
        // should round-trip across IDE restarts. ActivePaneId takes over forward from
        // SelectedTabIndex (which is kept for backward-compat — old layout.json files
        // still load and source applies SelectedTabIndex->pane-id fallback). DockLayoutJson
        // is the AvalonDock.Serializer.JsonLayoutSerializer serialized dock arrangement,
        // verbatim. Both default to null on a fresh layout.json — the apply-path treats
        // null DockLayoutJson as "use dock-manager default layout" and null ActivePaneId
        // as "fall back to SelectedTabIndex hint".
        /// <summary>The active dockable pane id selected on close. null on first run or
        /// when no pane host is registered. Forward successor to SelectedTabIndex (which
        /// is kept for backward-compat; the apply-path falls back to SelectedTabIndex when
        /// this is null).</summary>
        public string? ActivePaneId { get; set; }

        /// <summary>Serialized dock layout JSON from AvalonDock.Serializer.Json's
        /// LayoutSerializer (the v5 dock-manager.Layout round-tripped verbatim). null
        /// until Steps 3-6 register panes; the apply-path treats null as "use dock
        /// default layout".</summary>
        public string? DockLayoutJson { get; set; }
    }

    /// <summary>Load/save LayoutState to %AppData%\WpfTestIde\layout.json.
    /// File I/O is intentionally synchronous + on the UI thread - it runs once
    /// at startup (App.OnStartup) and once at window close, and the file is
    /// small (a few hundred bytes).</summary>
    public static class LayoutPersistence
    {
        private static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "WpfTestIde");

        private static string FilePath => Path.Combine(DirectoryPath, "layout.json");

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // Be tolerant of unknown fields / extra props added by future versions.
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        };

        public static LayoutState Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new LayoutState();
                var json = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(json)) return new LayoutState();
                return JsonSerializer.Deserialize<LayoutState>(json, Options) ?? new LayoutState();
            }
            catch
            {
                // Malformed/partial layout.json must never block startup; fall back
                // to defaults so the app still launches. The bad file stays in
                // place for the next Save() to overwrite cleanly.
                return new LayoutState();
            }
        }

        public static void Save(LayoutState state)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                var json = JsonSerializer.Serialize(state, Options);
                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // Persisting layout is best-effort; a permissions/quota error must
                // never crash the app on close. The user simply re-runs without a
                // saved state next time.
            }
        }
    }
}
