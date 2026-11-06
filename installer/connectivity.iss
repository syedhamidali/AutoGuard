; Build: iscc /DAppVersion=0.1.0 /DSourceDir=<publish dir> connectivity.iss
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish-connectivity"
#endif

[Setup]
AppId={{2B7E4C19-8A53-4D6F-B1C2-7E9A3F0D5B84}
AppName=AutoPortal Connectivity Manager
AppVersion={#AppVersion}
AppPublisher=AutoPortal contributors
DefaultDirName={autopf}\AutoPortal Connectivity Manager
DefaultGroupName=AutoPortal Connectivity Manager
OutputBaseFilename=AutoPortal-Connectivity-Manager-Setup-{#AppVersion}
LicenseFile=..\LICENSE
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\AutoPortalConnectivityManager.exe

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\AutoPortal Connectivity Manager"; Filename: "{app}\AutoPortalConnectivityManager.exe"

[Run]
Filename: "{app}\AutoPortalConnectivityManager.exe"; Description: "Launch Connectivity Manager"; Flags: postinstall nowait skipifsilent

[UninstallRun]
; Removes ONLY this application's own scheduled task. The saved password stays in Credential Manager until the user deletes it in the app.
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""AutoPortal Connectivity Manager"" /F"; Flags: runhidden; RunOnceId: "RemoveConnectivityTask"
