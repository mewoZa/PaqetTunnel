@echo off
:: ──────────────────────────────────────────────────────────────
:: Paqet Tunnel — Stop all instances and launch fresh
:: ──────────────────────────────────────────────────────────────
echo.
echo   Paqet Tunnel — Restart
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

:: Kill any running PaqetTunnel instance
echo [2/3] Stopping Paqet Tunnel...
taskkill /IM PaqetTunnel.exe /F >nul 2>&1
if %errorlevel% equ 0 (
    echo       Stopped.
    timeout /t 1 /nobreak >nul
) else (
    echo       Not running.
)

:: Launch the app
echo [3/3] Starting Paqet Tunnel...
cd /d "%~dp0"
if exist "publish\PaqetTunnel.exe" (
    start "" "publish\PaqetTunnel.exe"
    echo       Launched from publish\
) else if exist "src\PaqetTunnel\bin\Debug\net8.0-windows\PaqetTunnel.exe" (
    start "" "src\PaqetTunnel\bin\Debug\net8.0-windows\PaqetTunnel.exe"
    echo       Launched from Debug build
) else (
    echo       ERROR: PaqetTunnel.exe not found.
    echo       Run Build.bat first, or: dotnet build src\PaqetTunnel
    pause
    exit /b 1
)

echo.
echo   Done. Paqet Tunnel is running in the system tray.
