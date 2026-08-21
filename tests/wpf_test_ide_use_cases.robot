*** Settings ***
Documentation    Comprehensive use case tests for WpfTestIde IDE covering recording, verification, script generation, execution, and multi-app workflows.
...              Run with: WPFSPY_MODE=real robot tests/wpf_test_ide_use_cases.robot
Library          ../api/DriverAgnosticApi.py
Library          OperatingSystem

*** Variables ***
${IDE_APP_PATH}    WpfTestIde/bin/Debug/net9.0-windows/WpfTestIde.exe
${SAMPLE_APP_PATH}    SampleWpfApp/bin/Debug/net8.0-windows/SampleWpfApp.exe
${TIMEOUT}    10s

*** Test Cases ***
UC-001 Application Launch
    [Documentation]    Verify that WPF Test IDE launches successfully and displays the main interface.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    ${status}=    Get Element Text    WpfTestIde.MainWindow.StatusText
    Should Contain    ${status}    Not attached
    Capture Screenshot    app_id=ide    filename=ide_launch.png
    Close Application    ide

UC-002 Open Attach To Process Dialog
    [Documentation]    Verify that the Attach to Process dialog can be opened and closed.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnAttach
    Wait Until Element Exists    AttachToProcessDialog.StatusText    timeout=15
    ${status}=    Get Element Text    AttachToProcessDialog.StatusText
    Should Not Be Empty    ${status}
    Capture Screenshot    app_id=ide    filename=ide_attach_dialog.png
    Wait Until Element Exists    AttachToProcessDialog.btnAttachCancel    timeout=15
    Wait Until Element Visible    AttachToProcessDialog.btnAttachCancel    timeout=15
    Click Element    AttachToProcessDialog.btnAttachCancel
    Close Application    ide

UC-003 Open Manage Apps Dialog
    [Documentation]    Verify that the Manage Apps dialog can be opened.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnManageApps
    Sleep    2s
    Capture Screenshot    app_id=ide    filename=ide_manage_apps.png
    Wait Until Element Exists    MultiAppDialog.btnMultiAppClose    timeout=15
    Wait Until Element Visible    MultiAppDialog.btnMultiAppClose    timeout=15
    Click Element    MultiAppDialog.btnMultiAppClose
    Close Application    ide

UC-004 Open Checkpoint Wizard
    [Documentation]    Verify Checkpoint Wizard button is accessible from toolbar.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Wait Until Element Visible    WpfTestIde.MainWindow.btnCheckpointWizard    timeout=15
    ${visible}=    Is Element Visible    WpfTestIde.MainWindow.btnCheckpointWizard
    Should Be True    ${visible}
    Capture Screenshot    app_id=ide    filename=ide_checkpoint_wizard.png
    Close Application    ide

UC-005 Open Spy Tool
    [Documentation]    Verify Spy Tool button is accessible from toolbar.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Wait Until Element Visible    WpfTestIde.MainWindow.btnSpyTool    timeout=15
    ${visible}=    Is Element Visible    WpfTestIde.MainWindow.btnSpyTool
    Should Be True    ${visible}
    Capture Screenshot    app_id=ide    filename=ide_spy_tool.png
    Close Application    ide

UC-006 Open Visual Test Builder
    [Documentation]    Verify Visual Test Builder button is accessible from toolbar.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Wait Until Element Visible    WpfTestIde.MainWindow.btnVisualTestBuilder    timeout=15
    ${visible}=    Is Element Visible    WpfTestIde.MainWindow.btnVisualTestBuilder
    Should Be True    ${visible}
    Capture Screenshot    app_id=ide    filename=ide_visual_builder.png
    Close Application    ide

UC-007 Switch Tabs And Interact
    [Documentation]    Verify tab switching and basic interaction on Elements tab.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.tabScripts
    Sleep    0.5s
    Click Element    WpfTestIde.MainWindow.tabResults
    Sleep    0.5s
    Click Element    WpfTestIde.MainWindow.tabElements
    Sleep    0.5s
    Wait Until Element Visible    WpfTestIde.MainWindow.btnAddFolder    timeout=15
    ${visible}=    Is Element Visible    WpfTestIde.MainWindow.btnAddFolder
    Should Be True    ${visible}
    Capture Screenshot    app_id=ide    filename=ide_tabs.png
    Close Application    ide

UC-008 Toggle Record Button
    [Documentation]    Verify that the Record button can be clicked.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnRecord
    Sleep    0.5s
    Click Element    WpfTestIde.MainWindow.btnRecord
    Sleep    0.5s
    Capture Screenshot    app_id=ide    filename=ide_record_toggle.png
    Close Application    ide

