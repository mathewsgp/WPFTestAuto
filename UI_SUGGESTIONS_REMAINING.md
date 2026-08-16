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
| A5  | Run Output as bottom-docked resizable panel         | Medium       | ✅     | Duplicate bottom dock (per design decision). New collapsible `Expander` `RunOutputTailExpander` between OCR + MainTabControl hosts a read-only `txtRunOutputTail` bound to `RunOutputText` (same source as RESULTS-tab `txtRunOutput` — stays in sync). `RunOutputPanelExpanded` 2-way bool; auto-expanded in `RunAsync` when a run begins, re-collapsed in `Reset()`. `(empty)` badge via existing `EmptyStringToVisibleConverter`. RESULTS-tab `txtRunOutput` entry unchanged. YAML: 2 new entries. |
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
| D4  | Run Output: log-level filter + search (ListView)    | Medium       | ⬜ (design verified, not yet started) | Columns Time/Level/Message + Info/Warn/Error toggles. **Design question resolved during A5 exploration:** `RunOutputLines` is `ObservableCollection<string>` of Robot's structured stdout (`YYYYMMDD HH:MM:SS.nnn \| LEVEL \| msg`), so parsing into `LogEntry { Time, Level, Message }` for a `ListView` is viable (the tracker's preferred shape). Skipped before A5 per user direction; can resume here next time. |

## E. Global / window

| ID  | Item                                                | Effort       | Status | Notes                                                                                         |
|-----|-----------------------------------------------------|--------------|--------|-----------------------------------------------------------------------------------------------|
| E1  | Persist layout & theme to %AppData%\WpfTestIde\layout.json | Medium | ⬜      | Use YamlDotNet (already a dep) or System.Text.Json. Persist window size/pos, theme, last tab, splitter ratios, panel states, toolbar mode. |
| E2  | Keyboard shortcuts (InputGestureText + KeyBinding)  | Low          | ✅     | Commit `0196b82`. `Ctrl+S` Save · `Ctrl+R`/`F5` Run · `Ctrl+Shift+R` Toggle Record · `F12` Spy. Added Spy Tool menu item under Run. |
| E3  | Async command wrappers + notification toasts        | Low–Medium   | ⬜      | Fixes Attach/Run/Export UI freezes.                                                           |
| E4  | Dock Spy/Checkpoint/VisualBuilder as panes, not modals | Medium     | ⏸      | Polished version depends on A6.                                                               |

---

## Recommended next item: **A2 (Collapsible Element Tree pane — pin / unpin flyout)**

**Why A2 next:**
- **A5 is shipped** — Run Output has a bottom-docked collapsible tail that auto-opens on run start; script authoring + run-watching both work now.
- **Direct layout value.** Today the Element Tree occupies its full column permanently; a collapse/pin/unpin flyout would free that space for the Properties editor when the user isn't tree-browsing — mirrors the dock-tool UX in VS.
- **Open design question to settle before coding** (see *Scope* below): *overlay flyout* (the tree floats over content while un-pinned, like VS's auto-hide tool windows) vs. *push* (collapsing the tree simply shrinks the row back to a sliver / reclaims its column for Properties). The A1 `GridSplitter` already gives the user a manual width control; A2 must define its relationship to that splitter (e.g. collapse hides the splitter).
- **Medium effort, pure additive.** Adds a pin/unpin button + an `ElementTreePinned` bool + behaviors. No AutomationId on existing tree/internal controls needs to move; the new pin button gets a fresh AutomationId.

**Scope for A2:**
1. Decide overlay-vs-push (the one open question). Read `WpfTestIde/Views/ElementTreeView.xaml` + its host region in `MainWindow.xaml` and the A1 `GridSplitter` geometry to pick the model.
2. Add an `ElementTreePinned` (or `ElementTreeCollapsed`) bool on `ElementTreeViewModel` with 2-way expand-state semantics.
3. Add a pin/unpin `ToggleButton` to theELEMENTS-tab toolbar (fresh AutomationId `btnPinElementTree`); in the chosen overlay/push model, bind the tree's `Visibility`/`Width`/`ZIndex` to the pin state.
4. Handle the A1 `GridSplitter`: when the tree collapses, hide or disable the splitter so it doesn't drag an invisible column.
5. Add `btnPinElementTree` to `repository/elements/wpf_test_ide.yaml`; update `wpf_test_ide_use_cases.robot` only if a test interaction depends on tree visibility.
6. Build → commit → push → refresh this md → `graphify update .`.

**Implementation plan:**
1. Read `ElementTreeView.xaml` + its `MainWindow.xaml` host (the A1 3-col `Grid` with the splitter).
2. Pick overlay vs. push and capture the decision in the A2 row Notes on commit.
3. Implement the chosen shape + pin button + `ElementTreePinned` 2-way bool + splitter coordination.
4. YAML entry for the new AutomationId; Robot test update if needed.
5. Build, verify 0 errors, push, update md, `graphify update .`.

---

## Suggested overall order (remaining items)

1. **A2** — collapsible Element Tree pane *(recommended now; settle overlay-vs-push first)*
2. **D4** — Run Output log-level filter + search *(design verified; can pick up anytime)*
3. **E1** — layout/theme persistence *(do after the layout it persists stabilizes — i.e. after A2)*
4. **E3** — async wrappers + toasts
5. **A6** → **E4** — docking system then non-modal tool panes *(final milestone)*
6. **B3** — icon-only compact toolbar *(needs icon assets)*

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
| `7f99327`| **A5** — bottom-docked collapsible Run Output tail panel (`RunOutputTailExpander` + `txtRunOutputTail`); duplicate of RESULTS-tab `txtRunOutput`, auto-expands on run start, re-collapses in `Reset()`. YAML +2 entries. |
