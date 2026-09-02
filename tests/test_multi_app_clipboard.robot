*** Settings ***
Documentation    Layer 1 — Multi-application test: SampleWPFApp + Notepad clipboard flow.
...    Launches SampleWPFApp with WPFSpy agent, Notepad without agent,
...    performs login with invalid credentials, copies username to clipboard,
...    activates Notepad, pastes the username, and adds a confirmation line.
Library          ../api/DriverAgnosticApi.py
Resource         ../modules/multi_app_module.robot
Test Setup       Reset Multi App State
Test Teardown    Terminate All Applications

*** Variables ***
${SAMPLE_WPF_APP_PATH}    ${CURDIR}${/}..${/}SampleWpfApp${/}bin${/}Debug${/}net8.0-windows${/}SampleWpfApp.dll
${INVALID_USERNAME}       invalid_user_123
${INVALID_PASSWORD}       wrong_password

*** Test Cases ***
Login With Invalid Credentials And Copy Username To Notepad
    [Documentation]    End-to-end multi-app scenario:
    ...    1. Launch SampleWPFApp with WPFSpy agent (attach)
    ...    2. Launch Notepad without spy agent (attach)
    ...    3. Enter invalid username/password and click Submit
    ...    4. Copy the username from the textbox
    ...    5. Activate Notepad
    ...    6. Paste the copied username into Notepad
    ...    7. Add "Copy success" as the next line
    [Tags]    multi-app    clipboard    smoke

    Launch Sample Wpf App With Spy Agent
    Launch Notepad Without Spy Agent

    Login With Invalid Credentials    ${INVALID_USERNAME}    ${INVALID_PASSWORD}

    Copy Username To Clipboard

    Activate Notepad Window

    Paste Username Into Notepad

    Add Copy Success Line

    Verify Notepad Contains Username    ${INVALID_USERNAME}
    Verify Notepad Contains Text    Copy success
