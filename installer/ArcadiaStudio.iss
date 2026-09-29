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
  #define ProductName "Arcadia Studio Installer Test"
  #define FileStem "ArcadiaStudio-Installer-Test"
  #define ProductId "ARCADIA-STUDIO-Installer-Test"
  #define ProjectExtension ".arcadia-release-test"
  #define LegacyExtension ".wysicraftproj-release-test"
  #define ProjectType "ArcadiaStudio.ReleaseTest"
#else
  #define ProductName "Arcadia Studio"
  #define FileStem "ArcadiaStudio"
  #define ProductId "{{2EDF00FA-8E24-489C-BCF0-C35C37A88CB1}"
  ; Projects are .arcadia files; projects saved before the rename (.wysicraftproj) open in Arcadia Studio too.
  #define ProjectExtension ".arcadia"
  #define LegacyExtension ".wysicraftproj"
  #define ProjectType "ArcadiaStudio.Project"
#endif
#define AppExe "{app}\Designer\ArcadiaStudio.exe"

[Setup]
AppId={#ProductId}
AppName={#ProductName}
AppVersion={#AppVersion}
AppPublisher={#ProductName}
VersionInfoVersion={#NumericVersion}
VersionInfoProductName={#ProductName}
DefaultDirName={localappdata}\Programs\{#ProductName}
DefaultGroupName={#ProductName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename={#FileStem}-{#AppVersion}-Setup
SetupIconFile=..\assets\branding\arcadia-studio.ico
UninstallDisplayIcon={#AppExe}
UninstallDisplayName={#ProductName}
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
Name: "associate"; Description: "Open Arcadia Studio projects (.arcadia, and older .wysicraftproj) with this app"

[Files]
Source: "{#ReleaseDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#ProductName}"; Filename: "{#AppExe}"
Name: "{autodesktop}\{#ProductName}"; Filename: "{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\{#ProjectExtension}"; ValueType: string; ValueName: ""; ValueData: "{#ProjectType}"; Flags: createvalueifdoesntexist; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\{#ProjectExtension}\OpenWithProgids"; ValueType: string; ValueName: "{#ProjectType}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\{#LegacyExtension}"; ValueType: string; ValueName: ""; ValueData: "{#ProjectType}"; Flags: createvalueifdoesntexist; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\{#LegacyExtension}\OpenWithProgids"; ValueType: string; ValueName: "{#ProjectType}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\{#ProjectType}"; ValueType: string; ValueName: ""; ValueData: "Arcadia Studio Project"; Flags: uninsdeletekey; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\{#ProjectType}\DefaultIcon"; ValueType: string; ValueData: "{#AppExe},0"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\{#ProjectType}\shell\open\command"; ValueType: string; ValueData: """{#AppExe}"" ""%1"""; Tasks: associate

[Run]
Filename: "{#AppExe}"; Description: "Open Arcadia Studio"; Flags: nowait postinstall skipifsilent
; An update from inside the app runs this installer quietly with /UPDATE=1; Arcadia Studio opens again when it finishes.
Filename: "{#AppExe}"; Flags: nowait; Check: IsAppUpdate

[Code]
function IsAppUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

procedure ForgetExtension(Extension: String);
var Current: String;
begin
  if RegQueryStringValue(HKCU, 'Software\Classes\' + Extension, '', Current) then
    if Current = '{#ProjectType}' then
      RegDeleteValue(HKCU, 'Software\Classes\' + Extension, '');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    ForgetExtension('{#ProjectExtension}');
    ForgetExtension('{#LegacyExtension}');
  end;
end;
