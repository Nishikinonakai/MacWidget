; MacWidget 单用户安装器（Inno Setup 6）。
; 构建：ISCC /DMyAppVersion=0.4.0 /DSourceDir=<publish> installer\macwidget.iss
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
; 用户范围安装的 Inno Setup 有时不带中文包，故随仓库 vendored，CI/本机编译口径一致。
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[CustomMessages]
english.WebView2Required=MacWidget needs Microsoft Edge WebView2 Runtime. Connect to the internet and run the installer again.
chinesesimplified.WebView2Required=MacWidget 需要 Microsoft Edge WebView2 Runtime。请连接网络后重新运行安装程序。

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
; Microsoft 官方 Evergreen Bootstrapper（约 2 MB），仅在缺少 Runtime 时解压到 {tmp} 执行，
; 不会进入 MacWidget 安装目录；联网下载匹配架构的 Runtime，单用户安装无需 UAC。
Source: "MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\MacWidget"; Filename: "{app}\MacWidget.exe"

; 让 MacDesk 和 shell 都能按正式应用路径发现它；卸载时整键清理。
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\MacWidget.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\MacWidget.exe"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\MacWidget.exe"; ValueType: string; ValueName: "Path"; ValueData: "{app}"; Flags: uninsdeletekey

[Run]
; --restart-child 会等待旧实例释放单实例锁（最长 10 秒），避免升级后偶发首次拉起即退出。
Filename: "{app}\MacWidget.exe"; Parameters: "--restart-child"; Description: "{cm:LaunchProgram,MacWidget}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\MacWidget.exe"; Parameters: "--restart-child"; Flags: nowait; Check: ShouldRelaunch

[UninstallDelete]
; 卸载安装文件后才删除空目录；若用户手动存放内容，则保守保留。
Type: dirifempty; Name: "{app}"

[Code]
const
  WebView2ClientKey = 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2ClientKey64 = 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  WebView2Bootstrapper = 'MicrosoftEdgeWebview2Setup.exe';

function ShouldRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:RELAUNCH|0}') = '1';
end;

function IsInstalledRuntimeVersion(const Value: String): Boolean;
begin
  Result := (Value <> '') and (Value <> '0.0.0.0');
end;

// Microsoft 官方检测口径：x64 机器查 HKLM 的 WOW6432Node 与当前用户 HKCU 的 pv。
function HasWebView2Runtime: Boolean;
var Value: String;
begin
  Result := (RegQueryStringValue(HKLM64, WebView2ClientKey64, 'pv', Value) and IsInstalledRuntimeVersion(Value));
  if not Result then
    Result := (RegQueryStringValue(HKCU, WebView2ClientKey, 'pv', Value) and IsInstalledRuntimeVersion(Value));
end;

function EnsureWebView2Runtime: Boolean;
var R: Integer;
begin
  Result := HasWebView2Runtime;
  if Result then
  begin
    Log('WebView2 Runtime detected; Bootstrapper skipped');
    Exit;
  end;

  Log('WebView2 Runtime missing; launching Evergreen Bootstrapper');
  ExtractTemporaryFile(WebView2Bootstrapper);
  if not Exec(ExpandConstant('{tmp}\' + WebView2Bootstrapper), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, R) then
  begin
    Log('WebView2 Bootstrapper could not be started');
    Exit;
  end;
  Log(Format('WebView2 Bootstrapper exit code: %d', [R]));
  Result := (R = 0) and HasWebView2Runtime;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var R: Integer;
begin
  // 先补运行时再停旧版：离线/下载失败时不会无故关闭正在工作的旧版。
  if not EnsureWebView2Runtime then
  begin
    Result := ExpandConstant('{cm:WebView2Required}');
    Exit;
  end;
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
