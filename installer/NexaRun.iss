; NexaRun Inno Setup Script
;
; ALWAYS build via:  installer\build.ps1
; That script dotnet publish -c Release to installer\publish, then runs ISCC with /DPubDir.
; Do NOT point PubDir at bin\Debug or bin\Release under each project — only installer\publish.

#ifndef AppVersion
  #error AppVersion must be passed via /DAppVersion=... (use installer\build.ps1)
#endif
#ifndef PubDir
  #define PubDir "publish"
#endif
#ifndef OutDir
  #define OutDir "output"
#endif
#define AppName      "NexaRun"
#define AppPublisher "Skybin Technology Private Limited"
#define AppCopyright "Copyright (C) Skybin Technology Private Limited"
#define AppURL       "https://github.com/skybin-tech/NexaRun"
#define ServiceName  "NexaRunDaemon"
#define ServiceExe   "NexaRun.Daemon.exe"
#define TrayExe      "NexaRun.exe"
#define CliExe       "NexaRun.Cli.exe"
[Setup]
AppId={{E2A4F1B3-7C5D-4E8A-9F2B-1D3C6A8E0F47}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppCopyright={#AppCopyright}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
OutputDir={#OutDir}
OutputBaseFilename=NexaRun-{#AppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
; Relative to this .iss file (installer/). Do not pass absolute paths via /D — backslashes break (\N in NexaRun).
SetupIconFile=assets\NexaRun.ico
UninstallDisplayIcon={app}\{#TrayExe}
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Notify Windows (and new terminals) that PATH / environment changed
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startwitwindows"; Description: "Start NexaRun Tray at Windows login (all users)"; GroupDescription: "Startup:"; Flags: unchecked
Name: "desktopicon";     Description: "Create a &desktop shortcut for NexaRun Tray"; GroupDescription: "Additional icons:"

[Files]
; Daemon (Windows Service)
Source: "{#PubDir}\daemon\{#ServiceExe}"; DestDir: "{app}"; Flags: ignoreversion

; CLI — also copied to bin\ as "nexarun.exe" so it works from any terminal
Source: "{#PubDir}\cli\{#CliExe}"; DestDir: "{app}";     Flags: ignoreversion
Source: "{#PubDir}\cli\{#CliExe}"; DestDir: "{app}\bin"; Flags: ignoreversion; DestName: "nexarun.exe"

; Tray app
Source: "{#PubDir}\tray\{#TrayExe}";      DestDir: "{app}";      Flags: ignoreversion
Source: "..\nexarun-processes.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "fix-path.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; Lets Windows resolve nexarun.exe without relying on PATH alone (Shell / some launchers)
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\nexarun.exe"; \
  ValueType: string; ValueName: ""; ValueData: "{app}\bin\nexarun.exe"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\nexarun.exe"; \
  ValueType: string; ValueName: "Path"; ValueData: "{app}\bin"; Flags: uninsdeletekey

[Icons]
; Start Menu
Name: "{group}\NexaRun Tray";        Filename: "{app}\{#TrayExe}";   Comment: "NexaRun system tray app"
Name: "{group}\Uninstall NexaRun";   Filename: "{uninstallexe}"

; Desktop shortcut (optional task) — elevated: the tray needs admin to reach the daemon
Name: "{autodesktop}\NexaRun";       Filename: "{app}\{#TrayExe}";   Tasks: desktopicon; Flags: runasadmin

; Startup (optional task) — {commonstartup} matches admin install (machine-wide service)
; runasadmin makes Windows launch it with the UAC elevation verb at every user's logon
Name: "{commonstartup}\NexaRun Tray"; Filename: "{app}\{#TrayExe}"; Tasks: startwitwindows; Flags: runasadmin

[Run]
; Install and start the Windows Service silently after install
Filename: "sc.exe"; Parameters: "create ""{#ServiceName}"" binPath= ""{app}\{#ServiceExe}"" start= auto DisplayName= ""NexaRun Daemon"""; \
  Flags: runhidden waituntilterminated; StatusMsg: "Installing NexaRun Daemon service..."
Filename: "sc.exe"; Parameters: "description ""{#ServiceName}"" ""NexaRun background process manager daemon"""; \
  Flags: runhidden waituntilterminated
Filename: "sc.exe"; Parameters: "start ""{#ServiceName}"""; \
  Flags: runhidden waituntilterminated; StatusMsg: "Starting NexaRun Daemon..."

; Event Viewer source for tray crash reports (Application log)
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""if (-not [System.Diagnostics.EventLog]::SourceExists('NexaRun-Tray')) {{ New-EventLog -LogName Application -Source 'NexaRun-Tray' }}"""; \
  Flags: runhidden waituntilterminated

; Launch tray app after install (no console window)
Filename: "{app}\{#TrayExe}"; \
  Description: "Launch NexaRun Tray"; \
  Flags: runhidden nowait postinstall skipifsilent

; Ensure the data root exists before securing it (icacls is a no-op on a
; missing path — the daemon will create it on first run, but a fresh install
; has no prior data dir yet).
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""New-Item -ItemType Directory -Path ''%ProgramData%\NexaRun'' -Force | Out-Null"""; \
  Flags: runhidden nowait; StatusMsg: "Preparing %ProgramData%\NexaRun ..."

; Secure the data root (%ProgramData%\NexaRun) so only LocalSystem and
; Administrators have access. This is the installer-time equivalent of the
; daemon's ApplyDataSecurity() (NexaRun.Shared/NexaRunPaths.cs), which also
; runs at daemon startup as LocalSystem and strips stale permissive ACEs on
; every boot. The icacls commands below:
;   1. Set the owner to LocalSystem so the service can always reset/repair
;      its own DACL even if a previous install left it owned by a user.
;   2. Grant LocalSystem and the built-in Administrators group full control
;      with object + container inheritance (OI)(CI). No other principal
;      (Authenticated Users, Users, World, etc.) is granted — so secrets in
;      processes.json / settings.json and the log files under logs\ are
;      only readable/writable by a privileged principal.
; /c continues on per-file errors, /q suppresses output; both are runhidden.
;
; Inno's [Run] cannot carry multiple /grant* arguments in one icacls call
; (Inno flattens the parameter string), so the two grants run as separate
; [Run] entries against the same path.
Filename: "icacls.exe"; \
  Parameters: """%ProgramData%\NexaRun"" /setowner:""NT AUTHORITY\System"" /c /q"; \
  Flags: runhidden nowait; StatusMsg: "Securing %ProgramData%\NexaRun (owner: LocalSystem)..."
Filename: "icacls.exe"; \
  Parameters: """%ProgramData%\NexaRun"" /grant*:""NT AUTHORITY\SYSTEM:(OI)(CI)(F)"" /c /q"; \
  Flags: runhidden nowait; StatusMsg: "Securing %ProgramData%\NexaRun (LocalSystem full control)..."
Filename: "icacls.exe"; \
  Parameters: """%ProgramData%\NexaRun"" /grant*:""BUILTIN\Administrators:(OI)(CI)(F)"" /c /q"; \
  Flags: runhidden nowait; StatusMsg: "Securing %ProgramData%\NexaRun (Administrators full control)..."

; Ensure system PATH + broadcast (fixes upgrades that predate PATH logic)
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\fix-path.ps1"" -InstallDir ""{app}"""; \
  Flags: runhidden waituntilterminated; StatusMsg: "Adding nexarun to PATH..."

[UninstallRun]
; Stop and remove the Windows Service on uninstall (RunOnceId = run each entry once per uninstall)
Filename: "sc.exe"; Parameters: "stop ""{#ServiceName}"""; \
  Flags: runhidden waituntilterminated; RunOnceId: "NexaRunDaemonStop"
Filename: "sc.exe"; Parameters: "delete ""{#ServiceName}"""; \
  Flags: runhidden waituntilterminated; RunOnceId: "NexaRunDaemonDelete"
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""if ([System.Diagnostics.EventLog]::SourceExists('NexaRun-Tray')) {{ Remove-EventLog -Source 'NexaRun-Tray' }}"""; \
  Flags: runhidden waituntilterminated; RunOnceId: "NexaRunTrayEventLogRemove"

[UninstallDelete]
; Leave %ProgramData%\NexaRun (shared daemon/CLI/tray data) — only remove install dir
Type: filesandordirs; Name: "{app}"
Type: files; Name: "{commonstartup}\NexaRun Tray.lnk"

[Code]
const
  EnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  WM_SETTINGCHANGE = $001A;
  SMTO_ABORTIFHUNG = $0002;

function SendMessageTimeout(hWnd: LongInt; Msg, wParam, lParam, fuFlags, uTimeout: LongWord;
  var lpdwResult: LongWord): LongWord;
  external 'SendMessageTimeoutW@user32.dll stdcall';

procedure RefreshEnvironment();
var
  Dummy: LongWord;
begin
  SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, 0, SMTO_ABORTIFHUNG, 5000, Dummy);
end;

function NormalizePath(const S: string): string;
begin
  Result := UpperCase(Trim(S));
  while (Length(Result) > 0) and (Result[Length(Result)] = '\') do
    SetLength(Result, Length(Result) - 1);
end;

function PathContainsEntry(const Path, Entry: string): Boolean;
begin
  Result := Pos(';' + NormalizePath(Entry) + ';', ';' + NormalizePath(Path) + ';') > 0;
end;

function NeedsAddPath(Param: string): Boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', OrigPath) then
  begin
    Result := True;
    exit;
  end;
  Result := not PathContainsEntry(OrigPath, Param);
end;

procedure AddBinToSystemPath();
var
  BinPath, OrigPath, NewPath: string;
begin
  BinPath := ExpandConstant('{app}\bin');
  if not NeedsAddPath(BinPath) then
    exit;

  if RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', OrigPath) then
  begin
    if OrigPath = '' then
      NewPath := BinPath
    else
      NewPath := OrigPath + ';' + BinPath;
  end
  else
    NewPath := BinPath;

  if RegWriteExpandStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', NewPath) then
    RefreshEnvironment();
end;

procedure RemoveBinFromSystemPath();
var
  OrigPath, BinPath, NewPath: string;
  P: Integer;
begin
  BinPath := ExpandConstant('{app}\bin');

  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', OrigPath) then
    exit;

  NewPath := OrigPath;

  P := Pos(';' + BinPath, NewPath);
  if P > 0 then
    Delete(NewPath, P, Length(';' + BinPath))
  else if CompareText(NewPath, BinPath) = 0 then
    NewPath := ''
  else if CompareText(Copy(NewPath, 1, Length(BinPath) + 1), BinPath + ';') = 0 then
    Delete(NewPath, 1, Length(BinPath) + 1);

  if NewPath <> OrigPath then
  begin
    if RegWriteExpandStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', NewPath) then
      RefreshEnvironment();
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    AddBinToSystemPath();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveBinFromSystemPath();
end;
