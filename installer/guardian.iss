; Build with:  dotnet publish Guardian.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o publish
;              ISCC installer\guardian.iss
#define AppName "Guardian"
; installer\build.ps1 passes /DAppVersion=<version from Guardian.csproj>; this default is only a fallback.
#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#define AppExe "Guardian.exe"

[Setup]
AppId={{6B1E2A5C-3F4D-4C8A-9E71-5D0B8A2C41F7}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=RivaDeveloper
AppPublisherURL=https://github.com/RivaDeveloper/system-guardian
AppSupportURL=https://github.com/RivaDeveloper/system-guardian/issues
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
SetupIconFile=..\Assets\guardian.ico
OutputDir=..\dist
OutputBaseFilename=Guardian-Setup-{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dark
DisableWelcomePage=no
WizardImageFile=assets\panel-100.bmp,assets\panel-125.bmp,assets\panel-150.bmp,assets\panel-175.bmp,assets\panel-200.bmp
WizardSmallImageFile=assets\orb-55.png,assets\orb-69.png,assets\orb-83.png,assets\orb-96.png,assets\orb-110.png,assets\orb-138.png
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Program Files needs admin; the app itself still starts unelevated and offers "Restart as administrator".
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
UsedUserAreasWarning=no
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel1=Your PC, in check.
WelcomeLabel2=Guardian scans for threats, keeps drivers and apps current, and repairs Windows system files.%n%nNothing runs until you start it.
FinishedHeadingLabel=Guardian is ready.
FinishedLabel=Open Guardian to check your PC. Driver installs and repairs need it restarted as administrator.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\Assets\Fonts\Syne-ExtraBold.ttf"; DestDir: "{tmp}"; Flags: dontcopy
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Silent installs come from the in-app updater: reopen Guardian (as the signed-in user, not elevated).
Filename: "{app}\{#AppExe}"; Flags: nowait runasoriginaluser skipifnotsilent
Description: "Launch {#AppName}"; Filename: "{app}\{#AppExe}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Saved VirusTotal key (DPAPI-encrypted) for the user who uninstalls.
Type: filesandordirs; Name: "{localappdata}\Guardian"

[Code]
// Load the Syne display font for this process only so titles match the app.
function AddFontResourceEx(lpszFilename: String; fl: DWORD; pdv: DWORD): Integer;
  external 'AddFontResourceExW@gdi32.dll stdcall';

procedure InitializeWizard;
begin
  // Lumen palette (Delphi colours are BGR): page #06070c, header and footer #0b0c14.
  WizardForm.Color := $140C0B;
  WizardForm.MainPanel.Color := $140C0B;
  WizardForm.InnerPage.Color := $0C0706;
  WizardForm.WelcomePage.Color := $0C0706;
  WizardForm.FinishedPage.Color := $0C0706;

  ExtractTemporaryFile('Syne-ExtraBold.ttf');
  if AddFontResourceEx(ExpandConstant('{tmp}\Syne-ExtraBold.ttf'), $10, 0) > 0 then
  begin
    WizardForm.WelcomeLabel1.Font.Name := 'Syne ExtraBold';
    WizardForm.WelcomeLabel1.Font.Size := 17;
    WizardForm.FinishedHeadingLabel.Font.Name := 'Syne ExtraBold';
    WizardForm.FinishedHeadingLabel.Font.Size := 17;
    WizardForm.PageNameLabel.Font.Name := 'Syne ExtraBold';
    WizardForm.PageNameLabel.Font.Size := 11;
  end;
end;
