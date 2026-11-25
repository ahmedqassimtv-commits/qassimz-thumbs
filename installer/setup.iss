; QASSIMZ Thumbs — Inno Setup script (per-user, no admin)
#ifndef AppVer
  #define AppVer "1.0.0"
#endif
#define AppName "QASSIMZ Thumbs"
#define AppExe "QASSIMZ Thumbs.exe"
#define Repo "https://github.com/ahmedqassimtv-commits/qassimz-thumbs"

[Setup]
AppId=QASSIMZ.Thumbs
AppName={#AppName}
AppVersion={#AppVer}
AppPublisher=Ahmed Qassim — QASSIMZ
AppPublisherURL={#Repo}
AppSupportURL={#Repo}/issues
AppUpdatesURL={#Repo}/releases
DefaultDirName={localappdata}\Programs\QASSIMZ Thumbs
DefaultGroupName=QASSIMZ
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
CloseApplications=force
RestartApplications=no
OutputDir=..\dist
OutputBaseFilename=QASSIMZ-Thumbs-Setup-{#AppVer}
SetupIconFile=..\assets\logo.ico
UninstallDisplayIcon={app}\{#AppExe}
WizardStyle=modern
WizardImageFile=wizard.bmp
WizardSmallImageFile=wizard-small.bmp
InfoBeforeFile=..\docs\INSTRUCTIONS-AR.txt
LicenseFile=..\LICENSE
Compression=lzma2/ultra
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
#if FileExists(AddBackslash(SourcePath) + "Arabic.isl")
Name: "arabic"; MessagesFile: "Arabic.isl"
#endif
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "rawthumbs"; Description: "مصغّرات صور الكاميرا RAW (CR2 / NEF / ARW / DNG …) في الويندوز"; GroupDescription: "مصغّرات الويندوز:"; Flags: unchecked

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\INSTRUCTIONS-AR.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\تعليمات الاستخدام"; Filename: "{app}\INSTRUCTIONS-AR.txt"
Name: "{group}\GitHub — QASSIMZ Thumbs"; Filename: "{#Repo}"
Name: "{group}\الدعم والإبلاغ عن مشكلة"; Filename: "{#Repo}/issues"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; right-click a folder (or inside a folder) → "اعرض بـ QASSIMZ Thumbs"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\QassimzThumbs"; ValueType: string; ValueName: ""; ValueData: "اعرض بـ QASSIMZ Thumbs"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\shell\QassimzThumbs"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe}"
Root: HKCU; Subkey: "Software\Classes\Directory\shell\QassimzThumbs\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\QassimzThumbs"; ValueType: string; ValueName: ""; ValueData: "اعرض بـ QASSIMZ Thumbs"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\QassimzThumbs"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe}"
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\QassimzThumbs\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%V"""

[Run]
; turn on Explorer thumbnails (per-user)
Filename: "{app}\{#AppExe}"; Parameters: "--register"; Flags: runhidden waituntilterminated; Tasks: not rawthumbs
Filename: "{app}\{#AppExe}"; Parameters: "--register --raw"; Flags: runhidden waituntilterminated; Tasks: rawthumbs
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
Filename: "{#Repo}"; Description: "افتح صفحة البرنامج على GitHub"; Flags: postinstall shellexec skipifsilent unchecked
; after a silent self-update, reopen the app
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "Unregister"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\QASSIMZ Thumbs"
Type: filesandordirs; Name: "{userappdata}\QASSIMZ Thumbs"
