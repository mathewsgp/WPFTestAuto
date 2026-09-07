*** Settings ***
Documentation    Test launch/attach combinations for SampleWpfApp.
...              Covers .NET Core startup-hook path and .NET Framework
...              native-inject path, with driver priority variations.
Library           ../../TestAutoLayer/api/DriverAgnosticApi.py

*** Variables ***
${NET_CORE_APP}    C:/Users/mathe/source/repos/WPFTestAuto/bin/SampleWPFApp/Debug/net9.0-windows/SampleWpfApp.exe
${NET_FW_APP}      C:/Users/mathe/source/repos/WPFTestAuto/bin/SampleWPFApp/Debug/net461/SampleWpfApp.exe
${WAIT_TIMEOUT}    30s
${PIPE_WAIT_RETRIES}    10
${PIPE_WAIT_DELAY}      2s

*** Test Cases ***
Launch .NET Core with WPFSpy priority
    [Documentation]    Launch net9 app with WPFSpy first, fallback to FlaUI.
    Launch Application    ${NET_CORE_APP}    app_id=samplewpfapp_core_wpf    drivers=WPFSpy,FlaUI
    Wait Until Keyword Succeeds    5x    2s    Check Pipe    WPFSpyAgentPipe_samplewpfapp_core_wpf
    Attach To Application    samplewpfapp_core_wpf    ${EMPTY}    WPFSpy    WPFSpyAgentPipe_samplewpfapp_core_wpf
    Wait Until Keyword Succeeds    5x    2s    Check Pipe    WPFSpyAgentPipe_samplewpfapp_core_wpf
    Click Element    SampleWpfApp.LoginPage.btnSubmit    app_id=samplewpfapp_core_wpf

Launch .NET Core with FlaUI priority
    [Documentation]    Launch net9 app with FlaUI first, WPFSpy fallback.
    Launch Application    ${NET_CORE_APP}    app_id=samplewpfapp_core_fla    drivers=FlaUI,WPFSpy
    Wait Until Keyword Succeeds    5x    2s    Check Pipe    WPFSpyAgentPipe_samplewpfapp_core_fla
    Attach To Application    samplewpfapp_core_fla    ${EMPTY}    WPFSpy    WPFSpyAgentPipe_samplewpfapp_core_fla
    Wait Until Keyword Succeeds    5x    2s    Check Pipe    WPFSpyAgentPipe_samplewpfapp_core_fla
    Click Element    SampleWpfApp.LoginPage.btnSubmit    app_id=samplewpfapp_core_fla

Attach to .NET Framework runtime attach
    [Documentation]    Attach to already-running net461 app.
    # Pre-condition: SampleWpfApp.exe (net461) must already be running.
    Attach To Application    samplewpfapp_fw_attach    ${EMPTY}    WPFSpy    WPFSpyAgentPipe
    Wait Until Keyword Succeeds    5x    2s    Check Pipe    WPFSpyAgentPipe
    Click Element    SampleWpfApp.LoginPage.btnSubmit    app_id=samplewpfapp_fw_attach

Launch .NET Framework with pre-launch inject
    [Documentation]    Launch net461 app via IDE pre-launch + native inject.
    # The IDE will rewrite this Launch Application into Attach To Application
    # with the PID and pipe WPFSpyAgentPipe.
    Launch Application    ${NET_FW_APP}    app_id=samplewpfapp_fw_launch    drivers=WPFSpy,FlaUI
    Wait Until Keyword Succeeds    5x    2s    Check Pipe    WPFSpyAgentPipe_samplewpfapp_fw_launch
    Attach To Application    samplewpfapp_fw_launch    ${EMPTY}    WPFSpy    WPFSpyAgentPipe_samplewpfapp_fw_launch
    Wait Until Keyword Succeeds    5x    2s    Check Pipe    WPFSpyAgentPipe_samplewpfapp_fw_launch
    Click Element    SampleWpfApp.LoginPage.btnSubmit    app_id=samplewpfapp_fw_launch

Multi-app mixed frameworks
    [Documentation]    Launch both net9 and net461 apps in one script.
    Launch Application    ${NET_CORE_APP}    app_id=core_app    drivers=WPFSpy,FlaUI
    Launch Application    ${NET_FW_APP}    app_id=fw_app    drivers=WPFSpy,FlaUI
    Wait Until Keyword Succeeds    5x    2s    Check Pipe    WPFSpyAgentPipe_core_app
    Wait Until Keyword Succeeds    5x    2s    Check Pipe    WPFSpyAgentPipe_fw_app
    Attach To Application    core_app    ${EMPTY}    WPFSpy    WPFSpyAgentPipe_core_app
    Attach To Application    fw_app    ${EMPTY}    WPFSpy    WPFSpyAgentPipe_fw_app
    Click Element    SampleWpfApp.LoginPage.btnSubmit    app_id=core_app
    Click Element    SampleWpfApp.LoginPage.btnSubmit    app_id=fw_app

Driver priority fallback to FlaUI
    [Documentation]    WPFSpy unavailable, verify FlaUI fallback works.
    Launch Application    ${NET_CORE_APP}    app_id=samplewpfapp_flaonly    drivers=FlaUI
    Attach To Application    samplewpfapp_flaonly    ${EMPTY}    FlaUI
    Click Element    SampleWpfApp.LoginPage.btnSubmit    app_id=samplewpfapp_flaonly

*** Keywords ***
Check Pipe
    [Arguments]    ${pipe_name}
    ${result}=    Is Pipe Ready    ${pipe_name}
    Should Be True    ${result}    msg=Spy Agent pipe ${pipe_name} not ready
