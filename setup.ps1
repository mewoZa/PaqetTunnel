<#
.SYNOPSIS
    Paqet Tunnel — Universal Setup & Management Script
.DESCRIPTION
    One-liner:  irm https://raw.githubusercontent.com/mewoZa/PaqetTunnel/master/setup.ps1 -o $env:TEMP\pt.ps1; & $env:TEMP\pt.ps1
    With args:  .\setup.ps1 install | update | uninstall | status | server | help
.EXAMPLE
    .\setup.ps1                        # Interactive menu
    .\setup.ps1 install                # Install client
    .\setup.ps1 install -Addr 1.2.3.4:8443 -Key mykey
    .\setup.ps1 update                 # Update to latest
    .\setup.ps1 server -Addr 0.0.0.0:8443  # Install server
    .\setup.ps1 uninstall              # Remove everything
#>
param(
    [Parameter(Position=0)][string]$Command,
    [Alias('s')][switch]$Server,
    [Alias('a')][string]$Addr,
    [string]$Key,
    [Alias('i')][string]$Iface,
    [int]$SocksPort = 10800,
    [switch]$Force,
    [Alias('y')][switch]$Yes
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Script:AppVersion = '1.0.0'
$Script:Repo = 'mewoZa/PaqetTunnel'
$Script:RepoUrl = "https://github.com/$($Script:Repo).git"
$Script:RawUrl = "https://raw.githubusercontent.com/$($Script:Repo)/master"
$Script:ApiUrl = "https://api.github.com/repos/$($Script:Repo)"
$Script:AppName = 'Paqet Tunnel'
$Script:ExeName = 'PaqetTunnel.exe'
$Script:InstallDir = "$env:ProgramFiles\Paqet Tunnel"
$Script:DataDir = "$env:LOCALAPPDATA\PaqetTunnel"
$Script:SourceDir = "$env:USERPROFILE\PaqetTunnel"
$Script:PaqetBin = 'paqet_windows_amd64.exe'

# ═══════════════════════════════════════════════════════════════════
# UI Helpers
# ═══════════════════════════════════════════════════════════════════

function W($t,$c='White'){ Write-Host $t -ForegroundColor $c -NoNewline }
function WL($t,$c='White'){ Write-Host $t -ForegroundColor $c }
function Step($t){ W "  › " Cyan; WL $t }
function OK($t){ W "  ✓ " Green; WL $t }
function Warn($t){ W "  ! " Yellow; WL $t }
function Err($t){ W "  ✗ " Red; WL $t }
function Dim($t){ WL "    $t" DarkGray }
function Line(){ WL "  ────────────────────────────────" DarkGray }

function Banner {
    Write-Host ""
    W "  » " Cyan; WL "Paqet Tunnel" White
    W "    " DarkGray; WL "Setup and Management v$($Script:AppVersion)" DarkGray
    Line
}

function Confirm($prompt) {
    if ($Yes) { return $true }
    W "  ? " Yellow; W "$prompt " White; W '[Y/n] ' DarkGray
    $r = Read-Host
    return ($r -eq '' -or $r -match '^[Yy]')
}

function Menu {
    param([string]$Title, [string[]]$Items)
    WL ""; WL "  $Title" White; Line
    for ($i = 0; $i -lt $Items.Count; $i++) {
        W "  "; W "$($i+1)" Cyan; WL "  $($Items[$i])"
    }
    W "  "; W "0" DarkGray; WL "  Exit" DarkGray
    WL ""
    W "  Select " DarkGray; W "› " Cyan
    $sel = Read-Host
    return $sel
}

# ═══════════════════════════════════════════════════════════════════
# Detection
# ═══════════════════════════════════════════════════════════════════

function Get-Arch {
    $a = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLower()
    switch -Wildcard ($a) {
        '*arm64*' { 'arm64' }
        '*arm*'   { 'arm' }
        default   { 'x64' }
    }
}

function Get-OsInfo {
    $os = [System.Environment]::OSVersion
    $ver = $os.Version
    $arch = Get-Arch
    if ($IsLinux) { return "Linux $arch" }
    if ($IsMacOS) { return "macOS $arch" }
    return "Windows $($ver.Major).$($ver.Minor) $arch"
}

function Is-Installed {
    Test-Path "$Script:InstallDir\$Script:ExeName"
}

function Is-Running {
    $null -ne (Get-Process -Name 'PaqetTunnel' -ErrorAction SilentlyContinue)
}

function Get-InstalledVersion {
    $vf = "$Script:DataDir\.version"
    if (Test-Path $vf) { return (Get-Content $vf -Raw).Trim() }
    if (Is-Installed) { return "unknown" }
    return $null
}

function Has-Command($name) {
    $null -ne (Get-Command $name -ErrorAction SilentlyContinue)
}

function Is-Admin {
    if ($IsLinux -or $IsMacOS) { return ((id -u) -eq 0) }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $identity
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Require-Admin {
    if (Is-Admin) { return }
    WL ""
    Warn "Administrator privileges required. Elevating..."
    $argList = "-ExecutionPolicy Bypass -File `"$PSCommandPath`" $($Script:OriginalArgs -join ' ')"
    Start-Process powershell -ArgumentList $argList -Verb RunAs -Wait
    exit
}

# ═══════════════════════════════════════════════════════════════════
# Dependency Installation
# ═══════════════════════════════════════════════════════════════════

function Install-Dep($name, $testCmd, $wingetId, $fallbackUrl, $fallbackArgs) {
    if (Has-Command $testCmd) {
        OK "$name already installed"
        return $true
    }

    Step "Installing $name..."

    # Try winget first
    if (Has-Command 'winget') {
        try {
            $out = & winget install --id $wingetId --accept-source-agreements --accept-package-agreements --silent 2>&1
            if ($LASTEXITCODE -eq 0) {
                # Refresh PATH
                $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                            [System.Environment]::GetEnvironmentVariable('PATH', 'User')
                OK "$name installed via winget"
                return $true
            }
        } catch {}
    }

    # Fallback: direct download
    if ($fallbackUrl) {
        Step "Downloading $name directly..."
        $ext = if ($fallbackUrl -match '\.msi$') { '.msi' } else { '.exe' }
        $dl = "$env:TEMP\$name-setup$ext"
        Invoke-WebRequest -Uri $fallbackUrl -OutFile $dl -UseBasicParsing
        if ($ext -eq '.msi') {
            Start-Process msiexec -ArgumentList "/i `"$dl`" /qn" -Wait
        } else {
            Start-Process $dl -ArgumentList $fallbackArgs -Wait
        }
        Remove-Item $dl -Force -ErrorAction SilentlyContinue
        $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                    [System.Environment]::GetEnvironmentVariable('PATH', 'User')
        if (Has-Command $testCmd) {
            OK "$name installed"
            return $true
        }
    }

    Err "Failed to install $name. Please install manually."
    return $false
}

