; QASSIMZ Thumbs — Inno Setup script (per-user, no admin)
#ifndef AppVer
  #define AppVer "1.2.0"
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
WizardImageStretch=yes
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
Source: "anim\*.bmp"; DestDir: "{tmp}\anim"; Flags: dontcopy
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
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: ShouldSilentRun

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--unregister"; Flags: runhidden waituntilterminated; RunOnceId: "Unregister"

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\QASSIMZ Thumbs"
Type: filesandordirs; Name: "{userappdata}\QASSIMZ Thumbs"

[Code]
// --- QASSIMZ Dark Studio Wizard & Animated Brand Stage ---
const
  QZ_FRAMES = 24;
  QZ_FRAME_MS = 60;

var
  QzAnimTimer: LongWord;
  QzAnimFrame: Integer;
  QzAnimDir: String;
  QzStageImg: TBitmapImage;

function QzSetTimer(hWnd: LongWord; nIDEvent, uElapse: LongWord; lpTimerFunc: LongWord): LongWord;
  external 'SetTimer@user32.dll stdcall';
function QzKillTimer(hWnd: LongWord; uIDEvent: LongWord): BOOL;
  external 'KillTimer@user32.dll stdcall';

function QzPad2(N: Integer): String;
begin
  if N < 10 then Result := '0' + IntToStr(N) else Result := IntToStr(N);
end;

procedure QzAnimTick(Wnd: LongWord; Msg: LongWord; IdEvent: LongWord; Time: LongWord);
var
  S: String;
begin
  QzAnimFrame := (QzAnimFrame + 1) mod QZ_FRAMES;
  S := QzPad2(QzAnimFrame);
  try
    if WizardForm.WizardBitmapImage.Visible then
      WizardForm.WizardBitmapImage.Bitmap.LoadFromFile(QzAnimDir + '\big' + S + '.bmp');
    if WizardForm.WizardBitmapImage2.Visible then
      WizardForm.WizardBitmapImage2.Bitmap.LoadFromFile(QzAnimDir + '\big' + S + '.bmp');
    if WizardForm.WizardSmallBitmapImage.Visible then
      WizardForm.WizardSmallBitmapImage.Bitmap.LoadFromFile(QzAnimDir + '\small' + S + '.bmp');
    if (QzStageImg <> nil) and (WizardForm.CurPageID = wpInstalling) then
      QzStageImg.Bitmap.LoadFromFile(QzAnimDir + '\stage' + S + '.bmp');
  except
  end;
end;

procedure QzApplyDarkStudioWizard;
begin
  try
    WizardForm.Color := $170F0B;
    WizardForm.WelcomePage.Color := $170F0B;
    WizardForm.InnerPage.Color := $1C120D;
    WizardForm.MainPanel.Color := $140C08;
    WizardForm.FinishedPage.Color := $170F0B;
    WizardForm.PageNameLabel.Font.Color := $FFFFFF;
    WizardForm.PageDescriptionLabel.Font.Color := $E1D5CB;
    WizardForm.WelcomeLabel1.Font.Color := $FFFFFF;
    WizardForm.WelcomeLabel2.Font.Color := $E1D5CB;
    WizardForm.FinishedHeadingLabel.Font.Color := $FFFFFF;
    WizardForm.FinishedLabel.Font.Color := $E1D5CB;
    WizardForm.StatusLabel.Font.Color := $FFFFFF;
    WizardForm.FilenameLabel.Font.Color := $E1D5CB;
  except
  end;
  try
    ExtractTemporaryFiles('{tmp}\anim\*.bmp');
    QzAnimDir := ExpandConstant('{tmp}\anim');
    QzAnimFrame := 0;
    QzStageImg := TBitmapImage.Create(WizardForm);
    QzStageImg.Parent := WizardForm.InstallingPage;
    QzStageImg.Left := WizardForm.ProgressGauge.Left;
    QzStageImg.Top := WizardForm.ProgressGauge.Top + WizardForm.ProgressGauge.Height + ScaleY(14);
    QzStageImg.Width := WizardForm.ProgressGauge.Width;
    QzStageImg.Height := ScaleY(118);
    QzStageImg.Stretch := True;
    QzStageImg.Bitmap.LoadFromFile(QzAnimDir + '\stage00.bmp');
    QzAnimTimer := QzSetTimer(0, 0, QZ_FRAME_MS, CreateCallback(@QzAnimTick));
  except
    QzAnimTimer := 0;
  end;
end;

procedure QzStopAnim;
begin
  if QzAnimTimer <> 0 then QzKillTimer(0, QzAnimTimer);
end;
// --- END QASSIMZ Dark Studio Wizard ---

function ShouldSilentRun: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:AUTORUN|1}') = '1');
end;

procedure InitializeWizard;
begin
  QzApplyDarkStudioWizard;
end;

procedure DeinitializeSetup;
begin
  QzStopAnim;
end;
