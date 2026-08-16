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
| D2  | Properties: tabbed Inspector (Properties/XPath/Preview) | Medium   | ⬜      | Gives the XPath multiline box its own scrollable area.                                       |
| D3  | Steps ListBox → draggable re-order                   | Medium       | ⬜      | Lift drag pattern from `TestFlowDialog`.                                                      |
| D4  | Run Output: log-level filter + search (ListView)    | Medium       | ⬜      | Columns Time/Level/Message + Info/Warn/Error toggles.                                         |

## E. Global / window

| ID  | Item                                                | Effort       | Status | Notes                                                                                         |
|-----|-----------------------------------------------------|--------------|--------|-----------------------------------------------------------------------------------------------|
| E1  | Persist layout & theme to %AppData%\WpfTestIde\layout.json | Medium | ⬜      | Use YamlDotNet (already a dep) or System.Text.Json. Persist window size/pos, theme, last tab, splitter ratios, panel states, toolbar mode. |
| E2  | Keyboard shortcuts (InputGestureText + KeyBinding)  | Low          | ✅     | Commit `0196b82`. `Ctrl+S` Save · `Ctrl+R`/`F5` Run · `Ctrl+Shift+R` Toggle Record · `F12` Spy. Added Spy Tool menu item under Run. |
| E3  | Async command wrappers + notification toasts        | Low–Medium   | ⬜      | Fixes Attach/Run/Export UI freezes.                                                           |
| E4  | Dock Spy/Checkpoint/VisualBuilder as panes, not modals | Medium     | ⏸      | Polished version depends on A6.                                                               |

---

## Recommended next item: **D2 (Properties panel: tabbed Inspector Properties / XPath / Preview)**

**Why D2 next:**
- **Low–medium effort, pure additive.** Only restructures the `ElementEditorView` (`ElementEditorView.xaml`) into three sub-tabs; no other pane/tab/VM needs touching.
- **No open design questions**, unlike A2 (collapsible Element Tree: overlay vs. push) and A5 (Run Output: duplicate vs. move in-tab) which both still need a decision before they can start.
- **Direct clarity win.** Today the Properties editor is a single long vertical form with an awkward squashed XPath multiline box; D2 gives the XPath box its own scrollable area and separates read-only Preview from editable Properties.
- **Independent of everything.** Can be done in any order alongside D3/D4, but D2 is the smallest of the three.

**Scope for D2:**
1. Read `WpfTestIde/Views/ElementEditorView.xaml` and (`ElementEditorView.xaml.cs` if it has one) — map its current structure: Name/AutomationId/ControlType fields + XPath multi-line + Preview text.
2. Wrap the content in a 3-tab `TabControl`:
   - **Properties tab:** current Name/AutomationId/ControlType/etc. editable fields.
   - **XPath tab:** the multi-line `XPath` editor on its own, in a scrollable area with monospaced styling.
   - **Preview tab:** the read-only Preview text area.
3. Preserve existing AutomationIds on the inner fields (do not relocate individual AutomationIds — only restructure the container).
4. Default selected tab = Properties (matching current behavior).

**Implementation plan:**
1. Read `WpfTestIde/Views/ElementEditorView.xaml` (+ its code-behind) to confirm current content + AutomationIds.
2. Wrap the three content regions in a `TabControl` with three `TabItem`s (the existing `VsTabItem` style applies via the keyless default).
3. Build → commit → push → refresh this md → `graphify update .`.

---

## Suggested overall order (remaining items)

1. **D2** — Properties panel tabbed Inspector *(recommended now; low-medium effort, no open questions)*
2. **D3 / D4** — Steps draggable re-order / Run Output log filter *(independent; do in any order)*
3. **A2** — collapsible Element Tree pane *(needs overlay-vs-push decision first)*
4. **A5** — bottom-docked Run Output panel *(needs duplicate-vs-move decision first)*
5. **E1** — layout/theme persistence *(do after the layout it persists stabilizes — i.e. after A2/A5)*
6. **E3** — async wrappers + toasts
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
