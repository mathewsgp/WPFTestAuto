@echo off
rem ============================================================
rem WPFTestAuto - Build and Run for Visual Studio 2022
rem ============================================================
rem This wrapper calls the main build_and_run.bat script
rem with VS 2022 configuration (default).
rem ============================================================

setlocal

echo ============================================================
echo WPFTestAuto - VS 2022 Build Environment
echo ============================================================
echo.

:: VS2022 is the default, no need to set VS2026 flag
:: Pass all arguments to the main script
call "%~dp0build_and_run.bat" %*

endlocal
