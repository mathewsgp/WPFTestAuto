*** Settings ***
Documentation    Test runtime injection features
...
...              This test suite demonstrates:
...              1. Launching apps with Spy Agent via startup hook
...              2. Attaching to already-running processes
...              3. Using the AppLauncher API
...
...              Prerequisites:
...              - Build WpfSpyAgent.StartupHook project
...              - Set WPFSPY_STARTUP_HOOK_DLL env var (optional)
Library          ../api/robot_launcher.py

*** Variables ***
${APP_PATH}      ../SampleWpfApp/bin/Debug/net8.0-windows/SampleWpfApp.dll
${PIPE_NAME}     WPFSpyAgentPipe

*** Test Cases ***
Test Library Discovery
    [Documentation]    Verify AppLauncher library keywords are discovered
    Log    AppLauncher library loaded successfully
    ${path}=    Get Startup Hook Path
    Log    Startup hook path: ${path}

Test Get Startup Hook Path
    [Documentation]    Verify RuntimeInjector finds startup hook DLL
    ${path}=    Get Startup Hook Path
    Log    Startup hook path: ${path}
    Run Keyword If    '${path}' == 'None'
    ...    Log    WARNING: Startup hook DLL not found. Build WpfSpyAgent.StartupHook first.
    ...    ELSE    Log    Found hook at: ${path}

Test Launch Application With Spy Agent
    [Documentation]    Launch app with Spy Agent auto-injected
    [Tags]    runtime_injection
    ${path}=    Get Startup Hook Path
    Skip If    '${path}' == 'None'    Startup hook DLL not found. Build WpfSpyAgent.StartupHook first.
    Log    Launching application with Spy Agent...
    ${pid}=    Launch Application    ${APP_PATH}
    Log    Launched with PID: ${pid}
    Should Not Be Equal As Integers    ${pid}    ${0}
    Sleep    3
    ${terminated}=    Terminate Application    ${pid}
    Log    Terminated: ${terminated}

Test Terminate Application
    [Documentation]    Terminate launched application
    [Tags]    runtime_injection    cleanup
    ${path}=    Get Startup Hook Path
    Skip If    '${path}' == 'None'
    ${pid}=    Launch Application    ${APP_PATH}
    Sleep    2
    Log    Terminating PID: ${pid}
    ${terminated}=    Terminate Application    ${pid}
    Should Be True    ${terminated}

Test Agent Ready Check
    [Documentation]    Check if Spy Agent is ready on pipe
    [Tags]    runtime_injection
    ${ready}=    Is Agent Ready    ${PIPE_NAME}
    Log    Agent ready: ${ready}

Test Terminate All Applications
    [Documentation]    Cleanup all launched applications
    [Tags]    runtime_injection    cleanup
    ${path}=    Get Startup Hook Path
    Skip If    '${path}' == 'None'
    ${pid1}=    Launch Application    ${APP_PATH}
    ${pid2}=    Launch Application    ${APP_PATH}
    Sleep    2
    Log    Launched PIDs: ${pid1}, ${pid2}
    ${count}=    Terminate All Applications
    Log    Terminated ${count} applications
    Should Be True    ${count} >= 2
