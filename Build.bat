@echo off
echo ══════════════════════════════════════════════
echo   Paqet Tunnel — Production Build
echo ══════════════════════════════════════════════
echo.

cd /d "%~dp0"

REM Ensure Go and GCC are on PATH
set "PATH=C:\Program Files\Go\bin;C:\ProgramData\mingw64\mingw64\bin;%PATH%"

echo [1/5] Building paqet from source (Go submodule)...
if not exist "paqet\cmd\main.go" (
    echo       ERROR: paqet submodule not found. Run: git submodule update --init
    pause
    exit /b 1
)
pushd paqet
set CGO_ENABLED=1
set GOOS=windows
set GOARCH=amd64
for /f "tokens=*" %%i in ('git rev-parse HEAD 2^>nul') do set GIT_COMMIT=%%i
for /f "tokens=*" %%i in ('git describe --tags --exact-match 2^>nul') do set GIT_TAG=%%i
if "%GIT_TAG%"=="" set GIT_TAG=dev
for /f "tokens=*" %%i in ('powershell -NoProfile -Command "Get-Date -UFormat '%%Y-%%m-%%d %%H:%%M:%%S UTC'"') do set BUILD_TIME=%%i
echo       Version: %GIT_TAG% (%GIT_COMMIT:~0,8%)
go build -v -a -trimpath -gcflags "all=-l=4" -ldflags "-s -w -buildid= -X 'paqet/cmd/version.Version=%GIT_TAG%' -X 'paqet/cmd/version.GitCommit=%GIT_COMMIT%' -X 'paqet/cmd/version.GitTag=%GIT_TAG%' -X 'paqet/cmd/version.BuildTime=%BUILD_TIME%'" -o ..\publish\paqet_windows_amd64.exe .\cmd\main.go
if %errorlevel% neq 0 (
    echo       PAQET BUILD FAILED
    popd
    pause
    exit /b 1
)
popd
echo       Output: publish\paqet_windows_amd64.exe
echo.

echo [2/5] Building PaqetTunnel (.NET)...
dotnet publish src\PaqetTunnel\PaqetTunnel.csproj -c Release -o publish --self-contained true -r win-x64
if %errorlevel% neq 0 (
    echo BUILD FAILED
    pause
    exit /b 1
)
echo       Output: publish\PaqetTunnel.exe
echo.

echo [3/5] Downloading tun2socks (if missing)...
if not exist "publish\tun2socks.exe" (
    echo       Downloading tun2socks v2.6.0...
    powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -Uri 'https://github.com/xjasonlyu/tun2socks/releases/download/v2.6.0/tun2socks-windows-amd64.zip' -OutFile 'publish\tun2socks.zip'"
    if %errorlevel% neq 0 (
        echo       WARNING: tun2socks download failed
    ) else (
        powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; Expand-Archive -Path 'publish\tun2socks.zip' -DestinationPath 'publish' -Force"
        del "publish\tun2socks.zip" 2>nul
        if exist "publish\tun2socks-windows-amd64.exe" ren "publish\tun2socks-windows-amd64.exe" tun2socks.exe
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

echo [4/5] Downloading wintun.dll (if missing)...
if not exist "publish\wintun.dll" (
    echo       Downloading wintun v0.14.1...
    powershell -NoProfile -Command "$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -Uri 'https://www.wintun.net/builds/wintun-0.14.1.zip' -OutFile 'publish\wintun.zip'"
    if %errorlevel% neq 0 (
        echo       WARNING: wintun download failed
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

echo [5/5] Building installer...
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
    echo       Output: installer\Output\PaqetTunnelSetup.exe
) else (
    echo       Skipped — InnoSetup not found. Install it from: https://jrsoftware.org
)

echo.
echo ══════════════════════════════════════════════
echo   Build complete!
echo   Exe:       publish\PaqetTunnel.exe
if exist "publish\tun2socks.exe" echo   TUN:       publish\tun2socks.exe
if exist "publish\wintun.dll" echo   WinTun:    publish\wintun.dll
if exist "installer\Output\PaqetTunnelSetup.exe" echo   Installer: installer\Output\PaqetTunnelSetup.exe
echo ══════════════════════════════════════════════
pause
