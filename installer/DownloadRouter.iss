#define AppName "eslee Download Router"
#define AppVersion "0.2.0"
#define Publisher "eslee"

[Setup]
AppId={{43A55F01-6698-44C0-9359-18481B0765A9}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#Publisher}
DefaultDirName={localappdata}\Programs\eslee\DownloadRouter
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=eslee-download-router-setup
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\DownloadRouter.App.exe
DisableProgramGroupPage=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "바탕 화면 바로가기 만들기"; GroupDescription: "추가 바로가기:"; Flags: unchecked
Name: "autostart"; Description: "Windows 로그인 시 백그라운드로 자동 시작"; GroupDescription: "백그라운드 실행:"; Flags: checkedonce

[Files]
Source: "..\artifacts\publish\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\eslee Download Router"; Filename: "{app}\DownloadRouter.App.exe"
Name: "{autodesktop}\eslee Download Router"; Filename: "{app}\DownloadRouter.App.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "eslee Download Router"; ValueData: """{app}\DownloadRouter.App.exe"" --background"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\register-native-host.ps1"" -Action Register -Browser All -HostPath ""{app}\DownloadRouter.NativeHost.exe"""; Flags: runhidden
Filename: "{app}\DownloadRouter.App.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\unregister-native-host.ps1"""; Flags: runhidden; RunOnceId: "UnregisterNativeHost"
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\unregister-startup.ps1"""; Flags: runhidden; RunOnceId: "UnregisterStartup"

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  AppExe: String;
begin
  Result := '';
  AppExe := ExpandConstant('{app}\DownloadRouter.App.exe');
  if FileExists(AppExe) then
  begin
    if not Exec(AppExe, '--shutdown', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Result := '기존 eslee Download Router를 정상 종료하지 못했습니다.';
  end;
end;
