@echo off
:: ──────────────────────────────────────────────────────────────
:: Paqet Manager — Stop all instances and launch fresh
:: ──────────────────────────────────────────────────────────────
echo.
echo   Paqet Manager — Restart
echo   ─────────────────────────
echo.

:: Kill any running paqet tunnel process
echo [1/3] Stopping paqet tunnel...
taskkill /IM paqet_windows_amd64.exe /F >nul 2>&1
if %errorlevel% equ 0 (
    echo       Stopped.
) else (
    echo       Not running.
)

:: Kill any running PaqetManager instance
echo [2/3] Stopping Paqet Manager...
taskkill /IM PaqetManager.exe /F >nul 2>&1
if %errorlevel% equ 0 (
    echo       Stopped.
    timeout /t 1 /nobreak >nul
) else (
    echo       Not running.
)

:: Launch the app
echo [3/3] Starting Paqet Manager...
cd /d "%~dp0"
if exist "publish\PaqetManager.exe" (
    start "" "publish\PaqetManager.exe"
    echo       Launched from publish\
) else if exist "src\PaqetManager\bin\Debug\net8.0-windows\PaqetManager.exe" (
    start "" "src\PaqetManager\bin\Debug\net8.0-windows\PaqetManager.exe"
    echo       Launched from Debug build
) else (
    echo       ERROR: PaqetManager.exe not found.
    echo       Run Build.bat first, or: dotnet build src\PaqetManager
    pause
    exit /b 1
)

echo.
echo   Done. Paqet Manager is running in the system tray.
