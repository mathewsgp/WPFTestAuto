@echo on
setlocal enabledelayedexpansion

set "TARGET_APP=%~1"
set "CONFIGURATION=%~2"
set "RUN_IDE=%~3"
set "TARGET_VERSION=%~4"

if "%TARGET_APP%"=="" (
    echo ERROR: Target app path is required.
    echo Usage: build_and_run.bat ^<target_app_path^> [Configuration] [RunIde] [TargetVersion]
    pause
    exit /b 1
)

if "%CONFIGURATION%"=="" set "CONFIGURATION=Debug"
if "%RUN_IDE%"=="" set "RUN_IDE=false"

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
echo WPFSpy Build and Inject
echo ============================================================
echo Target App: %TARGET_APP%
echo Configuration: %CONFIGURATION%
echo Run IDE: %RUN_IDE%
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
echo Build and injection complete
echo ============================================================
echo.

echo Launching target app: %TARGET_APP%
echo.

set "APPDOMAIN_MANAGER_ASM=WpfSpyAgent.FrameworkHook, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
set "APPDOMAIN_MANAGER_TYPE=WpfSpyAgent.FrameworkHook.SpyAppDomainManager"

if /i "!TARGET_FW!"=="net8.0-windows" (
    echo Injection method: StartupHook via DOTNET_STARTUP_HOOKS
    echo   StartupHook: %TARGET_DIR%WpfSpyAgent.StartupHook.dll
    echo   WPFSPY_AGENT_ENABLED=1
) else (
    echo Injection method: Framework hook via environment variables
    echo   APPDOMAIN_MANAGER_ASM=!APPDOMAIN_MANAGER_ASM!
    echo   APPDOMAIN_MANAGER_TYPE=!APPDOMAIN_MANAGER_TYPE!
)

echo.
echo Waiting a few seconds for target app to initialize...
ping 127.0.0.1 -n 4 >nul

if /i "!TARGET_FW!"=="net8.0-windows" (
    set "DOTNET_STARTUP_HOOKS=%TARGET_DIR%WpfSpyAgent.StartupHook.dll"
    cmd.exe /c "set DOTNET_STARTUP_HOOKS=!DOTNET_STARTUP_HOOKS! && set WPFSPY_AGENT_ENABLED=1 && set WPFSPY_PIPE_NAME=WPFSpyAgentPipe && start "" /D "%TARGET_DIR%" "%TARGET_PATH%""
) else (
    cmd.exe /c "set APPDOMAIN_MANAGER_ASM=WpfSpyAgent.FrameworkHook, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null&& set APPDOMAIN_MANAGER_TYPE=WpfSpyAgent.FrameworkHook.SpyAppDomainManager&& set WPFSPY_PIPE_NAME=WPFSpyAgentPipe&& start "" /D "%TARGET_DIR%" "%TARGET_PATH%""
)

if /i "%RUN_IDE%"=="true" (
    echo.
    echo Launching WPF Test IDE...
    start "" /D "%FW_ROOT%" dotnet run --project "%IDE_PROJECT%" -f net8.0-windows -c %CONFIGURATION%
) else (
    echo.
    echo IDE launch skipped. To run IDE manually:
    echo   dotnet run --project "%IDE_PROJECT%" -f net8.0-windows -c %CONFIGURATION%
)

echo.
echo Batch file completed. Apps are running independently.
echo.

endlocal
exit /b 0

