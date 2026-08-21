@echo off
setlocal enabledelayedexpansion

set "FW_ROOT=%~dp0"

:: Parse arguments simply
set "INJECTION_MODE=runtime"
set "CONFIGURATION=Debug"
set "RUN_IDE=true"
set "TARGET_VERSION=framework"

:parse_args
if "%~1"=="" goto done_args
set "arg=%~1"
if /i "%arg%"=="runtime" set "INJECTION_MODE=runtime"
if /i "%arg%"=="launch" set "INJECTION_MODE=launch"
if /i "%arg%"=="attach" set "INJECTION_MODE=attach"
if /i "%arg%"=="debug" set "CONFIGURATION=Debug"
if /i "%arg%"=="release" set "CONFIGURATION=Release"
if /i "%arg%"=="net" set "TARGET_VERSION=net"
if /i "%arg%"=="framework" set "TARGET_VERSION=framework"
if /i "%arg%"=="true" set "RUN_IDE=true"
if /i "%arg%"=="false" set "RUN_IDE=false"
shift
goto parse_args

:done_args
set "TARGET_FW=net9.0-windows"
set "SAMPLE_APP_DIR=%FW_ROOT%\bin\%CONFIGURATION%\net9.0-windows"
if /i "%TARGET_VERSION%"=="framework" (
    set "TARGET_FW=net461"
    set "SAMPLE_APP_DIR=%FW_ROOT%\bin\%CONFIGURATION%\net461"
)
set "TARGET_PATH=%SAMPLE_APP_DIR%\SampleWpfApp.exe"

echo ============================================================
echo WPFTestAuto - Build and Run
echo ============================================================
echo Configuration: %CONFIGURATION%
echo Target Version: %TARGET_VERSION%
echo Injection Mode: %INJECTION_MODE%
echo Run IDE: %RUN_IDE%
echo ============================================================

set "AGENT_PROJECT=%FW_ROOT%WpfSpyAgent\WpfSpyAgent.csproj"
set "SAMPLE_APP_PROJECT=%FW_ROOT%SampleWpfApp\SampleWpfApp.csproj"
set "STARTUP_HOOK_PROJECT=%FW_ROOT%WpfSpyAgent.StartupHook\WpfSpyAgent.StartupHook.csproj"
set "FRAMEWORK_HOOK_PROJECT=%FW_ROOT%WpfSpyAgent.FrameworkHook\WpfSpyAgent.FrameworkHook.csproj"
set "IDE_PROJECT=%FW_ROOT%WpfTestIde\WpfTestIde.csproj"

echo.
echo [1/5] Building WpfSpyAgent...
dotnet build "%AGENT_PROJECT%" -c %CONFIGURATION% -f net9.0-windows
if errorlevel 1 (
    echo ERROR: Failed to build WpfSpyAgent ^(net9.0-windows^)
    pause
    exit /b 1
)
dotnet build "%AGENT_PROJECT%" -c %CONFIGURATION% -f net461
if errorlevel 1 (
    echo WARNING: Failed to build WpfSpyAgent ^(net461^) - may be OK
)

echo.
echo [2/5] Building SampleWpfApp...
dotnet build "%SAMPLE_APP_PROJECT%" -c %CONFIGURATION% -f %TARGET_FW%
if errorlevel 1 (
    echo ERROR: Failed to build SampleWpfApp
    pause
    exit /b 1
)

echo.
echo [3/5] Building injection hook...
if /i "%TARGET_FW%"=="net461" (
    dotnet build "%FRAMEWORK_HOOK_PROJECT%" -c %CONFIGURATION%
    set "HOOK_DIR=%FW_ROOT%\bin\%CONFIGURATION%\net461"
) else (
    dotnet build "%STARTUP_HOOK_PROJECT%" -c %CONFIGURATION%
    set "HOOK_DIR=%FW_ROOT%\bin\%CONFIGURATION%\net9.0-windows"
)

