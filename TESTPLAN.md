# MacWidget 技术原型测试计划

> 本文档独立成立：新会话不带任何上下文也能照此执行。
> 背景：MacWidget = Windows 上的 macOS 风格桌面小组件（付费产品，调研结论见记忆 `macwidget-idea`）。
> 本原型要在正式立项前回答三个技术问题，全部在 home-win 真机验证。

## 三个实验

### E1. WebView2 多组件真实内存曲线
**问题**：一个 C# 宿主 + 共享 CoreWebView2Environment，N 个组件窗口的内存随 N 怎么涨？边际成本多少 MB/组件？同源（同 site）能否合并 renderer 省内存？
**做法**：`run-once.ps1` 对 N=1/2/4/8 各跑一轮（settle 40s + 3 次采样取稳态），`-Origin same` 与 `-Origin multi` 各一遍。对照组：`-Control native -N 4`（纯 WPF，无 WebView2 的基线）。
**判据**：主观上限 = 8 组件全家 < 400MB WS 且边际 < 30MB/组件为"可接受"；multi 与 same 的差值即 renderer 合并收益。
**注意**：msedgewebview2 进程按命令行含 `widgetproto` 过滤（udf 在 app 目录下）；WS 与 Priv 都记，报告以 WS 为主。

### E2. 宿主云母/亚克力 + 网页内容叠加的视觉效果
**问题**：DWM 系统材质（宿主 HWND 上公开 API）能否透过透明背景的 WebView2 内容显示出来？hwnd 控件和 composition 控件谁行？
**做法**：分三步截图对比（`GET /screen`）：
1. `--control native --backdrop acrylic` —— 对照组，先证明 backdrop 配方本身成立（WPF 卡片下透出模糊壁纸即成功）；
2. `--control hwnd --backdrop acrylic` —— 经典 HWND 托管 WebView2 + DefaultBackgroundColor=Transparent；
3. `--control comp --backdrop acrylic` —— WebView2CompositionControl（本命方案，无空域问题）。
   每步再换 `--backdrop mica` 对比一次质感。
**判据**：网页卡片的半透明区域下透出**模糊后的壁纸**（不是黑/白/纯色）即成功；记录哪种控件+材质组合可用。
**已知理论风险**：`transparent`+材质在 Electron 是互斥的；WebView2 hwnd 控件的透明合成历史上有怪癖 —— comp 控件是主要指望。
**配方细节**（已按 MacDesk 踩坑记忆写死在代码里）：`AllowsTransparency=false`（铁律，layered 杀 WPF D3D）、`CompositionTarget.BackgroundColor=Transparent`（透明直通同款）、`DwmExtendFrameIntoClientArea(-1)`（`--glass none` 可关掉对比）、圆角 DWMWCP_ROUND、材质 DWMWA_SYSTEMBACKDROP_TYPE。

### E3. HWND_BOTTOM 贴桌面与全屏应用/桌面操作的相处
**问题**：贴底方案（SetWindowPos(HWND_BOTTOM) + WM_WINDOWPOSCHANGING 强制回底，不碰 WorkerW）在真实桌面上的行为。
**观察点**：
- a) 组件是否稳定在**所有普通窗口之下**、**桌面图标（MacDesk 图标层）之上**？（`zorder.ps1` dump + 截图）
- b) 点击组件是否抢焦点/把自己抬起来？（默认 WS_EX_NOACTIVATE + MA_NOACTIVATE）
- c) "显示桌面"：`(New-Object -ComObject Shell.Application).ToggleDesktop()`（COM，绕开 UIPI 注入限制）——组件会不会被最小化/消失？macOS 语义是 widgets 常驻桌面。
- d) 全屏应用在上面时组件是否完全被盖住、不闪不穿透？（借 WinForms 造一个 borderless 全屏窗 15 秒后自灭，期间截图+zorder）
- e) 有 Wallpaper Engine 动态壁纸同时跑时是否互不干扰（home-win 常态就开着 WE，天然测试）。
**判据**：a/b/d/e 必须通过；c 若贴底窗被 Win+D 收走，记录现象即可（Rainmeter 有解法，产品期再处理）。

## home-win 操作手册（浓缩自记忆 homewin-testing）

- **找机器**：IP 随 DHCP 漂（最近 = 192.168.1.8）。连不上就扫：对 192.168.1.0/24 逐个
  `curl -s --noproxy '*' -m 1.5 -H "X-Token: $TOKEN" http://IP:18800/ping`，应答 200 的就是。
- **token**：`cat ~/.config/macdesk/homewin-token.txt`。curl 一律带 `--noproxy '*'`（双方都开 Clash）。
- **执行 PowerShell**：`POST http://IP:18800/ps`，body 就是脚本文本，交互会话执行，返回 {code,out,err}。
  调用 .ps1 文件用 `& C:\work\widgetproto\tools\xxx.ps1 -N 4`；若被执行策略挡，改
  `powershell -ExecutionPolicy Bypass -File ...`。
- **截图**：`GET http://IP:18800/screen` → PNG（物理分辨率 4096×2160）。
- **别注入输入**：机主 PowerToys 全家以管理员常驻，合成输入会被 UIPI 静默丢弃。本测试计划已避开
  （ToggleDesktop 用 COM，全屏窗用自灭脚本）。
