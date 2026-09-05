@echo off
setlocal enabledelayedexpansion

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"

echo ============================================================
echo WPFTestAuto - Build and Launch
echo ============================================================
echo.

:: Build all projects with dotnet
echo [1/4] Building WpfSpyAgent (.NET 8)...
dotnet build "%ROOT%\src\csharp\WpfSpyAgent\WpfSpyAgent.csproj" -c Debug -f net8.0-windows
if errorlevel 1 (
    echo ERROR: Failed to build WpfSpyAgent
    pause
    exit /b 1
)

echo [2/4] Building WpfSpyAgent (.NET Framework)...
dotnet build "%ROOT%\src\csharp\WpfSpyAgent\WpfSpyAgent.csproj" -c Debug -f net461
if errorlevel 1 (
    echo WARNING: Failed to build for .NET Framework ^(may be OK if not installed^)
)

echo [3/4] Building SampleWpfApp (.NET Framework)...
dotnet build "%ROOT%\src\csharp\SampleWpfApp\SampleWpfApp.csproj" -c Debug -f net461
if errorlevel 1 (
    echo WARNING: Failed to build SampleWpfApp for .NET Framework
)

echo [4/4] Building WPF Test IDE...
dotnet build "%ROOT%\src\csharp\WpfTestIde\WpfTestIde.csproj" -c Debug
if errorlevel 1 (
    echo ERROR: Failed to build IDE
    pause
    exit /b 1
)

echo.
echo ============================================================
echo Build complete!
echo ============================================================
echo.
echo Launching WPF Test IDE...
echo.
cd /d "%ROOT%"
dotnet run --project src\csharp\WpfTestIde\WpfTestIde.csproj -c Debug -f net9.0-windows

endlocal
