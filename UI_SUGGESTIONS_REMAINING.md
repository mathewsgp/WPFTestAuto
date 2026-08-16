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
| A2  | Collapsible Element Tree pane (pin/unpin flyout)    | Medium       | ✅     | Push model (per design decision). `ToggleButton` `btnPinElementTree` in ELEMENTS toolbar toggles `ElementTree.ElementTreeCollapsed` (2-way bool, default pinned/visible). `DataTrigger` on column 0 swaps `Width` * → 0; tree `Border` + `GridSplitter` `Visibility` → `Collapsed` when down. Properties reclaim the freed width inline (no floating overlay). A1 `GridSplitter` is the existing resizer when expanded; hidden when collapsed. YAML: 1 new entry (`btnPinElementTree`, `ToggleButton[...]` XPath — ToggleButton is a ButtonBase not Button). |
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

## Recommended next item: **D4 (Run Output: log-level filter + search as a ListView)**

**Why D4 next:**
- **A2 is shipped** — Element Tree is now pin/unpin collapsible (push model); the ELEMENTS / Properties layout is finished.
- **Design already verified** during A5 exploration: `RunOutputLines` is `ObservableCollection<string>` of Robot Framework's structured stdout (`YYYYMMDD HH:MM:SS.nnn | LEVEL | msg`), so parsing each line into a `LogEntry { Time, Level, Message }` is feasible. The tracker's preferred `ListView` (Time/Level/Message columns + Info/Warn/Error toggles + search) can therefore be implemented directly — no remaining open design questions.
- **High debugging value.** Today A5's tail (and the RESULTS-tab `txtRunOutput`) are a single text box; with levels + search the user isolates failures fast in long runs. Pairs naturally with A5's bottom tail for live filtering.
- **Standalone, additive.** Touches the RESULTS-tab Run Output area + a small VM addition (parsed-log model + filter state). Existing `txtRunOutput` AutomationId can move onto the new `ListView`; if so, update the one Robot-test reference (`wpf_test_ide_use_cases.robot` only *clicks* `tabResults`, doesn't read `txtRunOutput` content — so safe).

**Scope for D4:**
1. Add `LogEntry { Time, Level, Message }` model + a `LogLineParser` regex for Robot's `'YYYYMMDD HH:MM:SS.nnn | LEVEL | msg'` format; lines that don't match (separators `'====...'`, headers) become `Level = "Info"` (or a distinct `"Raw"` level) with the full line as Message.
2. `MainViewModel`: add `RunOutputLog ObservableCollection<LogEntry>` seeded from each new `RunOutputLines` line; add filter state (`ShowInfo`/`ShowWarn`/`ShowError` checkboxes, default all true) + `LogSearchText`; expose `RunOutputFiltered` (via `CollectionViewSource` filter or a re-query). Keep `RunOutputLines`/`RunOutputText` for back-compat (A5 tail + RESULTS tab still bind to the raw text).
3. `MainWindow.xaml` RESULTS-tab `txtRunOutput`: replace the `TextBox` with a `ListView`/`DataGrid` (Time / Level / Message columns), preserving the `txtRunOutput` AutomationId. Add the filter strip above it: 3 level checkboxes (`chkLogLevelInfo`/`chkLogLevelWarn`/`chkLogLevelError`) + a search `TextBox` (`txtLogSearch`).
4. Optionally also upgrade the A5 tail (`txtRunOutputTail`) — keep it as a raw `TextBox` for now (live tail favors plain text over filtered rows); revisit later.
5. YAML: keep `txtRunOutput` entry but update `controlType` Text → List, `relativeXPath` `TextBox[...]` → `ListView[...]` (or `DataGrid[...]`); add `chkLogLevelInfo`/`chkLogLevelWarn`/`chkLogLevelError` + `txtLogSearch` entries.
6. Build → commit → push → refresh this md → `graphify update .`.

**Implementation plan:**
1. Add `LogEntry` model + `LogLineParser`.
2. Wire `RunOutputLines.CollectionChanged` to also seed `RunOutputLog`; add filter checkboxes + search text props + a `RunOutputFiltered` view.
3. Replace RESULTS-tab `txtRunOutput` `TextBox` with a `ListView`; add the filter strip.
4. Update YAML (txtRunOutput controlType + the 4 new AutomationIds); no Robot-test edit needed (no test reads `txtRunOutput`).
5. Build, verify 0 errors, push, update md, `graphify update .`.

---

## Suggested overall order (remaining items)

1. **D4** — Run Output log-level filter + search *(recommended now; design verified, no open questions)*
2. **E1** — layout/theme persistence *(do after the layout it persists stabilizes — A2/A5 now shipped, so it's unblocked; could go before D4 if a stable layout is preferred first)*
3. **E3** — async wrappers + toasts
4. **A6** → **E4** — docking system then non-modal tool panes *(final milestone)*
5. **B3** — icon-only compact toolbar *(needs icon assets)*

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
| `<pending>`| **A2** — collapsible Element Tree pane (push model): `ToggleButton` `btnPinElementTree` → `ElementTree.ElementTreeCollapsed` 2-way bool; `DataTrigger` swaps column 0 `Width` * → 0 and hides tree `Border` + `GridSplitter`. Properties reclaim width inline; no overlay. YAML +1 entry. |
