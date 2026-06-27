; Inno Setup script for KerwaKasse.
; Wraps the self-contained `dotnet publish` output into a single setup.exe.

; The app version is injected by publish.ps1 via /DAppVersion=X.Y.Z, sourced from the
; project file as the single place the version is maintained. If it is missing, the
; compile fails on purpose instead of producing an installer with a placeholder version.
#ifndef AppVersion
  #error AppVersion was not provided - build through publish.ps1, which passes /DAppVersion.
#endif

#define AppName "KerwaKasse"
#define AppPublisher "Manuel Röhrer"
#define AppExeName "KerwaKasse.exe"
; Published, self-contained app folder (relative to this .iss file).
#define PublishDir "..\KerwaKasse\bin\Release\net10.0-windows10.0.19041\win-x64\publish"

[Setup]
; Stable identity for KerwaKasse across all versions. It must never change: a constant
; AppId is what lets a new version upgrade the existing install in place, instead of the
; "already installed from another location" situation that ClickOnce produced.
AppId={{BFF561DC-C242-43DD-B52E-13230FEB07DA}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install, so no administrator rights are required. The database lives under
; %AppData%\KerwaKasse, which keeps install scope and data scope consistent. A value of
; "admin" installs per-machine under Program Files instead (requires elevation at install).
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\KerwaKasse\KerwaKasse.ico
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Close a running KerwaKasse during an update (Restart Manager) so its files are not locked
; while they are replaced. It is not relaunched automatically afterwards.
CloseApplications=yes
RestartApplications=no
OutputDir=Output
OutputBaseFilename=KerwaKasse_Setup_v{#AppVersion}

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; Clear the program folder before the new build is copied in, so files left over from an
; older version (for example renamed runtime DLLs) do not accumulate. The database lives
; under %AppData% and is not touched.
Type: filesandordirs; Name: "{app}\*"

[Files]
; Everything the publish produced, minus debug symbols.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