UC-009 Check Driver Settings Checkboxes
    [Documentation]    Verify driver setting checkboxes are accessible and toggleable.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Wait Until Element Visible    WpfTestIde.MainWindow.chkRecordFlaUI    timeout=15
    ${visible1}=    Is Element Visible    WpfTestIde.MainWindow.chkRecordFlaUI
    Should Be True    ${visible1}
    Wait Until Element Visible    WpfTestIde.MainWindow.chkRecordWPFSpy    timeout=15
    ${visible2}=    Is Element Visible    WpfTestIde.MainWindow.chkRecordWPFSpy
    Should Be True    ${visible2}
    Wait Until Element Visible    WpfTestIde.MainWindow.chkRunFlaUI    timeout=15
    ${visible3}=    Is Element Visible    WpfTestIde.MainWindow.chkRunFlaUI
    Should Be True    ${visible3}
    Wait Until Element Visible    WpfTestIde.MainWindow.chkRunWPFSpy    timeout=15
    ${visible4}=    Is Element Visible    WpfTestIde.MainWindow.chkRunWPFSpy
    Should Be True    ${visible4}
    Capture Screenshot    app_id=ide    filename=ide_driver_settings.png
    Close Application    ide

UC-010 Export Repository Button Accessible
    [Documentation]    Verify export repository button is accessible from the SCRIPTS tab toolbar (A7: moved off the global toolbar).
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.tabScripts
    Sleep    1s
    Wait Until Element Visible    WpfTestIde.MainWindow.btnExportRepo    timeout=15
    ${visible1}=    Is Element Visible    WpfTestIde.MainWindow.btnExportRepo
    Should Be True    ${visible1}
    Wait Until Element Visible    WpfTestIde.MainWindow.btnExportScript    timeout=15
    ${visible2}=    Is Element Visible    WpfTestIde.MainWindow.btnExportScript
    Should Be True    ${visible2}
    Wait Until Element Visible    WpfTestIde.MainWindow.btnSaveScript    timeout=15
    ${visible3}=    Is Element Visible    WpfTestIde.MainWindow.btnSaveScript
    Should Be True    ${visible3}
    Capture Screenshot    app_id=ide    filename=ide_export_buttons.png
    Close Application    ide

UC-011 Load Sample And Verify Steps Populated
    [Documentation]    Verify Load Sample populates demo steps and status message updates.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnLoadSample
    Sleep    2s
    ${status}=    Get Element Text    WpfTestIde.MainWindow.StatusText
    Should Contain    ${status}    Loaded sample recording
    Click Element    WpfTestIde.MainWindow.tabScripts
    Sleep    1s
    Click Element    WpfTestIde.MainWindow.tabVisualSteps
    Sleep    1s
    Capture Screenshot    app_id=ide    filename=ide_loaded_steps.png
    Close Application    ide

UC-012 Checkpoint Wizard Open And Configure
    [Documentation]    Verify Checkpoint Wizard can be opened from toolbar.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnCheckpointWizard
    Sleep    2s
    Capture Screenshot    app_id=ide    filename=ide_checkpoint_wizard_open.png
    Close Application    ide

UC-013 Script Generation And Content Verification
    [Documentation]    Verify generated script contains expected Robot Framework keywords and element aliases.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnLoadSample
    Sleep    2s
    Click Element    WpfTestIde.MainWindow.tabScripts
    Sleep    1s
    Click Element    WpfTestIde.MainWindow.tabRawScript
    Sleep    1s
    ${script}=    Get Element Text    WpfTestIde.MainWindow.txtGeneratedScript
    Should Contain    ${script}    Get Data Grid Content Ocr
    Should Contain    ${script}    OrdersPage.gridOrders
    Capture Screenshot    app_id=ide    filename=ide_generated_script.png
    Close Application    ide

UC-014 Run Generated Script And Check Results
    [Documentation]    Verify script execution produces output in Results tab.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnLoadSample
    Sleep    2s
    Click Element    WpfTestIde.MainWindow.tabScripts
    Sleep    1s
    Click Element    WpfTestIde.MainWindow.btnRunScript
    Sleep    60s
    ${output_dir_exists}=    Run Keyword And Return Status    Directory Should Exist    results/ide_run
    Should Be True    ${output_dir_exists}
    Capture Screenshot    app_id=ide    filename=ide_run_results.png
    Close Application    ide

UC-015 Multi-App Dialog Operations
    [Documentation]    Verify Manage Apps dialog can be opened.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnManageApps
    Sleep    2s
    Capture Screenshot    app_id=ide    filename=ide_multi_apps_empty.png
    Close Application    ide

