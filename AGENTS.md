## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- Dirty graphify-out/ files are expected after hooks or incremental updates; dirty graph files are not a reason to skip graphify. Only skip graphify if the task is about stale or incorrect graph output, or the user explicitly says not to use it.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).

## WpfTestIde layout save/restore (add-technical-design-spec branch)

- Layout persists to `%AppData%\WpfTestIde\layout.json` via `WpfTestIde/Helpers/LayoutState.cs`
  (`LayoutPersistence.Load/Save`). Loaded in `App.OnStartup` into
  `Application.Current.Properties["LayoutState"]`; applied in `MainWindow.MainWindow_Loaded`,
  snapshotted in `MainWindow.MainWindow_Closing`.
- Dock layout uses AvalonDock v5 (`Dirkster.AvalonDock` 5.0.0 + `Dirkster.AvalonDock.Serializer.Json`).
  Three `LayoutAnchorable` panes (ELEMENTS/SCRIPTS/RESULTS) sit in a horizontal `LayoutPanel`
  in `MainWindow.xaml`. Dock arrangement round-trips as `DockLayoutJson` via
  `JsonLayoutSerializer`.
- AvalonDock v5 DTO mapper serializes `DockWidth`/`DockHeight` as strings ("1*", "200") and
  restores them via `GridLengthConverter.ConvertFromInvariantString`. Star widths serialize
  as star; absolute pixel widths serialize as pixels. v5 DOES round-trip DockWidth. Auto-hide
  sides (Left/Right/Top/Bottom) are NOT deserialized by v5 - handled by the manual
  `RestoreAutoHidePanesFromJson` workaround; `DeduplicateLayout()` removes ghost duplicates.

  IMPORTANT (verified from Dirkster99/AvalonDock v5 source, 2026-08): the round-trip is NOT
  the failure point. `LayoutPositionableGroup.DockWidth` getter returns a COMPUTED GridLength:
  if `_dockWidth.IsAbsolute && _resizableAbsoluteDockWidth < _dockWidth.Value && HasValue`
  it returns `new GridLength(_resizableAbsoluteDockWidth.Value)`, else the raw `_dockWidth`.
  `CopyPositionableToDto` serializes `FixedDockWidth` (clamped to `_dockMinWidth=25`) for
  absolute widths - so the *initial* absolute width, not the user-resized one, is what's saved
  unless `_resizableAbsoluteDockWidth` already shrunk `_dockWidth`. The real width-loss mechanism
  is `LayoutPanelControl.OnFixChildrenDockLengths` (runs from `UpdateRowColDefinitions` on
  SizeChanged/children-changed): for a Horizontal LayoutPanel that contains NO
  LayoutDocumentPane/Group, the else-branch FORCES every non-star child DockWidth back to
  `new GridLength(1.0, GridUnitType.Star)`. This WPFTestIde root LayoutPanel has only
  LayoutAnchorablePanes (no document pane), so any absolute DockWidth set at restore time is
  overwritten to star by the next layout pass. That is why PaneWidths must be re-applied AFTER
  `dockManager.UpdateLayout()` in the deferred block (and may need a 2nd-tick re-apply).
  `JsonLayoutSerializer.Deserialize` does `Manager.Layout = layout;` (full replace) inside
  `StartDeserialization/EndDeserialization`, then `FixupLayout`.
- Internal ElementsPane splitter (Element Tree <-> Properties) is persisted separately as
  `TreeColumnWidth`/`PropertiesColumnWidth` and applied via the `ApplySplitterState(LayoutState)`
  helper, called from the deferred `DispatcherPriority.Loaded` block.
- DOCKED PANE WIDTHS (ELEMENTS/SCRIPTS/RESULTS) are persisted as an explicit
  `LayoutState.PaneWidths` map (absolute px keyed by pane title) in addition to
  `DockLayoutJson`, because the JSON round-trip alone was unreliable. Flow:
  `SnapshotPaneWidths()` at close records absolute `LayoutAnchorablePane.DockWidth`
  (star widths skipped); `ApplyPaneWidths()` is deferred to `DispatcherPriority.Loaded`
  AFTER `UpdateLayout` so it wins over the (possibly corrupt/default) dock tree.
  Window geometry is applied BEFORE the dock deserialize so AvalonDock rebuilds into a
  correctly-sized host. Serialize/snapshot/apply errors are logged to `Debug.WriteLine`
  (no longer swallowed). NOTE: cannot build here (no Windows SDK); build/test on Windows
  with `dotnet build`.
- Known weak spot (verified, NOT yet fixed): `ElementsPaneRoot` is referenced via
  `fe.FindName` in `MainWindow.xaml.cs` but is NOT defined in `ElementsPane.xaml` (only
  `colTree`/`colProperties` are named), so splitter restore relies on
  `FindVisualChild<ElementsPane>` working only if the dock content is already materialized
  at Loaded time - otherwise it silently no-ops (now logged).
