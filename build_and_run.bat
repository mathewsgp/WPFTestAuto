@echo off
setlocal enabledelayedexpansion

set "TARGET_APP=%~1"
set "INJECTION_MODE=%~2"
set "CONFIGURATION=%~3"
set "RUN_IDE=%~4"
set "TARGET_VERSION=%~5"

if "%TARGET_APP%"=="" (
    echo ============================================================
    echo WPFTestAuto - Build and Run
    echo ============================================================
    echo.
    echo Usage: build_and_run.bat ^<target_app_path^> [InjectionMode] [Configuration] [RunIde] [TargetVersion]
    echo.
    echo Arguments:
    echo   target_app_path    Path to the WPF application executable ^(required^)
    echo   InjectionMode     Injection method: runtime ^(default^), launch, cooperative, attach
    echo   Configuration      Build config: Debug ^(default^) or Release
    echo   RunIde            Launch IDE: true or false ^(default: false^)
    echo   TargetVersion     .NET version: net ^(default^) or framework
    echo.
    echo Injection Modes:
    echo   runtime      Attach to running process via Windows Hook API ^(default^)
    echo   launch       Launch with Startup Hook injection ^(requires restart^)
    echo   cooperative  Cooperative hosting ^(requires app modification^)
    echo   attach       Attach to running app via named pipe ^(existing agent^)
    echo.
    echo Examples:
    echo   build_and_run.bat SampleWpfApp.exe
    echo   build_and_run.bat SampleWpfApp.exe launch
    echo   build_and_run.bat SampleWpfApp.exe runtime true net
    echo.
    pause
    exit /b 1
)

:: Set defaults
if "%INJECTION_MODE%"=="" set "INJECTION_MODE=runtime"
if "%CONFIGURATION%"=="" set "CONFIGURATION=Debug"
if "%RUN_IDE%"=="" set "RUN_IDE=false"

:: Validate injection mode
if /i "%INJECTION_MODE%"=="runtime" (
    set "INJECTION_DESC=Attach to running process via Windows Hook API"
) else if /i "%INJECTION_MODE%"=="launch" (
    set "INJECTION_DESC=Launch with Startup Hook ^(requires restart^)"
) else if /i "%INJECTION_MODE%"=="cooperative" (
    set "INJECTION_DESC=Cooperative hosting ^(requires app modification^)"
) else if /i "%INJECTION_MODE%"=="attach" (
    set "INJECTION_DESC=Attach to existing Spy Agent via named pipe"
) else (
    echo ERROR: Invalid injection mode: %INJECTION_MODE%
    echo Valid modes: runtime, launch, cooperative, attach
    pause
    exit /b 1
)

if /i "%TARGET_VERSION%"=="framework" (
    set "TARGET_FW=net461"
) else if /i "%TARGET_VERSION%"=="net" (
    set "TARGET_FW=net8.0-windows"
) else (
    echo ERROR: TargetVersion must be "net" or "framework".
    pause
    exit /b 1
)

echo ============================================================
echo WPFTestAuto - Build and Inject
echo ============================================================
echo Target App:     %TARGET_APP%
echo Injection Mode: %INJECTION_MODE%
echo Description:    %INJECTION_DESC%
echo Configuration:  %CONFIGURATION%
echo Run IDE:        %RUN_IDE%
echo Target Version: %TARGET_VERSION%
echo ============================================================
echo.

set "FW_ROOT=%~dp0"
set "IDE_PROJECT=%FW_ROOT%WpfTestIde\WpfTestIde.csproj"
set "AGENT_PROJECT=%FW_ROOT%WpfSpyAgent\WpfSpyAgent.csproj"
set "STARTUP_HOOK_PROJECT=%FW_ROOT%WpfSpyAgent.StartupHook\WpfSpyAgent.StartupHook.csproj"
set "FRAMEWORK_HOOK_PROJECT=%FW_ROOT%WpfSpyAgent.FrameworkHook\WpfSpyAgent.FrameworkHook.csproj"

set "TARGET_DIR=%~dp1"
set "TARGET_PATH=%~f1"

echo [1/5] Target framework: !TARGET_FW!
echo.

