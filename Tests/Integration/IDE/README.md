# Category 3 — IDE Integration Tests (Independent of Framework)

These tests verify **WPFTestIDE features end-to-end without using TestAutoLayer directly**.

## Standalone pywinauto tests (truly independent)

**Location:** `Tests/Integration/IDE/test_ide_features.py`

Launches `WpfTestIde.exe` directly and drives it via **pywinauto UI Automation** (AutomationId-based). **Zero imports from TestAutoLayer**, so framework changes cannot break these tests.

### What's tested

| Test class | Test method | What it verifies |
|------------|-------------|-----------------|
| `TestUC001ApplicationLaunch` | `test_ide_launches_and_shows_main_window` | IDE launches, MainWindow visible |
| `TestUC002AttachToProcessDialog` | `test_attach_opens_dialog` | btnAttach opens dialog |
| `TestUC003ManageAppsDialog` | `test_manage_apps_button_accessible` | btnManageApps clickable |
| `TestUC004CheckpointWizard` | `test_checkpoint_wizard_button_clickable` | btnCheckpointWizard clickable |
| `TestUC005SpyTool` | `test_spy_tool_accessible_via_activity_bar` | Spy Tool via menu |
| `TestUC006VisualTestBuilder` | `test_visual_test_builder_button_clickable` | btnVisualTestBuilder clickable |
| `TestUC007SwitchTabs` | `test_tab_switching` | tabScripts/tabResults/tabElements |
| `TestUC008ToggleRecord` | `test_record_toggle_on_and_off` | btnRecord toggle on/off |
| `TestUC009DriverSettings` | `test_record_checkboxes_toggle` | chkRecord* checkboxes |
| | `test_run_checkboxes_toggle` | chkRun* checkboxes |
| `TestUC010ExportRepository` | `test_export_repository_button_accessible` | SCRIPTS tab switch |
| `TestUC011LoadSample` | `test_load_sample_populates_steps` | btnLoadSample, StatusText |
| `TestUC012CheckpointWizardOpen` | `test_checkpoint_wizard_from_scripts_tab` | Checkpoint Wizard from SCRIPTS |
| `TestUC013ScriptGeneration` | `test_generated_script_not_empty` | Script generation content |
| `TestUC014RunScript` | `test_run_script_produces_output` | btnRunScript |
| `TestUC015MultiAppDialog` | `test_manage_apps_after_attach` | btnManageApps |
| `TestUC016ElementTree` | `test_element_tree_tab_accessible` | tabElements |
| `TestUC017DriverSettingsToggle` | `test_all_driver_checkboxes_toggle` | All driver checkboxes |
| `TestUC018SpyToolOpenClose` | `test_spy_tool_open` | Spy Tool open |
| `TestUC019VisualTestBuilderOpen` | `test_visual_test_builder_open` | Visual Test Builder open |
| `TestREG001LoadVerifyRun` | `test_load_verify_run_regression` | Full regression flow |
| `TestREG002MultiAppDialog` | `test_multi_app_dialog_accessible` | Multi-App dialog |
| `TestREG003CheckpointWizardFull` | `test_checkpoint_wizard_full_interaction` | Checkpoint Wizard full |
| `TestREG004ElementTreeButtons` | `test_element_tree_buttons_accessible` | Element tree buttons |
| `TestREG005DriverSettingsAllModes` | `test_all_modes_toggle` | All mode toggles |
| `TestSikuli001RecordSikuli` | `test_record_sikuli_checkbox_toggle` | chkRecordSikuli |
| `TestSikuli002SikuliSettings` | `test_sikuli_settings_dialog` | Sikuli settings |
| `TestDebugPaths` | `test_ide_app_path_exists` | IDE app running |
| `TestAppSmoke` | `test_app_remains_responsive_after_multiple_interactions` | Smoke test |

## How to run

```powershell
# From repo root
python -m pytest Tests/Integration/IDE/test_ide_features.py -v
```

Requires:
- `pywinauto` (already installed)
- `WPFTestIDE\bin\Debug\net9.0-windows\WpfTestIde.exe` built

## Why no Robot IDE tests here?

The previous Robot IDE suites (`wpf_test_ide_use_cases.robot`, etc.) loaded
`TestAutoLayer/api/DriverAgnosticApi.py` directly. If the framework breaks
(import error, API change, config issue), **all** those Robot tests error out
— none runnable.

These standalone pywinauto tests have **zero TestAutoLayer dependency**, so they
provide IDE regression coverage independent of framework health.
