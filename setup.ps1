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
    [switch]$Build,
    [switch]$Force,
    [Alias('y')][switch]$Yes
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$Script:AppVersion = '1.0.0'
$Script:Repo = 'mewoZa/PaqetTunnel'
$Script:UpstreamRepo = 'hanselime/paqet'
$Script:RepoUrl = "https://github.com/$($Script:Repo).git"
$Script:RawUrl = "https://raw.githubusercontent.com/$($Script:Repo)/master"
$Script:ApiUrl = "https://api.github.com/repos/$($Script:Repo)"
$Script:UpstreamApiUrl = "https://api.github.com/repos/$($Script:UpstreamRepo)"
$Script:AppName = 'Paqet Tunnel'
$Script:ExeName = 'PaqetTunnel.exe'
$Script:InstallDir = "$env:LOCALAPPDATA\PaqetTunnel"
$Script:DataDir = "$env:LOCALAPPDATA\PaqetTunnel"
$Script:LegacyInstallDir = "$env:ProgramFiles\Paqet Tunnel"
$Script:SourceDir = "$env:USERPROFILE\PaqetTunnel"
$Script:PaqetBin = 'paqet_windows_amd64.exe'

# Self-update: if running from a local clone that's behind origin, pull and re-exec
if (-not $env:PAQET_SETUP_REEXEC -and $Command -and (Test-Path "$env:USERPROFILE\PaqetTunnel\.git")) {
    try {
        $oldEAP = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        Push-Location "$env:USERPROFILE\PaqetTunnel"
        $selfHash = (& git hash-object setup.ps1 2>$null)
        & git fetch --all --quiet 2>$null
        & git reset --hard origin/master --quiet 2>$null
        $newHash = (& git hash-object setup.ps1 2>$null)
        Pop-Location
        $ErrorActionPreference = $oldEAP
        if ($selfHash -and $newHash -and ($selfHash -ne $newHash)) {
            $env:PAQET_SETUP_REEXEC = '1'
            & "$env:USERPROFILE\PaqetTunnel\setup.ps1" @PSBoundParameters
            Remove-Item Env:\PAQET_SETUP_REEXEC -ErrorAction SilentlyContinue
            exit $LASTEXITCODE
        }
    } catch {}
}

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

function Download-File($url, $outFile) {
    # Use curl.exe (built into Windows 10+) — handles GitHub/TLS reliably
    $curlExe = "$env:SystemRoot\System32\curl.exe"
    if (Test-Path $curlExe) {
        $dir = Split-Path $outFile -Parent
        if ($dir -and -not (Test-Path $dir)) { New-Item -Path $dir -ItemType Directory -Force | Out-Null }
        $oldEAP = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & $curlExe -fSL --retry 3 --retry-delay 2 -o $outFile $url 2>$null
        $ec = $LASTEXITCODE; $ErrorActionPreference = $oldEAP
        if ($ec -eq 0 -and (Test-Path $outFile)) { return $true }
    }
    try {
        Invoke-WebRequest -Uri $url -OutFile $outFile -UseBasicParsing -ErrorAction Stop
        return $true
    } catch {
        Err "Download failed: $url"
        return $false
    }
}

