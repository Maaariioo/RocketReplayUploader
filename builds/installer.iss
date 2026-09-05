; Inno Setup script for Rocket Replay Uploader
; Build with: iscc installer.iss  (requires Inno Setup 6)

#define MyAppName "Rocket Replay Uploader"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Maaariioo"
#define MyAppExeName "rocket-replay-uploader.exe"

[Setup]
AppId={{8E2B8C3A-1F4A-4D6B-9A22-0A0784BC42BD}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\RocketReplayUploader
DisableProgramGroupPage=yes
OutputDir=.
OutputBaseFilename=rocket-replay-uploader-setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start automatically at login"; GroupDescription: "Startup:"

[Files]
Source: "rocket-replay-uploader.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RocketReplayUploader"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
