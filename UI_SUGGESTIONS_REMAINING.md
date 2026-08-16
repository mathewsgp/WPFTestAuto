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
| A7  | Per-tab context toolbars (de-duplicate toolbar)     | Medium       | ⬜      | Removes the Scripts/Raw ⇄ Driver-Settings-bar duplication noted in the analysis.              |

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
| D1  | Element Tree: filter chip bar + parent context      | Low          | ⬜      | Builds on existing search box; add "Clear ✕" chip + include-parents mode.                     |
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

## Recommended next item: **D1 (Element Tree filter chip bar + parent context)**

**Why D1 next:**
- **Low effort, user-facing clarity.** `ElementTreeView` already has a working live-filter search box — D1 upgrades it to a filter-chip experience instead of building from scratch.
- **Independent of everything.** Doesn't touch `MainViewModel`, the toolbar, or pane layout — purely additive on top of the existing tree view.
- **No open design questions**, unlike A2 (overlay vs. push) and A5 (duplicate vs. move in-tab output), which both still need a decision.
- **Pairs with the just-shipped B2.** With the toolbar now scannable and labeled, the next natural clarity win is the Element Tree's own filter affordance.

**Scope for D1:**
1. Find the existing search box in `ElementTreeView.xaml` and the VM property powering the filter (likely `SearchText` / `FilterText` on the element-tree VM).
2. Add a "Clear ✕" chip button next to the element count (`N elements`) that appears only when a filter is active and clears `SearchText`.
3. Add a "Filter includes parents" toggle so deep matches show their ancestor chain (VS Code "Filter includes parents" pattern) — purely a tree-virtualization tweak on the existing filter logic.
4. Optionally show match count "M / N elements" while a filter is active.

**Implementation plan:**
1. Read `WpfTestIde/Views/ElementTreeView.xaml` and its VM to locate the search box + filter plumbing.
2. Add the chip (toggle `Visibility` on `SearchText` non-empty via `NullToCollapsedConverter`/`BoolToVisibilityConverter`) and the include-parents toggle.
3. Build → commit → push → refresh this md → `graphify update .`.

---

## Suggested overall order (remaining items)

1. **D1** — Element Tree filter chip bar *(recommended now; low effort, user-facing clarity)*
2. **A7** — per-tab context toolbars *(removes the Scripts/Raw ⇄ Driver-Settings duplication)*
3. **A2** — collapsible Element Tree pane *(needs overlay-vs-push decision first)*
4. **A5** — bottom-docked Run Output panel *(needs duplicate-vs-move decision first)*
5. **D2 / D3 / D4** — pane-content upgrades *(independent; do in any order)*
6. **E1** — layout/theme persistence *(do after the layout it persists stabilizes — i.e. after A2/A5)*
7. **E3** — async wrappers + toasts
8. **A6** → **E4** — docking system then non-modal tool panes *(final milestone)*
9. **B3** — icon-only compact toolbar *(needs icon assets)*

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
