@echo off
:: ──────────────────────────────────────────────────────────────
:: Paqet Manager — Stop all processes
:: ──────────────────────────────────────────────────────────────
echo.
echo   Stopping all Paqet processes...
echo.

taskkill /IM paqet_windows_amd64.exe /F >nul 2>&1
if %errorlevel% equ 0 (
    echo   [x] paqet tunnel stopped
) else (
    echo   [ ] paqet tunnel was not running
)

taskkill /IM PaqetManager.exe /F >nul 2>&1
if %errorlevel% equ 0 (
    echo   [x] Paqet Manager stopped
) else (
    echo   [ ] Paqet Manager was not running
)

echo.
echo   All Paqet processes stopped.
