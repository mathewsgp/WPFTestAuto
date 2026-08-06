@echo off
setlocal enabledelayedexpansion

set "FW_ROOT=%~dp0"

set "ARG1=%~1"
set "ARG2=%~2"
set "ARG3=%~3"
set "ARG4=%~4"
set "ARG5=%~5"

:: Initialize all variables
set "TARGET_APP="
set "INJECTION_MODE="
set "CONFIGURATION="
set "RUN_IDE="
set "TARGET_VERSION="

:: Check all arguments and identify each by value
:: Order doesn't matter - identified by content

:: Detect .exe in any position (take first one found)
:: Only check non-empty arguments that end with .exe
if not "%ARG1%"=="" (
    if "%ARG1:~-4%"==".exe" set "TARGET_APP=%ARG1%"
)
if not "%ARG2%"=="" (
    if "%ARG2:~-4%"==".exe" (
        if "%TARGET_APP%"=="" set "TARGET_APP=%ARG2%"
    )
)
if not "%ARG3%"=="" (
    if "%ARG3:~-4%"==".exe" (
        if "%TARGET_APP%"=="" set "TARGET_APP=%ARG3%"
    )
)
if not "%ARG4%"=="" (
    if "%ARG4:~-4%"==".exe" (
        if "%TARGET_APP%"=="" set "TARGET_APP=%ARG4%"
    )
)
if not "%ARG5%"=="" (
    if "%ARG5:~-4%"==".exe" (
        if "%TARGET_APP%"=="" set "TARGET_APP=%ARG5%"
    )
)

:: Check if first arg is an injection mode
if /i "%ARG1%"=="runtime" set "INJECTION_MODE=%ARG1%"
if /i "%ARG1%"=="launch" set "INJECTION_MODE=%ARG1%"
if /i "%ARG1%"=="cooperative" set "INJECTION_MODE=%ARG1%"
if /i "%ARG1%"=="attach" set "INJECTION_MODE=%ARG1%"
if /i "%ARG1%"=="debug" set "CONFIGURATION=Debug"
if /i "%ARG1%"=="release" set "CONFIGURATION=Release"
if /i "%ARG1%"=="net" set "TARGET_VERSION=net"
if /i "%ARG1%"=="framework" set "TARGET_VERSION=framework"
if /i "%ARG1%"=="true" set "RUN_IDE=true"
if /i "%ARG1%"=="false" set "RUN_IDE=false"

:: Check other args too (in case .exe is first)
if /i "%ARG2%"=="runtime" set "INJECTION_MODE=%ARG2%"
if /i "%ARG2%"=="launch" set "INJECTION_MODE=%ARG2%"
if /i "%ARG2%"=="cooperative" set "INJECTION_MODE=%ARG2%"
if /i "%ARG2%"=="attach" set "INJECTION_MODE=%ARG2%"
if /i "%ARG2%"=="debug" set "CONFIGURATION=Debug"
if /i "%ARG2%"=="release" set "CONFIGURATION=Release"
if /i "%ARG2%"=="net" set "TARGET_VERSION=net"
if /i "%ARG2%"=="framework" set "TARGET_VERSION=framework"
if /i "%ARG2%"=="true" set "RUN_IDE=true"
if /i "%ARG2%"=="false" set "RUN_IDE=false"

if /i "%ARG3%"=="runtime" set "INJECTION_MODE=%ARG3%"
if /i "%ARG3%"=="launch" set "INJECTION_MODE=%ARG3%"
if /i "%ARG3%"=="cooperative" set "INJECTION_MODE=%ARG3%"
if /i "%ARG3%"=="attach" set "INJECTION_MODE=%ARG3%"
if /i "%ARG3%"=="debug" set "CONFIGURATION=Debug"
if /i "%ARG3%"=="release" set "CONFIGURATION=Release"
if /i "%ARG3%"=="net" set "TARGET_VERSION=net"
if /i "%ARG3%"=="framework" set "TARGET_VERSION=framework"
if /i "%ARG3%"=="true" set "RUN_IDE=true"
if /i "%ARG3%"=="false" set "RUN_IDE=false"

if /i "%ARG4%"=="runtime" set "INJECTION_MODE=%ARG4%"
if /i "%ARG4%"=="launch" set "INJECTION_MODE=%ARG4%"
if /i "%ARG4%"=="cooperative" set "INJECTION_MODE=%ARG4%"
if /i "%ARG4%"=="attach" set "INJECTION_MODE=%ARG4%"
if /i "%ARG4%"=="debug" set "CONFIGURATION=Debug"
if /i "%ARG4%"=="release" set "CONFIGURATION=Release"
if /i "%ARG4%"=="net" set "TARGET_VERSION=net"
if /i "%ARG4%"=="framework" set "TARGET_VERSION=framework"
if /i "%ARG4%"=="true" set "RUN_IDE=true"
if /i "%ARG4%"=="false" set "RUN_IDE=false"

