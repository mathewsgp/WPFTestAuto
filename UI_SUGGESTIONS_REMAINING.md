# UI Suggestions — Status Tracker

Full menu of UI improvements for `WpfTestIde`, with per-item status. Items completed before this
file was created (A1, A3, C1+C2, A4, dark-theme fixes, CheckpointWizard NRE guard, VsExpander
template fix) are recorded at the bottom for reference and then omitted from the remaining-work
sections.

Legend: ✅ done · 🚧 in progress · ⬜ not started · ⏸ blocked / depends on another item

---

## A. Layout / docking

| ID  | Item                                                | Effort       | Status | Notes                                                                                         |
|-----|-----------------------------------------------------|--------------|--------|-----------------------------------------------------------------------------------------------|
| A1  | Resizable Element Tree ↔ Properties split (GridSplitter) | Low          | ✅     | Commit `2e51580`. 3-col Grid + AccentBrush thumb.                                             |
| A2  | Collapsible Element Tree pane (pin/unpin flyout)    | Medium       | ⬜      | Coordinate with A1 splitter (hide it when collapsed).                                         |
| A3  | OCR Result → collapsible bottom Expander             | Low          | ✅     | Commit `4c760ea`. `OcrPanelExpanded` 2-way, auto-expand on new text, `(empty)` badge.          |
| A4  | Raw JSON → toggleable bottom Expander               | Medium       | ✅     | Commit `51a9ae9`. `RepositoryPanelExpanded` 2-way; `tabRawJson` AutomationId moved to Expander; YAML repo entry updated. |
| A5  | Run Output as bottom-docked resizable panel         | Medium       | ⬜      | Touches `MainViewModel` tab model. Mirror A3 auto-expand pattern with `HasRunOutput` bool.    |
| A6  | Proper docking system (AvalonDock / Dock.WPF)       | High         | ⬜      | Later milestone. Unlocks clean E4.                                                            |
| A7  | Per-tab context toolbars (de-duplicate toolbar)     | Medium       | ✅     | Commit `577da42`. Global toolbar keeps Session+Record+Tools only. SCRIPTS & RESULTS tabs each get their own toolbar (Run/Save/Export on SCRIPTS; Run Again/Export/Reset on RESULTS). Removed the 6 in-Raw `chkScript*` checkbox duplicates ( YAML entries dropped). UC-010 + UC-014 Robot tests updated to switch to SCRIPTS first. New AutomationIds `btnResultsRerun`/`btnResultsExportScript`/`btnResultsReset` (YAML entries added). |

## B. Top toolbar

| ID  | Item                                                | Effort       | Status | Notes                                                                                         |
|-----|-----------------------------------------------------|--------------|--------|-----------------------------------------------------------------------------------------------|
| B1  | Toolbar overflow → horizontal scroll                 | Low–Medium   | ✅     | Commit `6da97c3`. Toolbar `StackPanel` wrapped in horizontal `ScrollViewer`.                 |
| B2  | Labeled toolbar "bands" (Session/Record/Run/…)      | Low          | ✅     | Commit `768572e`. 5 vertical band sub-stacks (label + button row) inside B1's `ScrollViewer`. `ToolbarBandLabel`/`ToolbarBand` styles added. All AutomationIds/order/inter-Tools `Separator`s preserved. |
| B3  | Icon-only compact toolbar mode (View menu toggle)   | Low          | ⬜      | Needs icon assets. Gates behind a "Compact Toolbar" toggle.                                  |

## C. Status bar

| ID  | Item                                                | Effort       | Status | Notes                                                                                         |
|-----|-----------------------------------------------------|--------------|--------|-----------------------------------------------------------------------------------------------|
| C1  | Real StatusBar with status slots (VS-style)         | Low          | ✅     | Commit `8563440`. Left: StatusText + PipeStatusText; right: Record/Run checkboxes.            |
| C2  | Collapse Driver Settings strip into status bar      | Low          | ✅     | Commit `8563440` (done together with C1). All AutomationIds preserved.                        |

## D. Pane content

