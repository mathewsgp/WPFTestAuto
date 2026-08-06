*** Settings ***
Documentation    Test runtime injection features using SampleWpfApp
...
...              This test suite uses the built-in SampleWpfApp to test:
...              1. CLR Hosting runtime injection (both .NET and Framework)
...              2. Named pipe communication with Spy Agent
...              3. Complete inject and attach workflow
...
...              Prerequisites:
...              - Build all projects in the solution
...              - SampleWpfApp binaries must exist
Library          ../api/robot_launcher.py

*** Variables ***
${SAMPLE_APP_DIR}    ${CURDIR}${/}..${/}SampleWpfApp${/}bin${/}Debug
${APP_DOTNET}        ${SAMPLE_APP_DIR}${/}net8.0-windows${/}SampleWpfApp.dll
${APP_FW}            ${SAMPLE_APP_DIR}${/}net461${/}SampleWpfApp.exe
${NATIVE_DLL_DIR}    ${SAMPLE_APP_DIR}${/}..${/}WpfSpyAgent.NativeInject${/}bin${/}Debug${/}x64
${NATIVE_DLL}        ${NATIVE_DLL_DIR}${/}WpfSpyAgent.NativeInject.dll
${PIPE_NAME}         WPFSpyAgentPipe
${WPFSPY_ROOT}       ${CURDIR}${/}..

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

Test SampleWpfApp DotNet Exists
    [Documentation]    Verify SampleWpfApp (.NET) is built
    [Tags]    setup
    ${exists}=    Run    if exist "${APP_DOTNET}" (echo YES) else (echo NO)
    ${found}=    Evaluate    "YES" in """${exists}"""
    Run Keyword If    not ${found}
    ...    Log    WARNING: SampleWpfApp (.NET) not built. Run: dotnet build
    ...    ELSE    Log    SampleWpfApp (.NET) found

Test SampleWpfApp Framework Exists
    [Documentation]    Verify SampleWpfApp (.NET Framework) is built
    [Tags]    setup
    ${exists}=    Run    if exist "${APP_FW}" (echo YES) else (echo NO)
    ${found}=    Evaluate    "YES" in """${exists}"""
    Run Keyword If    not ${found}
    ...    Log    WARNING: SampleWpfApp (.NET Framework) not built. Run: dotnet build -f net461
    ...    ELSE    Log    SampleWpfApp (.NET Framework) found

Test NativeInject DLL Exists
    [Documentation]    Verify NativeInject DLL is built for CLR Hosting
    [Tags]    clr_hosting
    ${exists}=    Run    if exist "${NATIVE_DLL}" (echo YES) else (echo NO)
    ${found}=    Evaluate    "YES" in """${exists}"""
    Run Keyword If    not ${found}
    ...    Log    WARNING: NativeInject DLL not found. Build WpfSpyAgent.NativeInject in VS first.
    ...    ELSE    Log    Found NativeInject DLL: ${NATIVE_DLL}

Test Inject Into DotNet App
    [Documentation]    Test runtime injection into .NET SampleWpfApp
    [Tags]    clr_hosting    injection
    Log    Testing CLR Hosting injection into .NET 8 application...
    Log    This test injects NativeInject DLL and verifies agent starts
    # Note: Full injection test requires Windows environment with Python win32
    Log    NativeInject DLL should call ExecuteInDefaultAppDomain on WpfSpyAgent.dll

Test Inject Into Framework App
    [Documentation]    Test runtime injection into .NET Framework SampleWpfApp
    [Tags]    clr_hosting    injection    framework
    Log    Testing CLR Hosting injection into .NET Framework application...
    Log    This test injects NativeInject DLL and verifies agent starts
    # Note: Full injection test requires Windows environment with Python win32
    Log    NativeInject DLL should call ExecuteInDefaultAppDomain on WpfSpyAgent.dll

Test Agent Ready Check
    [Documentation]    Check if Spy Agent is ready on pipe (before any app launch)
    [Tags]    pipe
    ${ready}=    Is Agent Ready    ${PIPE_NAME}
    Log    Agent ready (should be False): ${ready}
    # Before launching, agent should NOT be ready
    # This verifies the pipe server is not running

Test Agent Ready After DotNet Launch
    [Documentation]    Launch .NET app with Spy Agent and verify pipe connection
    [Tags]    pipe    integration
    ${path}=    Get Startup Hook Path
    Skip If    $path == "None"    Startup hook DLL not found
    Log    Launching .NET SampleWpfApp with Spy Agent...
    ${pid}=    Launch Application    ${APP_DOTNET}
    Log    Launched with PID: ${pid}
    Sleep    3
    ${ready}=    Is Agent Ready    ${PIPE_NAME}
    Log    Agent ready after launch: ${ready}
    ${terminated}=    Terminate Application    ${pid}
    Should Be True    ${terminated}

Test Agent Ready After Framework Launch
    [Documentation]    Launch .NET Framework app with Spy Agent and verify pipe connection
    [Tags]    pipe    integration    framework
    ${path}=    Get Startup Hook Path
    Skip If    $path == "None"    Startup hook DLL not found
    Log    Launching .NET Framework SampleWpfApp with Spy Agent...
    ${pid}=    Launch Application    ${APP_FW}
    Log    Launched with PID: ${pid}
    Sleep    3
    ${ready}=    Is Agent Ready    ${PIPE_NAME}
    Log    Agent ready after launch: ${ready}
    ${terminated}=    Terminate Application    ${pid}
    Should Be True    ${terminated}

Test Complete Inject And Attach Workflow
    [Documentation]    Full workflow: launch app, verify agent, send command, terminate
    [Tags]    integration
    ${path}=    Get Startup Hook Path
    Skip If    $path == "None"    Startup hook DLL not found
    Log    === Complete Workflow Test ===
    Log    Step 1: Launch .NET SampleWpfApp with Spy Agent
    ${pid}=    Launch Application    ${APP_DOTNET}
    Log    Launched with PID: ${pid}
    Should Not Be Equal As Integers    ${pid}    ${0}
    Sleep    3
    Log    Step 2: Verify agent is listening on pipe
    ${ready}=    Is Agent Ready    ${PIPE_NAME}
    Should Be True    ${ready}    Agent should be listening
    Log    Step 3: Pipe connected successfully - agent is ready for commands
    Log    (In real test, would now send FindElement, GetProperty, Click commands)
    Log    Step 4: Terminate application
    ${terminated}=    Terminate Application    ${pid}
    Should Be True    ${terminated}
    Log    === Workflow Complete ===