echo [2/5] Building WpfSpyAgent...
pushd "%FW_ROOT%"
dotnet build "%AGENT_PROJECT%" -c %CONFIGURATION% -f net8.0-windows
if errorlevel 1 (
    echo ERROR: Failed to build WpfSpyAgent for net8.0-windows
    popd
    pause
    exit /b 1
)
dotnet build "%AGENT_PROJECT%" -c %CONFIGURATION% -f net461
if errorlevel 1 (
    echo ERROR: Failed to build WpfSpyAgent for net461
    popd
    pause
    exit /b 1
)
popd
echo       WpfSpyAgent built successfully.
echo.

echo [3/5] Building injection hooks...
if /i "!TARGET_FW!"=="net8.0-windows" (
    dotnet build "%STARTUP_HOOK_PROJECT%" -c %CONFIGURATION%
    if errorlevel 1 (
        echo ERROR: Failed to build StartupHook
        pause
        exit /b 1
    )
    echo       StartupHook built for .NET 8.0+
) else (
    dotnet build "%FRAMEWORK_HOOK_PROJECT%" -c %CONFIGURATION%
    if errorlevel 1 (
        echo ERROR: Failed to build FrameworkHook
        pause
        exit /b 1
    )
    echo       FrameworkHook built for .NET Framework
)
echo.

echo [4/5] Injecting WpfSpyAgent into target app...
set "AGENT_SOURCE="
set "HOOK_SOURCE="
set "STARTUP_HOOK_SOURCE="
if /i "!TARGET_FW!"=="net461" (
    set "AGENT_SOURCE=%FW_ROOT%WpfSpyAgent\bin\%CONFIGURATION%\net461\WpfSpyAgent.dll"
    set "HOOK_SOURCE=%FW_ROOT%WpfSpyAgent.FrameworkHook\bin\%CONFIGURATION%\net461\WpfSpyAgent.FrameworkHook.dll"
) else (
    set "AGENT_SOURCE=%FW_ROOT%WpfSpyAgent\bin\%CONFIGURATION%\net8.0-windows\WpfSpyAgent.dll"
    set "STARTUP_HOOK_SOURCE=%FW_ROOT%WpfSpyAgent.StartupHook\bin\%CONFIGURATION%\net8.0-windows\WpfSpyAgent.StartupHook.dll"
)

if not exist "!AGENT_SOURCE!" (
    echo ERROR: Agent DLL not found at: !AGENT_SOURCE!
    pause
    exit /b 1
)

copy /Y "!AGENT_SOURCE!" "%TARGET_DIR%"
if errorlevel 1 (
    echo ERROR: Failed to copy agent DLL to target directory
    pause
    exit /b 1
)
echo       Copied: !AGENT_SOURCE!
echo       To:     %TARGET_DIR%

if defined HOOK_SOURCE (
    if not exist "!HOOK_SOURCE!" (
        echo ERROR: FrameworkHook DLL not found at: !HOOK_SOURCE!
        pause
        exit /b 1
    )
    copy /Y "!HOOK_SOURCE!" "%TARGET_DIR%"
    if errorlevel 1 (
        echo ERROR: Failed to copy FrameworkHook DLL to target directory
        pause
        exit /b 1
    )
    echo       Copied: !HOOK_SOURCE!
    echo       To:     %TARGET_DIR%
)

if defined STARTUP_HOOK_SOURCE (
    if not exist "!STARTUP_HOOK_SOURCE!" (
        echo ERROR: StartupHook DLL not found at: !STARTUP_HOOK_SOURCE!
        pause
        exit /b 1
    )
    copy /Y "!STARTUP_HOOK_SOURCE!" "%TARGET_DIR%"
    if errorlevel 1 (
        echo ERROR: Failed to copy StartupHook DLL to target directory
        pause
        exit /b 1
    )
    echo       Copied: !STARTUP_HOOK_SOURCE!
    echo       To:     %TARGET_DIR%
)
echo.

echo [5/5] Building WPF Test IDE...
dotnet build "%IDE_PROJECT%" -c %CONFIGURATION%
if errorlevel 1 (
    echo ERROR: Failed to build IDE
    pause
    exit /b 1
)
echo       IDE built successfully.
echo.