| ID  | Item                                                | Effort       | Status | Notes                                                                                         |
|-----|-----------------------------------------------------|--------------|--------|-----------------------------------------------------------------------------------------------|
| D1  | Element Tree: filter chip bar + parent context      | Low          | ✅     | Commit `061b932`. Clear chip (btnClearSearch), `FilterIncludesParents` CheckBox (chkFilterIncludesParents, default ON, recursive ScoreNode+PropagateAncestors), `ElementCountText` "N / M elements" (txtElementCount). ApplyFilter now fully recursive. Existing `SearchBox` preserved. |
| D2  | Properties: tabbed Inspector (Properties/XPath/Preview) | Medium   | ❌ reverted     | Was `cfa4693`; reverted in `0cbfcf4` per user feedback. The three-tab restructure wasn't useful and the Preview strip surfaced only internal logs with no user-facing value. Properties panel is back to single-form with XPath inside it; Preview strip removed. Not worth retrying as designed — dropping D2 from the queue. |
| D3  | Steps ListBox → draggable re-order                   | Medium       | ✅     | Commit `a342bda`. Drag-to-reorder + ↑/↓ buttons. `StepTemplate` gained a handle column (☰ + `btnStepUp`/`btnStepDown`); `StepsListBox` wired `AllowDrop`+PreviewMouseLeftButtonDown/PreviewMouseMove/DragOver/Drop handlers in `MainWindow.xaml.cs` (drag suppressed over Buttons). `MainViewModel.MoveStepTo` (clamps + `ObservableCollection.Move` + `RegenerateScript`). YAML: 2 new entries (`btnStepUp`/`btnStepDown`). |
| D4  | Run Output: log-level filter + search (ListView)    | Medium       | ⬜      | Columns Time/Level/Message + Info/Warn/Error toggles.                                         |

## E. Global / window

| ID  | Item                                                | Effort       | Status | Notes                                                                                         |
|-----|-----------------------------------------------------|--------------|--------|-----------------------------------------------------------------------------------------------|
| E1  | Persist layout & theme to %AppData%\WpfTestIde\layout.json | Medium | ⬜      | Use YamlDotNet (already a dep) or System.Text.Json. Persist window size/pos, theme, last tab, splitter ratios, panel states, toolbar mode. |
| E2  | Keyboard shortcuts (InputGestureText + KeyBinding)  | Low          | ✅     | Commit `0196b82`. `Ctrl+S` Save · `Ctrl+R`/`F5` Run · `Ctrl+Shift+R` Toggle Record · `F12` Spy. Added Spy Tool menu item under Run. |
| E3  | Async command wrappers + notification toasts        | Low–Medium   | ⬜      | Fixes Attach/Run/Export UI freezes.                                                           |
| E4  | Dock Spy/Checkpoint/VisualBuilder as panes, not modals | Medium     | ⏸      | Polished version depends on A6.                                                               |

---

## Recommended next item: **D4 (Run Output: log-level filter + search)**

**Why D4 next:**
- **Medium effort, standalone.** Only touches the RESULTS-tab Run Output area + a small VM addition for the filter state. No inter-pane contracts to coordinate.
- **D3 is shipped** — `StepsListBox` now supports drag-to-reorder + ↑/↓ buttons; the Visual script editor is finished.
- **Direct debugging value.** Today Run Output is a single text box; with log levels and search you can isolate failures fast in long runs.
- **Open design question to settle before coding** (see *Scope* below): whether to re-color the existing `txtRunOutput` `TextBox` in place (cheap, no schema change), or upgrade to a `ListView`/`DataGrid` with Time/Level/Message columns (richer but needs a parsed-log model). The original suggestion named a `ListView`, so lean towards the columns model unless `txtRunOutput`'s current content is unstructured free text that can't be parsed into (time, level, message) rows.