function Download-String($url) {
    $curlExe = "$env:SystemRoot\System32\curl.exe"
    if (Test-Path $curlExe) {
        $tmpFile = "$env:TEMP\dl-$([guid]::NewGuid().ToString('N').Substring(0,8)).tmp"
        $oldEAP = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & $curlExe -fSL --retry 3 --retry-delay 2 -o $tmpFile $url 2>$null
        $ec = $LASTEXITCODE; $ErrorActionPreference = $oldEAP
        if ($ec -eq 0 -and (Test-Path $tmpFile)) {
            $content = Get-Content $tmpFile -Raw
            Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue
            return $content
        }
        Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue
    }
    try {
        return (Invoke-WebRequest -Uri $url -UseBasicParsing -ErrorAction Stop).Content
    } catch {
        return $null
    }
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

function Clean-LegacyInstall {
    # Remove old Program Files installation (pre-v1.1 installed there)
    if (Test-Path $Script:LegacyInstallDir) {
        Step "Removing legacy install from $Script:LegacyInstallDir..."
        # Run InnoSetup uninstaller if present
        $uninst = "$Script:LegacyInstallDir\unins000.exe"
        if (Test-Path $uninst) {
            Start-Process $uninst -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -ErrorAction SilentlyContinue
        }
        Remove-Item $Script:LegacyInstallDir -Recurse -Force -ErrorAction SilentlyContinue
        OK "Legacy install removed"
    }
    # Clean legacy registry autostart (InnoSetup used HKCU\Run)
    $regPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Remove-ItemProperty -Path $regPath -Name 'PaqetTunnel' -ErrorAction SilentlyContinue
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
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    if ($Command) { $argList += " $Command" }
    if ($Server) { $argList += ' -Server' }
    if ($Addr) { $argList += " -Addr `"$Addr`"" }
    if ($Key) { $argList += " -Key `"$Key`"" }
    if ($Iface) { $argList += " -Iface `"$Iface`"" }
    if ($SocksPort -ne 10800) { $argList += " -SocksPort $SocksPort" }
    if ($Force) { $argList += ' -Force' }
    if ($Yes) { $argList += ' -y' }
    Start-Process powershell -ArgumentList $argList -Verb RunAs -Wait
    exit
}

# ═══════════════════════════════════════════════════════════════════
# Network Detection
# ═══════════════════════════════════════════════════════════════════

function Detect-Network {
    $result = @{Interface=''; IP=''; RouterMAC=''; GUID=''; Gateway=''}

    try {
        # Get default route interface
        $route = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($route) {
            $idx = $route.InterfaceIndex
            $result.Gateway = $route.NextHop

            $adapter = Get-NetAdapter -InterfaceIndex $idx -ErrorAction SilentlyContinue
            if ($adapter) {
                $result.Interface = $adapter.Name
                $guidRaw = $adapter.InterfaceGuid
                if ($guidRaw) { $result.GUID = "\Device\NPF_$guidRaw" }
            }

            $ipAddr = Get-NetIPAddress -InterfaceIndex $idx -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($ipAddr) { $result.IP = $ipAddr.IPAddress }

            # Get gateway MAC from ARP table
            if ($result.Gateway) {
                # Ping gateway to ensure ARP entry exists
                ping -n 1 -w 500 $result.Gateway 2>$null | Out-Null
                $neighbor = Get-NetNeighbor -IPAddress $result.Gateway -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($neighbor -and $neighbor.LinkLayerAddress) {
                    $result.RouterMAC = ($neighbor.LinkLayerAddress -replace '-',':').ToLower()
                }
            }
        }
    } catch {}

    return $result
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
        if (-not (Download-File $fallbackUrl $dl)) { Err "Download failed for $name"; return $false }
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
    if (Has-Command 'git') { OK "Git already installed"; return $true }
    # Check common paths
    foreach ($p in @("$env:ProgramFiles\Git\cmd", "${env:ProgramFiles(x86)}\Git\cmd", "$env:ProgramFiles\Git\mingw64\bin")) {
        if (Test-Path "$p\git.exe") {
            if ($env:PATH -notlike "*$p*") { $env:PATH = "$p;$env:PATH" }
            if (Has-Command 'git') { OK "Git found at $p"; return $true }
        }
    }

    Step "Installing Git..."

    # Try winget first
    if (Has-Command 'winget') {
        try {
            & winget install --id Git.Git --accept-source-agreements --accept-package-agreements --silent 2>&1 | Out-Null
            $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                        [System.Environment]::GetEnvironmentVariable('PATH', 'User')
            if (Has-Command 'git') { OK "Git installed via winget"; return $true }
        } catch {}
    }

    # Fallback: use MinGit (portable, no installer needed — fast and reliable)
    Step "Downloading MinGit..."
    $arch = if ((Get-Arch) -eq 'arm64') { 'arm64' } else { '64-bit' }
    $minGitUrl = $null
    try {
        $json = Download-String 'https://api.github.com/repos/git-for-windows/git/releases/latest'
        if ($json) {
            $rel = $json | ConvertFrom-Json
            $asset = $rel.assets | Where-Object { $_.name -match "MinGit-.*-$arch\.zip$" -and $_.name -notmatch 'busybox' } | Select-Object -First 1
            if ($asset) { $minGitUrl = $asset.browser_download_url }
        }
    } catch {}
    if (-not $minGitUrl) { $minGitUrl = "https://github.com/git-for-windows/git/releases/download/v2.47.1.windows.1/MinGit-2.47.1-64-bit.zip" }

    $dl = "$env:TEMP\mingit.zip"
    if (-not (Download-File $minGitUrl $dl)) { Err "Git download failed"; return $false }

    $gitDir = "$env:ProgramFiles\Git"
    Step "Extracting Git..."
    Expand-Archive $dl -DestinationPath $gitDir -Force
    Remove-Item $dl -Force -ErrorAction SilentlyContinue

    # Add to PATH (current session + persistent)
    $cmdDir = "$gitDir\cmd"
    if (-not (Test-Path $cmdDir)) { $cmdDir = "$gitDir\mingw64\bin" }
    if (Test-Path $cmdDir) {
        $env:PATH = "$cmdDir;$env:PATH"
        $machinePath = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine')
        if ($machinePath -notlike "*$cmdDir*") {
            [System.Environment]::SetEnvironmentVariable('PATH', "$cmdDir;$machinePath", 'Machine')
        }
    }
    if (Has-Command 'git') { OK "Git installed (MinGit)"; return $true }
    Err "Git installation failed"
    return $false
}

function Ensure-Dotnet {
    # Check existing install (both Program Files and user-local)
    $dotnetPaths = @("$env:ProgramFiles\dotnet", "$env:LOCALAPPDATA\Microsoft\dotnet")
    foreach ($p in $dotnetPaths) {
        $exe = "$p\dotnet.exe"
        if (Test-Path $exe) {
            $ver = & $exe --version 2>$null
            if ($ver -match '^8\.') {
                if ($env:PATH -notlike "*$p*") { $env:PATH = "$p;$env:PATH" }
                OK ".NET 8 SDK ($ver)"
                return $true
            }
        }
    }

    # Try winget first
    if (Has-Command 'winget') {
        Step "Installing .NET 8 SDK via winget..."
        try {
            & winget install --id Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements --silent 2>&1 | Out-Null
            $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                        [System.Environment]::GetEnvironmentVariable('PATH', 'User')
            if (Has-Command 'dotnet') {
                $ver = & dotnet --version 2>$null
                if ($ver -match '^8\.') { OK ".NET 8 SDK ($ver)"; return $true }
            }
        } catch {}
    }

    # Fallback: use official dotnet-install script (installs to Program Files for system-wide use)
    Step "Installing .NET 8 SDK..."
    $instScript = "$env:TEMP\dotnet-install.ps1"
    if (-not (Download-File 'https://dot.net/v1/dotnet-install.ps1' $instScript)) { Err ".NET download failed"; return $false }
    $installDir = "$env:ProgramFiles\dotnet"
    & $instScript -Channel 8.0 -InstallDir $installDir
    if ($env:PATH -notlike "*$installDir*") { $env:PATH = "$installDir;$env:PATH" }
    # Persist to system PATH for future sessions
    $machinePath = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine')
    if ($machinePath -notlike "*$installDir*") {
        [System.Environment]::SetEnvironmentVariable('PATH', "$installDir;$machinePath", 'Machine')
    }
    $ver = & "$installDir\dotnet.exe" --version 2>$null
    if ($ver -match '^8\.') { OK ".NET 8 SDK ($ver)"; return $true }
    Err ".NET 8 SDK installation failed"
    return $false
}

function Ensure-Go {
    if (Has-Command 'go') {
        $ver = (& go version 2>$null) -replace 'go version go','' -replace ' .*',''
        OK ('Go (' + $ver + ')')
        return $true
    }
    $arch = Get-Arch
    $goArch = if ($arch -eq 'arm64') { 'arm64' } else { 'amd64' }

    # Try winget first
    if (Has-Command 'winget') {
        Step "Installing Go via winget..."
        try {
            & winget install --id GoLang.Go --accept-source-agreements --accept-package-agreements --silent 2>&1 | Out-Null
            $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                        [System.Environment]::GetEnvironmentVariable('PATH', 'User')
            if (Has-Command 'go') { $v = (& go version 2>$null) -replace 'go version go','' -replace ' .*',''; OK "Go ($v)"; return $true }
        } catch {}
    }

    # Fallback: detect latest Go version from go.dev and download zip (works in more regions than MSI)
    Step "Installing Go..."
    $latest = 'go1.25.0'
    try {
        $json = Download-String 'https://go.dev/dl/?mode=json'
        if ($json) {
            $dlPage = $json | ConvertFrom-Json
            $ver = ($dlPage | Where-Object { $_.stable -eq $true } | Select-Object -First 1).version
            if ($ver) { $latest = $ver }
        }
    } catch {}

    # Try MSI first, then zip as fallback
    $goUrl = "https://go.dev/dl/$latest.windows-$goArch.msi"
    Dim "Downloading $latest..."
    $dl = "$env:TEMP\go-setup.msi"
    $installed = $false

    if (Download-File $goUrl $dl) {
        Start-Process msiexec -ArgumentList "/i `"$dl`" /qn /norestart" -Wait
        Remove-Item $dl -Force -ErrorAction SilentlyContinue
        $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                    [System.Environment]::GetEnvironmentVariable('PATH', 'User')
        if (Has-Command 'go') { $installed = $true }
    }

    # Fallback: use zip archive (bypasses dl.google.com issues)
    if (-not $installed) {
        Dim "MSI failed, trying zip archive..."
        $goZipUrl = "https://go.dev/dl/$latest.windows-$goArch.zip"
        $zipDl = "$env:TEMP\go.zip"
        if (Download-File $goZipUrl $zipDl) {
            $goRoot = "$env:SystemDrive\Go"
            if (Test-Path $goRoot) { Remove-Item $goRoot -Recurse -Force -ErrorAction SilentlyContinue }
            Step "Extracting Go..."
            Expand-Archive $zipDl -DestinationPath "$env:SystemDrive\" -Force
            Remove-Item $zipDl -Force -ErrorAction SilentlyContinue
            $goBin = "$goRoot\bin"
            if (Test-Path "$goBin\go.exe") {
                $env:PATH = "$goBin;$env:PATH"
                $machinePath = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine')
                if ($machinePath -notlike "*$goBin*") {
                    [System.Environment]::SetEnvironmentVariable('PATH', "$goBin;$machinePath", 'Machine')
                }
                $installed = $true
            }
        }
        Remove-Item $zipDl -Force -ErrorAction SilentlyContinue
    }

    if ($installed -and (Has-Command 'go')) {
        $v = (& go version 2>$null) -replace 'go version go','' -replace ' .*',''
        OK "Go ($v)"
        return $true
    }
    Err "Go installation failed. Install manually from https://go.dev/dl/"
    return $false
}

function Ensure-Npcap {
    # Check if Npcap is installed
    $npcapDll = "$env:SystemRoot\System32\Npcap\wpcap.dll"
    if (Test-Path $npcapDll) { OK "Npcap already installed"; return $true }
    # Also check WinPcap compatibility path
    if (Test-Path "$env:SystemRoot\System32\wpcap.dll") { OK "WinPcap/Npcap found"; return $true }

    Step "Installing Npcap (required for packet capture)..."
    if (Has-Command 'winget') {
        try {
            & winget install --id Insecure.Npcap --accept-source-agreements --accept-package-agreements --silent 2>&1 | Out-Null
            if (Test-Path $npcapDll) { OK "Npcap installed via winget"; return $true }
        } catch {}
    }

    # Fallback: download and install
    $npcapUrl = 'https://npcap.com/dist/npcap-1.80.exe'
    $dl = "$env:TEMP\npcap-setup.exe"
    if (-not (Download-File $npcapUrl $dl)) { Err "Npcap download failed"; return $false }
    Start-Process $dl -ArgumentList '/S /winpcap_mode=yes' -Wait
    Remove-Item $dl -Force -ErrorAction SilentlyContinue
    if (Test-Path $npcapDll) { OK "Npcap installed"; return $true }
    # Check again with refreshed view
    if (Test-Path "$env:SystemRoot\System32\wpcap.dll") { OK "Npcap installed"; return $true }
    Warn "Npcap may need manual install from https://npcap.com"
    return $true
}

function Ensure-MinGW {
    if (Has-Command 'gcc') { OK "GCC (MinGW) found"; return $true }
    # Check common MinGW paths
    foreach ($p in @("$env:ProgramFiles\mingw64\bin", "C:\mingw64\bin", "C:\msys64\mingw64\bin")) {
        if (Test-Path "$p\gcc.exe") {
            $env:PATH = "$p;$env:PATH"
            OK "GCC found at $p"
            return $true
        }
    }

    Step "Installing MinGW (GCC for CGO)..."
    if (Has-Command 'winget') {
        try {
            & winget install --id MartinStorsjo.LLVM-MinGW --accept-source-agreements --accept-package-agreements --silent 2>&1 | Out-Null
            $env:PATH = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine') + ';' +
                        [System.Environment]::GetEnvironmentVariable('PATH', 'User')
            if (Has-Command 'gcc') { OK "MinGW installed via winget"; return $true }
        } catch {}
    }

    # Fallback: download MinGW-w64 (auto-detect latest)
    Step "Downloading MinGW-w64..."
    $mingwZipUrl = $null
    try {
        $rel = Invoke-RestMethod 'https://api.github.com/repos/brechtsanders/winlibs_mingw/releases/latest' -ErrorAction SilentlyContinue
        foreach ($a in $rel.assets) {
            if ($a.name -like 'winlibs-x86_64-posix-seh-gcc-*-mingw-w64*-*.zip' -and $a.name -notlike '*.sha*') {
                $mingwZipUrl = $a.browser_download_url
                break
            }
        }
    } catch {}
    if (-not $mingwZipUrl) {
        $mingwZipUrl = 'https://github.com/brechtsanders/winlibs_mingw/releases/download/15.2.0posix-13.0.0-msvcrt-r5/winlibs-x86_64-posix-seh-gcc-15.2.0-mingw-w64msvcrt-13.0.0-r5.zip'
    }
    $dl = "$env:TEMP\mingw.zip"
    if (-not (Download-File $mingwZipUrl $dl)) {
        Warn "MinGW download failed. Install GCC manually."
        return $false
    }
    Step "Extracting MinGW (this may take a few minutes)..."
    Expand-Archive $dl -DestinationPath "C:\" -Force
    Remove-Item $dl -Force -ErrorAction SilentlyContinue
    # Find gcc.exe
    $gccPath = Get-ChildItem "C:\mingw64\bin\gcc.exe" -ErrorAction SilentlyContinue
    if (-not $gccPath) { $gccPath = Get-ChildItem "C:\mingw*\bin\gcc.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1 }
    if ($gccPath) {
        $binDir = Split-Path $gccPath.FullName -Parent
        $env:PATH = "$binDir;$env:PATH"
        $machinePath = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine')
        if ($machinePath -notlike "*$binDir*") {
            [System.Environment]::SetEnvironmentVariable('PATH', "$binDir;$machinePath", 'Machine')
        }
        OK "MinGW installed"
        return $true
    }
    Warn "MinGW extraction issue. Install GCC manually."
    return $false
}

# ═══════════════════════════════════════════════════════════════════
# Build
# ═══════════════════════════════════════════════════════════════════

function Download-PaqetRelease {
    Step "Fetching latest paqet release..."
    $archName = if ((Get-Arch) -eq 'arm64') { 'arm64' } else { 'amd64' }
    $releaseUrl = "$Script:UpstreamApiUrl/releases/latest"
    $assetUrl = $null
    $tag = ''
    try {
        $json = Download-String $releaseUrl
        if ($json) {
            $rel = $json | ConvertFrom-Json
            $tag = $rel.tag_name
            $asset = $rel.assets | Where-Object { $_.name -match "paqet-windows-$archName" -and $_.name -match '\.zip$' } | Select-Object -First 1
            if ($asset) { $assetUrl = $asset.browser_download_url }
        }
    } catch {}

    if (-not $assetUrl) {
        Warn "Could not find paqet release for windows-$archName"
        return $null
    }
    Dim "Release: $tag"

    $pubDir = "$Script:SourceDir\publish"
    New-Item -Path $pubDir -ItemType Directory -Force | Out-Null

    $zipFile = "$env:TEMP\paqet-release.zip"
    Step "Downloading paqet ($archName)..."
    if (-not (Download-File $assetUrl $zipFile)) {
        Warn "Download failed"
        return $null
    }
    $extractDir = "$env:TEMP\paqet-rel"
    Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive $zipFile -DestinationPath $extractDir -Force
    $bin = Get-ChildItem $extractDir -Filter 'paqet_windows*' -Recurse | Select-Object -First 1
    if ($bin) {
        Copy-Item $bin.FullName "$pubDir\$Script:PaqetBin"
        OK "paqet downloaded ($tag, $([math]::Round($bin.Length/1MB, 1)) MB)"
    } else {
        Warn "Binary not found in release archive"
    }
    Remove-Item $zipFile -Force -ErrorAction SilentlyContinue
    Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue
    return $(if ($bin) { "$pubDir\$Script:PaqetBin" } else { $null })
}

function Build-FromSource {
    $src = $Script:SourceDir

    # Clone or pull
    if (Test-Path "$src\.git") {
        Step "Updating source..."
        Push-Location $src
        $oldEAP = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & git fetch --all --quiet 2>$null
        & git reset --hard origin/master --quiet 2>$null
        & git submodule update --init --recursive --quiet 2>$null
        $ErrorActionPreference = $oldEAP
        Pop-Location
    } else {
        Step "Cloning repository..."
        $oldEAP = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & git clone --recursive $Script:RepoUrl $src 2>$null
        $ErrorActionPreference = $oldEAP
    }
    OK "Source ready"

    # Get paqet binary — build or download
    $pubDir = "$src\publish"
    New-Item -Path $pubDir -ItemType Directory -Force | Out-Null
    $paqetBuilt = $false

    if ($Build -and (Has-Command 'go')) {
        Step "Building paqet from source..."
        Push-Location "$src\paqet"
        $env:CGO_ENABLED = '1'
        $env:GOOS = 'windows'
        $goArch = if ((Get-Arch) -eq 'arm64') { 'arm64' } else { 'amd64' }
        $env:GOARCH = $goArch
        $oldEAP = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & go build -trimpath -ldflags='-s -w' -o "$pubDir\$Script:PaqetBin" ./cmd/main.go 2>&1
        $buildResult = $LASTEXITCODE
        $ErrorActionPreference = $oldEAP
        Pop-Location
        if ($buildResult -eq 0 -and (Test-Path "$pubDir\$Script:PaqetBin")) {
            OK "paqet built from source"
            $paqetBuilt = $true
        } else {
            Warn "paqet local build failed (CGO/pcap issue), falling back to download"
        }
    }

    if (-not $paqetBuilt) {
        $result = Download-PaqetRelease
        if ($result -and (Test-Path "$pubDir\$Script:PaqetBin")) {
            $paqetBuilt = $true
        }
    }

    if (-not $paqetBuilt) {
        Err "paqet binary could not be built or downloaded"
        return $false
    }

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
        if (-not (Download-File $t2sUrl $zipFile)) { Err "tun2socks download failed"; return $false }
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
        if (-not (Download-File $wUrl $wZip)) { Err "WinTun download failed"; return $false }
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

# ═══════════════════════════════════════════════════════════════════
# Commands
# ═══════════════════════════════════════════════════════════════════

function Do-Install {
    param([switch]$ServerMode)
    Banner
    Step "Installing $Script:AppName$(if($ServerMode){' (Server)'}else{' (Client)'})..."
    WL ""

    # Add Defender exclusions before downloading binaries
    Require-Admin
    Add-DefenderExclusions
    WL ""

    # Clean legacy Program Files installation if present
    Clean-LegacyInstall

    # Check and install dependencies
    Step "Checking dependencies..."
    WL ""
    if (-not (Ensure-Git))    { Err "Git is required"; return }
    if (-not (Ensure-Dotnet)) { Err ".NET 8 SDK is required"; return }
    # Go + MinGW + Npcap only needed when building paqet from source
    if ($Build) {
        $hasGo = Ensure-Go
        if ($hasGo) {
            Ensure-MinGW | Out-Null
            Ensure-Npcap | Out-Null
        }
    }
    WL ""

    # Build from source (doesn't need admin)
    Step "Building from source..."
    WL ""
    if (-not (Build-FromSource)) { return }
    WL ""

    # Install step requires admin
    Require-Admin

    if ($ServerMode) {
        Do-ServerInstall
        return
    }

    # Install directly to AppData (single unified location)
    Step "Installing..."
    Stop-App
    New-Item -Path $Script:InstallDir -ItemType Directory -Force | Out-Null
    # Copy exe to install root, helper binaries to bin/
    $pubDir = "$Script:SourceDir\publish"
    Copy-Item "$pubDir\$Script:ExeName" "$Script:InstallDir\$Script:ExeName" -Force
    New-Item -Path "$Script:InstallDir\bin" -ItemType Directory -Force | Out-Null
    foreach ($f in @($Script:PaqetBin, 'tun2socks.exe', 'wintun.dll')) {
        $src = "$pubDir\$f"
        if (Test-Path $src) { Copy-Item $src "$Script:InstallDir\bin\$f" -Force }
    }
    OK "Installed to $Script:InstallDir"
    Create-Shortcuts

    # Save version
    $Script:AppVersion | Out-File "$Script:InstallDir\.version" -NoNewline

    # Reserve SOCKS5 port (same as InnoSetup post-install)
    & netsh int ipv4 add excludedportrange protocol=tcp startport=$SocksPort numberofports=1 2>&1 | Out-Null
    & netsh int ipv4 add excludedportrange protocol=udp startport=$SocksPort numberofports=1 2>&1 | Out-Null

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

    # Clean legacy Program Files installation if present
    Clean-LegacyInstall

    Push-Location $Script:SourceDir
    $oldEAP = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & git fetch --all --quiet 2>$null
    $local = & git rev-parse HEAD 2>$null
    $remote = & git rev-parse origin/master 2>$null
    $ErrorActionPreference = $oldEAP
    Pop-Location

    if ($local -eq $remote -and -not $Force) {
        OK "Already up to date ($($local.Substring(0,7)))"
        return
    }

    Step "Updating $($local.Substring(0,7)) → $($remote.Substring(0,7))..."
    WL ""

    if (-not (Build-FromSource)) { return }
    WL ""

    # Install directly to AppData
    Stop-App
    $pubDir = "$Script:SourceDir\publish"
    Copy-Item "$pubDir\$Script:ExeName" "$Script:InstallDir\$Script:ExeName" -Force
    New-Item -Path "$Script:InstallDir\bin" -ItemType Directory -Force | Out-Null
    foreach ($f in @($Script:PaqetBin, 'tun2socks.exe', 'wintun.dll')) {
        $src = "$pubDir\$f"
        if (Test-Path $src) { Copy-Item $src "$Script:InstallDir\bin\$f" -Force }
    }
    OK "Updated to $($remote.Substring(0,7))"

    $Script:AppVersion | Out-File "$Script:InstallDir\.version" -NoNewline
    WL ""
    OK "Update complete!"

    if (Confirm "Start $($Script:AppName)?") { Start-App }
}

function Do-Uninstall {
    Banner
    if (-not (Is-Installed) -and -not (Test-Path $Script:DataDir) -and -not (Test-Path $Script:SourceDir) -and -not (Test-Path $Script:LegacyInstallDir)) {
        Warn "$Script:AppName is not installed"
        return
    }

    if (-not (Confirm "Uninstall $($Script:AppName)? This removes all data.")) { return }
    Require-Admin

    Stop-App

    # Remove legacy Program Files installation
    Clean-LegacyInstall

    # Remove AppData installation
    if (Test-Path $Script:InstallDir) {
        Step "Removing files..."
        Remove-Item $Script:InstallDir -Recurse -Force -ErrorAction SilentlyContinue
        OK "App data removed"
    }

    # Clean autostart (scheduled task + legacy registry entry)
    & schtasks /delete /tn "PaqetTunnel" /f 2>&1 | Out-Null
    & schtasks /delete /tn "PaqetTunnelService" /f 2>&1 | Out-Null
    $regPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    Remove-ItemProperty -Path $regPath -Name 'PaqetTunnel' -ErrorAction SilentlyContinue
    # Clean startup folder shortcut
    $startupFolder = [Environment]::GetFolderPath('Startup')
    Remove-Item "$startupFolder\Paqet Tunnel.lnk" -Force -ErrorAction SilentlyContinue

    # Clean shortcuts
    $desktop = [Environment]::GetFolderPath('Desktop')
    Remove-Item "$desktop\Paqet Tunnel.lnk" -Force -ErrorAction SilentlyContinue
    Remove-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Paqet Tunnel.lnk" -Force -ErrorAction SilentlyContinue

    # Remove Defender exclusions
    foreach ($p in @($Script:InstallDir, $Script:LegacyInstallDir, $Script:SourceDir)) {
        try { Remove-MpPreference -ExclusionPath $p -ErrorAction SilentlyContinue } catch {}
    }

    # Clean source
    if (Test-Path $Script:SourceDir) {
        if (Confirm "Remove source code ($($Script:SourceDir))?") {
            Remove-Item $Script:SourceDir -Recurse -Force -ErrorAction SilentlyContinue
            OK "Source removed"
        }
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
    W "  Source    "; WL "$(if(Test-Path "$Script:SourceDir\.git"){'Yes'}else{'No'})" DarkGray
    if (Test-Path $Script:LegacyInstallDir) {
        W "  "; Warn "Legacy install found at $Script:LegacyInstallDir (run 'update' to clean)"
    }

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
        if ($content -match 'server:\s*\r?\n\s*addr:\s*"([^"]+)"') { Dim "Server: $($Matches[1])" }
        if ($content -match 'listen:\s*"([^"]+)"') { Dim "SOCKS5: $($Matches[1])" }
        if ($content -match 'protocol:\s*"([^"]+)"') { Dim "Transport: $($Matches[1])" }
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
    $port = ($serverAddr -split ':')[-1]

    # Auto-detect network
    $net = Detect-Network
    $ifaceName = if ($Iface) { $Iface } else { $net.Interface }
    $localIP = $net.IP
    $routerMAC = $net.RouterMAC
    $guid = $net.GUID

    if (-not $ifaceName) { $ifaceName = "Ethernet" }
    if (-not $localIP) { $localIP = "0.0.0.0" }
    if (-not $routerMAC) { $routerMAC = "00:00:00:00:00:00" }

    Dim "Interface: $ifaceName"
    Dim "Local IP:  $localIP"
    Dim "Router MAC: $routerMAC"

    # Escape backslashes for YAML double-quoted strings
    $yamlGuid = if ($guid) { $guid.Replace('\', '\\') } else { '' }

    # Create config
    $cfgDir = "$Script:DataDir\config"
    New-Item -Path $cfgDir -ItemType Directory -Force | Out-Null
    $serverCfg = @"
role: "server"
log:
  level: "info"
listen:
  addr: ":$port"
network:
  interface: "$ifaceName"
  guid: "$yamlGuid"
  ipv4:
    addr: "${localIP}:$port"
    router_mac: "$routerMAC"
  tcp:
    local_flag: ["PA"]
transport:
  protocol: "kcp"
  kcp:
    mode: "fast"
    block: "aes"
    key: "$secret"
"@
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText("$cfgDir\server.yaml", $serverCfg, $utf8NoBom)
    OK "Server config created"

    # Install as service
    Step "Installing Windows service..."
    $svcBin = "$Script:InstallDir\bin\$Script:PaqetBin"
    New-Item -Path "$Script:InstallDir\bin" -ItemType Directory -Force | Out-Null
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
    & netsh advfirewall firewall delete rule name="Paqet Server" 2>&1 | Out-Null
    & netsh advfirewall firewall add rule name="Paqet Server" dir=in action=allow protocol=udp localport=$port 2>&1 | Out-Null
    & netsh advfirewall firewall add rule name="Paqet Server TCP" dir=in action=allow protocol=tcp localport=$port 2>&1 | Out-Null
    OK "Firewall rules added (TCP+UDP $port)"

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
    $serverKey = if ($Key) { $Key } else { "your-key-here" }
    $socks = "127.0.0.1:$SocksPort"

    # Auto-detect network
    $net = Detect-Network
    $ifaceName = if ($Iface) { $Iface } else { $net.Interface }
    $localIP = $net.IP
    $routerMAC = $net.RouterMAC
    $guid = $net.GUID

    if (-not $ifaceName) { $ifaceName = "Ethernet" }
    if (-not $localIP) { $localIP = "0.0.0.0" }
    if (-not $routerMAC) { $routerMAC = "00:00:00:00:00:00" }

    Dim "Interface: $ifaceName"
    Dim "Local IP:  $localIP"
    Dim "Router MAC: $routerMAC"
    if ($guid) { Dim "GUID: $guid" }

    # Escape backslashes for YAML double-quoted strings
    $yamlGuid = if ($guid) { $guid.Replace('\', '\\') } else { '' }

    $cfg = @"
role: "client"
log:
  level: "info"
socks5:
  - listen: "$socks"
server:
  addr: "$serverAddr"
network:
  interface: "$ifaceName"
  guid: "$yamlGuid"
  ipv4:
    addr: "${localIP}:0"
    router_mac: "$routerMAC"
  tcp:
    local_flag: ["PA"]
    remote_flag: ["PA"]
transport:
  protocol: "kcp"
  kcp:
    mode: "fast"
    block: "aes"
    key: "$serverKey"
"@
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText("$cfgDir\client.yaml", $cfg, $utf8NoBom)
    OK "Client config saved"
}

function Add-DefenderExclusions {
    $paths = @($Script:InstallDir, $Script:DataDir, $Script:SourceDir)
    $changed = $false
    $existing = @()
    try { $existing = @((Get-MpPreference).ExclusionPath) } catch {}
    foreach ($p in $paths) {
        if ($p -and ($existing -notcontains $p)) {
            try {
                Add-MpPreference -ExclusionPath $p -ErrorAction Stop
                $changed = $true
            } catch {}
        }
    }
    if ($changed) { OK "Windows Defender exclusions added" }
    else { Dim "Defender exclusions already configured" }
}

function Stop-App {
    'PaqetTunnel','tun2socks','paqet_windows_amd64' | ForEach-Object {
        Get-Process -Name $_ -ErrorAction SilentlyContinue | ForEach-Object {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep 1
}

function Create-Shortcuts {
    $exe = "$Script:InstallDir\$Script:ExeName"
    if (-not (Test-Path $exe)) { return }
    $ws = New-Object -ComObject WScript.Shell

    # Desktop shortcut
    try {
        $desktop = [Environment]::GetFolderPath('Desktop')
        $lnk = $ws.CreateShortcut("$desktop\Paqet Tunnel.lnk")
        $lnk.TargetPath = $exe
        $lnk.WorkingDirectory = $Script:InstallDir
        $lnk.Description = 'Paqet Tunnel'
        $iconPath = "$Script:InstallDir\$Script:ExeName"
        if (Test-Path $iconPath) { $lnk.IconLocation = "$iconPath,0" }
        $lnk.Save()
        OK "Desktop shortcut created"
    } catch { Warn "Desktop shortcut failed" }

    # Start Menu shortcut
    try {
        $startMenu = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs"
        $lnk = $ws.CreateShortcut("$startMenu\Paqet Tunnel.lnk")
        $lnk.TargetPath = $exe
        $lnk.WorkingDirectory = $Script:InstallDir
        $lnk.Description = 'Paqet Tunnel'
        $iconPath = "$Script:InstallDir\$Script:ExeName"
        if (Test-Path $iconPath) { $lnk.IconLocation = "$iconPath,0" }
        $lnk.Save()
        OK "Start Menu shortcut created"
    } catch { Warn "Start Menu shortcut failed" }
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
    WL "    -Build              Build from source (default: download release)" DarkGray
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

switch ($(if ($Command) { $Command.ToLower() } else { '' })) {
    'install'   { if ($Server) { Do-Install -ServerMode } else { Do-Install } }
    'update'    { Do-Update }
    'uninstall' { Do-Uninstall }
    'status'    { Do-Status }
    'server'    { Do-Install -ServerMode }
    'help'      { Show-Help }
    ''          { Show-Interactive }
    default     { Err "Unknown command: $Command"; Show-Help }
}
