; HT File Hash Checker Setup - Inno Setup 6.3+ (built and tested with Inno Setup 6.7).
;
; Built by scripts\build-release.ps1, which compiles and signs the application into release\stage first:
;   ISCC /DStage=<stage> /DOutputRoot=<dir> [/Shtsign=... /DSignTool=htsign] HTFileHashChecker.iss
;
; Follows HT-Software-Template/HTREASURE-Branding/IntegrationExamples/Installer: publisher HTREASURE LTD,
; Program Files\HTREASURE\<product>, Start menu HTREASURE\<product>, "An HTREASURE LTD product", the kit's wizard
; artwork, a product-specific AppId that is never changed or reused.
;
; Setup is a native program, so it runs on a computer without .NET Framework. It selects the application build:
;   .NET Framework 4.8 build   - Windows 7 SP1, 8.1, 10, 11
;   .NET Framework 4.6.1 build - Windows 8.0 (cannot install 4.8)
; and installs the matching .NET Framework from download.microsoft.com only if it is missing and the user agrees.
; This file must stay UTF-8 with BOM so the copyright symbol is preserved.

#define AppVersion     "1.0.0"
#define ProductName    "HT File Hash Checker"
#define ProductExe     "HTFileHashChecker.exe"
#define AppGuid        "{{EFF67025-D374-416A-8D05-D6836E52E97E}"
; The same id as Windows stores it (single brace), for registry look-ups in [Code].
#define AppGuidKey     "{EFF67025-D374-416A-8D05-D6836E52E97E}"
#define Publisher      "HTREASURE LTD"
#define CopyrightYear  GetDateTimeString('yyyy', '', '')
#define Copyright      "Copyright © " + CopyrightYear + " HTREASURE LTD. All rights reserved."
#define BrandingRoot   SourcePath + "..\external\HT-Software-Template\HTREASURE-Branding"
#define Images         BrandingRoot + "\IntegrationExamples\Installer\Images"
#define VerbKey        "HTFileHashChecker.Calculate"
#define VerbText       "Calculate Hash with HT File Hash Checker"

#ifndef Stage
  #define Stage SourcePath + "..\release\stage"
#endif
#ifndef OutputRoot
  #define OutputRoot SourcePath + "..\release\v" + AppVersion
#endif

#if !FileExists(Stage + "\app\net48\" + ProductExe) || !FileExists(Stage + "\app\net461\" + ProductExe)
  #error Staged builds missing. Run scripts\build-release.ps1.
#endif
#if !FileExists(Stage + "\docs\LICENSE.txt")
  #error Staged documents missing. Run scripts\build-release.ps1.
#endif

; ---------------------------------------------------------------- prerequisite manifest (compile time)
#define Manifest SourcePath + "prerequisites.ini"
#define NDP48_NAME   ReadIni(Manifest, "NDP48", "Name", "")
#define NDP48_URL    ReadIni(Manifest, "NDP48", "Url", "")
#define NDP48_FILE   ReadIni(Manifest, "NDP48", "FileName", "")
#define NDP48_SHA    ReadIni(Manifest, "NDP48", "SHA256", "")
#define NDP48_SIZE   ReadIni(Manifest, "NDP48", "SizeBytes", "0")
#define NDP48_ARGS   ReadIni(Manifest, "NDP48", "InstallArgs", "")
#define NDP48_INFO   ReadIni(Manifest, "NDP48", "InfoPage", "")
#define NDP461_NAME  ReadIni(Manifest, "NDP461", "Name", "")
#define NDP461_URL   ReadIni(Manifest, "NDP461", "Url", "")
#define NDP461_FILE  ReadIni(Manifest, "NDP461", "FileName", "")
#define NDP461_SHA   ReadIni(Manifest, "NDP461", "SHA256", "")
#define NDP461_SIZE  ReadIni(Manifest, "NDP461", "SizeBytes", "0")
#define NDP461_ARGS  ReadIni(Manifest, "NDP461", "InstallArgs", "")
#define NDP461_INFO  ReadIni(Manifest, "NDP461", "DownloadCenterPage", "")
#if NDP48_SHA == "" || NDP461_SHA == ""
  #error Prerequisites are not pinned. Run scripts\pin-prerequisites.ps1 -Mode Pin first.
