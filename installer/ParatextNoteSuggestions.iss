; Windows installer for the Note Suggestions plugin for Paratext 9.5. Built by scripts/package.ps1:
;   ISCC /DAppVersion=0.4.0-preview /DFileVersion=0.4.0.0 /DSourceDir=<staged files> /DOutputDir=<folder> ParatextNoteSuggestions.iss
; It installs the plugin into Paratext's Plugins folder, refreshes Paratext's plugin cache, and
; registers an uninstaller in Windows' installed apps. Paratext must be closed.

#ifndef AppVersion
  #error AppVersion is required; build with scripts/package.ps1
#endif
#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif
#ifndef SourceDir
  #error SourceDir is required; build with scripts/package.ps1
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
; Fixed: updates replace the installed plugin instead of adding a second entry.
AppId={{16F5C142-42FE-47D4-B147-27194A470FAB}
AppName=Note Suggestions for Paratext
AppVersion={#AppVersion}
AppVerName=Note Suggestions for Paratext {#AppVersion}
AppPublisher=selvabenz
AppPublisherURL=https://github.com/selvabenz/Paratext-sug-in-context-menu
AppSupportURL=https://github.com/selvabenz/Paratext-sug-in-context-menu/issues
AppUpdatesURL=https://github.com/selvabenz/Paratext-sug-in-context-menu/releases
VersionInfoVersion={#FileVersion}
VersionInfoProductTextVersion={#AppVersion}
DefaultDirName={code:PluginDir}
DisableDirPage=yes
UsePreviousAppDir=no
DisableProgramGroupPage=yes
DisableReadyPage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Paratext is checked explicitly below; the Restart Manager would offer to close it.
CloseApplications=no
LicenseFile={#SourceDir}\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=ParatextNoteSuggestions-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Note Suggestions for Paratext {#AppVersion}
; Paratext scans its Plugins folder; keep only the plugin and its documents there.
UninstallFilesDir={commonappdata}\ParatextNoteSuggestions\Uninstall

[Files]
Source: "{#SourceDir}\ParatextNoteSuggestions.ptxplg"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}\docs"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "{#SourceDir}\README.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#SourceDir}\INSTALL.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#SourceDir}\RELEASE_NOTES.md"; DestDir: "{app}\docs"; Flags: ignoreversion

[UninstallDelete]
; Also when the folder came from an earlier install.ps1 install.
Type: dirifempty; Name: "{app}"

[Run]
Filename: "{code:ParatextFolder}\UpdateParatextPluginCache.exe"; StatusMsg: "Updating Paratext's plugin list..."; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
var
  ParatextDir: String;

// Paratext 9 records its folder in the 32-bit registry view.
function FindParatext(): Boolean;
begin
  if not RegQueryStringValue(HKLM32, 'SOFTWARE\Paratext\8', 'Paratext9_Full_Release_AppPath', ParatextDir) then
    ParatextDir := '';
  if (ParatextDir = '') or not FileExists(AddBackslash(ParatextDir) + 'Paratext.exe') then
    ParatextDir := ExpandConstant('{commonpf64}\Paratext 9');
  ParatextDir := RemoveBackslashUnlessRoot(ParatextDir);
  Result := FileExists(AddBackslash(ParatextDir) + 'Paratext.exe');
end;

function ParatextRunning(): Boolean;
var
  Code: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq Paratext.exe" /NH | find /I "Paratext.exe"', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

// Paratext must be closed so the plugin file is not in use and the cache refresh is picked up.
function WaitForParatextClosed(): Boolean;
begin
  Result := True;
  while ParatextRunning() do
    if SuppressibleMsgBox('Paratext is running. Save your work and close Paratext, then click Retry.', mbError, MB_RETRYCANCEL, IDCANCEL) = IDCANCEL then
    begin
      Result := False;
      exit;
    end;
end;

function InitializeSetup(): Boolean;
var
  Version: String;
begin
  Result := False;
  if not FindParatext() then
  begin
    SuppressibleMsgBox('Paratext 9 was not found. Install Paratext 9.5 first.', mbError, MB_OK, IDOK);
    exit;
  end;
  if not GetVersionNumbersString(AddBackslash(ParatextDir) + 'Paratext.exe', Version) then Version := '';
  if Copy(Version, 1, 4) <> '9.5.' then
  begin
    SuppressibleMsgBox('This plugin requires Paratext 9.5. Found Paratext ' + Version + ' in ' + ParatextDir + '.', mbError, MB_OK, IDOK);
    exit;
  end;
  if Version <> '9.5.110.1' then
    if SuppressibleMsgBox('This plugin was tested with Paratext 9.5.110.1; this computer has ' + Version + '. Install anyway?', mbConfirmation, MB_YESNO, IDYES) = IDNO then
      exit;
  Result := WaitForParatextClosed();
end;

function PluginDir(Param: String): String;
begin
  Result := AddBackslash(ParatextDir) + 'Plugins\ParatextNoteSuggestions';
end;

function ParatextFolder(Param: String): String;
begin
  Result := ParatextDir;
end;

function InitializeUninstall(): Boolean;
begin
  Result := WaitForParatextClosed();
end;

// After the plugin is removed, let Paratext rebuild its plugin list. {app} is <Paratext>\Plugins\ParatextNoteSuggestions.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Updater: String;
  Code: Integer;
begin
  if CurUninstallStep <> usPostUninstall then exit;
  Updater := AddBackslash(ExtractFileDir(ExtractFileDir(ExpandConstant('{app}')))) + 'UpdateParatextPluginCache.exe';
  if FileExists(Updater) then Exec(Updater, '', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;
