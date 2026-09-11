; Flux Display classic EXE installer (Inno Setup 6).
; Built in CI from the unpackaged portable publish, so installing needs
; neither a signing certificate (unlike MSIX) nor admin rights.
; Usage (from repo root): ISCC.exe installer\FluxDisplay.iss
;   Source files : artifacts\FluxDisplay-win-x64\*
;   Output       : artifacts\FluxDisplay-<version>-setup.exe (renamed by CI)

#define MyAppName "Flux Display"
; Install folder name WITHOUT space. Never rename: in-place upgrades and the
; updater's installed-copy detection (%LocalAppData%\Programs\FluxDisplay)
; depend on this exact directory.
#define MyAppDirName "FluxDisplay"
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
DefaultDirName={localappdata}\Programs\{#MyAppDirName}
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\artifacts
OutputBaseFilename=FluxDisplay-win-x64-setup
SetupIconFile=..\src\FluxDisplay.App\Assets\tray.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
; Forced dark appearance for Setup and Uninstall (built-in dark style).
WizardStyle=modern dark
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

[Code]
// Uninstall cleanup. The app's side effects outside {app}:
//   - HKCU\Software\Microsoft\Windows\CurrentVersion\Run value "FluxDisplay"
//     (autostart entry written by StartupManager)
//   - %LocalAppData%\FluxDisplay (settings.json, presets, logs)
// Shortcuts, the install dir itself and the Uninstall entry are removed by
// Inno automatically.
// NOTE: CreateCustomPage does NOT work in the uninstaller (it silently
// breaks the uninstall chain), so the checkbox lives on a CreateCustomForm
// dialog instead. In silent uninstall the dialog is skipped and the default
// (delete settings) wins.
var
  DeleteData: Boolean;

function InitializeUninstall(): Boolean;
var
  Form: TSetupForm;
  Title, Desc: TNewStaticText;
  Check: TNewCheckBox;
  OkButton, CancelButton: TNewButton;
begin
  // Default (unchecked box, also used for silent uninstall): keep settings.
  DeleteData := False;
  Result := True;
  if UninstallSilent() then
  begin
    Log('FluxDisplay: InitializeUninstall, silent uninstall, default keep');
    Exit;
  end;
  Log('FluxDisplay: InitializeUninstall, interactive, showing form');

  // NOTE: CreateCustomPage does NOT work in the uninstaller — use a custom
  // form. Since Inno 6.6.0 the size goes into CreateCustomForm itself and
  // ClientWidth/ClientHeight are read-only afterwards.
  // NOTE 2: WinUI/XAML cannot be hosted here — the uninstaller is native
  // Win32, so this stays a styled VCL dialog.
  Form := CreateCustomForm(ScaleX(420), ScaleY(170), False, False);
  try
    Form.Caption := 'Flux Display Uninstall';
    Form.Position := poScreenCenter;

    Title := TNewStaticText.Create(Form);
    Title.Parent := Form;
    Title.Left := ScaleX(16);
    Title.Top := ScaleY(12);
    Title.Width := Form.ClientWidth - ScaleX(32);
    Title.Caption := 'Uninstall options';
    Title.Font.Style := [fsBold];
    Title.ShowAccelChar := False;

    Desc := TNewStaticText.Create(Form);
    Desc.Parent := Form;
    Desc.Left := ScaleX(16);
    Desc.Top := ScaleY(34);
    Desc.Width := Form.ClientWidth - ScaleX(32);
    Desc.Caption := 'The application itself is always removed. ' +
      'Choose what else should go:';
    Desc.ShowAccelChar := False;

    Check := TNewCheckBox.Create(Form);
    Check.Parent := Form;
    Check.Left := ScaleX(16);
    Check.Top := ScaleY(64);
    Check.Width := Form.ClientWidth - ScaleX(32);
    Check.Caption := 'Delete application settings and presets';
    Check.Checked := False;

    OkButton := TNewButton.Create(Form);
    OkButton.Parent := Form;
    OkButton.Caption := 'OK';
    OkButton.ModalResult := mrOk;
    OkButton.Default := True;
    OkButton.Width := ScaleX(75);
    OkButton.Height := ScaleY(23);
    OkButton.Left := Form.ClientWidth - ScaleX(168);
    OkButton.Top := Form.ClientHeight - ScaleY(36);

    CancelButton := TNewButton.Create(Form);
    CancelButton.Parent := Form;
    CancelButton.Caption := 'Cancel';
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;
    CancelButton.Width := ScaleX(75);
    CancelButton.Height := ScaleY(23);
    CancelButton.Left := Form.ClientWidth - ScaleX(85);
    CancelButton.Top := Form.ClientHeight - ScaleY(36);

    Form.ActiveControl := OkButton;

    Log('FluxDisplay: showing keep-data form');
    if Form.ShowModal() <> mrOk then
    begin
      Log('FluxDisplay: form cancelled, aborting uninstall');
      Result := False;
      Exit;
    end;
    DeleteData := Check.Checked;
    if DeleteData then
      Log('FluxDisplay: form OK, delete settings')
    else
      Log('FluxDisplay: form OK, keep settings');
  finally
    Form.Free();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if DeleteData then
      Log('FluxDisplay: post-uninstall cleanup, deleting settings')
    else
      Log('FluxDisplay: post-uninstall cleanup, keeping settings');
    // Autostart is always removed: a leftover Run entry pointing at a
    // deleted exe is never what the user wants.
    RegDeleteValue(HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'FluxDisplay');
    // Settings are kept unless the user checks the box (also kept on
    // silent uninstall, where the dialog is skipped).
    if DeleteData then
      DelTree(ExpandConstant('{localappdata}\FluxDisplay'), True, True, True);
  end;
end;