#endif
#if Pos("https://download.microsoft.com/", NDP48_URL) != 1 || Pos("https://download.microsoft.com/", NDP461_URL) != 1
  #error Prerequisite URLs must be https://download.microsoft.com/ URLs.
#endif

[Setup]
AppId={#AppGuid}
AppName={#ProductName}
AppVersion={#AppVersion}
AppVerName={#ProductName} {#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL=https://htreasure.com
AppSupportURL=https://htreasure.com
AppCopyright={#Copyright}
AppComments=An HTREASURE LTD product
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#Publisher}
VersionInfoCopyright={#Copyright}
VersionInfoProductName={#ProductName}
VersionInfoProductVersion={#AppVersion}
VersionInfoDescription={#ProductName} Setup
OutputDir={#OutputRoot}
OutputBaseFilename=HTFileHashChecker_Setup_v{#AppVersion}

; Windows 7 SP1 is the oldest supported system (and Inno Setup 6's own minimum).
MinVersion=6.1sp1
; The application is AnyCPU: 64-bit on x64 (and Arm64 via emulation), 32-bit on x86.
ArchitecturesAllowed=x86compatible x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; All users (Program Files, administrator) by default; "only for me" (no administrator rights) is offered too.
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
DefaultDirName={autopf}\HTREASURE\{#ProductName}
DefaultGroupName=HTREASURE\{#ProductName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
LicenseFile={#Stage}\docs\LICENSE.txt
InfoBeforeFile={#Stage}\docs\PRIVACY.txt
SetupIconFile={#BrandingRoot}\Icons\Windows\HTREASURE.ico
UninstallDisplayIcon={app}\{#ProductExe}
UninstallDisplayName={#ProductName}
WizardStyle=modern
WizardImageFile={#Images}\inno-wizard-large-164x314.bmp,{#Images}\inno-wizard-large-246x459.bmp,{#Images}\inno-wizard-large-328x604.bmp
WizardSmallImageFile={#Images}\inno-wizard-small-55x55.bmp,{#Images}\inno-wizard-small-83x80.bmp,{#Images}\inno-wizard-small-110x106.bmp
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes
; The running application holds this mutex; Setup asks the user to close it before replacing files.
AppMutex=HTREASURE_HTFileHashChecker_Running
CloseApplications=yes
RestartApplications=no
; Upgrades: same AppId installs over the previous version in place, keeping its folder and task choices.
UsePreviousAppDir=yes
UsePreviousTasks=yes
ChangesAssociations=yes
#ifdef SignTool
; Self-signed HTREASURE LTD certificate (see docs\CODE-SIGNING.md): Setup and the uninstaller are signed.
SignTool={#SignTool}
SignedUninstaller=yes
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nAn HTREASURE LTD product.%n%nSetup checks this computer and installs Microsoft .NET Framework only if it is missing and you agree.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "explorer"; Description: "Add ""{#VerbText}"" to the File Explorer right-click menu"; GroupDescription: "Windows integration (optional):"; Flags: unchecked

[Files]
; Exactly one application build is installed (see SelectBuild in decision.iss); builds are never mixed.
Source: "{#Stage}\app\net48\*";  Excludes: "*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsSelectedBuild('net48')
Source: "{#Stage}\app\net461\*"; Excludes: "*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: IsSelectedBuild('net461')
Source: "{#Stage}\docs\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\docs\THIRD_PARTY_NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\docs\PRIVACY.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#Stage}\docs\README.txt"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; A portable marker must never exist in an installed copy (it would move settings into the program folder).
Type: files; Name: "{app}\HTFileHashChecker.portable"

[Icons]
Name: "{group}\{#ProductName}"; Filename: "{app}\{#ProductExe}"; Comment: "Calculate, compare and verify file checksums"
Name: "{autodesktop}\{#ProductName}"; Filename: "{app}\{#ProductExe}"; Comment: "Calculate, compare and verify file checksums"; Tasks: desktopicon

[Registry]
; Optional Explorer verb for files and folders: plain registry keys, no shell-extension DLL, no service.
; HKA = HKLM\Software\Classes for an all-users install, HKCU\Software\Classes for "only for me".
Root: HKA; Subkey: "Software\Classes\*\shell\{#VerbKey}"; ValueType: string; ValueName: ""; ValueData: "{#VerbText}"; Flags: uninsdeletekey; Tasks: explorer
Root: HKA; Subkey: "Software\Classes\*\shell\{#VerbKey}"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{#VerbText}"; Tasks: explorer
Root: HKA; Subkey: "Software\Classes\*\shell\{#VerbKey}"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#ProductExe}"",0"; Tasks: explorer
Root: HKA; Subkey: "Software\Classes\*\shell\{#VerbKey}"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: explorer
Root: HKA; Subkey: "Software\Classes\*\shell\{#VerbKey}\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#ProductExe}"" --calculate ""%1"""; Tasks: explorer
Root: HKA; Subkey: "Software\Classes\Directory\shell\{#VerbKey}"; ValueType: string; ValueName: ""; ValueData: "{#VerbText}"; Flags: uninsdeletekey; Tasks: explorer
Root: HKA; Subkey: "Software\Classes\Directory\shell\{#VerbKey}"; ValueType: string; ValueName: "MUIVerb"; ValueData: "{#VerbText}"; Tasks: explorer
Root: HKA; Subkey: "Software\Classes\Directory\shell\{#VerbKey}"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#ProductExe}"",0"; Tasks: explorer
Root: HKA; Subkey: "Software\Classes\Directory\shell\{#VerbKey}"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Player"; Tasks: explorer
Root: HKA; Subkey: "Software\Classes\Directory\shell\{#VerbKey}\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#ProductExe}"" --calculate ""%1"""; Tasks: explorer

[Run]
Filename: "{app}\{#ProductExe}"; Description: "Launch {#ProductName}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
; Also remove a right-click entry the user turned on inside the application (stored for their account only).
Filename: "{app}\{#ProductExe}"; Parameters: "--unregister-explorer"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterExplorer"

#include "decision.iss"

[Code]
const
  MAX_DOWNLOAD_ATTEMPTS = 3;
  UNINSTALL_SUBKEY = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#AppGuidKey}_is1';

var
  Sys: TSysInfo;
  SelBuild, SelReason, BlockReason, InstalledVersion: String;
  Supported, NeedPrereq: Boolean;
  PrereqName, PrereqUrl, PrereqFile, PrereqSha, PrereqArgs, PrereqInfo, PrereqLocalPath: String;
  PrereqSize: Int64;
  CompatPage, PrereqPage: TWizardPage;
  DownloadPage: TDownloadWizardPage;
  ConsentCheck: TNewCheckBox;

function IsSelectedBuild(const Build: String): Boolean;
begin
  Result := Supported and (SelBuild = Build);
end;

function FormatMB(Bytes: Int64): String;
begin
  Result := IntToStr(Bytes div (1024 * 1024)) + ' MB';
end;

procedure LoadPrerequisite(const Id: String);
begin
  if Id = 'NDP461' then begin
    PrereqName := '{#NDP461_NAME}'; PrereqUrl := '{#NDP461_URL}'; PrereqFile := '{#NDP461_FILE}';
    PrereqSha := '{#NDP461_SHA}'; PrereqArgs := '{#NDP461_ARGS}'; PrereqSize := {#NDP461_SIZE}; PrereqInfo := '{#NDP461_INFO}';
  end else begin
    PrereqName := '{#NDP48_NAME}'; PrereqUrl := '{#NDP48_URL}'; PrereqFile := '{#NDP48_FILE}';
    PrereqSha := '{#NDP48_SHA}'; PrereqArgs := '{#NDP48_ARGS}'; PrereqSize := {#NDP48_SIZE}; PrereqInfo := '{#NDP48_INFO}';
  end;
end;

function ReadInstalledVersion(): String;
begin
  Result := '';
  if IsWin64 and RegQueryStringValue(HKLM64, UNINSTALL_SUBKEY, 'DisplayVersion', Result) then exit;
  if RegQueryStringValue(HKLM32, UNINSTALL_SUBKEY, 'DisplayVersion', Result) then exit;
  if RegQueryStringValue(HKCU, UNINSTALL_SUBKEY, 'DisplayVersion', Result) then exit;
  Result := '';
end;

function MakeLabel(Page: TWizardPage; const Text: String; X, Y, W: Integer; Bold: Boolean): TNewStaticText;
begin
  Result := TNewStaticText.Create(Page);
  Result.Parent := Page.Surface;
  Result.Left := X;
  Result.Top := Y;
  Result.AutoSize := False;
  Result.Width := W;
  Result.Height := ScaleY(16);
  Result.Caption := Text;
  if Bold then Result.Font.Style := [fsBold];
end;

function AddRow(Page: TWizardPage; Y: Integer; const Caption, Value: String): Integer;
begin
  MakeLabel(Page, Caption, 0, Y, ScaleX(150), True);
  MakeLabel(Page, Value, ScaleX(155), Y, Page.SurfaceWidth - ScaleX(155), False);
  Result := Y + ScaleY(20);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  DetectSystem(Sys);
  SelBuild := SelectBuild(Sys, SelReason);
  Supported := SelBuild <> '';
  BlockReason := '';
  if not Supported then BlockReason := SelReason;
  NeedPrereq := Supported and not NetSatisfied(Sys, SelBuild);
  if Supported then LoadPrerequisite(PrerequisiteId(SelBuild));

  InstalledVersion := ReadInstalledVersion();
  if Supported and (InstalledVersion <> '') and (CompareVersionStrings(InstalledVersion, '{#AppVersion}') > 0) then begin
    Supported := False;
    BlockReason := 'A newer version of {#ProductName} (' + InstalledVersion + ') is already installed. Setup will not downgrade it.';
  end;
  // Installing .NET Framework needs administrator rights.
  if Supported and NeedPrereq and not IsAdminInstallMode then begin
    Supported := False;
    BlockReason := PrereqName + ' is required and is not installed. Installing it needs administrator rights: run Setup again and choose "Install for all users", or install '
      + PrereqName + ' from Microsoft (' + PrereqInfo + ') first.';
  end;

  Log(Format('System: %s; %s; .NET %s; build %s (%s); prerequisite needed: %d; admin: %d; installed: %s', [
    OsDisplayName(Sys), ArchDisplayName(Sys), NetReleaseDisplayName(Sys.NetRelease), SelBuild, SelReason,
    Ord(NeedPrereq), Ord(IsAdminInstallMode), InstalledVersion]));

  if WizardSilent and not Supported then begin
    Log('Silent install stopped: ' + BlockReason);
    Result := False;
  end;
end;

procedure ConsentClick(Sender: TObject);
begin
  WizardForm.NextButton.Enabled := ConsentCheck.Checked;
end;

procedure InitializeWizard();
var
  Y: Integer;
  Status, Notes, ExistingText: String;
  T: TNewStaticText;
  Memo: TNewMemo;
begin
  CompatPage := CreateCustomPage(wpInfoBefore, 'System Compatibility Check',
    'Setup has checked this computer and selected the correct version of {#ProductName}.');
  Y := 0;
  Y := AddRow(CompatPage, Y, 'Operating system', OsDisplayName(Sys));
  Y := AddRow(CompatPage, Y, 'Architecture', ArchDisplayName(Sys));
  if SelBuild = BUILD_NET461 then
    Y := AddRow(CompatPage, Y, 'Application build', '.NET Framework 4.6.1 build (Windows 8)')
  else if SelBuild <> '' then
    Y := AddRow(CompatPage, Y, 'Application build', '.NET Framework 4.8 build')
  else
    Y := AddRow(CompatPage, Y, 'Application build', 'None available for this system');
  if SelBuild <> '' then Y := AddRow(CompatPage, Y, 'Required .NET', RequiredNetName(SelBuild));
  Y := AddRow(CompatPage, Y, 'Installed .NET', NetReleaseDisplayName(Sys.NetRelease));
  if InstalledVersion <> '' then ExistingText := 'Version ' + InstalledVersion + ' (will be upgraded)' else ExistingText := 'None';
  Y := AddRow(CompatPage, Y, 'Existing installation', ExistingText);
  if not Supported then Status := 'Cannot install' else Status := 'Compatible';
  MakeLabel(CompatPage, 'Status', 0, Y, ScaleX(150), True);
  T := MakeLabel(CompatPage, Status, ScaleX(155), Y, CompatPage.SurfaceWidth - ScaleX(155), True);
  if Supported then T.Font.Color := $477606 else T.Font.Color := $1823B4;   // BGR of #067647 / #B42318
  Y := Y + ScaleY(24);
  Notes := '';
  if BlockReason <> '' then Notes := Notes + BlockReason + #13#10#13#10;
  if SupportNotice(Sys) <> '' then Notes := Notes + 'Security notice: ' + SupportNotice(Sys) + #13#10#13#10;
  if RuntimeNotice(SelBuild) <> '' then Notes := Notes + RuntimeNotice(SelBuild) + #13#10#13#10;
  if Notes = '' then Notes := SelReason;
  Memo := TNewMemo.Create(CompatPage);
  Memo.Parent := CompatPage.Surface;
  Memo.Top := Y;
  Memo.Width := CompatPage.SurfaceWidth;
  Memo.Height := CompatPage.SurfaceHeight - Y;
  Memo.ReadOnly := True;
  Memo.ScrollBars := ssVertical;
  Memo.Text := Trim(Notes);

  PrereqPage := CreateCustomPage(CompatPage.ID, 'Prerequisite Check', 'Only components this computer is missing are installed.');
  Y := 0;
  if Supported and NeedPrereq then begin
    T := MakeLabel(PrereqPage, PrereqName + ' (publisher: Microsoft Corporation) is required and is not installed. Setup will download it ('
      + FormatMB(PrereqSize) + ') directly from Microsoft (download.microsoft.com) over HTTPS, check its SHA-256 fingerprint and Microsoft '
      + 'digital signature, and then run Microsoft''s installer. Windows may need to restart afterwards; then run Setup again.',
      0, Y, PrereqPage.SurfaceWidth, False);
    T.WordWrap := True;
    T.Height := ScaleY(84);
    Y := Y + ScaleY(90);
    ConsentCheck := TNewCheckBox.Create(PrereqPage);
    ConsentCheck.Parent := PrereqPage.Surface;
    ConsentCheck.Top := Y;
    ConsentCheck.Width := PrereqPage.SurfaceWidth;
    ConsentCheck.Height := ScaleY(18);
    ConsentCheck.Caption := 'I agree to download and install ' + PrereqName + ' (Microsoft licence terms apply)';
    ConsentCheck.OnClick := @ConsentClick;
  end else if Supported then begin
    T := MakeLabel(PrereqPage, 'Everything {#ProductName} needs is already installed (' + NetReleaseDisplayName(Sys.NetRelease)
      + '). Nothing will be downloaded.', 0, Y, PrereqPage.SurfaceWidth, False);
    T.WordWrap := True;
    T.Height := ScaleY(40);
  end;
  DownloadPage := CreateDownloadPage('Downloading Microsoft .NET Framework', 'Downloading from download.microsoft.com and verifying the file.', nil);
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = CompatPage.ID then
    WizardForm.NextButton.Enabled := Supported
  else if CurPageID = PrereqPage.ID then begin
    if Supported and NeedPrereq then WizardForm.NextButton.Enabled := ConsentCheck.Checked
    else WizardForm.NextButton.Enabled := Supported;
  end;
end;

function VerifyPackage(const Path: String; var Problem: String): Boolean;
var
  Sig: Integer;
  Detail: String;
begin
  Result := False;
  if not HashMatches(Path, PrereqSha) then begin
    Problem := 'The downloaded file does not match the official ' + PrereqName + ' package (SHA-256). It was not run.';
    DeleteFile(Path);
    exit;
  end;
  Sig := CheckAuthenticode(Path, 'Microsoft Corporation', Detail);
  Log('Signature check: ' + IntToStr(Sig) + ' - ' + Detail);
  if Sig = 1 then begin
    Problem := 'The package does not carry a valid Microsoft digital signature. It was not run. (' + Detail + ')';
    DeleteFile(Path);
    exit;
  end;
  Result := True;
end;

function AcquirePrerequisite(): Boolean;
var
  Attempt: Integer;
  Problem, Path: String;
  Done: Boolean;
begin
  Result := False;
  Path := ExpandConstant('{tmp}\') + PrereqFile;
  Done := False;
  Attempt := 0;
  while not Done do begin
    Attempt := Attempt + 1;
    DownloadPage.Clear;
    DownloadPage.Add(PrereqUrl, PrereqFile, PrereqSha);   // Inno Setup checks the SHA-256 itself as well
    DownloadPage.Show;
    try
      try
        DownloadPage.Download;
        Done := True;
      except
        Problem := GetExceptionMessage;
        Log('Download attempt ' + IntToStr(Attempt) + ' failed: ' + Problem);
        if DownloadPage.AbortedByUser then exit;
        if (Attempt >= MAX_DOWNLOAD_ATTEMPTS) or (SuppressibleMsgBox('Downloading ' + PrereqName + ' failed:' + #13#10 + Problem
            + #13#10#13#10 + 'Check your internet connection and try again.', mbError, MB_RETRYCANCEL, IDCANCEL) <> IDRETRY) then begin
          SuppressibleMsgBox('Setup could not download ' + PrereqName + ' securely. Setup never weakens security to work around this.'
            + #13#10#13#10 + 'Install ' + PrereqName + ' from Microsoft (' + PrereqInfo + ') and run this Setup again.', mbInformation, MB_OK, IDOK);
          exit;
        end;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
  if not VerifyPackage(Path, Problem) then begin
    SuppressibleMsgBox(Problem, mbCriticalError, MB_OK, IDOK);
    exit;
  end;
  PrereqLocalPath := Path;
  Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and Supported and NeedPrereq and (PrereqLocalPath = '') then
    Result := AcquirePrerequisite();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Result := '';
  if WizardSilent and NeedPrereq and (PrereqLocalPath = '') then begin
    // No interactive consent: prerequisites are never downloaded silently.
    Result := PrereqName + ' is required. Install it first, or run Setup interactively.';
    exit;
  end;
  if PrereqLocalPath = '' then exit;
  if not Exec(PrereqLocalPath, PrereqArgs, '', SW_SHOW, ewWaitUntilTerminated, Code) then begin
    Result := 'Could not start the ' + PrereqName + ' installer: ' + SysErrorMessage(Code);
    exit;
  end;
  Log(PrereqName + ' installer exit code: ' + IntToStr(Code));
  case Code of
    0: begin
         Sys.NetRelease := ReadNetRelease();
         if not NetSatisfied(Sys, SelBuild) then
           Result := PrereqName + ' reported success, but Setup cannot detect it. Restart Windows and run Setup again.';
       end;
    1641, 3010: begin
         NeedsRestart := True;
         Result := PrereqName + ' was installed. Restart Windows, then run this Setup again to install {#ProductName}.';
       end;
    1602: Result := 'The ' + PrereqName + ' installation was cancelled. {#ProductName} needs it and was not installed.';
    5100: Result := 'Microsoft''s installer reports that this computer does not meet the requirements for ' + PrereqName + '.';
  else
    Result := PrereqName + ' could not be installed (Microsoft installer exit code ' + IntToStr(Code) + ').';
  end;
end;

procedure RemoveExplorerVerb(RootKey: Integer);
begin
  RegDeleteKeyIncludingSubkeys(RootKey, 'Software\Classes\*\shell\{#VerbKey}');
  RegDeleteKeyIncludingSubkeys(RootKey, 'Software\Classes\Directory\shell\{#VerbKey}');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    if PrereqLocalPath <> '' then DeleteFile(PrereqLocalPath);
    // All-users upgrade with the Explorer option now unticked: remove the entry an earlier Setup created.
    // (Per-user entries are left alone: the user may have turned them on inside the application.)
    if IsAdminInstallMode and not WizardIsTaskSelected('explorer') then RemoveExplorerVerb(HKLM);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then begin
    // User files and checksums are never touched. Settings are kept unless the user asks to remove them.
    if MsgBox('Also remove your {#ProductName} settings and log files?' + #13#10#13#10
        + 'Your files, reports and checksum manifests are never removed.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then begin
      DelTree(ExpandConstant('{userappdata}\HTREASURE LTD\HT File Hash Checker'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\HTREASURE LTD\HT File Hash Checker'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\HTREASURE\ht-file-hash-checker'), True, True, True);
    end;
  end;
end;
