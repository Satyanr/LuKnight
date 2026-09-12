#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
AppId={{195B7E97-4AFA-4815-8B92-1DFCA05D5C3B}
AppName=Lu-Knight
AppVersion={#AppVersion}
AppPublisher=Lu-Knight contributors
AppPublisherURL=https://github.com/Satyanr/LuKnight
DefaultDirName={localappdata}\Programs\LuKnight
DefaultGroupName=Lu-Knight
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
OutputDir=..\artifacts\release
OutputBaseFilename=LuKnightSetup
SetupIconFile=..\Assets\LuKnight.ico
UninstallDisplayIcon={app}\LuKnight.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=no
AppMutex=Local\LuKnight-App
VersionInfoVersion={#AppVersion}.0

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.env,.env*,settings.json,*.bak,*.tmp,*.pfx,*.p12"

[Icons]
Name: "{group}\Lu-Knight"; Filename: "{app}\LuKnight.exe"
Name: "{autodesktop}\Lu-Knight"; Filename: "{app}\LuKnight.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "LuKnight"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\LuKnight.exe"; Description: "Launch Lu-Knight"; Flags: nowait postinstall skipifsilent; Check: not IsUpdate
Filename: "{app}\LuKnight.exe"; Flags: nowait; Check: IsUpdate

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

function IsUpdate: Boolean;
var I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/UPDATE') = 0 then Result := True;
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

procedure CurStepChanged(CurStep: TSetupStep);
var Existing: string;
begin
  if CurStep = ssPostInstall then begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'LuKnight', Existing) and (Existing <> '') then
      RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'LuKnight', '"' + ExpandConstant('{app}\LuKnight.exe') + '" --startup');
  end;
end;

function InitializeUninstall: Boolean;
begin
  RemovePreferences := False;
  Result := not CheckForMutexes('Local\LuKnight-App');
  if not Result then begin
    MsgBox('Exit Lu-Knight from the system tray before uninstalling.', mbInformation, MB_OK);
    Exit;
  end;
  if not UninstallSilent then
    RemovePreferences := MsgBox('Also remove your Lu-Knight settings and saved API key? Choose No to keep them.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and RemovePreferences then begin
    DelTree(ExpandConstant('{localappdata}\LuKnight'), True, True, True);
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\LuKnight');
    CredDelete('LuKnight/GeminiApiKey', 1, 0);
  end;
end;
