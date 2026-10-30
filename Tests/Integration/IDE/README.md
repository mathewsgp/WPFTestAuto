# Category 3 — IDE Integration Tests (Independent of Framework)

These tests verify **WPFTestIDE features end-to-end without using TestAutoLayer directly**.

## Two sub-layers

### 3a — Standalone pywinauto tests (truly independent)

`test_ide_features.py` launches `WpfTestIde.exe` directly and drives it via **pywinauto UI Automation** (AutomationId-based). Zero imports from TestAutoLayer.

| Test | What it verifies |
|------|-----------------|
| `test_record_toggle_turns_on_and_off` | btnRecord toggle state |
| `test_attach_opens_dialog` | btnAttach opens Attach dialog |
| `test_checkpoint_wizard_button_exists_and_clickable` | btnCheckpointWizard |
| `test_visual_test_builder_button_clickable` | btnVisualTestBuilder |
| `test_switch_tabs` | tabElements/tabScripts/tabResults panes |
| `test_record_checkboxes_toggle` | chkRecordFlaUI/WPFSpy/Sikuli |
| `test_run_checkboxes_toggle` | chkRunFlaUI/WPFSpy/Sikuli |
| `test_settings_button_clickable` | btnSettings in activity bar |
| `test_app_launches_and_responds` | Smoke test — multiple clicks, no crash |

**Run:** `python -m pytest Tests/Integration/IDE/ -k "not robot"`

### 3b — Robot IDE tests (uses TestAutoLayer)

These Robot suites test IDE features **through** TestAutoLayer (`DriverAgnosticApi.py`). They are end-to-end IDE tests but depend on the framework loading correctly.

| Test file | What it verifies |
|-----------|-----------------|
| `wpf_test_ide_use_cases.robot` | UC-001 through UC-019 + REG/Sikuli use cases |
| `wpf_test_ide_wpfspy_use_cases.robot` | Same UC tests using WPFSpy driver |
| `debug_paths.robot` | IDE debug path resolution |
| `test_launch_attach_combinations.robot` | Launch/attach driver combinations |
| `wpf_test_ide_self_test.robot` | Framework self-test through IDE |

**Run:** `robot --outputdir output Tests/Integration/IDE/*.robot`

## How to run all Category 3

```powershell
# Standalone (no framework dependency)
python -m pytest Tests/Integration/IDE/test_ide_features.py -v

# Robot IDE tests (uses TestAutoLayer)
robot --outputdir output Tests/Integration/IDE/*.robot
```

## CI guidance

Standalone tests should run on every commit — they cannot be broken by framework changes. Robot IDE tests should run when the framework is known-good.
