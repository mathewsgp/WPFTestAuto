@echo off
rem ============================================================
rem WPFTestAuto - Build and Run for Visual Studio 2026
rem ============================================================
rem This wrapper sets the VS2026 environment variable and
rem calls the main build_and_run.bat script.
rem ============================================================

setlocal

echo ============================================================
echo WPFTestAuto - VS 2026 Build Environment
echo ============================================================
echo.

:: Set VS2026 flag for build_and_run.bat
set VS2026=1

:: Pass all arguments to the main script
call "%~dp0build_and_run.bat" %*

endlocal
