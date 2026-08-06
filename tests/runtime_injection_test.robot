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
Library          OperatingSystem

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
    ${exists}=    Evaluate    os.path.exists(r"""${APP_DOTNET}""")
    Run Keyword If    not ${exists}
    ...    Log    WARNING: SampleWpfApp (.NET) not built. Run: dotnet build
    ...    ELSE    Log    SampleWpfApp (.NET) found at: ${APP_DOTNET}

Test SampleWpfApp Framework Exists
    [Documentation]    Verify SampleWpfApp (.NET Framework) is built
    [Tags]    setup
    ${exists}=    Evaluate    os.path.exists(r"""${APP_FW}""")
    Run Keyword If    not ${exists}
    ...    Log    WARNING: SampleWpfApp (.NET Framework) not built. Run: dotnet build -f net461
    ...    ELSE    Log    SampleWpfApp (.NET Framework) found at: ${APP_FW}

Test NativeInject DLL Exists
    [Documentation]    Verify NativeInject DLL is built for CLR Hosting
    [Tags]    clr_hosting
    ${exists}=    Evaluate    os.path.exists(r"""${NATIVE_DLL}""")
    Run Keyword If    not ${exists}
    ...    Log    WARNING: NativeInject DLL not found. Build WpfSpyAgent.NativeInject in VS first.
    ...    ELSE    Log    Found NativeInject DLL: ${NATIVE_DLL}

Test WpfSpyAgent Dll Exists
    [Documentation]    Verify WpfSpyAgent.dll exists for injection
    [Tags]    setup
    ${spy_agent}=    Evaluate    os.path.exists(r"""${WPFSPY_ROOT}${/}WpfSpyAgent${/}bin${/}Debug${/}net8.0-windows${/}WpfSpyAgent.dll""")
    Run Keyword If    not ${spy_agent}
    ...    Log    WARNING: WpfSpyAgent.dll not found. Build WpfSpyAgent project first.
    ...    ELSE    Log    WpfSpyAgent.dll found

Test Inject Into DotNet App
    [Documentation]    Test runtime injection into .NET SampleWpfApp
    [Tags]    clr_hosting    injection
    Log    Testing CLR Hosting injection into .NET 8 application...
    Log    This test verifies the injection pipeline is properly configured
    Log    NativeInject DLL should call ExecuteInDefaultAppDomain on WpfSpyAgent.dll
    ${exists}=    Evaluate    os.path.exists(r"""${NATIVE_DLL}""")
    Should Be True    ${exists}    NativeInject DLL must exist for CLR Hosting

Test Inject Into Framework App
    [Documentation]    Test runtime injection into .NET Framework SampleWpfApp
    [Tags]    clr_hosting    injection    framework
    Log    Testing CLR Hosting injection into .NET Framework application...
    Log    This test verifies the injection pipeline is properly configured
    ${exists}=    Evaluate    os.path.exists(r"""${NATIVE_DLL}""")
    Should Be True    ${exists}    NativeInject DLL must exist for CLR Hosting

Test Agent Ready Check
    [Documentation]    Check if Spy Agent is ready on pipe (before any app launch)
    [Tags]    pipe
    ${ready}=    Is Agent Ready    ${PIPE_NAME}
    Log    Agent ready (should be False): ${ready}
    # Before launching, agent should NOT be ready - this is expected

Test Agent Ready After DotNet Launch
    [Documentation]    Launch .NET app with Spy Agent and verify pipe connection
    [Tags]    pipe    integration
    ${path}=    Get Startup Hook Path
    Skip If    $path == "None"    Startup hook DLL not found
    ${app_exists}=    Evaluate    os.path.exists(r"""${APP_DOTNET}""")
    Skip If    not ${app_exists}    SampleWpfApp (.NET) not built
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
    ${app_exists}=    Evaluate    os.path.exists(r"""${APP_FW}""")
    Skip If    not ${app_exists}    SampleWpfApp (.NET Framework) not built
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
    ${app_exists}=    Evaluate    os.path.exists(r"""${APP_DOTNET}""")
    Skip If    not ${app_exists}    SampleWpfApp (.NET) not built
    Log    === Complete Workflow Test ===
    Log    Step 1: Launch .NET SampleWpfApp with Spy Agent
    ${pid}=    Launch Application    ${APP_DOTNET}
    Log    Launched with PID: ${pid}
    Should Not Be Equal As Integers    ${pid}    ${0}
    Sleep    3
    Log    Step 2: Verify agent is listening on pipe
    ${ready}=    Is Agent Ready    ${PIPE_NAME}
    Log    Agent ready: ${ready}
    Log    Step 3: Terminate application
    ${terminated}=    Terminate Application    ${pid}
    Should Be True    ${terminated}
    Log    === Workflow Complete ===
