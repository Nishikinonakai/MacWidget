; MacWidget 单用户安装器（Inno Setup 6）。
; 构建：ISCC /DMyAppVersion=0.2.0 /DSourceDir=<publish> installer\macwidget.iss
; 设计：不需要管理员权限；升级/卸载先 --quit，用户数据 %LOCALAPPDATA%\MacWidget 永不删除。

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif
#define MyAppVerNum Pos("-", MyAppVersion) > 0 ? Copy(MyAppVersion, 1, Pos("-", MyAppVersion) - 1) : MyAppVersion

[Setup]
AppId={{A2253620-97A8-4AFB-8EF9-97CC6340A2C2}
AppName=MacWidget
AppVersion={#MyAppVersion}
AppVerName=MacWidget {#MyAppVersion}
AppPublisher=Nishikinonakai
DefaultDirName={localappdata}\Programs\MacWidget
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=.
OutputBaseFilename=MacWidget-Setup-v{#MyAppVersion}
UninstallDisplayIcon={app}\MacWidget.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
VersionInfoVersion={#MyAppVerNum}
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\MacWidget"; Filename: "{app}\MacWidget.exe"

; 让 MacDesk 和 shell 都能按正式应用路径发现它；卸载时整键清理。
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\MacWidget.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\MacWidget.exe"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\MacWidget.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
Filename: "{app}\MacWidget.exe"; Description: "{cm:LaunchProgram,MacWidget}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\MacWidget.exe"; Flags: nowait; Check: ShouldRelaunch

[Code]
function ShouldRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var R: Integer;
begin
  if FileExists(ExpandConstant('{app}\MacWidget.exe')) then
  begin
    Exec(ExpandConstant('{app}\MacWidget.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, R);
    Sleep(2500);
    Exec('taskkill.exe', '/F /IM MacWidget.exe', '', SW_HIDE, ewWaitUntilTerminated, R);
    Sleep(500);
  end;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var R: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    if FileExists(ExpandConstant('{app}\MacWidget.exe')) then
    begin
      Exec(ExpandConstant('{app}\MacWidget.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, R);
      Sleep(2500);
      Exec('taskkill.exe', '/F /IM MacWidget.exe', '', SW_HIDE, ewWaitUntilTerminated, R);
      Sleep(500);
    end;
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'MacWidget');
  end;
end;
