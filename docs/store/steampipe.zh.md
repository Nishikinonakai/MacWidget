# MacWidget SteamPipe 上传准备

状态：已准备好模板；没有 App ID、Depot ID 和 Steamworks 构建账号时，**不能也不会**上传任何内容。

MacWidget 在 Steam 上应直接分发 self-contained publish 目录，而不是 Inno 安装器。Steam 负责安装、更新和
回滚；应用继续将用户布局和设置存放在 `%LOCALAPPDATA%\MacWidget`，不会被 Steam 内容更新覆盖。

## Steamworks 侧一次性设置（机主）

拿到 App ID 后，在 Steamworks：

1. 添加 Windows 启动项，Executable 填 `MacWidget.exe`，不加前导斜杠或点号。
2. 创建或确认一个 Windows 内容 Depot，记录 Depot ID；它应包含自包含 publish 目录的所有文件。
3. 使用权限最小化的构建账号，至少授予该 App 的 `Edit App Metadata` 和 `Publish App Changes To Steam`。
4. 在构建机上从 Steamworks SDK 的 `tools\ContentBuilder\builder` 首次运行 `steamcmd.exe`，完成 Steam Guard 登录。

上述口径来自 Valve 的 SteamPipe 上传文档；默认分支不能由脚本自动设为 live，建议先把构建放到私有 beta
branch 中测试，再在 Steamworks 后台决定是否发布。

## 取得可复现的内容目录

每次私有 CI 成功后，都会保留 14 天一个 `MacWidget-Steam-Content-<version>` artifact。它包含：

- `publish/`：与同次 Inno 安装器同源的 self-contained 应用内容；
- `steam-content/MacWidget-Steam-Content-<version>.sha256`：内容逐文件 SHA-256；
- `tools/verify-steam-content.ps1`：工件解压后的内容校验器。

解压后先校验再将 `publish/` 作为 SteamPipe `ContentRoot`：

```powershell
& .\tools\verify-steam-content.ps1 `
  -ContentRoot .\publish `
  -ChecksumPath .\steam-content\MacWidget-Steam-Content-<version>.sha256
```

如果需要本地临时构建，也可直接生成相同结构的 self-contained publish 目录：

## 生成 VDF（不上传）

先从当前源码生成正式内容目录：

```powershell
dotnet publish .\src\WidgetProto\WidgetProto.csproj `
  --configuration Release --runtime win-x64 --self-contained true `
  --output C:\build\macwidget\publish
```

然后生成 VDF。`-Preview` 会在 VDF 中写入 SteamPipe 的预览标志；该脚本本身不调用 SteamCMD，也不接收密码或
Steam Guard 凭据。

```powershell
& .\tools\prepare-steampipe.ps1 `
  -AppId 1234567 -DepotId 1234568 `
  -ContentRoot C:\build\macwidget\publish `
  -SteamPipeScriptsDir D:\SteamworksSDK\tools\ContentBuilder\scripts\MacWidget `
  -BuildOutputDir D:\SteamworksSDK\tools\ContentBuilder\output\MacWidget `
  -Version 0.2.0 -Preview
```

脚本会拒绝缺少 `MacWidget.exe`、三个 self-contained .NET 运行时文件或 `THIRD-PARTY-NOTICES.md` 的内容目录，
也拒绝覆盖已有 VDF。

## 上传（机主授权后）

检查生成的 `app_build_<AppID>.vdf` 与 `depot_build_<DepotID>.vdf` 后，使用已经完成 Steam Guard 登录的构建账号：

```powershell
& D:\SteamworksSDK\tools\ContentBuilder\builder\steamcmd.exe `
  +login <build-account> `
  +run_app_build D:\SteamworksSDK\tools\ContentBuilder\scripts\MacWidget\app_build_<AppID>.vdf `
  +quit
```

上传后在 Steamworks 的 Builds 页面检查日志和 Manifest；先分配给私有 beta branch 验证，再决定是否切换默认分支。

## 依据

- [Valve: Uploading to Steam](https://partner.steamgames.com/doc/sdk/uploading) — ContentBuilder 目录、VDF、preview、
  branch 与 SteamCMD 工作流。