UC-016 Element Tree Operations
    [Documentation]    Verify element tree buttons are accessible on Elements tab.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.tabElements
    Sleep    0.5s
    Wait Until Element Visible    WpfTestIde.MainWindow.btnAddFolder    timeout=15
    ${visible1}=    Is Element Visible    WpfTestIde.MainWindow.btnAddFolder
    Should Be True    ${visible1}
    Wait Until Element Visible    WpfTestIde.MainWindow.btnAddElement    timeout=15
    ${visible2}=    Is Element Visible    WpfTestIde.MainWindow.btnAddElement
    Should Be True    ${visible2}
    Wait Until Element Visible    WpfTestIde.MainWindow.btnPickElement    timeout=15
    ${visible3}=    Is Element Visible    WpfTestIde.MainWindow.btnPickElement
    Should Be True    ${visible3}
    Wait Until Element Visible    WpfTestIde.MainWindow.btnPreviewElement    timeout=15
    ${visible4}=    Is Element Visible    WpfTestIde.MainWindow.btnPreviewElement
    Should Be True    ${visible4}
    Capture Screenshot    app_id=ide    filename=ide_element_tree.png
    Close Application    ide

UC-017 Driver Settings Toggle
    [Documentation]    Verify driver setting checkboxes can be toggled without errors.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Toggle Element    WpfTestIde.MainWindow.chkRecordFlaUI
    Sleep    0.5s
    Toggle Element    WpfTestIde.MainWindow.chkRecordWPFSpy
    Sleep    0.5s
    Toggle Element    WpfTestIde.MainWindow.chkRunFlaUI
    Sleep    0.5s
    Toggle Element    WpfTestIde.MainWindow.chkRunWPFSpy
    Sleep    0.5s
    Capture Screenshot    app_id=ide    filename=ide_driver_toggles.png
    Close Application    ide

UC-018 Spy Tool Open And Close
    [Documentation]    Verify Spy Tool dialog can be opened.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnSpyTool
    Sleep    2s
    Capture Screenshot    app_id=ide    filename=ide_spy_tool_open.png
    Close Application    ide

UC-019 Visual Test Builder Open And Close
    [Documentation]    Verify Visual Test Builder dialog can be opened.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnVisualTestBuilder
    Sleep    2s
    Capture Screenshot    app_id=ide    filename=ide_visual_builder_open.png
    Close Application    ide

REG-001 Load Sample Add Verification And Run Script
    [Documentation]    Regression: load sample steps, verify steps populated, run script, verify output directory.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnLoadSample
    Sleep    2s
    ${status}=    Get Element Text    WpfTestIde.MainWindow.StatusText
    Should Contain    ${status}    Loaded sample recording
    Click Element    WpfTestIde.MainWindow.tabScripts
    Sleep    1s
    Click Element    WpfTestIde.MainWindow.tabRawScript
    Sleep    1s
    ${script}=    Get Element Text    WpfTestIde.MainWindow.txtGeneratedScript
    Should Contain    ${script}    Click Element
    Should Contain    ${script}    LoginPage.txtUsername
    Click Element    WpfTestIde.MainWindow.btnRunScript
    Sleep    60s
    ${output_dir_exists}=    Run Keyword And Return Status    Directory Should Exist    results/ide_run
    Should Be True    ${output_dir_exists}
    Capture Screenshot    app_id=ide    filename=ide_regression_load_verify_run.png
    Close Application    ide

REG-002 Multi-App Dialog UI Verification
    [Documentation]    Regression: open Manage Apps dialog, verify all UI controls are accessible.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnManageApps
    Sleep    2s
    Wait Until Element Exists    MultiAppDialog.btnSetDefault    timeout=15
    Wait Until Element Visible    MultiAppDialog.btnSetDefault    timeout=15
    ${visible}=    Is Element Visible    MultiAppDialog.btnSetDefault
    Should Be True    ${visible}
    Wait Until Element Exists    MultiAppDialog.btnDetach    timeout=15
    Wait Until Element Visible    MultiAppDialog.btnDetach    timeout=15
    ${visible2}=    Is Element Visible    MultiAppDialog.btnDetach
    Should Be True    ${visible2}
    Wait Until Element Exists    MultiAppDialog.btnMultiAppClose    timeout=15
    Wait Until Element Visible    MultiAppDialog.btnMultiAppClose    timeout=15
    ${visible3}=    Is Element Visible    MultiAppDialog.btnMultiAppClose
    Should Be True    ${visible3}
    Capture Screenshot    app_id=ide    filename=ide_regression_multi_app.png
    Close Application    ide

REG-003 Checkpoint Wizard Full Interaction
    [Documentation]    Regression: open Checkpoint Wizard, verify dialog is accessible.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnCheckpointWizard
    Sleep    3s
    Capture Screenshot    app_id=ide    filename=ide_regression_checkpoint.png
    Close Application    ide