echo.
echo [4/5] Building NativeInject C++ DLL...
:: Find MSBuild for C++ project and select the correct vcxproj
:: Note: VS2026 = internal version 18, VS2022 = internal version 17
set "MSBUILD_VS="
set "NATIVE_VCPROJ=%FW_ROOT%WpfSpyAgent.NativeInject\WpfSpyAgent.NativeInject.VS2022.vcxproj"
if exist "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set "MSBUILD_VS=C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
    set "NATIVE_VCPROJ=%FW_ROOT%WpfSpyAgent.NativeInject\WpfSpyAgent.NativeInject.VS2026.vcxproj"
) else if exist "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" (
    set "MSBUILD_VS=C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"
) else if exist "C:\Program Files (x86)\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set "MSBUILD_VS=C:\Program Files (x86)\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
    set "NATIVE_VCPROJ=%FW_ROOT%WpfSpyAgent.NativeInject\WpfSpyAgent.NativeInject.VS2026.vcxproj"
) else if exist "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set "MSBUILD_VS=C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
) else if exist "C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" (
    set "MSBUILD_VS=C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
)
if defined MSBUILD_VS (
    "!MSBUILD_VS!" "!NATIVE_VCPROJ!" /p:Configuration=%CONFIGURATION% /p:Platform=x64 /t:Build /v:minimal
    if not errorlevel 1 (
        echo       NativeInject C++ DLL built successfully.
        set "NATIVE_BUILT=1"
    ) else (
        echo       WARNING: NativeInject build failed - runtime injection may not work.
    )
) else (
    echo       WARNING: VS MSBuild not found - NativeInject not built.
    echo       Build WpfSpyAgent.NativeInject manually in Visual Studio.
)

echo.
echo [5/5] Copying DLLs...
copy /Y "%FW_ROOT%\bin\%CONFIGURATION%\%TARGET_FW%\WpfSpyAgent.dll" "%SAMPLE_APP_DIR%\" >nul
if /i "%TARGET_FW%"=="net461" (
    copy /Y "%HOOK_DIR%\WpfSpyAgent.FrameworkHook.dll" "%SAMPLE_APP_DIR%\" >nul
) else (
    copy /Y "%HOOK_DIR%\WpfSpyAgent.StartupHook.dll" "%SAMPLE_APP_DIR%\" >nul
)
if defined NATIVE_BUILT (
    copy /Y "%FW_ROOT%\WpfSpyAgent.NativeInject\bin\%CONFIGURATION%\x64\WpfSpyAgent.NativeInject.dll" "%SAMPLE_APP_DIR%\" >nul
    copy /Y "%FW_ROOT%\WpfSpyAgent.NativeInject\bin\%CONFIGURATION%\x64\WpfSpyAgent.NativeInject.dll" "%FW_ROOT%\bin\%CONFIGURATION%\net9.0-windows\" >nul 2>nul
)

echo.
echo [6/6] Building IDE...
dotnet build "%IDE_PROJECT%" -c %CONFIGURATION%
if errorlevel 1 (
    echo ERROR: Failed to build IDE
    pause
    exit /b 1
)

echo.
echo ============================================================
echo Build complete - Ready
echo ============================================================

:: Launch app and/or IDE based on mode
if /i "%INJECTION_MODE%"=="runtime" (
    echo.
    echo [MODE: RUNTIME - Starting app for attach]
    echo Target: %TARGET_PATH%
    start "" /D "%SAMPLE_APP_DIR%" "%TARGET_PATH%"
    echo App started. Use IDE to attach.
    echo.
)

if /i "%RUN_IDE%"=="true" (
    echo Launching IDE...
    start "" /D "%FW_ROOT%" dotnet run --project "%IDE_PROJECT%" -f net9.0-windows -c %CONFIGURATION%
)

echo.
echo Done.
endlocal
exit /b 0
