@echo off
REM Run all Robot Framework tests
REM Usage: run_tests.bat [test_file]
REM   - No args: runs all tests in src/python/tests/ directory
REM   - With arg: runs specific test file

echo ========================================
echo WPFTestAuto - Robot Framework Test Runner
echo ========================================
echo.

cd /d "%~dp0"
set PYTHONPATH=src\python;%PYTHONPATH%

if "%1"=="" (
    echo Running all tests...
    robot --outputdir output src/python/tests/
) else (
    echo Running: %1
    robot --outputdir output %1
)

echo.
echo ========================================
echo Tests complete!
echo ========================================
pause
