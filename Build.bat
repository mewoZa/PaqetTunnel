@echo off
echo ══════════════════════════════════════════════
echo   Paqet Manager — Production Build
echo ══════════════════════════════════════════════
echo.

cd /d "%~dp0"

echo [1/4] Building single-file executable...
dotnet publish src\PaqetManager\PaqetManager.csproj -c Release -o publish --self-contained true -r win-x64
if %errorlevel% neq 0 (
    echo BUILD FAILED
    pause
    exit /b 1
)
echo       Output: publish\PaqetManager.exe
echo.

echo [2/4] Downloading tun2socks (if missing)...
if not exist "publish\tun2socks.exe" (
    echo       Downloading tun2socks v2.6.0...
    powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -Uri 'https://github.com/xjasonlyu/tun2socks/releases/download/v2.6.0/tun2socks-windows-amd64.zip' -OutFile 'publish\tun2socks.zip'"
    if %errorlevel% neq 0 (
        echo       WARNING: tun2socks download failed — app will auto-download at runtime
    ) else (
        powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; Expand-Archive -Path 'publish\tun2socks.zip' -DestinationPath 'publish' -Force"
        del "publish\tun2socks.zip" 2>nul
        if exist "publish\tun2socks.exe" (
            echo       OK: tun2socks.exe
        ) else (
            echo       WARNING: tun2socks.exe not found after extraction
        )
    )
) else (
    echo       Already exists: publish\tun2socks.exe
)
echo.

echo [3/4] Downloading wintun.dll (if missing)...
if not exist "publish\wintun.dll" (
    echo       Downloading wintun v0.14.1...
    powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -Uri 'https://www.wintun.net/builds/wintun-0.14.1.zip' -OutFile 'publish\wintun.zip'"
    if %errorlevel% neq 0 (
        echo       WARNING: wintun download failed — app will auto-download at runtime
    ) else (
        powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; Expand-Archive -Path 'publish\wintun.zip' -DestinationPath 'publish\wintun_extract' -Force; Copy-Item 'publish\wintun_extract\wintun\bin\amd64\wintun.dll' 'publish\wintun.dll' -Force"
        rd /s /q "publish\wintun_extract" 2>nul
        del "publish\wintun.zip" 2>nul
        if exist "publish\wintun.dll" (
            echo       OK: wintun.dll
        ) else (
            echo       WARNING: wintun.dll not found after extraction
        )
    )
) else (
    echo       Already exists: publish\wintun.dll
)
echo.

echo [4/4] Building installer...
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
if exist "publish\tun2socks.exe" echo   TUN:       publish\tun2socks.exe
if exist "publish\wintun.dll" echo   WinTun:    publish\wintun.dll
if exist "installer\Output\PaqetManagerSetup.exe" echo   Installer: installer\Output\PaqetManagerSetup.exe
echo ══════════════════════════════════════════════
pause
