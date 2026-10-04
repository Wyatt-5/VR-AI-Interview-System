@echo off
chcp 65001 >nul
setlocal

title VR Interview Backend

set "PROJECT_ROOT=%~dp0"
set "BACKEND_DIR="

rem Locate the backend folder without depending on a Chinese code page.
for /d %%D in ("%PROJECT_ROOT%*") do (
    if exist "%%~fD\main.py" (
        if exist "%%~fD\.venv\Scripts\python.exe" (
            set "BACKEND_DIR=%%~fD"
        )
    )
)

echo ========================================
echo       VR Interview Backend Launcher
echo ========================================
echo.

if not defined BACKEND_DIR (
    echo [ERROR] Backend folder was not found.
    echo Expected files: main.py and .venv\Scripts\python.exe
    echo Run the first-time backend setup script before this launcher.
    echo Project root: %PROJECT_ROOT%
    echo.
    pause
    exit /b 1
)

set "PYTHON_EXE=%BACKEND_DIR%\.venv\Scripts\python.exe"
set "HEALTH_URL=http://127.0.0.1:8000/health"

curl.exe --silent --fail --max-time 2 "%HEALTH_URL%" >nul 2>&1
if not errorlevel 1 (
    echo [OK] The backend is already running.
    echo URL: http://127.0.0.1:8000
    echo.
    ping 127.0.0.1 -n 4 >nul
    exit /b 0
)

cd /d "%BACKEND_DIR%"

set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"
set "PYTHONUNBUFFERED=1"

echo [START] Starting the backend. Keep this window open.
echo [URL]   http://127.0.0.1:8000
echo [READY] Unity can run after "Application startup complete" appears.
echo.

"%PYTHON_EXE%" -m uvicorn main:app --host 127.0.0.1 --port 8000
set "BACKEND_EXIT_CODE=%errorlevel%"

echo.
if "%BACKEND_EXIT_CODE%"=="0" (
    echo [STOPPED] The backend service has stopped.
) else (
    echo [ERROR] The backend exited with code %BACKEND_EXIT_CODE%.
    echo Keep the error messages above for troubleshooting.
)
echo.
pause
exit /b %BACKEND_EXIT_CODE%
