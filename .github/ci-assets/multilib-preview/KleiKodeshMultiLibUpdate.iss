; Inno Setup script - unofficial "multi-library" preview updater for the official KleiKodesh install.
; Built by .github/workflows/build-multilib-preview.yml. Defines come from ISCC /D switches:
;   AppVersion  e.g. multilib-preview-20261023     NumVersion e.g. 1.0.0.0
;   PayloadDir  folder with payload-manifest.json + files
;   ScriptDir   folder with apply-update.ps1 and Hebrew.isl
;   OutDir      output folder         OutName  output file name (no .exe)
#ifndef AppVersion
  #define AppVersion "dev"
#endif
#ifndef NumVersion
  #define NumVersion "1.0.0.0"
#endif

[Setup]
AppId={{7C1A5B52-3E0D-4C77-9B8E-5F6A0D3C21B4}
AppName=עדכון ריבוי ספריות - כתבי הקודש (גרסת תצוגה)
AppVersion={#AppVersion}
AppVerName=עדכון ריבוי ספריות - כתבי הקודש {#AppVersion}
AppPublisher=YairDaniel11 (פורק לא רשמי)
VersionInfoVersion={#NumVersion}
VersionInfoDescription=Unofficial preview update (multi-library support) for KleiKodesh - not an official release
VersionInfoCompany=YairDaniel11 fork (unofficial)
VersionInfoProductName=KleiKodesh multi-library preview update
DefaultDirName={localappdata}\KleiKodesh
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
DirExistsWarning=no
CreateUninstallRegKey=no
Uninstallable=no
PrivilegesRequired=lowest
CloseApplications=no
RestartApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
ShowLanguageDialog=no
DisableWelcomePage=no
WizardStyle=modern
SolidCompression=yes
Compression=lzma2/ultra64
OutputDir={#OutDir}
OutputBaseFilename={#OutName}
SetupLogging=yes

[Languages]
Name: "he"; MessagesFile: "{#ScriptDir}\Hebrew.isl"

[Files]
Source: "{#ScriptDir}\apply-update.ps1"; DestDir: "{tmp}"; Flags: ignoreversion deleteafterinstall
Source: "{#PayloadDir}\*"; DestDir: "{tmp}\kkpayload"; Flags: ignoreversion recursesubdirs createallsubdirs deleteafterinstall

[Code]
var
  TargetDir: String;
  TestMode: Boolean;

function ValidOfficial(const Dir: String): Boolean;
begin
  Result := FileExists(Dir + '\KitveiHakodeshLib.dll') and
            (FileExists(Dir + '\KleiKodesh.vsto') or FileExists(Dir + '\KleiKodesh.dll'));
end;

function InitializeSetup: Boolean;
begin
  TargetDir := ExpandConstant('{param:DIR|}');
  TestMode := TargetDir <> '';
  if not TestMode then
    TargetDir := ExpandConstant('{localappdata}\KleiKodesh');
  TargetDir := RemoveBackslash(TargetDir);
  Result := ValidOfficial(TargetDir);
  if not Result then
  begin
    Log('Official KleiKodesh install not found at: ' + TargetDir);
    SuppressibleMsgBox(FmtMessage(CustomMessage('NotFound'), []), mbError, MB_OK, IDOK);
  end;
end;

procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel1.Caption := CustomMessage('WelcomeTitle');
  WizardForm.WelcomeLabel2.Caption := FmtMessage(CustomMessage('WelcomeBody'), []);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 + FmtMessage(CustomMessage('FinishNote'), []);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Ps, Params, LogPath: String;
  Rc: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    WizardForm.StatusLabel.Caption := CustomMessage('Applying');
    Ps := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
    LogPath := ExpandConstant('{tmp}\..\KleiKodesh-multilib-update.log');
    LogPath := ExpandFileName(LogPath);
    Params := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\apply-update.ps1') +
              '" -PayloadDir "' + ExpandConstant('{tmp}\kkpayload') +
              '" -TargetDir "' + TargetDir + '" -LogFile "' + LogPath + '"';
    if TestMode then Params := Params + ' -TestMode';
    Log('Running: ' + Ps + ' ' + Params);
    if not Exec(Ps, Params, '', SW_HIDE, ewWaitUntilTerminated, Rc) then Rc := 99;
    Log('apply-update.ps1 exit code: ' + IntToStr(Rc));
    if Rc <> 0 then
    begin
      RaiseException(FmtMessage(CustomMessage('Failed'), [IntToStr(Rc), LogPath]));
    end;
  end;
end;