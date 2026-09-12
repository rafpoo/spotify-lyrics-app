#define MyAppName "LyricFloat"
#define MyAppVersion "0.1.0"
#define SourceDir "..\artifacts\release\portable"

[Setup]
AppId={{22EBDAAB-9853-49C1-BB2C-A364B38D8F2C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=LyricFloat
AppComments=Floating synchronized lyrics overlay for Windows
DefaultDirName={localappdata}\Programs\LyricFloat
DefaultGroupName=LyricFloat
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\release\installer
OutputBaseFilename=LyricFloat-Setup-{#MyAppVersion}
SetupIconFile=..\LyricFloat.App\Assets\LyricFloat.ico
UninstallDisplayIcon={app}\LyricFloat.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\LyricFloat"; Filename: "{app}\LyricFloat.exe"
Name: "{autodesktop}\LyricFloat"; Filename: "{app}\LyricFloat.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\LyricFloat.exe"; Description: "Launch LyricFloat"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'LyricFloat', Command) then
    begin
      if (CompareText(Trim(Command), '"' + ExpandConstant('{app}\LyricFloat.exe') + '"') = 0) or
         (CompareText(Trim(Command), ExpandConstant('{app}\LyricFloat.exe')) = 0) then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'LyricFloat');
    end;
  end;
end;

// User data in {userappdata}\LyricFloat is intentionally preserved on uninstall.
