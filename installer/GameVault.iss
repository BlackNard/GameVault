; Compile only through scripts/build-installer.ps1 (validated explicit payload).
#ifndef PayloadDirectory
  #error Use scripts/build-installer.ps1
#endif
; AppVersion comes from Directory.Build.props via build-installer.ps1; never define it here.
#ifndef AppVersion
  #error AppVersion must be supplied by scripts/build-installer.ps1
#endif

[Setup]
AppId={{11DE0F05-D06D-4594-A970-85DDE91DC106}
AppName=GameVault
AppVersion={#AppVersion}
AppVerName=GameVault {#AppVersion} (development)
AppPublisher=GameVault
AppPublisherURL=https://github.com/Zaid-Alhmouz/GameVault
DefaultDirName={localappdata}\Programs\GameVault
DefaultGroupName=GameVault
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
SetupIconFile=../src/GameVault.App/Assets/GameVault.ico
SetupArchitecture=x64
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.19041
WizardStyle=modern dynamic
DisableWelcomePage=no
OutputDir={#SetupOutput}
OutputBaseFilename=GameVault-Setup-{#AppVersion}-win-x64
Compression=lzma2/fast
SolidCompression=yes
UninstallDisplayIcon={app}\GameVault.exe
CloseApplications=no
RestartApplications=no
RestartIfNeededByRun=no
SetupMutex=GameVaultSetupWizard
InfoBeforeFile=notice.txt

[Tasks]
Name: startmenuicon; Description: "Create a Start menu shortcut"; Flags: checkedonce
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
#include PayloadEntries

[Icons]
Name: "{userprograms}\GameVault"; Filename: "{app}\GameVault.exe"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{userdesktop}\GameVault"; Filename: "{app}\GameVault.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Prerequisites\VC_redist.x64.exe"; Parameters: "/install /passive /norestart"; Description: "Install Microsoft Visual C++ x64 runtime for screenshots and recording (administrator approval required)"; Verb: "runas"; Flags: shellexec waituntilterminated postinstall skipifsilent unchecked
Filename: "{app}\GameVault.exe"; Description: "Launch GameVault"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent unchecked

; Deliberately no wildcard cleanup / UninstallDelete. Data, Profiles and the
; coordination lock are never owned by setup or logged for removal.
[Code]
var
  ApplicationLease: THandle;

function OpenLease(Name: String; Access, Sharing: Cardinal; Security: NativeUInt;
  Creation, Attributes: Cardinal; Template: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';
function CloseLease(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function PathAttributes(Name: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function PathIsSafe(Path: String): Boolean;
var
  Parent, Segment: String;
  Attributes: Cardinal;
begin
  Result := False;
  if (Length(Path) < 3) or (Copy(Path, 2, 2) <> ':\') then Exit;
  while Length(Path) > 3 do begin
    Segment := ExtractFileName(Path);
    if (CompareText(Segment, 'Data') = 0) or (CompareText(Segment, 'Profiles') = 0) then Exit;
    Attributes := PathAttributes(Path);
    if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then Exit;
    Parent := ExtractFileDir(Path);
    if CompareText(Path, Parent) = 0 then Exit;
    Path := Parent;
  end;
  Result := True;
end;

#include DestinationGuards

procedure ReleaseApplicationLease;
begin
  if ApplicationLease <> 0 then begin
    CloseLease(ApplicationLease);
    ApplicationLease := 0;
  end;
end;

function AcquireApplicationLease: String;
var
  Directory: String;
begin
  Result := '';
  ReleaseApplicationLease;
  Directory := RemoveBackslashUnlessRoot(ExpandConstant('{app}'));
  if (Length(Directory) <= 3) or not PathIsSafe(Directory) or
    not PathIsSafe(Directory + '\.gamevault.lock') or not PayloadPathsAreSafe or
    not PathIsSafe(Directory + '\unins000.exe') or not PathIsSafe(Directory + '\unins000.dat') then begin
    Result := 'Choose a local application folder without redirected folders. Do not install inside Data or Profiles.';
    Exit;
  end;
  if not ForceDirectories(Directory) then begin
    Result := 'GameVault needs a writable application folder. Choose a folder owned by your Windows account.';
    Exit;
  end;
  // OPEN_ALWAYS + GENERIC_READ/WRITE + no sharing matches ApplicationWorkspace.
  // Keep the file in place: deleting it could create two independent leases.
  ApplicationLease := OpenLease(Directory + '\.gamevault.lock', $C0000000, 0, 0, 4, $80, 0);
  if NativeInt(ApplicationLease) = -1 then begin
    ApplicationLease := 0;
    Result := 'Quit GameVault using its tray menu and retry. If it is already closed, check that this folder is writable. Setup will not close GameVault or interrupt a game session.';
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := AcquireApplicationLease;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // All application replacement is finished before the optional launch runs.
  if CurStep = ssPostInstall then ReleaseApplicationLease;
end;

procedure DeinitializeSetup;
begin
  ReleaseApplicationLease;
end;

function InitializeUninstall: Boolean;
var
  Failure: String;
begin
  Failure := AcquireApplicationLease;
  Result := Failure = '';
  if not Result then SuppressibleMsgBox(Failure, mbError, MB_OK, IDOK);
end;

procedure DeinitializeUninstall;
begin
  ReleaseApplicationLease;
end;
