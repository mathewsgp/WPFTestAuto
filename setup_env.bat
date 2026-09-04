@echo off
REM ============================================================
REM WPFTestAuto - Python Development Environment Setup
REM ============================================================
REM This script creates a Python virtual environment and installs
REM all required dependencies for the framework's Python layers.
REM ============================================================

setlocal enabledelayedexpansion

set "ROOT=%~dp0"
set "ROOT=%ROOT:~0,-1%"
set "VENV_DIR=%ROOT%\.venv"
set "PYTHON=python"

echo ============================================================
echo WPFTestAuto - Python Environment Setup
echo ============================================================
echo.

REM ------------------------------------------------------------
REM 1. Check Python installation
REM ------------------------------------------------------------
echo [1/5] Checking Python installation...
%PYTHON% --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: Python is not installed or not in PATH.
    echo Please install Python 3.9+ from https://www.python.org/
    echo and make sure "python" is available in your PATH.
    pause
    exit /b 1
)

%PYTHON% --version
echo.

REM ------------------------------------------------------------
REM 2. Create virtual environment (if it doesn't exist)
REM ------------------------------------------------------------
echo [2/5] Setting up virtual environment...
if exist "%VENV_DIR%" (
    echo Virtual environment already exists at %VENV_DIR%
    echo Skipping creation. Delete the folder if you want a fresh one.
) else (
    %PYTHON% -m venv "%VENV_DIR%"
    if errorlevel 1 (
        echo ERROR: Failed to create virtual environment.
        pause
        exit /b 1
    )
    echo Virtual environment created at %VENV_DIR%
)
echo.

REM ------------------------------------------------------------
REM 3. Activate virtual environment
REM ------------------------------------------------------------
echo [3/5] Activating virtual environment...
call "%VENV_DIR%\Scripts\activate.bat"
if errorlevel 1 (
    echo ERROR: Failed to activate virtual environment.
    pause
    exit /b 1
)
echo Virtual environment activated.
echo.

REM ------------------------------------------------------------
REM 4. Upgrade pip and install core dependencies
REM ------------------------------------------------------------
echo [4/5] Installing Python dependencies...
echo.

echo   -- Upgrading pip --
python -m pip install --upgrade pip
if errorlevel 1 (
    echo WARNING: pip upgrade failed. Continuing with existing pip.
)
echo.

echo   -- Installing core dependencies --
echo   robotframework       (test execution)
echo   pyyaml               (YAML parsing)
echo   pytest               (unit tests)
echo   robotframework-requests (HTTP client)
echo   pywin32              (Windows named pipe / Win32 APIs)
echo.
python -m pip install robotframework pyyaml pytest robotframework-requests pywin32
if errorlevel 1 (
    echo WARNING: Some core packages failed to install. Check the output above.
)
echo.

echo   -- Installing optional UI driver dependencies --
echo   robotframework-flaui      (FlaUI UI Automation wrapper)
echo   robotframework-SikuliLibrary (Sikuli image-based driver)
echo.
python -m pip install robotframework-flaui robotframework-SikuliLibrary
if errorlevel 1 (
    echo WARNING: Optional UI driver packages failed to install.
    echo   FlaUI/Sikuli drivers will not be available until these are installed.
)
echo.

echo   -- Installing optional OCR dependencies --
echo   pytesseract          (OCR engine binding)
echo   Pillow               (image processing for OCR)
echo.
python -m pip install pytesseract Pillow
if errorlevel 1 (
    echo WARNING: Optional OCR packages failed to install.
    echo   OCR features will not be available until these are installed.
    echo   Note: pytesseract also requires the Tesseract OCR binary on your PATH.
)
echo.

echo   -- Installing optional Sikuli image-matching dependencies --
echo   opencv-python        (template matching)
echo   numpy                (array backend for OpenCV)
echo   mss                  (cross-platform screen capture)
echo   pyautogui            (mouse/keyboard input)
echo   pyperclip            (clipboard-based text entry)
echo.
python -m pip install opencv-python numpy mss pyautogui pyperclip
if errorlevel 1 (
    echo WARNING: Optional Sikuli packages failed to install.
    echo   Sikuli image-based recording/playback will not be available.
    echo   On Windows, also install Tesseract OCR if you want get_text:
    echo   https://github.com/tesseract-ocr/tesseract
)
echo.

REM ------------------------------------------------------------
REM 5. Verify installation
REM ------------------------------------------------------------
echo [5/5] Verifying installation...
echo.

echo   Installed packages:
python -m pip list
echo.

echo   Python version:
python --version
echo.

echo ============================================================
echo Setup Complete
echo ============================================================
echo.
echo To activate the virtual environment in the future, run:
echo   .venv\Scripts\activate.bat
echo.
echo To run tests:
echo   robot --outputdir output tests/
echo   or
echo   .\run_tests.bat
echo.
echo To deactivate the virtual environment:
echo   deactivate
echo.
pause
endlocal
exit /b 0
