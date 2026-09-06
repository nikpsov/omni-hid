; OmniHID Inno Setup Script
#ifndef MyAppVersion
  #define MyAppVersion "0.0.2"
#endif

#define MyAppName "OmniHID"
#define MyAppPublisher "nikpsov"
#define MyAppURL "https://github.com/nikpsov/omni-hid"
#define MyAppExeName "omni-hid.exe"

[Setup]
AppId={{D1E2F3A4-B5C6-47D8-9E0F-1A2B3C4D5E6F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=Output
OutputBaseFilename=omni-hid-v{#MyAppVersion}-setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ChangesEnvironment=yes
DisableProgramGroupPage=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "envPath"; Description: "Add OmniHID to User PATH environment variable (recommended - run omni-hid from any terminal)"; GroupDescription: "System Integration:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\dist\omni-hid.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\OmniHid.Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\devices\*"; DestDir: "{app}\devices"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\dist\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\README.ru.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\OmniHID Command Prompt"; Filename: "{cmd}"; Parameters: "/K ""{app}\{#MyAppExeName}"" --help"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\OmniHID"; Filename: "{cmd}"; Parameters: "/K ""{app}\{#MyAppExeName}"" --help"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{cmd}"; Parameters: "/K ""{app}\{#MyAppExeName}"" --help"; Description: "Launch OmniHID CLI help"; Flags: postinstall skipifsilent nowait unchecked

[Code]
const
  EnvironmentKey = 'Environment';

procedure AddPathToEnvironment(PathToAdd: string);
var
  Paths: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Paths) then
    Paths := '';

  if Pos(';' + Uppercase(PathToAdd) + ';', ';' + Uppercase(Paths) + ';') = 0 then
  begin
    if (Paths <> '') and (Paths[Length(Paths)] <> ';') then
      Paths := Paths + ';';
    Paths := Paths + PathToAdd;
    RegWriteStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Paths);
  end;
end;

procedure RemovePathFromEnvironment(PathToRemove: string);
var
  Paths, UpperPaths, UpperTarget: string;
  P, L: Integer;
begin
  if RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Paths) then
  begin
    UpperPaths := ';' + Uppercase(Paths) + ';';
    UpperTarget := ';' + Uppercase(PathToRemove) + ';';
    P := Pos(UpperTarget, UpperPaths);
    if P > 0 then
    begin
      L := Length(PathToRemove);
      Delete(Paths, P, L);
      StringChangeEx(Paths, ';;', ';', True);
      if (Length(Paths) > 0) and (Paths[1] = ';') then
        Delete(Paths, 1, 1);
      if (Length(Paths) > 0) and (Paths[Length(Paths)] = ';') then
        Delete(Paths, Length(Paths), 1);
      RegWriteStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Paths);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('envPath') then
  begin
    AddPathToEnvironment(ExpandConstant('{app}'));
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RemovePathFromEnvironment(ExpandConstant('{app}'));
  end;
end;