- **机主随时可能在用机**：动手前查 idle（GetLastInputInfo）> 3 分钟；测完杀干净 MacWidget 全家。
- 机器常态：4K@300%，Wallpaper Engine + MacDesk v1.5.0（透明直通模式）都在跑——保持原样，这就是目标环境。

## 构建与部署（Mac 侧）

```bash
cd ~/Documents/Windows_desktop_macOSfied/widgetproto
./deploy.sh 192.168.1.8        # publish（框架依赖，home-win 已有 .NET 10 Desktop 运行时）+ scp
```
NuGet 必须绕代理（deploy.sh 已处理）。ssh/scp 用 `nakai@<ip>`，key 认证已配好。
`deploy.sh` 是快速开发部署，故有意保持框架依赖；GitHub Actions 产出的正式 Inno 安装器是
`win-x64` 自包含发布，必须包含 `coreclr.dll`、`hostfxr.dll` 和 `hostpolicy.dll`，终端用户无需单独安装 .NET。

## 正式安装版冒烟检查

每次安装器安装或升级后，用此脚本验证应用路径、单实例、WebView2 Runtime、WebView2/托盘启动信号、
天气服务和可选的 MacDesk 管道联动。脚本默认只读；首次检查可加 `-StartIfNeeded` 拉起尚未运行的正式安装版。

```powershell
& C:\work\widgetproto\tools\smoke-installed.ps1 `
  -StartIfNeeded -RequireMacDeskLink -ExpectedVersion 0.2.0-ci.17 |
  Format-List
```

`MacDeskLinked` 是状态字段：没有安装或没有运行 MacDesk 时应为 `False`，但不应妨碍 MacWidget 独立使用；
只有传入 `-RequireMacDeskLink` 时才将其作为失败条件。网络临时不可用时可传 `-SkipNetwork`，其余本地启动项
仍会检查。
`ExpectedVersion` 应填入正在验证的安装器文件名中的版本号（例如
`MacWidget-Setup-v0.2.0-ci.17.exe` 对应 `0.2.0-ci.17`）；它按前缀比较，以允许 CI 写入
commit 信息版本。该检查同时确认安装目录带有 `coreclr.dll`、`hostfxr.dll` 和 `hostpolicy.dll`，而不是
意外依赖测试机预装的 .NET。WebView2、托盘和 MacDesk 管道信号必须出现在最近一次 `=== start:`
启动标记之后，旧会话日志不能使本次启动误通过。
脚本会在 `ReadyTimeoutSeconds` 内轮询这些信号；首次安装或升级后可把该值提高到 `60`。

要同时覆盖 WebView2 Runtime 更新后的安全重启交接，可显式传入 `-ExerciseRestart`。这会使正在运行的
MacWidget 交接至新 PID，并把单实例、托盘和 MacDesk 就绪信号作为同一次检查的通过条件：

```powershell
& C:\work\widgetproto\tools\smoke-installed.ps1 `
  -ExerciseRestart -ReadyTimeoutSeconds 60 -RequireMacDeskLink `
  -ExpectedVersion 0.2.0-ci.17 |
  Format-List
```

从私有 CI artifact 取得 Beta 安装器时，其中已含同名 `.sha256` 与 `Verify-MacWidgetInstaller.ps1`；安装前在
解压目录执行以下命令核验。文件名与散列必须同时匹配，任一不符都会失败退出：

```powershell
& .\Verify-MacWidgetInstaller.ps1 `
  -InstallerPath .\MacWidget-Setup-v0.2.0-ci.11.exe `
  -ChecksumPath .\MacWidget-Setup-v0.2.0-ci.11.exe.sha256 |
  Format-List
```

### Evergreen WebView2 Runtime 更新

Evergreen Runtime 在后台安装新版本时，MacWidget 记录 `webview2 runtime update available` 并显示托盘提示，
不会强制打断桌面。用户从托盘浮层选择“重新启动 MacWidget”后，复用显示拓扑交接的 `--restart-child` 路径。
回归时使用上方的 `-ExerciseRestart` 触发同一路径并确认交接后的单实例、托盘和 MacDesk 联动都恢复正常。

## 结果记录模板

```
E1 内存（4K@300%, WE+MacDesk 常驻背景下）:
  native N=4:            WS = ___ MB（宿主基线）
  same   N=1/2/4/8:      WS = ___ / ___ / ___ / ___ MB   边际 ≈ ___ MB/组件
  multi  N=1/2/4/8:      WS = ___ / ___ / ___ / ___ MB   边际 ≈ ___ MB/组件
  renderer 数（same vs multi, N=8）: ___ vs ___
E2 视觉: native+acrylic ___ / hwnd+acrylic ___ / comp+acrylic ___ / comp+mica ___
E3: zorder ___ / 焦点 ___ / ToggleDesktop ___ / 全屏 ___ / WE 共存 ___
```

## 清场

```powershell
Stop-Process -Name MacWidget,WidgetProto -Force -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" |
  Where-Object { $_.CommandLine -like '*widgetproto*' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
```
