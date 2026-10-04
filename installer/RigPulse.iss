#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
[Setup]
AppId={{923BFD25-8B41-4F64-AFA9-D02B99B4D03D}
AppName=RigPulse
AppVersion={#AppVersion}
AppPublisher=nazntrade
AppPublisherURL=https://github.com/nazntrade/rigpulse
AppSupportURL=https://github.com/nazntrade/rigpulse/issues
DefaultDirName={localappdata}\Programs\RigPulse
DefaultGroupName=RigPulse
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=RigPulse-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LicenseFile=..\LICENSE
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\RigPulse.exe
#ifdef SignedBuild
SignTool=rigpulse
SignedUninstaller=yes
#endif
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
Name: "sensorsupport"; Description: "Install the official PawnIO sensor driver if missing (administrator approval required)"; Flags: unchecked; Check: NeedsSensorDriver
[Files]
Source: "..\artifacts\publish\RigPulse.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "..\tools\PawnIO_setup.exe"; DestDir: "{tmp}"; Flags: dontcopy
[Icons]
Name: "{group}\RigPulse"; Filename: "{app}\RigPulse.exe"
Name: "{group}\Uninstall RigPulse"; Filename: "{uninstallexe}"
Name: "{autodesktop}\RigPulse"; Filename: "{app}\RigPulse.exe"; Tasks: desktopicon
[Run]
Filename: "{tmp}\PawnIO_setup.exe"; Description: "Install the official sensor driver"; Flags: waituntilterminated; Tasks: sensorsupport; Check: PrepareSensorDriver
Filename: "{app}\RigPulse.exe"; Description: "Launch RigPulse"; Flags: nowait postinstall skipifsilent
[UninstallDelete]
Type: files; Name: "{app}\LICENSE"
Type: files; Name: "{app}\THIRD-PARTY-NOTICES.md"
[Code]
function NeedsSensorDriver: Boolean;
begin
  Result := not FileExists(ExpandConstant('{commonpf64}\PawnIO\PawnIOLib.dll'));
end;

function PrepareSensorDriver: Boolean;
begin
  Result := NeedsSensorDriver;
  if Result then ExtractTemporaryFile('PawnIO_setup.exe');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'RigPulse');
end;