if /i "%ARG5%"=="runtime" set "INJECTION_MODE=%ARG5%"
if /i "%ARG5%"=="launch" set "INJECTION_MODE=%ARG5%"
if /i "%ARG5%"=="cooperative" set "INJECTION_MODE=%ARG5%"
if /i "%ARG5%"=="attach" set "INJECTION_MODE=%ARG5%"
if /i "%ARG5%"=="debug" set "CONFIGURATION=Debug"
if /i "%ARG5%"=="release" set "CONFIGURATION=Release"
if /i "%ARG5%"=="net" set "TARGET_VERSION=net"
if /i "%ARG5%"=="framework" set "TARGET_VERSION=framework"
if /i "%ARG5%"=="true" set "RUN_IDE=true"
if /i "%ARG5%"=="false" set "RUN_IDE=false"

:: Set defaults
if "%INJECTION_MODE%"=="" set "INJECTION_MODE=launch"
if "%CONFIGURATION%"=="" set "CONFIGURATION=Debug"
if "%RUN_IDE%"=="" set "RUN_IDE=false"
if "%TARGET_VERSION%"=="" set "TARGET_VERSION=net"

:: Validate injection mode
if /i "%INJECTION_MODE%"=="runtime" (
    set "INJECTION_DESC=Inject DLL into running process ^(requires NativeInject.dll^)"
) else if /i "%INJECTION_MODE%"=="launch" (
    set "INJECTION_DESC=Launch with Startup Hook injection ^(default^)"
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
    set "SAMPLE_APP_DIR=%FW_ROOT%SampleWpfApp\bin\%CONFIGURATION%\net461"
) else (
    set "TARGET_FW=net8.0-windows"
    set "SAMPLE_APP_DIR=%FW_ROOT%SampleWpfApp\bin\%CONFIGURATION%\net8.0-windows"
)

:: If no target app specified, use SampleWpfApp
if "%TARGET_APP%"=="" (
    set "TARGET_APP=SampleWpfApp.exe"
    set "TARGET_DIR=%SAMPLE_APP_DIR%"
    set "TARGET_PATH=%SAMPLE_APP_DIR%\SampleWpfApp.exe"
    echo ============================================================
    echo WPFTestAuto - Auto-detecting SampleWpfApp
    echo ============================================================
    echo Configuration: %CONFIGURATION%
    echo Target Version: %TARGET_VERSION%
    echo Target Framework: %TARGET_FW%
    echo Auto-detected path: %TARGET_PATH%
    echo ============================================================
) else (
    :: A custom .exe was provided - find its path
    :: Find .exe in any argument and get its path
    set "TARGET_DIR="
    set "TARGET_PATH="
    
    if not "%ARG1%"=="" (
        if "%ARG1:~-4%"==".exe" (
            set "TARGET_DIR=%~dp1"
            set "TARGET_PATH=%~f1"
        )
    )
    if not "%ARG2%"=="" (
        if "%ARG2:~-4%"==".exe" (
            set "TARGET_DIR=%~dp2"
            set "TARGET_PATH=%~f2"
        )
    )
    if not "%ARG3%"=="" (
        if "%ARG3:~-4%"==".exe" (
            set "TARGET_DIR=%~dp3"
            set "TARGET_PATH=%~f3"
        )
    )
    if not "%ARG4%"=="" (
        if "%ARG4:~-4%"==".exe" (
            set "TARGET_DIR=%~dp4"
            set "TARGET_PATH=%~f4"
        )
    )
    if not "%ARG5%"=="" (
        if "%ARG5:~-4%"==".exe" (
            set "TARGET_DIR=%~dp5"
            set "TARGET_PATH=%~f5"
        )
    )
    
    echo ============================================================
    echo WPFTestAuto - Build and Inject
    echo ============================================================
)

echo Target App:     %TARGET_APP%
echo Injection Mode: %INJECTION_MODE%
echo Description:    %INJECTION_DESC%
echo Configuration:  %CONFIGURATION%
echo Run IDE:        %RUN_IDE%
echo Target Version: %TARGET_VERSION%
echo ============================================================
echo.

