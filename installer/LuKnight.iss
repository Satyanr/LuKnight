#ifndef AppIdValue
  #define AppIdValue "{{195B7E97-4AFA-4815-8B92-1DFCA05D5C3B}"
#endif

#ifndef AppNameValue
  #define AppNameValue "Lu-Knight"
#endif

#ifndef DefaultDirNameValue
  #define DefaultDirNameValue "{localappdata}\Programs\LuKnight"
#endif

#ifndef DefaultGroupNameValue
  #define DefaultGroupNameValue "Lu-Knight"
#endif

#ifndef RunValueNameValue
  #define RunValueNameValue "LuKnight"
#endif

#ifndef PreferenceKeyValue
  #define PreferenceKeyValue "Software\LuKnight"
#endif

#ifndef AppMutexValue
  #define AppMutexValue "Local\LuKnight-App"
#endif

#ifndef DataDirectoryValue
  #define DataDirectoryValue "{localappdata}\LuKnight"
#endif

#ifndef CredentialTargetValue
  #define CredentialTargetValue "LuKnight/GeminiApiKey"
#endif

#ifndef OutputDirValue
  #define OutputDirValue "..\artifacts\release"
#endif

#ifndef OutputBaseFilenameValue
  #define OutputBaseFilenameValue "LuKnightSetup"
#endif

#ifndef AllowSilentRemovePreferences
  #define AllowSilentRemovePreferences 0
#endif

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={#AppIdValue}
AppName={#AppNameValue}
AppVersion={#AppVersion}
AppPublisher=Lu-Knight contributors
AppPublisherURL=https://github.com/Satyanr/LuKnight
DefaultDirName={#DefaultDirNameValue}
DefaultGroupName={#DefaultGroupNameValue}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
OutputDir={#OutputDirValue}
OutputBaseFilename={#OutputBaseFilenameValue}
SetupIconFile=..\Assets\LuKnight.ico
UninstallDisplayIcon={app}\LuKnight.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=no
AppMutex={#AppMutexValue}
VersionInfoVersion={#AppVersion}.0

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.env,.env*,settings.json,*.bak,*.tmp,*.pfx,*.p12"

[Icons]
Name: "{group}\{#AppNameValue}"; Filename: "{app}\LuKnight.exe"
Name: "{autodesktop}\{#AppNameValue}"; Filename: "{app}\LuKnight.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#RunValueNameValue}"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\LuKnight.exe"; Description: "Launch Lu-Knight"; Flags: nowait postinstall skipifsilent; Check: not IsUpdate
Filename: "{app}\LuKnight.exe"; Flags: nowait; Check: ShouldLaunchUpdate

[Code]
function OpenProcess(Access: LongWord; Inherit: Boolean; ProcessId: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function CredDelete(Target: string; CredType, Flags: LongWord): Boolean;
  external 'CredDeleteW@advapi32.dll stdcall';

var RemovePreferences: Boolean;

function HasParameter(Name: string): Boolean;
var
  I: Integer;
begin
  Result := False;

  for I := 1 to ParamCount do
    if CompareText(
         ParamStr(I),
         Name) = 0 then begin

      Result := True;
      Exit;
    end;
end;

function IsUpdate: Boolean;
begin
  Result :=
    HasParameter(
      '/UPDATE');
end;

function ShouldLaunchUpdate: Boolean;
begin
  Result :=
    IsUpdate and
    not HasParameter(
      '/NORUNAPP');
end;

function InitializeSetup: Boolean;
var Pid: Integer; ProcessHandle: THandle;
begin
  Result := True;
  Pid := StrToIntDef(ExpandConstant('{param:TARGETPID|0}'), 0);
  if IsUpdate and (Pid > 0) then begin
    ProcessHandle := OpenProcess($00100000, False, Pid);
    if ProcessHandle <> 0 then begin
      Result := WaitForSingleObject(ProcessHandle, 60000) = 0;
      CloseHandle(ProcessHandle);
      if not Result then MsgBox('Lu-Knight is still running. Close it and retry the update.', mbError, MB_OK);
    end;
  end;
end;

procedure CurStepChanged(
  CurStep: TSetupStep);

var
  Existing: string;

begin
  if CurStep = ssPostInstall then begin

    if RegQueryStringValue(
         HKCU,
         'Software\Microsoft\Windows\CurrentVersion\Run',
         '{#RunValueNameValue}',
         Existing) and
       (Existing <> '') then

      RegWriteStringValue(
        HKCU,
        'Software\Microsoft\Windows\CurrentVersion\Run',
        '{#RunValueNameValue}',
        '"' +
        ExpandConstant(
          '{app}\LuKnight.exe') +
        '" --startup');
  end;
end;

function InitializeUninstall: Boolean;
begin
  RemovePreferences := False;


#if Int(AllowSilentRemovePreferences) == 1
  if HasParameter(
       '/REMOVEPREFERENCES') then
    RemovePreferences :=
      True;
#endif


  Result :=
    not CheckForMutexes(
      '{#AppMutexValue}');


  if not Result then begin

    MsgBox(
      'Exit Lu-Knight before uninstalling.',
      mbInformation,
      MB_OK);

    Exit;
  end;


  if not UninstallSilent and
     not RemovePreferences then

    RemovePreferences :=
      MsgBox(
        'Also remove your Lu-Knight settings and saved API key? Choose No to keep them.',
        mbConfirmation,
        MB_YESNO or
        MB_DEFBUTTON2) =
      IDYES;
end;

procedure CurUninstallStepChanged(
  CurUninstallStep:
    TUninstallStep);

begin
  if (CurUninstallStep =
        usPostUninstall) and
     RemovePreferences then begin

    DelTree(
      ExpandConstant(
        '{#DataDirectoryValue}'),
      True,
      True,
      True);


    RegDeleteKeyIncludingSubkeys(
      HKCU,
      '{#PreferenceKeyValue}');


    CredDelete(
      '{#CredentialTargetValue}',
      1,
      0);
  end;
end;
