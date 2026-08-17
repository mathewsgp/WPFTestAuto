*** Settings ***
Documentation    Self-test for WPFTestAuto framework using SampleWpfApp and WpfTestIde.
...              Validates that the automation framework can drive both WPF apps.
...              Note: PasswordBox automation is limited by UIAutomation security;
...              this test focuses on elements that are automatable.
Library          ../api/DriverAgnosticApi.py

*** Variables ***
${SAMPLE_APP_PATH}    SampleWpfApp/bin/Debug/net8.0-windows/SampleWpfApp.exe
${IDE_APP_PATH}       WpfTestIde/bin/Debug/net9.0-windows/WpfTestIde.exe

*** Test Cases ***
Framework Can Launch And Drive SampleWpfApp
    [Documentation]    Validate core framework by driving the sample app end-to-end.
    Launch Application    sample    ${SAMPLE_APP_PATH}    WPFSpy
    Wait For Application    sample    timeout=30
    Switch Application    sample
    Click Element    LoginPage.MainWindow.btnSubmit
    Sleep    1s
    Close Application    sample

Framework Can Launch WpfTestIde
    [Documentation]    Validate that the framework can launch and verify the IDE app.
    Launch Application    ide    ${IDE_APP_PATH}    FlaUI
    Wait For Application    ide    timeout=30
    Switch Application    ide
    ${apps}=    Get Application List
    Should Contain    ${apps}    ide
    Capture Screenshot    app_id=ide    filename=ide_launch.png
    Close Application    ide

Multi App Both Apps Running
    [Documentation]    Validate multi-app context with both apps simultaneously.
    Launch Application    sample    ${SAMPLE_APP_PATH}    WPFSpy
    Launch Application    ide    ${IDE_APP_PATH}    FlaUI
    Wait For Application    sample    timeout=30
    Wait For Application    ide    timeout=30
    ${apps}=    Get Application List
    Should Contain    ${apps}    sample
    Should Contain    ${apps}    ide
    Set Default Application    sample
    Switch Application    sample
    Click Element    LoginPage.MainWindow.btnSubmit
    Sleep    1s
    Switch Application    ide
    Capture Screenshot    app_id=ide    filename=ide_multi_app.png
    Close Application    sample
    Close Application    ide

Legacy Mode Still Works
    [Documentation]    Ensure backward compatibility when no apps are registered.
    Launch Application    legacy    ${SAMPLE_APP_PATH}    WPFSpy
    Wait For Application    legacy    timeout=30
    Switch Application    legacy
    Click Element    LoginPage.MainWindow.txtUsername
    Close Application    legacy
