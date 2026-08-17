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
| A2  | Collapsible Element Tree pane (pin/unpin flyout)    | Medium       | ❌ reverted     | Was `2676c02` (push-model: `ToggleButton` `btnPinElementTree` → `ElementTree.ElementTreeCollapsed` 2-way bool + `DataTrigger` swaps column 0 `Width` * → 0 + hides `GridSplitter`); reverted in `0e43d23` / `0e43d23+1` per user feedback. Worth retrying as an **overlay flyout** (VS auto-hide style) next time — the push model added a toolbar pin button that the user didn't find useful.                              |
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
| D4  | Run Output: log-level filter + search (ListView)    | Medium       | ✅     | Commit `ed8f214`. `Models/LogEntry.cs` (`LogEntry { Raw, Time, Level, Message, LevelText }` + `LogLevel` enum incl. `Raw` for unstructured lines + static `LogLineParser` with compiled regex `YYYYMMDD HH:MM:SS.nnn \| LEVEL \| msg`; non-matching lines → `LogLevel.Raw`, Raw=full line as Message, Time=null). `MainViewModel`: `RunOutputLog ObservableCollection<LogEntry>` mirrored off `RunOutputLines.CollectionChanged` (Raw text collection kept for A5 tail + `RunOutputText`); `RunOutputFiltered` via `CollectionViewSource.GetDefaultView(RunOutputLog)` with `FilterLogEntry` predicate; INPC `ShowInfo`/`ShowWarn`/`ShowError` (all default true; TRACE/DEBUG/Raw group under Info) + `LogSearchText`; `RefreshLogFilter` re-pumps the view on each setter. `MainWindow.xaml` RESULTS-tab: replaced `txtRunOutput` TextBox with `ListView` (Time 160 / Level 70 / Message 600 columns, binds `RunOutputFiltered`); kept `txtRunOutput` AutomationId on the ListView (3 existing Robot locators still work). New filter-strip `Border`: `chkLogLevelInfo`/`chkLogLevelWarn`/`chkLogLevelError` (CheckBoxes) + `txtLogSearch` (TextBox). `RunAsync`/`Reset` now clear `RunOutputLog` alongside `RunOutputLines`. YAML: 2 `txtRunOutput` entries Text→List, TextBox→ListView; +4 new (chkLogLevelInfo/Warn/Error + txtLogSearch). |

## E. Global / window

| ID  | Item                                                | Effort       | Status | Notes                                                                                         |
|-----|-----------------------------------------------------|--------------|--------|-----------------------------------------------------------------------------------------------|
| E1  | Persist layout & theme to %AppData%\WpfTestIde\layout.json | Medium | ✅     | `Helpers/LayoutState.cs` POCO + `LayoutPersistence` Load/Save (System.Text.Json, indented, best-effort try/catch). Persisted: window State/Top/Left/Width/Height, Theme key (`ThemeManager.ApplyTheme`), `SelectedTabIndex`, A1 splitter column widths in px (named `colTree`/`colProperties`), A3/A4/A5 panel-expanded bools. `App.OnStartup` loads → `Application.Current.Properties["LayoutState"]`; `MainWindow.OnLoaded` applies (with on-screen/size guards + center fallback for off-monitor positions); `MainWindow.OnClosing` snapshots + saves. `MainTabControl.SelectedIndex` now 2-way-bound to `MainViewModel.SelectedTabIndex` (fixes a latent bug where the show-commands set an unbound VM value). No AutomationIds added → no YAML edits. |
| E2  | Keyboard shortcuts (InputGestureText + KeyBinding)  | Low          | ✅     | Commit `0196b82`. `Ctrl+S` Save · `Ctrl+R`/`F5` Run · `Ctrl+Shift+R` Toggle Record · `F12` Spy. Added Spy Tool menu item under Run. |
| E3  | Async command wrappers + notification toasts        | Low–Medium   | ✅      | New `WpfTestIde/AsyncRelayCommand.cs` (ICommand + INPC `IsRunning`; auto-disables bound Button while the wrapped `Task` is in flight via `CanExecute == !IsRunning`; re-enables on completion via `CommandManager.InvalidateRequerySuggested`; fail-soft writes `... failed: {ex.Message}` into `StatusText` mirroring the existing `CheckPipeConnection`/`RunAsync` pattern). `MainViewModel`: `CheckPipeConnectionCommand` + `RunCommand` swapped from `RelayCommand(async _ => …)` / `RelayCommand(_ => CheckPipeConnection())` to `AsyncRelayCommand`; new `CheckPipeConnectionAsync` runs the 3-attempt loop + `Thread.Sleep`-equivalent back-off + synchronous pipe `Send` on a ThreadPool thread and marshals `PipeStatusText` setters via `Dispatcher.Invoke` (no more ~3 s UI freeze on a Check Pipe click; per-attempt status messages + log file are unchanged). Additive toast surface: `Models/ToastMessage.cs` (`ToastMessage { Text, Kind }` + `enum ToastKind { Info, Success, Warning, Error }`); `Converters/ToastKindToBrushConverter` (added to existing `Converters.cs`, registered as `ToastKindToBrushConverter` key in `App.xaml`, resolves the `TextPrimaryBrush`/`SuccessBrush`/`WarningBrush`/`ErrorBrush` from `Application.Current.Resources` so theme swaps are followed); `MainViewModel.ActiveToasts` (`ObservableCollection<ToastMessage>`, capped at 1 visible) + INPC `IsToastVisible` (gates the `ToastBar` Border `Visibility` via `BoolToVisibilityConverter`); `EnqueueToast(text, kind)` marshals onto the dispatcher, replaces any current toast, schedules a one-shot 4-s `System.Threading.Timer` that hides-then-dequeues (two-phase removal keeps the `ActiveToasts[0]` indexer binding safe against the empty collection while the Border is still visible — see plan risk register). `MainWindow.xaml`: one new `StatusBarItem HorizontalAlignment=Center` between the left `StatusText`/`PipeStatusText` slot and the right driver-checkboxes slot, containing a `Border` named `ToastBar` (`AutomationId=ToastBar`) + inner `TextBlock` named `ToastText` (`AutomationId=ToastText`, `StatusBarTextBrush` foreground for contrast on the kind-coloured background). `Themes/Styles.xaml`: keyed `ToastBar` Style with a `Trigger` on `Visibility=Visible` → 250 ms `DoubleAnimation Opacity 0→1` (fade-in only; exit fade deferred per plan). Toasts emitted at four call sites: end of `RunAsync` (Success/Error, only when `summary.Total>0`), end of `ExportScript` (Info), end of `SaveScript` (Success), end of `CheckPipeConnectionAsync` (Success on success / Warning on failure). `repository/elements/wpf_test_ide.yaml`: +2 additive entries (`WpfTestIde.MainWindow.ToastBar`, `WpfTestIde.MainWindow.ToastText`) under a new "E3 Notifications" section — `StatusText`/`PipeStatusText`/all 73 existing entries untouched; no AutomationId collisions. `RelayCommand.cs` unchanged (the other 18 commands keep using it). Live freeze fixed; Run/Save/Export double-fire prevented; new capability is purely additive so the Robot drive UCs (UC-001/002/010/014) which assert only `StatusText` by AutomationId are unaffected. Build: 0 errors / 6 warnings (all pre-existing: CS8625 ×1, CS4014 ×1 on the untouched `PreviewElementCommand`, CS0169 ×1, each reported once for the temp-proj + once for the real csproj). |
| E4  | Dock Spy/Checkpoint/VisualBuilder as panes, not modals | Medium     | ⏸      | Polished version depends on A6.                                                               |

