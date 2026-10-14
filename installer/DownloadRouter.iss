#define AppName "eslee Download Router"
#ifndef AppVersion
  #error AppVersion must be supplied from Directory.Build.props by scripts/build-installer.ps1
#endif
#ifndef AppCommit
  #define AppCommit "unknown"
#endif
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
SetupIconFile=..\assets\branding\eslee-download-router.ico
; The executable keeps its name across versions, so Explorer must be told to drop cached icons
; or the taskbar and Start menu keep showing the previous icon right after an upgrade.
ChangesAssociations=yes
UninstallDisplayIcon={app}\DownloadRouter.App.exe
DisableProgramGroupPage=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription={#AppName} installer ({#AppCommit})

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
#if FileExists(CompilerPath + "\Languages\Korean.isl")
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
#elif FileExists(CompilerPath + "\Languages\Unofficial\Korean.isl")
Name: "korean"; MessagesFile: "compiler:Languages\Unofficial\Korean.isl"
#else
  #error Korean.isl was not found in Languages or Languages\Unofficial.
#endif

[CustomMessages]
korean.DesktopIconTask=바탕 화면 바로가기 만들기
korean.ExtraShortcutsGroup=추가 바로가기:
korean.AutoStartTask=Windows 로그인 시 백그라운드로 자동 시작
korean.BackgroundGroup=백그라운드 실행:
korean.ShutdownFailed=기존 eslee Download Router를 정상 종료하지 못했습니다.
english.DesktopIconTask=Create a desktop shortcut
english.ExtraShortcutsGroup=Additional shortcuts:
english.AutoStartTask=Start in the background when I sign in to Windows
english.BackgroundGroup=Background:
english.ShutdownFailed=The running eslee Download Router could not be closed.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; GroupDescription: "{cm:ExtraShortcutsGroup}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutoStartTask}"; GroupDescription: "{cm:BackgroundGroup}"; Flags: checkedonce

[Files]
Source: "..\artifacts\publish\app\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\eslee Download Router"; Filename: "{app}\DownloadRouter.App.exe"
Name: "{autodesktop}\eslee Download Router"; Filename: "{app}\DownloadRouter.App.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "eslee Download Router"; ValueData: """{app}\DownloadRouter.App.exe"" --background"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\register-native-host.ps1"" -Action Register -Browser All -HostPath ""{app}\DownloadRouter.NativeHost.exe"""; Flags: runhidden
Filename: "{app}\DownloadRouter.App.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\DownloadRouter.App.exe"; Parameters: "--unregister-notifications"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterNotifications"
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
      Result := CustomMessage('ShutdownFailed');
  end;
end;
