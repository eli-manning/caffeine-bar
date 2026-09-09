; Inno Setup script for Caffeine Bar.
;
; Produces a single Caffeine-Bar-Setup.exe that carries both x64 and arm64
; payloads and installs whichever matches the machine, so there is only ever one
; download link and no architecture for the user to get wrong.
;
; Per-user by design (PrivilegesRequired=lowest): a tray utility has no business
; asking for admin at install time, and installing under the user's own
; AppData means no UAC prompt at all.
;
; Build:  iscc /DAppVersion=1.3.0 CaffeineBar.iss
; Expects payload\win-x64\ and payload\win-arm64\ to already be published.

#define AppName "Caffeine Bar"
#define AppExe "CaffeineBar.exe"
#define AppPublisher "Eli Manning"
#define AppUrl "https://github.com/eli-manning/caffeine-bar"

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
; Never change AppId: it is how Windows recognises an existing install and
; upgrades it in place rather than stacking a second copy beside it.
AppId={{7B2A9C4E-5D31-4F8A-9E6B-3C1D8A0F42B7}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

; With PrivilegesRequired=lowest, {autopf} resolves to
; %LOCALAPPDATA%\Programs, matching what build.ps1 -Install uses.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=dist
OutputBaseFilename=Caffeine-Bar-Setup
SetupIconFile=..\Resources\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible or arm64

; The app is a tray icon with no window, so a running copy would otherwise
; lock its own executable and fail the upgrade. Restart Manager closes it.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start {#AppName} when I sign in"; GroupDescription: "Additional options:"

[Files]
; Exactly one of these two runs. Arm64 Windows can emulate x64, but shipping
; the native build avoids the emulation penalty on a process that redraws the
; tray icon every frame while animating.
Source: "payload\win-x64\*"; DestDir: "{app}"; \
  Flags: recursesubdirs createallsubdirs ignoreversion; Check: not IsArm64
Source: "payload\win-arm64\*"; DestDir: "{app}"; \
  Flags: recursesubdirs createallsubdirs ignoreversion; Check: IsArm64

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Registry]
; Written in the same format LaunchAtLogin.cs reads and writes, so the app's own
; "Launch at Login" toggle stays in sync with whatever was chosen here rather
; than showing the opposite state.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "CaffeineBar"; ValueData: """{app}\{#AppExe}"""; \
  Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Run {#AppName} now"; \
  Flags: nowait postinstall skipifsilent

[Code]
// The lid-close feature registers two elevated scheduled tasks. They were
// created with admin rights, so a per-user uninstaller cannot remove them;
// point the user at the one command that does rather than leaving them behind
// silently. Only mentioned when the tasks actually exist.
function TasksExist(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\schtasks.exe'),
    '/query /tn "Caffeine Bar\Disable Lid Sleep"', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and TasksExist() then
    MsgBox('Caffeine Bar registered two scheduled tasks for "Prevent Sleep on '
      + 'Lid Close". Removing them needs administrator rights, so run this in an '
      + 'admin terminal if you want them gone:'#13#10#13#10
      + 'schtasks /delete /tn "Caffeine Bar\Disable Lid Sleep" /f'#13#10
      + 'schtasks /delete /tn "Caffeine Bar\Restore Lid Sleep" /f',
      mbInformation, MB_OK);
end;