---

## Recommended next item: **A6 (docking system) → E4 (non-modal tool panes)**

**Why A6 next:**
- **E3 shipped** — `AsyncRelayCommand` eliminates the `Check Pipe` UI freeze + auto-disables `Run` mid-flight; a single-slot transient `ToastBar` (right of `StatusText`, inside the StatusBar) surfaces Run-finished/Save/Export/Pipe-OK confirmations without trampling the `StatusText` slot the Robot use-case tests assert against. `RunCommand`/`CheckPipeConnectionCommand` swapped; the other 18 commands keep using `RelayCommand` (no churn). New `ToastBar`/`ToastText` AutomationIds are additive.
- **A6 is the suggested final milestone** after E3: docking lets Spy/Checkpoint/VisualBuilder dock as panes (E4 depends on it). B3 (icon-only compact toolbar) needs icon assets and is a lower-leverage cosmetic pass — pick B3 up only if A6 gets deferred.

**E3 status:** ✅ shipped — see the E3 row above for the full asset list (new `AsyncRelayCommand.cs` / `Models/ToastMessage.cs` / `ToastKindToBrushConverter` in `Converters.cs` / `App.xaml` converter key / `MainViewModel` toast surface + `CheckPipeConnectionAsync` offload / 4 call-site toasts / `MainWindow.xaml` `ToastBar` slot / `Styles.xaml` fade Style / `wpf_test_ide.yaml` +2 additive entries). Build: 0 errors / 6 warnings (all pre-existing).

---

## Suggested overall order (remaining items)

1. **E3** — async wrappers + toasts *(shipped ✅)*
2. **A6** → **E4** — docking system then non-modal tool panes *(recommended now — final milestone)*
3. **B3** — icon-only compact toolbar *(needs icon assets)*

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
| `24d2423`| **E1** — layout/theme persistence to `%AppData%\WpfTestIde\layout.json`: `Helpers/LayoutState.cs` POCO + `LayoutPersistence` (System.Text.Json). `App.OnStartup` loads → `MainWindow.OnLoaded` applies (window geometry/theme/last-tab/A1 splitter px widths/A3-A5 panel bools w/ on-screen guards) → `OnClosing` snapshots + saves. `MainTabControl.SelectedIndex` 2-way-bound to `MainViewModel.SelectedTabIndex` (fixes latent unbound-VM bug). No AutomationIds added → no YAML edits. |
| `ed8f214`| **D4** — Run Output as structured `ListView`: `Models/LogEntry.cs` (`LogEntry` + `LogLevel` + `LogLineParser` regex for `YYYYMMDD HH:MM:SS.nnn \| LEVEL \| msg`, non-matching → `LogLevel.Raw`); `MainViewModel.RunOutputLog` mirrored off `RunOutputLines.CollectionChanged`, `RunOutputFiltered` via `CollectionViewSource` filter, INPC `ShowInfo`/`ShowWarn`/`ShowError`/`LogSearchText` (all ON by default; TRACE/DEBUG/Raw grouped under Info); `MainWindow.xaml` RESULTS-tab `txtRunOutput` TextBox → `ListView` (Time/Level/Message cols, AutomationId preserved) + filter-strip `chkLogLevelInfo`/`chkLogLevelWarn`/`chkLogLevelError` + `txtLogSearch`; YAML 2 `txtRunOutput` entries Text→List/TextBox→ListView + 4 new entries (chkLogLevelInfo/Warn/Error + txtLogSearch). |
