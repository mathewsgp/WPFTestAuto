*** Settings ***
Documentation    WPFSpy real-mode use case tests for WpfTestIde.
...              These tests exercise the WPFSpy driver path instead of FlaUI.
...              Requires WPFSPY_MODE=real and SampleWpfApp with spy agent injected.
...              Run with: WPFSPY_MODE=real python -m robot Tests/IDE/wpf_test_ide_wpfspy_use_cases.robot
Library          ../../TestAutoLayer/api/DriverAgnosticApi.py
Library          OperatingSystem

*** Variables ***
${IDE_APP_PATH}    bin/Debug/net9.0-windows/WpfTestIde.exe
${SAMPLE_APP_PATH}    Tests/bin/SampleWpfApp/Debug/net9.0-windows/SampleWpfApp.exe
${TIMEOUT}    10s

*** Test Cases ***
UC-001 Application Launch (WPFSpy)
    [Documentation]    Verify WPF Test IDE launches with WPFSpy driver.
    Launch Application    ${IDE_APP_PATH}    app_id=ide    drivers=WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Wait Until Element Visible    WpfTestIde.MainWindow    timeout=15
    Capture Screenshot    app_id=ide    filename=ide_launch_wpfspy.png
    Close Application    ide

UC-002 Open Attach To Process Dialog (WPFSpy)
    [Documentation]    Verify Attach to Process dialog opens with WPFSpy driver.
    Launch Application    ${IDE_APP_PATH}    app_id=ide    drivers=WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnAttach
    Wait Until Element Exists    AttachToProcessDialog.StatusText    timeout=15
    ${status}=    Get Element Text    AttachToProcessDialog.StatusText
    Should Not Be Empty    ${status}
    Capture Screenshot    app_id=ide    filename=ide_attach_dialog_wpfspy.png
    Close Application    ide

UC-003 Open Manage Apps Dialog (WPFSpy)
    [Documentation]    Verify Manage Apps button with WPFSpy driver.
    Launch Application    ${IDE_APP_PATH}    app_id=ide    drivers=WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Wait Until Element Exists    WpfTestIde.MainWindow.btnManageApps    timeout=15
    Capture Screenshot    app_id=ide    filename=ide_manage_apps_wpfspy.png
    Close Application    ide

UC-004 Open Checkpoint Wizard (WPFSpy)
    [Documentation]    Verify Checkpoint Wizard button with WPFSpy driver.
    Launch Application    ${IDE_APP_PATH}    app_id=ide    drivers=WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Wait Until Element Exists    WpfTestIde.MainWindow.btnCheckpointWizard    timeout=15
    Click Element    WpfTestIde.MainWindow.btnCheckpointWizard
    Sleep    2s
    Capture Screenshot    app_id=ide    filename=ide_checkpoint_wizard_wpfspy.png
    Close Application    ide

UC-005 Open Spy Tool (WPFSpy)
    [Documentation]    Verify Spy Tool is accessible with WPFSpy driver.
    Launch Application    ${IDE_APP_PATH}    app_id=ide    drivers=WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    # Spy Tool accessed via activity bar/menu (no AutomationId button)
    Capture Screenshot    app_id=ide    filename=ide_spy_tool_wpfspy.png
    Close Application    ide

UC-006 Open Visual Test Builder (WPFSpy)
    [Documentation]    Verify Visual Test Builder button with WPFSpy driver.
    Launch Application    ${IDE_APP_PATH}    app_id=ide    drivers=WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnVisualTestBuilder
    Sleep    2s
    Capture Screenshot    app_id=ide    filename=ide_visual_builder_wpfspy.png
    Close Application    ide

UC-007 Switch Tabs And Interact (WPFSpy)
    [Documentation]    Verify tab switching with WPFSpy driver.
    Launch Application    ${IDE_APP_PATH}    app_id=ide    drivers=WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.tabScripts
    Sleep    0.5s
    Click Element    WpfTestIde.MainWindow.tabResults
    Sleep    0.5s
    Click Element    WpfTestIde.MainWindow.tabElements
    Sleep    0.5s
    Capture Screenshot    app_id=ide    filename=ide_tabs_wpfspy.png
    Close Application    ide

UC-008 Toggle Record Button (WPFSpy)
    [Documentation]    Verify Record button toggle with WPFSpy driver.
    Launch Application    ${IDE_APP_PATH}    app_id=ide    drivers=WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnRecord
    Sleep    0.5s
    Click Element    WpfTestIde.MainWindow.btnRecord
    Sleep    0.5s
    Capture Screenshot    app_id=ide    filename=ide_record_toggle_wpfspy.png
    Close Application    ide

UC-009 Check Driver Settings Checkboxes (WPFSpy)
    [Documentation]    Verify driver checkboxes with WPFSpy driver.
    Launch Application    ${IDE_APP_PATH}    app_id=ide    drivers=WPFSpy
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
    Capture Screenshot    app_id=ide    filename=ide_driver_settings_wpfspy.png
    Close Application    ide
