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
| B2  | Labeled toolbar "bands" (Session/Record/Run/…)      | Low          | ⬜      | Separators exist; add thin label TextBlocks above each band.                                  |
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
| E2  | Keyboard shortcuts (InputGestureText + KeyBinding)  | Low          | ⬜      | `VsMenuItem` template already reserves a shortcut column.                                    |
| E3  | Async command wrappers + notification toasts        | Low–Medium   | ⬜      | Fixes Attach/Run/Export UI freezes.                                                           |
| E4  | Dock Spy/Checkpoint/VisualBuilder as panes, not modals | Medium     | ⏸      | Polished version depends on A6.                                                               |

---

## Recommended next item: **E2 (Keyboard shortcuts)**

**Why E2 next:**
- **Low effort, high discoverability payoff.** The `VsMenuItem` template already reserves a shortcut-gesture column that is currently empty — wiring `InputGestureText` is pure XAML with no template work.
- **No layout/behavior risk.** Unlike A2 / A5 (which reshape panes and touch `MainViewModel`), E2 only adds gesture text and `KeyBinding`s alongside existing commands. It cannot regress any of the A/B/C work already shipped.
- **Independent of unresolved design questions.** A2 (overlay vs. push semantics) and A5 (duplicate vs. move in-tab output) both have open questions that need a decision before implementation; E2 has none.
- **Unblocks nothing but blocks nothing.** It's a clean, self-contained win that keeps momentum while the A2/A5 design questions are settled.

**Scope for E2:**

| Action          | Shortcut         | Target                         |
|-----------------|------------------|--------------------------------|
| Save            | `Ctrl+S`         | Save menu item / command        |
| Run             | `Ctrl+R` and `F5`| Run Script menu item / command  |
| Toggle Record   | `Ctrl+Shift+R`   | Toggle Record menu item / command |
| Spy (Pick)      | `F12`            | Spy / Pick menu item / command  |

**Implementation plan:**
1. Audit `MainWindow.xaml` menu items and the `ICommand`s they bind to (confirm command names in `MainViewModel`).
2. Add `InputGestureText="Ctrl+S"` etc. to each `MenuItem` — visible immediately in the reserved shortcut column.
3. Add `<KeyBinding>` entries to `MainWindow.InputBindings` (or the relevant container's `InputBindings`) bound to the same commands. For two-gesture actions (Run: `Ctrl+R` + `F5`) add two `KeyBinding`s pointing at the same command.
4. Preserve all AutomationIds and existing command wiring — only add gesture text + key bindings.
5. Build → commit → push → `graphify update .`.

---

## Suggested overall order (remaining items)

1. **E2** — keyboard shortcuts *(recommended now; low risk, high value)*
2. **B2** — labeled toolbar bands *(low effort; pairs naturally with B1)*
3. **D1** — Element Tree filter chip bar *(low effort; user-facing clarity)*
4. **A7** — per-tab context toolbars *(removes the Scripts/Raw ⇄ Driver-Settings duplication)*
5. **A2** — collapsible Element Tree pane *(needs overlay-vs-push decision first)*
6. **A5** — bottom-docked Run Output panel *(needs duplicate-vs-move decision first)*
7. **D2 / D3 / D4** — pane-content upgrades *(independent; do in any order)*
8. **E1** — layout/theme persistence *(do after the layout it persists stabilizes — i.e. after A2/A5)*
9. **E3** — async wrappers + toasts
10. **A6** → **E4** — docking system then non-modal tool panes *(final milestone)*
11. **B3** — icon-only compact toolbar *(needs icon assets)*

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
