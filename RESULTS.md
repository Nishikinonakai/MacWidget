# 测试结果（2026-07-16，home-win 真机）

环境：Win11 26200，4096×2160 DCI-4K@300%，Wallpaper Engine 动态壁纸 + MacDesk v1.5.0 透明直通模式常驻，
WebView2 Runtime 150.0.4078.65，.NET 10.0.2，机主系统设置 **透明效果=关闭**（EnableTransparency=0，深色主题）。

## E1 内存曲线（WS=工作集 / Priv=私有提交，MB，稳态两次采样均值）

| 配置 | 进程数 | WS | Priv | renderer 数 |
|---|---|---|---|---|
| native N=4（纯 WPF 基线） | 1 | **108** | 65 | — |
| hwnd same N=1 | 7 | 460 | 240 | 1 |
| hwnd same N=2 | 8 | 525 | 277 | 2 |
| hwnd same N=4 | 10 | 693 | 413 | 4 |
| hwnd same N=8 | 14 | **975** | 601 | 8 |
| hwnd same N=8 + **--process-per-site** | 7 | **543** | 455 | **1** |

- **同 site 默认不合并 renderer**：每个 WebView2 控件一个 renderer（60-90MB WS），边际 ≈ **74MB WS/组件**——放任不管就是 Electron 级的账。
- **`--process-per-site`（CoreWebView2EnvironmentOptions.AdditionalBrowserArguments）是胜负手**：8 renderer→1，N=8 总量 -44%，边际降到 ≈ **12MB WS/组件**。
- 固定 Chromium 税 ≈ 450-550MB WS（browser 152 + GPU 108-120 + host ~100 + utility ~75）。GPU 进程私有随可见像素涨（4K 下 188-232MB）——1080p 用户会显著低。
- 注意：--process-per-site 是非官方 Chromium 开关，失效的后果只是回到默认多进程（功能无损），可接受。
- N=8 时 2 个组件排到屏外（网格溢出），受 Chromium 遮挡节流影响数字略偏乐观。
- 产品期可再叠加：对被全屏遮挡的组件调 `CoreWebView2.TrySuspend()`。

## E2 材质叠加视觉

| 组合 | 结果 |
|---|---|
| 透明效果=关（机主默认） | 一切材质→**纯色回退**（深灰卡片），效果居然不难看，产品必须把这形态当一等公民设计 |
| native WPF + DWMSBT acrylic（透明开） | ✅ 真模糊，壁纸/图标透出 |
| **hwnd WebView2 + DWMSBT acrylic（透明开）** | ✅✅ **产品配方**：网页透明背景 + DWM 亚克力叠加成立，身后图标隔卡透出，正宗 macOS 质感 |
| comp WebView2CompositionControl | ⚠️ 裸 `net10.0-windows` 直接崩（缺 Microsoft.Windows.SDK.NET，TFM 必须 `net10.0-windows10.0.19041.0`）；修好后能渲染，但**网页透明区合成在黑底上，DWM 材质透不过来**——材质路线不可用，仅在需要 WPF 元素叠 webview 时再启用 |
| mica | ❌ 深色下近全黑纯板，不采样实时内容，不适合本产品 |
| wca（SetWindowCompositionAttribute accent） | 透明关时同样纯色回退，无优势；弃用，坚持公开 API DWMSBT |

细节：DWM 调用在透明关闭时照样返回 S_OK——**判断回退态必须读注册表 EnableTransparency**，产品要监听它切换卡片样式。
伪造 WM_NCACTIVATE 不影响材质渲染（非激活回退假说被排除，根因就是系统开关）。

## E3 贴底与桌面相处（HWND_BOTTOM + WM_WINDOWPOSCHANGING）

- ✅ z 序：组件恰好压在 Progman 上一层、**所有**应用窗口之下、MacDesk 图标层（DefView 子窗）之上。
- ✅ 启动不抢前台（GetForegroundWindow 前后一致）；WS_EX_NOACTIVATE + MA_NOACTIVATE 生效。
- ✅ **"显示桌面"（ToggleDesktop）组件不被收走**，照常渲染——白捡 macOS 的"组件住在桌面"语义。
- ✅ 无边框全屏窗完全盖住组件，零穿透零闪烁；关闭后组件完好。
- ✅ 与 WE 动态壁纸 + MacDesk 透明直通全程共存无冲突。

## E4 正式安装包回归（2026-07-26，home-win 真机）

工件为私有 CI run `30166713335` 的 `MacWidget-Setup-v0.2.0-ci.17.exe`，对应源码
`854451c`。在安装前使用工件随附的 `Verify-MacWidgetInstaller.ps1` 校验文件名与 SHA-256，
结果通过；安装器大小为 59,161,904 bytes。

| 检查项 | 结果 |
|---|---|
| 旧正式版升级到 CI.17 | ✅ 安装完成；安装目录版本为 `0.2.0-ci.17+854451c...` |
| 自包含 .NET | ✅ `coreclr.dll`、`hostfxr.dll`、`hostpolicy.dll` 均在安装目录 |
| 启动与单实例 | ✅ `--restart` PID 切换后只保留一个进程；安装后首次启动也通过 |
| WebView2 / 托盘 / MacDesk | ✅ Runtime 就绪、托盘就绪，且 `widgetlink connected to MacDesk` 出现在本次启动日志 |
| 天气与许可通知 | ✅ MET Norway 请求 HTTP 200；安装目录包含完整 `THIRD-PARTY-NOTICES.md` |
| 数据保留 | ✅ 同版本静默重装退出码 `0`，`settings.json`、`layout.json` SHA-256 均未变化 |

这次回归也收紧了 `tools/smoke-installed.ps1`：它会断言安装目录的自包含运行时与预期版本，
并在限定时间内只接受最近一次启动后的 WebView2、托盘和 MacDesk 就绪信号。这样可避免旧日志或
进程刚创建时的启动竞态造成误通过或误失败。

## 结论：架构定案输入

**C# WPF 宿主 + 共享 CoreWebView2Environment(--process-per-site) + hwnd WebView2 控件（DefaultBackgroundColor=Transparent）
+ DWMSBT acrylic（监听 EnableTransparency 做纯色回退）+ HWND_BOTTOM 贴底** ——三关全过，可以立项。

遗留课题（产品期）：组件间输入（点击/拖拽摆位）、多屏、每显示器 DPI、`TrySuspend` 省内存、
纯色回退态的视觉精修、1080p 内存复测。