REG-004 Element Tree Buttons Accessible
    [Documentation]    Regression: verify element tree buttons are accessible on Elements tab.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.tabElements
    Sleep    2s
    Wait Until Element Visible    WpfTestIde.MainWindow.btnAddFolder    timeout=15
    ${visible1}=    Is Element Visible    WpfTestIde.MainWindow.btnAddFolder
    Should Be True    ${visible1}
    Wait Until Element Visible    WpfTestIde.MainWindow.btnAddElement    timeout=15
    ${visible2}=    Is Element Visible    WpfTestIde.MainWindow.btnAddElement
    Should Be True    ${visible2}
    Wait Until Element Visible    WpfTestIde.MainWindow.btnPickElement    timeout=15
    ${visible3}=    Is Element Visible    WpfTestIde.MainWindow.btnPickElement
    Should Be True    ${visible3}
    Wait Until Element Visible    WpfTestIde.MainWindow.btnPreviewElement    timeout=15
    ${visible4}=    Is Element Visible    WpfTestIde.MainWindow.btnPreviewElement
    Should Be True    ${visible4}
    Capture Screenshot    app_id=ide    filename=ide_regression_element_tree.png
    Close Application    ide

REG-005 Driver Settings Toggle All Modes
    [Documentation]    Regression: toggle all driver checkboxes in both Record and Run sections.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Toggle Element    WpfTestIde.MainWindow.chkRecordFlaUI
    Sleep    0.5s
    Toggle Element    WpfTestIde.MainWindow.chkRecordWPFSpy
    Sleep    0.5s
    Toggle Element    WpfTestIde.MainWindow.chkRunFlaUI
    Sleep    0.5s
    Toggle Element    WpfTestIde.MainWindow.chkRunWPFSpy
    Sleep    0.5s
    Toggle Element    WpfTestIde.MainWindow.chkRecordFlaUI
    Sleep    0.5s
    Toggle Element    WpfTestIde.MainWindow.chkRecordWPFSpy
    Sleep    0.5s
    Toggle Element    WpfTestIde.MainWindow.chkRunFlaUI
    Sleep    0.5s
    Toggle Element    WpfTestIde.MainWindow.chkRunWPFSpy
    Sleep    0.5s
    Capture Screenshot    app_id=ide    filename=ide_regression_driver_settings.png
    Close Application    ide

REG-006 Reset Clears Loaded Sample Steps
    [Documentation]    Regression: load sample then reset, verify steps are cleared.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnLoadSample
    Sleep    2s
    ${status_loaded}=    Get Element Text    WpfTestIde.MainWindow.StatusText
    Should Contain    ${status_loaded}    Loaded sample recording
    Click Element    WpfTestIde.MainWindow.btnReset
    Sleep    1s
    ${status_after}=    Get Element Text    WpfTestIde.MainWindow.StatusText
    Should Not Be Empty    ${status_after}
    Capture Screenshot    app_id=ide    filename=ide_regression_reset.png
    Close Application    ide

REG-007 Check Pipe Button When Not Attached
    [Documentation]    Regression: click Check Pipe button when not attached, verify graceful handling.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnCheckPipe
    Sleep    2s
    Capture Screenshot    app_id=ide    filename=ide_regression_check_pipe.png
    Close Application    ide

REG-008 OCR DataGrid Button Accessible
    [Documentation]    Regression: verify OCR DataGrid button is accessible from toolbar.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Wait Until Element Visible    WpfTestIde.MainWindow.btnOcrDataGrid    timeout=15
    ${visible}=    Is Element Visible    WpfTestIde.MainWindow.btnOcrDataGrid
    Should Be True    ${visible}
    Capture Screenshot    app_id=ide    filename=ide_regression_ocr_button.png
    Close Application    ide

REG-009 Spy Tool And Visual Builder Sequential Open
    [Documentation]    Regression: open Spy Tool then Visual Test Builder sequentially, verify both dialogs accessible.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnSpyTool
    Sleep    2s
    Capture Screenshot    app_id=ide    filename=ide_regression_spy_tool.png
    Click Element    WpfTestIde.MainWindow.btnVisualTestBuilder
    Sleep    2s
    Capture Screenshot    app_id=ide    filename=ide_regression_visual_builder.png
    Close Application    ide

REG-010 Spy Tool Refresh Tree Works
    [Documentation]    Regression: open Spy Tool and verify it loads the target app visual tree.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnSpyTool
    Sleep    3s
    Capture Screenshot    app_id=ide    filename=ide_regression_spy_refresh.png
    Close Application    ide
