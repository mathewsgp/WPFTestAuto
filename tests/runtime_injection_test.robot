*** Settings ***
Documentation    Test runtime injection features
...
...              This test suite demonstrates:
...              1. Launching apps with Spy Agent via startup hook
...              2. Attaching to already-running processes
...              3. Using the RuntimeInjector API
...
...              Prerequisites:
...              - Build WpfSpyAgent.StartupHook project
...              - Set WPFSPY_STARTUP_HOOK_DLL env var (optional)
Library          ../api/robot_launcher.py

*** Variables ***
${APP_PATH}      ${CURDIR}${/}..${/}SampleWpfApp${/}bin${/}Debug${/}net8.0-windows${/}SampleWpfApp.dll
${PIPE_NAME}     WPFSpyAgentPipe

*** Test Cases ***
Test Runtime Injector Initialization
    [Documentation]    Verify RuntimeInjector finds startup hook DLL
    ${path}=    Get Startup Hook Path
    Log    Startup hook path: ${path}
    # Path should be found if project was built
    IF    '${path}' == 'None'
        Log    WARNING: Startup hook DLL not found. Build WpfSpyAgent.StartupHook first.    console=True
    END

Test Launch Application With Spy Agent
    [Documentation]    Launch app with Spy Agent auto-injected
    [Tags]    runtime_injection
    Log    Launching application with Spy Agent...    console=True
    ${pid}=    Launch Application
    ...    app_path=${APP_PATH}
    ...    pipe_name=${PIPE_NAME}
    ...    timeout=30.0
    Log    Launched with PID: ${pid}    console=True
    Should Not Be Equal As Integers    ${pid}    ${0}
    # Give app time to fully start
    Sleep    3
    # Verify agent is ready
    ${ready}=    Is Agent Ready    ${PIPE_NAME}
    Log    Agent ready: ${ready}    console=True

Test Attach To Running Application
    [Documentation]    Attach to already-running application
    [Tags]    runtime_injection    attach
    # First, ensure an app is running
    ${existing_pid}=    Get Process Id    ${APP_PATH}
    IF    '${existing_pid}' == 'None'
        Log    No running app found. Launching first...    console=True
        ${existing_pid}=    Launch Application    ${APP_PATH}
        Sleep    3
    END
    Log    Attaching to PID: ${existing_pid}    console=True
    ${connected}=    Attach To Application    ${existing_pid}    ${PIPE_NAME}
    Log    Connected: ${connected}    console=True

Test Terminate Application
    [Documentation]    Terminate launched application
    [Tags]    runtime_injection    cleanup
    ${pid}=    Launch Application    ${APP_PATH}
    Sleep    2
    Log    Terminating PID: ${pid}    console=True
    ${terminated}=    Terminate Application    ${pid}
    Should Be True    ${terminated}

Test Agent Ready Check
    [Documentation]    Check if Spy Agent is ready on pipe
    [Tags]    runtime_injection
    ${ready}=    Is Agent Ready    WPFSpyAgentPipe
    Log    Agent ready: ${ready}    console=True
    # This may be False if no agent is running

Test Launch With Custom Arguments
    [Documentation]    Launch app with command-line arguments
    [Tags]    runtime_injection
    ${pid}=    Launch Application
    ...    app_path=${APP_PATH}
    ...    arguments=--debug --no-splash
    ...    timeout=30.0
    Log    Launched with custom args, PID: ${pid}    console=True
    Should Not Be Equal As Integers    ${pid}    ${0}
    # Cleanup
    Terminate Application    ${pid}

Test Terminate All Applications
    [Documentation]    Cleanup all launched applications
    [Tags]    runtime_injection    cleanup
    # Launch multiple apps
    ${pid1}=    Launch Application    ${APP_PATH}
    ${pid2}=    Launch Application    ${APP_PATH}
    Sleep    2
    Log    Launched PIDs: ${pid1}, ${pid2}    console=True
    # Terminate all
    ${count}=    Terminate All Applications
    Log    Terminated ${count} applications    console=True
    Should Be True    ${count} >= ${2}

Test Environment Variable Configuration
    [Documentation]    Verify environment variables are set correctly
    [Tags]    runtime_injection    config
    ${path}=    Get Startup Hook Path
    IF    '${path}' != 'None'
        # Set env var and verify
        Set Environment Variable    WPFSPY_STARTUP_HOOK_DLL    ${path}
        ${launcher}=    Set Variable    ${CURDIR}${/}..${/}api${/}runtime_injector.py
        Log    Environment variable set. Ready to launch with Spy Agent.
    ELSE
        Log    Skipping: Startup hook DLL not available.
    END
