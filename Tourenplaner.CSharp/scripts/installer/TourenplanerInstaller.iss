#ifndef PublishedFilesRoot
  #error PublishedFilesRoot define is required.
#endif

#ifndef InstallerRoot
  #error InstallerRoot define is required.
#endif

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef WizardImageFile
  #error WizardImageFile define is required.
#endif

#ifndef WizardSmallImageFile
  #error WizardSmallImageFile define is required.
#endif

#define MyAppName "GAWELA Tourenplaner"
#define MyAppPublisher "GAWELA"
#define MyAppExeName "GAWELA.Tourenplaner.exe"
#define MyAppAssocName MyAppName + " Setup"

[Setup]
AppId={{A289BFAB-5C40-4B17-9FEC-4B63A37F730A}
AppName={#MyAppName}
AppVersion={#AppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\GAWELA\Tourenplaner
DefaultGroupName=GAWELA
DisableProgramGroupPage=yes
WizardStyle=modern
WizardImageFile={#WizardImageFile}
WizardSmallImageFile={#WizardSmallImageFile}
OutputDir={#InstallerRoot}
OutputBaseFilename=GAWELA-Tourenplaner-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
SetupLogging=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
RestartApplications=no

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; GroupDescription: "Verknüpfungen:"; Flags: checkedonce
Name: "startmenuicon"; Description: "Startmenü-Verknüpfung erstellen"; GroupDescription: "Verknüpfungen:"; Flags: checkedonce

[Files]
Source: "{#PublishedFilesRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\GAWELA Tourenplaner"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{group}\GAWELA Tourenplaner"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: startmenuicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,GAWELA Tourenplaner}"; Flags: nowait postinstall skipifsilent