function Ensure-Git {
    Install-Dep 'Git' 'git' 'Git.Git' `
        'https://github.com/git-for-windows/git/releases/latest/download/Git-2.47.1-64-bit.exe' `
        '/VERYSILENT /NORESTART'
}

function Ensure-Dotnet {
    if (Has-Command 'dotnet') {
        $ver = & dotnet --version 2>$null
        if ($ver -match '^8\.') { OK ".NET 8 SDK ($ver)"; return $true }
    }
    Install-Dep '.NET 8 SDK' 'dotnet' 'Microsoft.DotNet.SDK.8' `
        'https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.ps1' ''
    # Fallback: use dotnet-install script
    if (-not (Has-Command 'dotnet') -or -not ((& dotnet --version 2>$null) -match '^8\.')) {
        Step "Using dotnet-install script..."
        $script = "$env:TEMP\dotnet-install.ps1"
        Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $script -UseBasicParsing
        & $script -Channel 8.0
        $env:PATH = $env:LOCALAPPDATA + '\Microsoft\dotnet;' + $env:PATH
    }
    return (Has-Command 'dotnet')
}

function Ensure-Go {
    if (Has-Command 'go') {
        $ver = (& go version 2>$null) -replace 'go version go','' -replace ' .*',''
        OK ('Go (' + $ver + ')')
        return $true
    }
    $arch = Get-Arch
    $goArch = if ($arch -eq 'arm64') { 'arm64' } else { 'amd64' }
    Install-Dep 'Go' 'go' 'GoLang.Go' `
        "https://go.dev/dl/go1.24.0.windows-$goArch.msi" ''
    return (Has-Command 'go')
}

