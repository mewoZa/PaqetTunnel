; ──────────────────────────────────────────────────────────────
; Paqet Tunnel — InnoSetup Installer Script
; Builds: PaqetTunnelSetup.exe
;
; Installs PaqetTunnel.exe to {autopf}\Paqet Tunnel
; Creates data dirs at %LOCALAPPDATA%\PaqetTunnel\{bin,config,logs}
; Bundles paqet binary, tun2socks, wintun if present in publish\ folder
; ──────────────────────────────────────────────────────────────

#define MyAppName "Paqet Tunnel"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Paqet"
#define MyAppURL "https://github.com/mewoZa/PaqetTunnel"
#define MyAppExeName "PaqetTunnel.exe"
#define PaqetBinary "paqet_windows_amd64.exe"
#define Tun2Socks "tun2socks.exe"
#define WintunDll "wintun.dll"

[Setup]
AppId={{B8F2A3C7-4D5E-6F7A-8B9C-0D1E2F3A4B5C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=Output
OutputBaseFilename=PaqetTunnelSetup
SetupIconFile=..\src\PaqetTunnel\Assets\paqet.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Start automatically with Windows"; GroupDescription: "Other options:"

[Files]
; Main application binary
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; Bundle paqet binary if present (optional — app will auto-download if missing)
Source: "..\publish\{#PaqetBinary}"; DestDir: "{localappdata}\PaqetTunnel\bin"; Flags: ignoreversion skipifsourcedoesntexist
; TUN binaries (optional — app will auto-download if missing)
Source: "..\publish\{#Tun2Socks}"; DestDir: "{localappdata}\PaqetTunnel\bin"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\publish\{#WintunDll}"; DestDir: "{localappdata}\PaqetTunnel\bin"; Flags: ignoreversion skipifsourcedoesntexist

[Dirs]
; Create data directories under %LOCALAPPDATA%\PaqetTunnel
Name: "{localappdata}\PaqetTunnel"
Name: "{localappdata}\PaqetTunnel\bin"
Name: "{localappdata}\PaqetTunnel\config"
Name: "{localappdata}\PaqetTunnel\logs"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PaqetTunnel"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Kill tun2socks before uninstall (restore routes)
Filename: "taskkill"; Parameters: "/IM {#Tun2Socks} /F"; Flags: runhidden; RunOnceId: "KillTun2Socks"
; Kill paqet tunnel process before uninstall
Filename: "taskkill"; Parameters: "/IM {#PaqetBinary} /F"; Flags: runhidden; RunOnceId: "KillPaqet"
; Kill the manager app before uninstall
Filename: "taskkill"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillPaqetTunnel"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
; Clean up data directory on uninstall
Type: filesandordirs; Name: "{localappdata}\PaqetTunnel"
; Also clean old pre-rename data directory
Type: filesandordirs; Name: "{localappdata}\PaqetManager"

[Code]
// Kill running instances before install/upgrade
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssInstall then
  begin
    Exec('taskkill', '/IM ' + '{#MyAppExeName}' + ' /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // Also kill old PaqetManager.exe (pre-rename) if running
    Exec('taskkill', '/IM PaqetManager.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill', '/IM ' + '{#Tun2Socks}' + ' /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill', '/IM ' + '{#PaqetBinary}' + ' /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
    // Clean old autostart registry entry from pre-rename
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'PaqetManager');
  end;
  if CurStep = ssPostInstall then
  begin
    // Reserve SOCKS5 port to prevent ICS/SharedAccess from stealing it
    Exec('netsh', 'int ipv4 add excludedportrange protocol=tcp startport=10800 numberofports=1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('netsh', 'int ipv4 add excludedportrange protocol=udp startport=10800 numberofports=1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
