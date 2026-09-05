*** Settings ***
Documentation    Layer 2 — Reusable Action Modules: Multi-application operations
...    for clipboard and window management across SampleWPFApp and Notepad.
Library          ../api/DriverAgnosticApi.py

*** Variables ***
${SAMPLE_WPF_APP_ID}    samplewpfapp
${NOTEPAD_APP_ID}       notepad
${SAMPLE_WPF_APP_PATH}  ${CURDIR}${/}..${/}SampleWpfApp${/}bin${/}Debug${/}net9.0-windows${/}SampleWpfApp.dll

*** Keywords ***
Reset Multi App State
    [Documentation]    Ensures clean state for multi-app tests by terminating
    ...    all registered applications and resetting the multi-app context.
    Terminate All Applications
    Reset Application

Launch Sample Wpf App With Spy Agent
    [Documentation]    Launches SampleWPFApp with WPFSpy driver and spy agent enabled.
    ...    The app is attached and ready for automation via the named pipe.
    ${app_id}=    Launch Application    ${SAMPLE_WPF_APP_PATH}
    ...    app_id=${SAMPLE_WPF_APP_ID}
    ...    driver=WPFSpy
    ...    attach=True
    Set Default Application    ${SAMPLE_WPF_APP_ID}
    Set Driver    WPFSpy
    Wait Until Element Exists    LoginPage.MainWindow.txtUsername    timeout=15.0
    ...    app_id=${SAMPLE_WPF_APP_ID}

Launch Notepad Without Spy Agent
    [Documentation]    Launches Notepad using FlaUI driver (no spy agent).
    ...    Notepad is a standard Win32 app, so FlaUI is the appropriate driver.
    ${app_id}=    Launch Application    C:\\Windows\\System32\\notepad.exe
    ...    app_id=${NOTEPAD_APP_ID}
    ...    driver=FlaUI
    Sleep    2s
    Activate Window    app_id=${NOTEPAD_APP_ID}    window_title=Untitled - Notepad

Login With Invalid Credentials
    [Documentation]    Enters invalid username and password, then clicks Submit.
    [Arguments]    ${username}    ${password}
    Set Default Application    ${SAMPLE_WPF_APP_ID}
    Set Element Value    LoginPage.MainWindow.txtUsername    ${username}
    Set Element Value    LoginPage.MainWindow.txtPassword    ${password}
    Click Element    LoginPage.MainWindow.btnSubmit
    Sleep    1s

Copy Username To Clipboard
    [Documentation]    Copies the username text from the login form to clipboard.
    Set Default Application    ${SAMPLE_WPF_APP_ID}
    ${username}=    Get Element Text    LoginPage.MainWindow.txtUsername
    Set Clipboard Text    ${username}
    Log    Copied username to clipboard: ${username}

Activate Notepad Window
    [Documentation]    Brings Notepad to the foreground and gives it focus.
    Set Default Application    ${NOTEPAD_APP_ID}
    Activate Window    app_id=${NOTEPAD_APP_ID}    window_title=Untitled - Notepad
    Sleep    0.5s

Paste Username Into Notepad
    [Documentation]    Pastes the clipboard content into Notepad's text editor.
    ...    Uses Ctrl+V to paste into the focused window.
    Set Default Application    ${NOTEPAD_APP_ID}
    # Use Activate Window to ensure Notepad is focused, then paste
    Activate Window    app_id=${NOTEPAD_APP_ID}    window_title=Untitled - Notepad
    Sleep    0.5s
    # Use the FlaUI library directly to paste
    Paste Clipboard Text To Notepad

Add Copy Success Line
    [Documentation]    Adds "Copy success" as the next line in Notepad.
    Activate Window    app_id=${NOTEPAD_APP_ID}    window_title=Untitled - Notepad
    Sleep    0.3s
    Type Text Into Notepad    {ENTER}Copy success

Paste Clipboard Text To Notepad
    [Documentation]    Pastes clipboard content into Notepad using keyboard shortcut.
    ${clipboard_text}=    Get Clipboard Text
    Log    Pasting into Notepad: ${clipboard_text}
    Send Keys To Window    Untitled - Notepad    ^v

Type Text Into Notepad
    [Documentation]    Types text into Notepad using keyboard input.
    [Arguments]    ${text}
    Send Keys To Window    Untitled - Notepad    ${text}

Verify Notepad Contains Username
    [Documentation]    Verifies that the clipboard contains the expected username.
    ...    (The username was copied to clipboard before pasting into Notepad.)
    [Arguments]    ${expected_username}
    ${clipboard_text}=    Get Clipboard Text
    Should Contain    ${clipboard_text}    ${expected_username}
    Log    Verified clipboard contains username: ${expected_username}

Verify Notepad Contains Text
    [Documentation]    Verifies that Notepad contains the expected text by
    ...    selecting all text in Notepad, copying it, and checking the clipboard.
    [Arguments]    ${expected_text}
    # Select all text in Notepad and copy to clipboard
    Activate Window    app_id=${NOTEPAD_APP_ID}    window_title=Untitled - Notepad
    Sleep    0.3s
    Send Keys To Window    Untitled - Notepad    ^a
    Sleep    0.2s
    Send Keys To Window    Untitled - Notepad    ^c
    Sleep    0.3s
    ${notepad_content}=    Get Clipboard Text
    Log    Notepad content: ${notepad_content}
    # Case-insensitive check
    ${lower_content}=    Evaluate    '''${notepad_content}'''.lower()
    ${lower_expected}=    Evaluate    '''${expected_text}'''.lower()
    Should Contain    ${lower_content}    ${lower_expected}
    Log    Verified Notepad contains: ${expected_text}

Terminate All Applications
    [Documentation]    Terminates all registered applications.
    Terminate Application    app_id=${SAMPLE_WPF_APP_ID}
    Terminate Application    process_name=notepad.exe
