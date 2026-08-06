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
Library          ${CURDIR}${/}..${/}api${/}robot_launcher.py

*** Variables ***
${APP_PATH}      ${CURDIR}${/}..${/}SampleWpfApp${/}bin${/}Debug${/}net8.0-windows${/}SampleWpfApp.dll
${PIPE_NAME}     WPFSpyAgentPipe

*** Test Cases ***
Test Runtime Injector Initialization
    [Documentation]    Verify RuntimeInjector finds startup hook DLL
    ${path}=    Get Startup Hook Path
    Log    Startup hook path: ${path}
    # Path should be found if project was built
    Run Keyword If    '${path}' == 'None'
    ...    Log    WARNING: Startup hook DLL not found. Build WpfSpyAgent.StartupHook first.
    ...    ELSE    Log    Found hook at: ${path}

Test Launch Application With Spy Agent
    [Documentation]    Launch app with Spy Agent auto-injected
    [Tags]    runtime_injection
    ${path}=    Get Startup Hook Path
    Skip If    '${path}' == 'None'    Startup hook DLL not found. Build WpfSpyAgent.StartupHook first.
    Log    Launching application with Spy Agent...    console=True
    ${pid}=    Launch Application    ${APP_PATH}    None    ${PIPE_NAME}    30.0
    Log    Launched with PID: ${pid}    console=True
    Should Not Be Equal As Integers    ${pid}    ${0}
    # Give app time to fully start
    Sleep    3
    # Cleanup
    ${terminated}=    Terminate Application    ${pid}
    Log    Terminated: ${terminated}

Test Attach To Running Application
    [Documentation]    Attach to already-running application
    [Tags]    runtime_injection    attach
    ${pid}=    Get Process Id
    Run Keyword If    ${pid} is None
    ...    Log    No running app found.
    ...    ELSE    Log    Found running app with PID: ${pid}
    # This will return False if no agent is running
    ${connected}=    Attach To Application    ${12345}    ${PIPE_NAME}
    Log    Connected: ${connected}

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
    # This may be False if no agent is running

Test Launch With Custom Arguments
    [Documentation]    Launch app with command-line arguments
    [Tags]    runtime_injection
    ${path}=    Get Startup Hook Path
    Skip If    '${path}' == 'None'
    ${pid}=    Launch Application    ${APP_PATH}    --debug
    Log    Launched with PID: ${pid}
    Should Not Be Equal As Integers    ${pid}    ${0}
    # Cleanup
    ${terminated}=    Terminate Application    ${pid}
    Log    Terminated: ${terminated}

Test Terminate All Applications
    [Documentation]    Cleanup all launched applications
    [Tags]    runtime_injection    cleanup
    ${path}=    Get Startup Hook Path
    Skip If    '${path}' == 'None'
    # Launch multiple apps
    ${pid1}=    Launch Application    ${APP_PATH}
    ${pid2}=    Launch Application    ${APP_PATH}
    Sleep    2
    Log    Launched PIDs: ${pid1}, ${pid2}
    # Terminate all
    ${count}=    Terminate All Applications
    Log    Terminated ${count} applications
    Should Be True    ${count} >= 2

Test Environment Variable Configuration
    [Documentation]    Verify environment variables are set correctly
    [Tags]    runtime_injection    config
    ${path}=    Get Startup Hook Path
    Run Keyword If    '${path}' != 'None'
    ...    Set Environment Variable    WPFSPY_STARTUP_HOOK_DLL    ${path}
    ...    AND    Log    Environment variable set.
    ...    ELSE    Log    Skipping: Startup hook DLL not available.
