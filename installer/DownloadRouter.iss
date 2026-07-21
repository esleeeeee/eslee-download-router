#define AppName "eslee Download Router"
#define AppVersion "0.1.0"
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

[Files]
Source: "..\artifacts\publish\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\register-native-host.ps1"" -Action Register -HostPath ""{app}\DownloadRouter.NativeHost.exe"""; Flags: runhidden
Filename: "{app}\DownloadRouter.App.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\unregister-native-host.ps1"""; Flags: runhidden; RunOnceId: "UnregisterNativeHost"
