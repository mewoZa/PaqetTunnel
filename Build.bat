@echo off
echo ══════════════════════════════════════════════
echo   Paqet Manager — Production Build
echo ══════════════════════════════════════════════
echo.

cd /d "%~dp0"

echo [1/2] Building single-file executable...
dotnet publish src\PaqetManager\PaqetManager.csproj -c Release -o publish --self-contained true -r win-x64
if %errorlevel% neq 0 (
    echo BUILD FAILED
    pause
    exit /b 1
)
echo       Output: publish\PaqetManager.exe
echo.

echo [2/2] Building installer...
set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\iscc.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\iscc.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\iscc.exe"

if exist "%ISCC%" (
    "%ISCC%" installer\PaqetSetup.iss
    if %errorlevel% neq 0 (
        echo INSTALLER BUILD FAILED
        pause
        exit /b 1
    )
    echo       Output: installer\Output\PaqetManagerSetup.exe
) else (
    echo       Skipped — InnoSetup not found. Install it from: https://jrsoftware.org
)

echo.
echo ══════════════════════════════════════════════
echo   Build complete!
echo   Exe:       publish\PaqetManager.exe
if exist "installer\Output\PaqetManagerSetup.exe" echo   Installer: installer\Output\PaqetManagerSetup.exe
echo ══════════════════════════════════════════════
pause
