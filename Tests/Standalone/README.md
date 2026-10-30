# Standalone IDE Tests

These tests verify **WPFTestIDE features independently of TestAutoLayer**.

## Why separate?

The Robot Framework suites in `Tests/IDE/` load `TestAutoLayer/api/DriverAgnosticApi.py` as a library. If the framework breaks (import error, API change, config issue), **all** IDE Robot tests error out — none runnable.

These standalone tests launch `bin/Debug/net9.0-windows/WpfTestIde.exe` directly and drive it via **pywinauto UI Automation** (AutomationId-based). They have **zero imports from TestAutoLayer**, so:

- Framework changes cannot break them
- They run even if `DriverAgnosticApi.py` fails to import
- They provide a safety net for IDE regressions independent of framework regressions

## What they test

| Class | What it verifies |
|-------|-----------------|
| `TestRecordToggle` | btnRecord toggle on/off |
| `TestAttachDialog` | btnAttach opens dialog |
| `TestCheckpointWizard` | btnCheckpointWizard clickable |
| `TestVisualTestBuilder` | btnVisualTestBuilder clickable |
| `TestTabSwitching` | tabElements/tabScripts/tabResults panes |
| `TestDriverSettingsCheckboxes` | chkRecord*/chkRun* checkboxes toggle |
| `TestSettingsButton` | btnSettings in activity bar |
| `TestAppDoesNotCrash` | Smoke test — multiple clicks, no crash |

## How to run

```powershell
# From repo root
python -m pytest Tests/Standalone/ -v
```

Requires:
- `pywinauto` (already installed)
- `WpfTestIde.exe` built at `bin/Debug/net9.0-windows/WpfTestIde.exe`

## How they differ from Robot IDE tests

| | Standalone (pytest + pywinauto) | Robot IDE tests |
|---|---|---|
| Framework dependency | None | Loads `DriverAgnosticApi.py` |
| UI automation | pywinauto (direct UIA) | FlaUILibrary via DriverAgnosticApi |
| App launch | Direct `subprocess.start` | Via `Launch Application` keyword |
| Runs if framework broken | Yes | No — all error out |
| Best for | IDE feature regression, smoke tests | End-to-end framework+IDE validation |
