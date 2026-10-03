; Inno Setup script for the direct-download installer (Setup.exe).
; Built by: .\build.ps1 -Installer   (passes /DAppVersion=... /DSourceDir=... /DOutputDir=...)
; Per-user install, no admin prompt: %LOCALAPPDATA%\Programs\Advanced Clipboard Manager

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "Advanced Clipboard Manager"
#define AppExe "ClipboardManager.exe"

[Setup]
; Keep this GUID forever: it identifies the app for upgrades and uninstall.
AppId={{7C2B8F4E-3A61-4D2E-9B0C-5E8A1F6D4C21}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=duytiena2
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=AdvancedClipboardManager-Setup-{#AppVersion}
SetupIconFile=Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Clean up loose DLLs and config files from previous multi-file installations to prevent Smart App Control issues
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.json"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the tray menu's "Start with Windows" writes (StartupRegistration.cs), so both stay in sync.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AdvancedClipboardManager"; ValueData: """{app}\{#AppExe}"" --background"; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
{ The tray app has no visible window to close, so stop it before replacing or removing its files
  (works the same in silent mode, unlike AppMutex which aborts there). }
procedure StopRunningApp();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopRunningApp();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { The Run value may also have been added later from the tray menu, not only by the "startup" task. }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'AdvancedClipboardManager');
    { History and settings live in %LOCALAPPDATA%\ClipboardManager; ask before removing them. }
    if DirExists(ExpandConstant('{localappdata}\ClipboardManager')) then
      if SuppressibleMsgBox('Also delete your clipboard history and settings?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(ExpandConstant('{localappdata}\ClipboardManager'), True, True, True);
  end;
end;
