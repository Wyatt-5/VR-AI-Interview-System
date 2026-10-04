@echo off
chcp 65001 >nul
setlocal EnableExtensions

title VR Interview Backend Setup

set "PROJECT_ROOT=%~dp0"
set "BACKEND_DIR="

rem Locate the backend source without depending on a Chinese code page.
for /d %%D in ("%PROJECT_ROOT%*") do (
    if exist "%%~fD\main.py" (
        if exist "%%~fD\requirements.txt" (
            set "BACKEND_DIR=%%~fD"
        )
    )
)

echo ========================================
echo     VR Interview First-time Setup
echo ========================================
echo.

if not defined BACKEND_DIR (
    echo [ERROR] Backend source folder was not found.
    echo Expected files: main.py and requirements.txt
    echo.
    pause
    exit /b 1
)

set "VENV_PYTHON=%BACKEND_DIR%\.venv\Scripts\python.exe"

if exist "%VENV_PYTHON%" goto install_dependencies

py -3.10 -c "import sys; raise SystemExit(0 if sys.version_info[:2] == (3, 10) and sys.maxsize > 2**32 else 1)" >nul 2>&1
if not errorlevel 1 (
    echo [1/4] Creating a Python 3.10 virtual environment...
    py -3.10 -m venv "%BACKEND_DIR%\.venv"
    goto verify_venv
)

python -c "import sys; raise SystemExit(0 if sys.version_info[:2] == (3, 10) and sys.maxsize > 2**32 else 1)" >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Python 3.10 x64 was not found.
    echo Install Python 3.10 x64, enable the Python Launcher, then retry.
    echo.
    pause
    exit /b 1
)

echo [1/4] Creating a Python 3.10 virtual environment...
python -m venv "%BACKEND_DIR%\.venv"

:verify_venv
if not exist "%VENV_PYTHON%" (
    echo [ERROR] Virtual environment creation failed.
    echo.
    pause
    exit /b 1
)

:install_dependencies
echo [2/4] Updating pip...
"%VENV_PYTHON%" -m pip install --upgrade pip
if errorlevel 1 goto setup_failed

echo [3/4] Installing backend dependencies...
"%VENV_PYTHON%" -m pip install -r "%BACKEND_DIR%\requirements.txt"
if errorlevel 1 goto setup_failed

echo [4/4] Preparing the local environment file...
if not exist "%BACKEND_DIR%\.env" (
    copy /y "%BACKEND_DIR%\.env.example" "%BACKEND_DIR%\.env" >nul
    echo [NOTE] A blank .env file was created.
    echo Add your own DEEPSEEK_API_KEY before using online AI features.
) else (
    echo [OK] Existing .env was kept unchanged.
)

echo.
echo [SUCCESS] Backend setup is complete.
echo Next: double-click the backend launcher.
echo.
pause
exit /b 0

:setup_failed
echo.
echo [ERROR] Dependency installation failed.
echo Check the network and the error messages above, then retry.
echo.
pause
exit /b 1