function Ensure-InnoSetup {
    $iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    if (Test-Path $iscc) { OK "Inno Setup 6"; return $iscc }
    $iscc2 = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (Test-Path $iscc2) { OK "Inno Setup 6"; return $iscc2 }

    Step "Installing Inno Setup 6..."
    if (Has-Command 'winget') {
        & winget install --id JRSoftware.InnoSetup --accept-source-agreements --accept-package-agreements --silent 2>&1 | Out-Null
    } else {
        $dl = "$env:TEMP\innosetup.exe"
        Invoke-WebRequest 'https://jrsoftware.org/download.php/is.exe' -OutFile $dl -UseBasicParsing
        Start-Process $dl -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES' -Wait
        Remove-Item $dl -Force -ErrorAction SilentlyContinue
    }
    foreach ($p in @($iscc, $iscc2)) { if (Test-Path $p) { OK "Inno Setup installed"; return $p } }
    Err "Inno Setup installation failed"
    return $null
}

# ═══════════════════════════════════════════════════════════════════
# Build
# ═══════════════════════════════════════════════════════════════════

function Build-FromSource {
    $src = $Script:SourceDir

    # Clone or pull
    if (Test-Path "$src\.git") {
        Step "Updating source..."
        Push-Location $src
        & git pull --quiet 2>&1 | Out-Null
        & git submodule update --init --recursive --quiet 2>&1 | Out-Null
        Pop-Location
    } else {
        Step "Cloning repository..."
        & git clone --recursive $Script:RepoUrl $src 2>&1 | Out-Null
    }
    OK "Source ready"

    # Build paqet (Go)
    Step "Building paqet..."
    Push-Location "$src\paqet"
    $env:CGO_ENABLED = '0'
    $env:GOOS = 'windows'
    $goArch = if ((Get-Arch) -eq 'arm64') { 'arm64' } else { 'amd64' }
    $env:GOARCH = $goArch
    & go build -trimpath -ldflags='-s -w' -o "$src\publish\$Script:PaqetBin" ./cmd/paqet 2>&1
    if ($LASTEXITCODE -ne 0) { Pop-Location; Err "paqet build failed"; return $false }
    Pop-Location
    OK "paqet built"

    # Build PaqetTunnel (.NET)
    Step "Building PaqetTunnel..."
    $rid = if ((Get-Arch) -eq 'arm64') { 'win-arm64' } else { 'win-x64' }
    & dotnet publish "$src\src\PaqetTunnel\PaqetTunnel.csproj" `
        -c Release -r $rid --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o "$src\publish" --nologo --verbosity quiet 2>&1
    if ($LASTEXITCODE -ne 0) { Err "PaqetTunnel build failed"; return $false }
    OK "PaqetTunnel built"

    # Download dependencies
    $pubDir = "$src\publish"
    $arch = Get-Arch
    if (-not (Test-Path "$pubDir\tun2socks.exe")) {
        Step "Downloading tun2socks..."
        $t2sArch = if ($arch -eq 'arm64') { 'arm64' } else { 'amd64' }
        $t2sUrl = "https://github.com/xjasonlyu/tun2socks/releases/latest/download/tun2socks-windows-$t2sArch.zip"
        $zipFile = "$env:TEMP\tun2socks.zip"
        Invoke-WebRequest $t2sUrl -OutFile $zipFile -UseBasicParsing
        Expand-Archive $zipFile -DestinationPath "$env:TEMP\t2s" -Force
        Get-ChildItem "$env:TEMP\t2s" -Filter '*.exe' -Recurse | Select-Object -First 1 |
            Copy-Item -Destination "$pubDir\tun2socks.exe"
        Remove-Item $zipFile, "$env:TEMP\t2s" -Recurse -Force -ErrorAction SilentlyContinue
        OK "tun2socks downloaded"
    }
    if (-not (Test-Path "$pubDir\wintun.dll")) {
        Step "Downloading WinTun..."
        $wUrl = 'https://www.wintun.net/builds/wintun-0.14.1.zip'
        $wZip = "$env:TEMP\wintun.zip"
        Invoke-WebRequest $wUrl -OutFile $wZip -UseBasicParsing
        Expand-Archive $wZip -DestinationPath "$env:TEMP\wintun" -Force
        $wArch = if ($arch -eq 'arm64') { 'arm64' } else { 'amd64' }
        Copy-Item "$env:TEMP\wintun\wintun\bin\$wArch\wintun.dll" "$pubDir\wintun.dll"
        Remove-Item $wZip, "$env:TEMP\wintun" -Recurse -Force -ErrorAction SilentlyContinue
        OK "WinTun downloaded"
    }

    # Save build commit
    Push-Location $src
    $sha = & git rev-parse HEAD 2>$null
    Pop-Location
    if ($sha) {
        New-Item -Path $Script:DataDir -ItemType Directory -Force | Out-Null
        $sha | Out-File "$Script:DataDir\.commit" -NoNewline
    }

    OK "Build complete"
    return $true
}

function Build-Installer {
    $iscc = Ensure-InnoSetup
    if (-not $iscc) { return $null }

    Step "Building installer..."
    $issFile = "$Script:SourceDir\installer\PaqetSetup.iss"
    if (-not (Test-Path $issFile)) { Err "Installer script not found"; return $null }

    & $iscc $issFile 2>&1 | Out-Null
    $setup = "$Script:SourceDir\installer\Output\PaqetTunnelSetup.exe"
    if (Test-Path $setup) { OK "Installer built"; return $setup }
    Err "Installer build failed"
    return $null
}

# ═══════════════════════════════════════════════════════════════════
# Commands
# ═══════════════════════════════════════════════════════════════════

function Do-Install {
    param([switch]$ServerMode)
    Banner
    Step "Installing $Script:AppName$(if($ServerMode){' (Server)'}else{' (Client)'})..."
    WL ""
    Require-Admin

    # Check dependencies
    Step "Checking dependencies..."
    WL ""
    if (-not (Ensure-Git))    { Err "Git is required"; return }
    if (-not (Ensure-Dotnet)) { Err ".NET 8 SDK is required"; return }
    if (-not (Ensure-Go))     { Err "Go is required"; return }
    WL ""

    # Build
    Step "Building from source..."
    WL ""
    if (-not (Build-FromSource)) { return }
    WL ""

    if ($ServerMode) {
        Do-ServerInstall
        return
    }

    # Build and run installer
    $setup = Build-Installer
    if ($setup) {
        Step "Installing..."
        Stop-App
        Start-Process $setup -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /MERGETASKS=desktopicon' -Wait
        OK "Installed to $Script:InstallDir"
    } else {
        # Fallback: manual install
        Step "Installing manually (no InnoSetup)..."
        Stop-App
        New-Item -Path $Script:InstallDir -ItemType Directory -Force | Out-Null
        Copy-Item "$Script:SourceDir\publish\*" $Script:InstallDir -Recurse -Force
        OK "Installed to $Script:InstallDir"
    }

    # Save version
    New-Item -Path $Script:DataDir -ItemType Directory -Force | Out-Null
    $Script:AppVersion | Out-File "$Script:DataDir\.version" -NoNewline

    # Configure if args provided
    if ($Addr -or $Key) { Do-Configure }

    WL ""
    Line
    OK "Installation complete!"
    Dim "Run from Start Menu or Desktop shortcut"
    Dim ('Or: ' + $Script:InstallDir + '\' + $Script:ExeName)
    WL ""
}

function Do-Update {
    Banner
    Step "Checking for updates..."

    if (-not (Test-Path "$Script:SourceDir\.git")) {
        Warn "Source not found. Running full install..."
        Do-Install
        return
    }

    Require-Admin

    Push-Location $Script:SourceDir
    & git fetch --quiet 2>&1 | Out-Null
    $local = & git rev-parse HEAD 2>$null
    $remote = & git rev-parse origin/master 2>$null
    Pop-Location

    if ($local -eq $remote -and -not $Force) {
        OK "Already up to date ($($local.Substring(0,7)))"
        return
    }

    Step "Updating $($local.Substring(0,7)) → $($remote.Substring(0,7))..."
    WL ""

    if (-not (Build-FromSource)) { return }
    WL ""

    $setup = Build-Installer
    if ($setup) {
        Step "Upgrading..."
        Stop-App
        Start-Process $setup -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait
        OK "Updated successfully"
    } else {
        Stop-App
        Copy-Item "$Script:SourceDir\publish\*" $Script:InstallDir -Recurse -Force
        OK "Updated (manual copy)"
    }

    $Script:AppVersion | Out-File "$Script:DataDir\.version" -NoNewline
    WL ""
    OK "Update complete!"

    if (Confirm "Start $($Script:AppName)?") { Start-App }
}

function Do-Uninstall {
    Banner
    if (-not (Is-Installed)) {
        Warn "$Script:AppName is not installed"
        return
    }

    if (-not (Confirm "Uninstall $($Script:AppName)? This removes all data.")) { return }
    Require-Admin

    Stop-App

    # Run InnoSetup uninstaller if exists
    $uninst = "$Script:InstallDir\unins000.exe"
    if (Test-Path $uninst) {
        Step "Running uninstaller..."
        Start-Process $uninst -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait
        OK "Uninstaller completed"
    } else {
        Step "Removing files..."
        Remove-Item $Script:InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Clean data
    if (Confirm "Remove app data ($($Script:DataDir))?") {
        Remove-Item $Script:DataDir -Recurse -Force -ErrorAction SilentlyContinue
        OK "Data removed"
    }

    # Clean autostart
    $regPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Remove-ItemProperty -Path $regPath -Name 'PaqetTunnel' -ErrorAction SilentlyContinue

    # Clean source (optional)
    if ((Test-Path $Script:SourceDir) -and (Confirm "Remove source code ($($Script:SourceDir))?")) {
        Remove-Item $Script:SourceDir -Recurse -Force -ErrorAction SilentlyContinue
        OK "Source removed"
    }

    WL ""
    OK "Uninstalled successfully"
}

function Do-Status {
    Banner
    $installed = Is-Installed
    $running = Is-Running
    $ver = Get-InstalledVersion

    W "  Status    "; if ($installed) { WL "Installed" Green } else { WL "Not installed" DarkGray }
    W "  Version   "; WL "$(if($ver){$ver}else{'—'})" $(if($ver){'White'}else{'DarkGray'})
    W "  Running   "; if ($running) { WL "Yes ●" Green } else { WL "No" DarkGray }
    W "  Arch      "; WL (Get-OsInfo) White
    W "  Install   "; WL "$(if($installed){$Script:InstallDir}else{'—'})" DarkGray
    W "  Data      "; WL "$(if(Test-Path $Script:DataDir){$Script:DataDir}else{'—'})" DarkGray
    W "  Source    "; WL "$(if(Test-Path "$Script:SourceDir\.git"){'Yes'}else{'No'})" DarkGray

    if (Test-Path "$Script:DataDir\.commit") {
        $sha = (Get-Content "$Script:DataDir\.commit" -Raw).Trim()
        W "  Build     "; WL $sha.Substring(0, [Math]::Min(7, $sha.Length)) DarkGray
    }

    # Check processes
    if ($running) {
        WL ""
        Step "Running processes:"
        Get-Process -Name 'PaqetTunnel','paqet_windows_amd64','tun2socks' -ErrorAction SilentlyContinue |
            ForEach-Object { Dim "$($_.ProcessName) (PID $($_.Id), $([math]::Round($_.WorkingSet64/1MB))MB)" }
    }

    # Config check
    $cfg = "$Script:DataDir\config\client.yaml"
    if (Test-Path $cfg) {
        WL ""
        Step "Configuration:"
        $content = Get-Content $cfg -Raw
        if ($content -match 'addr:\s*"([^"]+)"') { Dim "Server: $($Matches[1])" }
        if ($content -match 'listen:\s*"([^"]+)"') { Dim "SOCKS5: $($Matches[1])" }
    }
    WL ""
}

function Do-ServerInstall {
    Step "Setting up paqet server..."

    $binDir = "$Script:SourceDir\publish"
    $paqetExe = "$binDir\$Script:PaqetBin"
    if (-not (Test-Path $paqetExe)) { Err "paqet binary not found"; return }

    # Generate secret
    $secret = & $paqetExe secret 2>$null
    if (-not $secret) { $secret = [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 })) }
    $secret = $secret.Trim()

    # Determine server address
    $serverAddr = if ($Addr) { $Addr } else {
        W "  ? " Yellow; W "Server bind address " White; W '(0.0.0.0:8443) ' DarkGray
        $r = Read-Host
        if ($r) { $r } else { "0.0.0.0:8443" }
    }

    # Create config
    $cfgDir = "$Script:DataDir\config"
    New-Item -Path $cfgDir -ItemType Directory -Force | Out-Null
    $serverCfg = @"
role: "server"
log:
  level: "info"
server:
  addr: "$serverAddr"
  key: "$secret"
"@
    $serverCfg | Out-File "$cfgDir\server.yaml" -Encoding UTF8
    OK "Server config created"

    # Install as service
    Step "Installing Windows service..."
    $svcBin = "$Script:InstallDir\$Script:PaqetBin"
    New-Item -Path $Script:InstallDir -ItemType Directory -Force | Out-Null
    Copy-Item $paqetExe $svcBin -Force

    $existingSvc = Get-Service -Name 'PaqetServer' -ErrorAction SilentlyContinue
    if ($existingSvc) {
        Stop-Service 'PaqetServer' -Force -ErrorAction SilentlyContinue
        & sc.exe delete PaqetServer 2>&1 | Out-Null
        Start-Sleep 1
    }

    & sc.exe create PaqetServer binPath= "`"$svcBin`" run --config `"$cfgDir\server.yaml`"" start= auto 2>&1 | Out-Null
    & sc.exe description PaqetServer "Paqet Tunnel Server" 2>&1 | Out-Null
    Start-Service 'PaqetServer'
    OK "Server service started"

    # Firewall
    $port = ($serverAddr -split ':')[-1]
    & netsh advfirewall firewall delete rule name="Paqet Server" 2>&1 | Out-Null
    & netsh advfirewall firewall add rule name="Paqet Server" dir=in action=allow protocol=udp localport=$port 2>&1 | Out-Null
    OK "Firewall rule added (UDP $port)"

    WL ""
    Line
    OK "Server running!"
    WL ""
    WL "  Client configuration:" White
    Dim "Server:  $serverAddr"
    Dim "Key:     $secret"
    WL ""
}

