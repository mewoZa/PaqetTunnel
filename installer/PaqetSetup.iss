; ──────────────────────────────────────────────────────────────
; Paqet Manager — InnoSetup Installer Script
; Builds: PaqetManagerSetup.exe
;
; Installs PaqetManager.exe to {autopf}\Paqet Manager
; Creates data dirs at %LOCALAPPDATA%\PaqetManager\{bin,config,logs}
; Bundles paqet binary, tun2socks, wintun if present in publish\ folder
; ──────────────────────────────────────────────────────────────

#define MyAppName "Paqet Manager"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Paqet"
#define MyAppURL "https://github.com/hanselime/paqet"
#define MyAppExeName "PaqetManager.exe"
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
OutputBaseFilename=PaqetManagerSetup
SetupIconFile=..\src\PaqetManager\Assets\paqet.ico
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
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start automatically with Windows"; GroupDescription: "Other options:"

[Files]
; Main application binary
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; Bundle paqet binary if present (optional — app will auto-download if missing)
Source: "..\publish\{#PaqetBinary}"; DestDir: "{localappdata}\PaqetManager\bin"; Flags: ignoreversion skipifsourcedoesntexist
; TUN binaries (optional — app will auto-download if missing)
Source: "..\publish\{#Tun2Socks}"; DestDir: "{localappdata}\PaqetManager\bin"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\publish\{#WintunDll}"; DestDir: "{localappdata}\PaqetManager\bin"; Flags: ignoreversion skipifsourcedoesntexist

[Dirs]
; Create data directories under %LOCALAPPDATA%\PaqetManager
Name: "{localappdata}\PaqetManager"
Name: "{localappdata}\PaqetManager\bin"
Name: "{localappdata}\PaqetManager\config"
Name: "{localappdata}\PaqetManager\logs"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "PaqetManager"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Kill tun2socks before uninstall (restore routes)
Filename: "taskkill"; Parameters: "/IM {#Tun2Socks} /F"; Flags: runhidden; RunOnceId: "KillTun2Socks"
; Kill paqet tunnel process before uninstall
Filename: "taskkill"; Parameters: "/IM {#PaqetBinary} /F"; Flags: runhidden; RunOnceId: "KillPaqet"
; Kill the manager app before uninstall
Filename: "taskkill"; Parameters: "/IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillPaqetManager"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
; Clean up data directory on uninstall
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
    Exec('taskkill', '/IM ' + '{#Tun2Socks}' + ' /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec('taskkill', '/IM ' + '{#PaqetBinary}' + ' /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
  end;
end;
