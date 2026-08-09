*** Settings ***
Documentation    Use case tests for WpfTestIde application using the automation framework.
...              Validates core IDE workflows: launch, toolbar, dialogs, tabs, recording, script execution.
...              Run with: WPFSPY_MODE=real robot tests/wpf_test_ide_use_cases.robot
Library          ../api/DriverAgnosticApi.py

*** Variables ***
${IDE_APP_PATH}    WpfTestIde/bin/Debug/net8.0-windows/WpfTestIde.exe
${SAMPLE_APP_PATH}    SampleWpfApp/bin/Debug/net8.0-windows/SampleWpfApp.exe

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
    [Documentation]    Verify that the Attach to Process dialog can be opened.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnAttach
    Sleep    1s
    ${status}=    Get Element Text    AttachToProcessDialog.StatusText
    Should Not Be Empty    ${status}
    Capture Screenshot    app_id=ide    filename=ide_attach_dialog.png
    Click Element    AttachToProcessDialog.btnAttachCancel
    Close Application    ide

UC-003 Open Manage Apps Dialog
    [Documentation]    Verify that the Manage Apps dialog can be opened.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    Click Element    WpfTestIde.MainWindow.btnManageApps
    Sleep    1s
    Capture Screenshot    app_id=ide    filename=ide_manage_apps.png
    Close Application    ide

UC-004 Open Checkpoint Wizard
    [Documentation]    Verify Checkpoint Wizard button is accessible from toolbar.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    ${visible}=    Is Element Visible    WpfTestIde.MainWindow.btnCheckpointWizard
    Should Be True    ${visible}
    Capture Screenshot    app_id=ide    filename=ide_checkpoint_wizard.png
    Close Application    ide

UC-005 Open Spy Tool
    [Documentation]    Verify Spy Tool button is accessible from toolbar.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    ${visible}=    Is Element Visible    WpfTestIde.MainWindow.btnSpyTool
    Should Be True    ${visible}
    Capture Screenshot    app_id=ide    filename=ide_spy_tool.png
    Close Application    ide

UC-006 Open Visual Test Builder
    [Documentation]    Verify Visual Test Builder button is accessible from toolbar.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
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
    [Documentation]    Verify driver setting checkboxes are accessible.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    ${visible1}=    Is Element Visible    WpfTestIde.MainWindow.chkRecordFlaUI
    Should Be True    ${visible1}
    ${visible2}=    Is Element Visible    WpfTestIde.MainWindow.chkRecordWPFSpy
    Should Be True    ${visible2}
    ${visible3}=    Is Element Visible    WpfTestIde.MainWindow.chkRunFlaUI
    Should Be True    ${visible3}
    ${visible4}=    Is Element Visible    WpfTestIde.MainWindow.chkRunWPFSpy
    Should Be True    ${visible4}
    Capture Screenshot    app_id=ide    filename=ide_driver_settings.png
    Close Application    ide

UC-010 Export Repository Button Accessible
    [Documentation]    Verify export repository button is accessible from toolbar.
    Launch Application    ide    ${IDE_APP_PATH}    WPFSpy
    Wait For Application    ide    timeout=30
    Switch Application    ide
    ${visible1}=    Is Element Visible    WpfTestIde.MainWindow.btnExportRepo
    Should Be True    ${visible1}
    ${visible2}=    Is Element Visible    WpfTestIde.MainWindow.btnExportScript
    Should Be True    ${visible2}
    ${visible3}=    Is Element Visible    WpfTestIde.MainWindow.btnSaveScript
    Should Be True    ${visible3}
    Capture Screenshot    app_id=ide    filename=ide_export_buttons.png
    Close Application    ide