function Do-Configure {
    $cfgDir = "$Script:DataDir\config"
    New-Item -Path $cfgDir -ItemType Directory -Force | Out-Null

    $serverAddr = if ($Addr) { $Addr } else { "your-server-ip:8443" }
    $serverKey = if ($Key) { $Key } else { "" }
    $iface = if ($Iface) { $Iface } else { "Ethernet" }
    $socks = "127.0.0.1:$SocksPort"

    $cfg = @"
role: "client"
log:
  level: "info"
socks5:
  - listen: "$socks"
network:
  interface: "$iface"
server:
  addr: "$serverAddr"
  key: "$serverKey"
"@
    $cfg | Out-File "$cfgDir\client.yaml" -Encoding UTF8
    OK "Client config saved"
}

function Stop-App {
    'PaqetTunnel','tun2socks','paqet_windows_amd64' | ForEach-Object {
        Get-Process -Name $_ -ErrorAction SilentlyContinue | ForEach-Object {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep 1
}

function Start-App {
    $exe = "$Script:InstallDir\$Script:ExeName"
    if (-not (Test-Path $exe)) { Err "Not installed"; return }
    Step "Starting $Script:AppName..."
    # Use scheduled task for admin elevation
    & schtasks /create /tn 'PaqetLaunch' /tr "`"$exe`"" /sc once /st 00:00 /rl HIGHEST /f 2>&1 | Out-Null
    & schtasks /run /tn 'PaqetLaunch' 2>&1 | Out-Null
    & schtasks /delete /tn 'PaqetLaunch' /f 2>&1 | Out-Null
    OK "Started"
}

function Show-Help {
    Banner
    WL "  Usage:" White
    WL '    setup.ps1 <command> <options>' Cyan
    WL ""
    WL "  Commands:" White
    WL "    install        Install PaqetTunnel client" DarkGray
    WL "    update         Update to latest version" DarkGray
    WL "    uninstall      Remove PaqetTunnel" DarkGray
    WL "    status         Show installation status" DarkGray
    WL "    server         Install paqet server" DarkGray
    WL "    help           Show this help" DarkGray
    WL ""
    WL "  Options:" White
    WL "    -Addr <ip:port>     Server address" DarkGray
    WL "    -Key <secret>       Server key" DarkGray
    WL "    -Iface <name>       Network interface" DarkGray
    WL "    -SocksPort <port>   SOCKS5 port (default: 10800)" DarkGray
    WL "    -Server             Server mode" DarkGray
    WL "    -Force              Force operation" DarkGray
    WL "    -y                  Skip confirmations" DarkGray
    WL ""
    WL "  One-liner install:" White
    WL ('    irm ' + $Script:RawUrl + '/setup.ps1 -o $env:TEMP\pt.ps1; & $env:TEMP\pt.ps1') Cyan
    WL ""
}

# ═══════════════════════════════════════════════════════════════════
# Interactive Menu
# ═══════════════════════════════════════════════════════════════════

function Show-Interactive {
    Banner
    $os = Get-OsInfo
    $installed = Is-Installed
    $running = Is-Running
    $ver = Get-InstalledVersion

    W "  System    "; WL $os White
    W "  Status    "
    if ($running) { WL "Running ●" Green }
    elseif ($installed) { WL "Installed" Cyan }
    else { WL "Not installed" DarkGray }
    if ($ver) { W "  Version   "; WL $ver White }

    $items = if ($installed) {
        @('Update', 'Reinstall', 'Uninstall', 'Status', 'Start / Stop', 'Server Setup')
    } else {
        @('Install Client', 'Install Server', 'Status', 'Help')
    }

    $sel = Menu '' $items

    if ($installed) {
        switch ($sel) {
            '1' { Do-Update }
            '2' { Do-Install }
            '3' { Do-Uninstall }
            '4' { Do-Status }
            '5' { if (Is-Running) { Stop-App; OK "Stopped" } else { Start-App } }
            '6' { Do-Install -ServerMode }
            '0' { return }
            default { Warn "Invalid selection" }
        }
    } else {
        switch ($sel) {
            '1' { Do-Install }
            '2' { Do-Install -ServerMode }
            '3' { Do-Status }
            '4' { Show-Help }
            '0' { return }
            default { Warn "Invalid selection" }
        }
    }
}

# ═══════════════════════════════════════════════════════════════════
# Entry Point
# ═══════════════════════════════════════════════════════════════════

# Build arg string for re-elevation
$Script:OriginalArgs = @()
if ($Command) { $Script:OriginalArgs += $Command }
if ($Server) { $Script:OriginalArgs += '-Server' }
if ($Addr) { $Script:OriginalArgs += "-Addr `"$Addr`"" }
if ($Key) { $Script:OriginalArgs += "-Key `"$Key`"" }
if ($Iface) { $Script:OriginalArgs += "-Iface `"$Iface`"" }
if ($SocksPort -ne 10800) { $Script:OriginalArgs += "-SocksPort $SocksPort" }
if ($Force) { $Script:OriginalArgs += '-Force' }
if ($Yes) { $Script:OriginalArgs += '-y' }

switch ($Command.ToLower()) {
    'install'   { if ($Server) { Do-Install -ServerMode } else { Do-Install } }
    'update'    { Do-Update }
    'uninstall' { Do-Uninstall }
    'status'    { Do-Status }
    'server'    { Do-Install -ServerMode }
    'help'      { Show-Help }
    ''          { Show-Interactive }
    default     { Err "Unknown command: $Command"; Show-Help }
}
