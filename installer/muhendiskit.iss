; MühendisKit setup (Inno Setup 6). Build with installer\build.ps1, which builds the web modules and publishes first.
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{5B7E2C91-3D4A-4E8F-A1C6-9F2D8B4E7A30}
AppName=MühendisKit
AppVersion={#AppVersion}
AppVerName=MühendisKit {#AppVersion}
AppPublisher=MühendisKit
AppPublisherURL=https://github.com/Furkan-Erg
; Fixed folder: Claude's MCP registration points at {app}\MuhendisKit.Mcp.exe, the same path on every PC.
DefaultDirName={autopf}\MuhendisKit
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=MuhendisKit-Setup-{#AppVersion}
SetupIconFile=..\src\MuhendisKit\Assets\app.ico
UninstallDisplayIcon={app}\MuhendisKit.exe
UninstallDisplayName=MühendisKit
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
ChangesAssociations=yes
VersionInfoVersion={#AppVersion}
VersionInfoDescription=MühendisKit kurulumu

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne MühendisKit kısayolu oluştur"
Name: "claude"; Description: "Claude'a bağla (bu bilgisayarda Claude Code veya Claude Desktop varsa, MCP sunucusunu kaydeder)"

[Files]
Source: "..\publish\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not WebView2Installed

[Icons]
Name: "{autoprograms}\MühendisKit"; Filename: "{app}\MuhendisKit.exe"
Name: "{autodesktop}\MühendisKit"; Filename: "{app}\MuhendisKit.exe"; Tasks: desktopicon

[Registry]
; Double-clicking a project file opens it in MühendisKit.
Root: HKA; Subkey: "Software\Classes\.mkproj"; ValueType: string; ValueName: ""; ValueData: "MuhendisKit.Project"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\MuhendisKit.Project"; ValueType: string; ValueName: ""; ValueData: "MühendisKit projesi"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\MuhendisKit.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\MuhendisKit.exe,0"
Root: HKA; Subkey: "Software\Classes\MuhendisKit.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\MuhendisKit.exe"" ""%1"""

[Run]
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Microsoft Edge WebView2 kuruluyor..."; Check: not WebView2Installed; Flags: waituntilterminated
; Claude config lives in the user's profile, so this runs as the user who started setup, not as admin.
Filename: "{app}\MuhendisKit.exe"; Parameters: "--register-claude"; StatusMsg: "Claude'a bağlanıyor..."; Tasks: claude; Flags: runasoriginaluser waituntilterminated runhidden
Filename: "{app}\MuhendisKit.exe"; Description: "MühendisKit'i başlat"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
; Caches only (WebView2 profile, converted drawings, CAD fonts). Projects in Documents and settings stay.
Type: filesandordirs; Name: "{localappdata}\MuhendisKit"

[Code]
function WebView2Installed: Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

// Both exes may be running: the window, and the MCP server Claude started (it locks its files).
procedure StopRunning;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im MuhendisKit.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im MuhendisKit.Mcp.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunning;
  Result := '';
end;

function InitializeUninstall: Boolean;
begin
  StopRunning;
  Result := True;
end;
