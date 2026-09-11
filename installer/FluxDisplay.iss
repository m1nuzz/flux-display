; FluxDisplay classic EXE installer (Inno Setup 6).
; Built in CI from the unpackaged portable publish, so installing needs
; neither a signing certificate (unlike MSIX) nor admin rights.
; Usage (from repo root): ISCC.exe installer\FluxDisplay.iss
;   Source files : artifacts\FluxDisplay-win-x64\*
;   Output       : artifacts\FluxDisplay-win-x64-setup.exe

#define MyAppName "FluxDisplay"
#ifndef MyAppVersion
#define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "m1nuzz"
#define MyAppURL "https://github.com/m1nuzz/flux-display"
#define MyAppExeName "FluxDisplay.App.exe"

[Setup]
AppId={{FF430B59-007E-459A-AEE5-E029DB1E2C39}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts
OutputBaseFilename=FluxDisplay-win-x64-setup
SetupIconFile=..\src\FluxDisplay.App\Assets\tray.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DisableProgramGroupPage=yes
UninstallDisplayName={#MyAppName}
; Silent updates: close the running app instead of showing "files in use".
CloseApplications=yes
; The ONLY restart mechanism is the [Run] entry below (silent updates) and the
; launch checkbox (interactive installs). Restart Manager must never restart
; the app on its own — this app does not register for restart, and a second
; mechanism would risk launching it twice. Per-user install => no UAC, so the
; relaunched app inherits the user's normal (non-elevated) token; no
; runasoriginaluser needed.
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\FluxDisplay-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
; Silent updates relaunch the app exactly once (the checkbox above is skipped
; in silent mode, so the two entries are mutually exclusive). Chosen instead
; of RegisterApplicationRestart: no Restart Manager registration, no risk of a
; double launch, deterministic with CloseApplications=yes.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall skipifnotsilent
