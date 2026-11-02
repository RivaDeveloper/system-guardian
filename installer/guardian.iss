; Build with:  dotnet publish Guardian.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o publish
;              ISCC installer\guardian.iss
#define AppName "Guardian"
#define AppVersion "1.0.0"
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
WizardStyle=modern
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

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Description: "Launch {#AppName}"; Filename: "{app}\{#AppExe}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Saved VirusTotal key (DPAPI-encrypted) for the user who uninstalls.
Type: filesandordirs; Name: "{localappdata}\Guardian"