set "IDE_PROJECT=%FW_ROOT%WpfTestIde\WpfTestIde.csproj"
set "AGENT_PROJECT=%FW_ROOT%WpfSpyAgent\WpfSpyAgent.csproj"
set "STARTUP_HOOK_PROJECT=%FW_ROOT%WpfSpyAgent.StartupHook\WpfSpyAgent.StartupHook.csproj"
set "FRAMEWORK_HOOK_PROJECT=%FW_ROOT%WpfSpyAgent.FrameworkHook\WpfSpyAgent.FrameworkHook.csproj"
set "NATIVE_INJECT_PROJECT=%FW_ROOT%WpfSpyAgent.NativeInject\WpfSpyAgent.NativeInject.vcxproj"
set "SAMPLE_APP_PROJECT=%FW_ROOT%SampleWpfApp\SampleWpfApp.csproj"

echo [1/5] Target framework: !TARGET_FW!
echo.

:: Build SampleWpfApp if using auto-detected path
if /i "%TARGET_APP%"=="SampleWpfApp.exe" (
    echo [Building SampleWpfApp...]
    if /i "!TARGET_FW!"=="net461" (
        dotnet build "!SAMPLE_APP_PROJECT!" -c %CONFIGURATION% -f net461
        if errorlevel 1 (
            echo ERROR: Failed to build SampleWpfApp for net461
            pause
            exit /b 1
        )
    ) else (
        dotnet build "!SAMPLE_APP_PROJECT!" -c %CONFIGURATION% -f net8.0-windows
        if errorlevel 1 (
            echo ERROR: Failed to build SampleWpfApp for net8.0-windows
            pause
            exit /b 1
        )
    )
    echo       SampleWpfApp built successfully.
    echo.
)

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

:: Build native injection DLL for runtime attach
echo [Building Native Inject DLL for runtime injection...]
if exist "%NATIVE_INJECT_PROJECT%" (
    :: Detect which solution/project to build based on installed VS version
    :: Default to VS 2022 (v143). Override with VS2026 environment variable.
    if "%VS2026%"=="1" (
        set "NATIVE_INJECT_PROJECT=%FW_ROOT%WpfSpyAgent.NativeInject\WpfSpyAgent.NativeInject.VS2026.vcxproj"
        echo       Building for VS 2026 ^(v145^)...
    ) else (
        set "NATIVE_INJECT_PROJECT=%FW_ROOT%WpfSpyAgent.NativeInject\WpfSpyAgent.NativeInject.VS2022.vcxproj"
        echo       Building for VS 2022 ^(v143^)...
    )
    
    msbuild "%NATIVE_INJECT_PROJECT%" /p:Configuration=%CONFIGURATION% /p:Platform=x64 /t:Build /v:minimal
    if errorlevel 1 (
        echo WARNING: Failed to build NativeInject DLL ^(runtime injection may not work^)
        echo       Make sure you have C++ workload installed in Visual Studio.
        if "%VS2026%"=="" (
            echo       For VS 2026: set VS2026=1 before running this script
        )
    ) else (
        echo       NativeInject DLL built for runtime injection.
        echo       Output: bin\%CONFIGURATION%\x64\WpfSpyAgent.NativeInject.dll
    )
) else (
    echo       NativeInject project not found ^(skip runtime injection setup^)
    echo       Use WpfTestFramework.VS2022.sln or WpfTestFramework.VS2026.sln instead.
)
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

echo [4/5] Copying DLLs to output directories...
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

echo [5/5] Building WPF Test IDE and copying native DLL...
dotnet build "%IDE_PROJECT%" -c %CONFIGURATION%
if errorlevel 1 (
    echo ERROR: Failed to build IDE
    pause
    exit /b 1
)
echo       IDE built successfully.
echo.

:: Copy native injection DLL to IDE output for runtime attach feature
echo.
echo [Copying DLLs for runtime injection...]
set "NATIVE_DLL_FOUND="

:: Source: bin\%CONFIGURATION%\x64\ (C++ project output)
:: Target: WpfTestIde\bin\%CONFIGURATION%\net8.0-windows\ (IDE directory)
if exist "%FW_ROOT%bin\%CONFIGURATION%\x64\WpfSpyAgent.NativeInject.dll" (
    copy /Y "%FW_ROOT%bin\%CONFIGURATION%\x64\WpfSpyAgent.NativeInject.dll" "%FW_ROOT%WpfTestIde\bin\%CONFIGURATION%\net8.0-windows\" >nul
    echo       Copied NativeInject.dll to IDE directory.
    set "NATIVE_DLL_FOUND=1"
)

if not defined NATIVE_DLL_FOUND (
    echo       WARNING: NativeInject.dll not found.
    echo       Build WpfSpyAgent.NativeInject project in Visual Studio first.
)
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
    echo DEBUG: TARGET_DIR = %TARGET_DIR%
    echo DEBUG: TARGET_PATH = %TARGET_PATH%
    echo.
    if not exist "%TARGET_PATH%" (
        echo ERROR: Application not found at: %TARGET_PATH%
        pause
        exit /b 1
    )
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