**Scope for D4:**
1. Read `WpfTestIde/MainWindow.xaml` RESULTS tab + `MainViewModel` `RunOutput`/`txtRunOutput` to understand the current shape (is it pre-formatted log text, or already line-structured?).
2. Decide: columns `ListView` (parse the existing log lines into `LogEntry { Time, Level, Message }`) vs. in-place filter on the existing `TextBox`. Prefer the `ListView` to satisfy the suggestion as written, fall back to in-place if the log source isn't line-structured.
3. Add filter checkboxes (Info / Warn / Error) + a search box above the log; wire a `CollectionViewSource` filter or a re-query into `FilteredRunOutput`.
4. Preserve `txtRunOutput` AutomationId. If switching to `ListView`, add a new AutomationId on it (e.g. `lstRunOutput`) + entry in `wpf_test_ide.yaml`; keep the old one as a hidden-in-place control only if still referenced by Robot tests (check `wpf_test_ide_use_cases.robot`).
5. Build → commit → push → refresh this md → `graphify update .`.

**Implementation plan:**
1. Read the RESULTS tab region in `MainWindow.xaml` + the `RunOutput` setter + any Robot test referencing `txtRunOutput`.
2. Decide ListView-vs-inplace and capture the decision in the D4 row's Notes when committing.
3. Implement the chosen shape + Info/Warn/Error toggles + search box + filter logic in VM.
4. Update YAML if a new AutomationId is introduced; update `wpf_test_ide_use_cases.robot` if a locator changes.
5. Build, verify 0 errors, push, update md, `graphify update .`.

---

## Suggested overall order (remaining items)

1. **D4** — Run Output log-level filter + search *(recommended now)*
2. **A2** — collapsible Element Tree pane *(needs overlay-vs-push decision first)*
3. **A5** — bottom-docked Run Output panel *(needs duplicate-vs-move decision first)*
4. **E1** — layout/theme persistence *(do after the layout it persists stabilizes — i.e. after A2/A5)*
5. **E3** — async wrappers + toasts
7. **A6** → **E4** — docking system then non-modal tool panes *(final milestone)*
8. **B3** — icon-only compact toolbar *(needs icon assets)*

---

## Reference — already completed (before this file)

| Commit   | Item                                                                 |
|----------|----------------------------------------------------------------------|
| `7d65f54`| Dark-theme fixes: keyless default `Vs*` styles, themed dialogs, ElementTreeView foreground, colored-button foregrounds. |
| `2e51580`| **A1** — Element Tree ↔ Properties resizable split via `GridSplitter`. |
| `4c760ea`| **A3** — OCR Result as collapsible bottom `Expander`.                 |
| `04d8d8b`| CheckpointWizard `TypeCombo_SelectionChanged` NRE guard.             |
| `8563440`| **C1+C2** — consolidated real `StatusBar` + Driver Settings strip.  |
| `51a9ae9`| **A4** — Raw JSON promoted to collapsible bottom `Expander`; YAML repo entry updated. |
| `3b89a73`| `VsExpander` template rewrite (content `ContentPresenter` + Visibility trigger). |
| `6da97c3`| **B1** — toolbar horizontally scrollable (no more silent clip).     |
| `0196b82`| **E2** — keyboard shortcuts (`Ctrl+S`/`Ctrl+R`+`F5`/`Ctrl+Shift+R`/`F12`) + Spy Tool menu item. |
| `d827fa6`| fix(keybinding): `Control+Shift` (was `Control,Shift`) for ToggleRecord binding — resolves runtime `XamlParseException`. |
| `768572e`| **B2** — toolbar grouped into 5 labeled bands (Session/Record/Run/Export/Tools) inside B1's `ScrollViewer`. |
| `061b932`| **D1** — Element Tree filter chip bar: Clear chip + `Filter includes parents` toggle + "M / N elements" count; `ApplyFilter` now recursive with ancestor propagation. |
| `577da42`| **A7** — per-tab context toolbars: Run+Export moved to SCRIPTS tab; RESULTS gets Run Again/Export/Reset toolbar. In-Raw duplicate `chkScript*` removed; UC-010 + UC-014 Robot tests updated. YAML updated (-6 entries, +3 new). |
| `a342bda`| **D3** — Steps ListBox draggable re-order: `StepTemplate` handle column (☰ + `btnStepUp`/`btnStepDown`) + `StepsListBox` AllowDrop/drag handlers + `MainViewModel.MoveStepTo` (clamps, `ObservableCollection.Move`, `RegenerateScript`). YAML +2 entries. |
