#ifndef ReleaseDir
  #error ReleaseDir is required
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0-rc.1"
#endif
#ifndef NumericVersion
  #define NumericVersion "1.0.0.0"
#endif
#ifdef TestInstall
  #define ProductName "Wysicraft Installer Test"
  #define ProductId "WYSICRAFT-Installer-Test"
  #define ProjectExtension ".wysicraft-release-test"
  #define ProjectType "Wysicraft.ReleaseTest"
#else
  #define ProductName "Wysicraft"
  #define ProductId "{{1A57F799-B36A-45E5-941D-B56350D754B8}"
  #define ProjectExtension ".wysicraftproj"
  #define ProjectType "Wysicraft.Project"
#endif

[Setup]
AppId={#ProductId}
AppName={#ProductName}
AppVersion={#AppVersion}
VersionInfoVersion={#NumericVersion}
DefaultDirName={localappdata}\Programs\{#ProductName}
DefaultGroupName={#ProductName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename={#ProductName}-{#AppVersion}-Setup
SetupIconFile=..\assets\branding\wysicraft.ico
UninstallDisplayIcon={app}\Designer\Wysicraft.Designer.exe
LicenseFile=..\LICENSE
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "associate"; Description: "Register Wysicraft project files with this app"

[Files]
Source: "{#ReleaseDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#ProductName}"; Filename: "{app}\Designer\Wysicraft.Designer.exe"
Name: "{autodesktop}\{#ProductName}"; Filename: "{app}\Designer\Wysicraft.Designer.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\{#ProjectExtension}"; ValueType: string; ValueName: ""; ValueData: "{#ProjectType}"; Flags: createvalueifdoesntexist; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\{#ProjectExtension}\OpenWithProgids"; ValueType: string; ValueName: "{#ProjectType}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\{#ProjectType}"; ValueType: string; ValueName: ""; ValueData: "Wysicraft Project"; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\{#ProjectType}\DefaultIcon"; ValueType: string; ValueData: "{app}\Designer\Wysicraft.Designer.exe,0"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\{#ProjectType}\shell\open\command"; ValueType: string; ValueData: """{app}\Designer\Wysicraft.Designer.exe"" ""%1"""; Tasks: associate

[Run]
Filename: "{app}\Designer\Wysicraft.Designer.exe"; Description: "Open Wysicraft"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Current: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Classes\{#ProjectExtension}', '', Current) then
      if Current = '{#ProjectType}' then
        RegDeleteValue(HKCU, 'Software\Classes\{#ProjectExtension}', '');
end;