echo ============================================================
echo Build complete - Ready for injection
echo ============================================================
echo.

:: ============================================================
:: Handle injection mode
:: ============================================================

if /i "%INJECTION_MODE%"=="runtime" (
    echo [MODE: RUNTIME - Attach to running process]
    echo.
    echo Starting target app WITHOUT Spy Agent...
    echo The Spy Agent will be injected via Windows Hook API when you use the IDE.
    echo.
    start "" /D "%TARGET_DIR%" "%TARGET_PATH%"
    echo App launched. Now use the IDE to attach to the running process.
    echo.
)

if /i "%INJECTION_MODE%"=="launch" (
    echo [MODE: LAUNCH - Startup Hook injection]
    echo.
    if /i "!TARGET_FW!"=="net8.0-windows" (
        echo Injecting via DOTNET_STARTUP_HOOKS...
        echo   StartupHook: %TARGET_DIR%WpfSpyAgent.StartupHook.dll
        echo   WPFSPY_AGENT_ENABLED=1
        echo.
        set "DOTNET_STARTUP_HOOKS=%TARGET_DIR%WpfSpyAgent.StartupHook.dll"
        cmd.exe /c "set DOTNET_STARTUP_HOOKS=!DOTNET_STARTUP_HOOKS! && set WPFSPY_AGENT_ENABLED=1 && set WPFSPY_PIPE_NAME=WPFSpyAgentPipe && start "" /D "%TARGET_DIR%" "%TARGET_PATH%""
    ) else (
        echo Injecting via APPDOMAIN_MANAGER...
        echo   APPDOMAIN_MANAGER_ASM=WpfSpyAgent.FrameworkHook
        echo   APPDOMAIN_MANAGER_TYPE=WpfSpyAgent.FrameworkHook.SpyAppDomainManager
        echo.
        cmd.exe /c "set APPDOMAIN_MANAGER_ASM=WpfSpyAgent.FrameworkHook, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null&& set APPDOMAIN_MANAGER_TYPE=WpfSpyAgent.FrameworkHook.SpyAppDomainManager&& set WPFSPY_PIPE_NAME=WPFSpyAgentPipe&& start "" /D "%TARGET_DIR%" "%TARGET_PATH%""
    )
)

if /i "%INJECTION_MODE%"=="cooperative" (
    echo [MODE: COOPERATIVE - App hosts Spy Agent]
    echo.
    echo This mode requires the app to be built with Spy Agent host code.
    echo The app should call SpyAgentHost.Start^(^) during initialization.
    echo.
    echo If your app is already configured for cooperative hosting, starting it now...
    start "" /D "%TARGET_DIR%" "%TARGET_PATH%"
    echo.
    echo If not configured, modify your app to call:
    echo   WpfSpyAgent.SpyAgentHost.Start^("WPFSpyAgentPipe"^);
)

if /i "%INJECTION_MODE%"=="attach" (
    echo [MODE: ATTACH - Connect to existing agent]
    echo.
    echo Starting target app WITHOUT Spy Agent...
    echo After the app starts, use the IDE to attach to the existing Spy Agent.
    echo.
    start "" /D "%TARGET_DIR%" "%TARGET_PATH%"
    echo.
    echo Wait for the app to start, then use the IDE's Attach feature
    echo to connect to the running process.
)

echo.
if /i "%RUN_IDE%"=="true" (
    echo Launching WPF Test IDE...
    echo.
    if /i "%INJECTION_MODE%"=="attach" (
        echo The IDE will open with the Attach dialog.
    )
    start "" /D "%FW_ROOT%" dotnet run --project "%IDE_PROJECT%" -f net8.0-windows -c %CONFIGURATION%
) else (
    echo.
    echo IDE launch skipped. To run IDE manually:
    echo   dotnet run --project "%IDE_PROJECT%" -f net8.0-windows -c %CONFIGURATION%
    echo.
    echo To use runtime injection with the IDE:
    echo   1. Start the app first: "%TARGET_PATH%"
    echo   2. Then launch IDE and use Attach to Running Process
)

echo.
echo ============================================================
echo Ready
echo ============================================================
echo.

endlocal
exit /b 0

