; Inno Setup script. Build: iscc /DAppVersion=0.1.0 /DSourceDir=<publish dir> guardian.iss
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish"
#endif

[Setup]
AppId={{6F1C2B7A-3D54-4B0E-9C8A-5A17E0F1D2C3}
AppName=AutoPortal System Guardian
AppVersion={#AppVersion}
AppPublisher=AutoPortal contributors
DefaultDirName={autopf}\AutoPortal System Guardian
DefaultGroupName=AutoPortal System Guardian
OutputBaseFilename=AutoPortal-System-Guardian-Setup-{#AppVersion}
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\AutoPortalSystemGuardian.exe

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\AutoPortal System Guardian"; Filename: "{app}\AutoPortalSystemGuardian.exe"
Name: "{commondesktop}\AutoPortal System Guardian"; Filename: "{app}\AutoPortalSystemGuardian.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"
; "Start with Windows" is enabled from inside the app (Settings), which creates the Task Scheduler entry.

[Run]
Filename: "{app}\AutoPortalSystemGuardian.exe"; Description: "Launch System Guardian"; Flags: postinstall nowait skipifsilent

[UninstallRun]
; Removes ONLY this application's own scheduled task.
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""AutoPortal System Guardian"" /F"; Flags: runhidden; RunOnceId: "RemoveStartupTask"
