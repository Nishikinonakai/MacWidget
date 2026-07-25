# MacWidget

MacWidget 是 Windows 桌面小组件应用：把时钟、日历、天气、系统状态、正在播放、照片和电池状态
放在桌面上，自由摆放并随桌面状态自动调整外观。它是原生 C# / WPF 宿主，组件界面使用 WebView2
渲染；不依赖 Electron，也不需要账号。

当前处于可安装的 Beta 收口阶段。发布准备状态见
[Steam 上架清单](docs/store/steam-checklist.zh.md)。

## 功能

- **七种组件**：时钟、日历、天气、系统监视、正在播放、照片轮播、电池。
- **桌面级交互**：右键组件可改尺寸、配置或移除；进入“编辑小组件”后可从组件库拖出新组件。
- **真实数据**：系统状态、媒体会话和电池在本机读取；天气直接请求 MET Norway 的公开服务。
- **自动外观**：跟随系统明暗主题；普通窗口覆盖当前显示器时，组件自动变为低调单色。
- **节能与多屏**：完全被普通窗口遮挡的组件会挂起 WebView2 与数据采样；布局按显示器标识和分辨率保存，
  显示器变化后安全交接。
- **托盘入口**：正式图标、原生 WPF 浮层、编辑组件、自启开关和退出。
- **可选 MacDesk 联动**：MacDesk 可避让组件占用区域；桌面右键的 “Edit Widgets…” 会打开组件库。

## 安装与使用

1. 运行 `MacWidget-Setup-v*.exe`，按当前用户安装，不需要管理员权限。
2. 安装器会检测 Microsoft Edge WebView2 Runtime；缺失时自动运行微软 Evergreen Bootstrapper。
   首次补装 Runtime 时需要联网，已安装 Runtime 的电脑不会重复下载。
3. 从开始菜单启动 MacWidget；单击或右击通知区图标可打开浮层。
4. 选择“编辑小组件…”即可打开组件库，拖动卡片到桌面完成添加。

WebView2 Runtime 会由 Microsoft 独立更新。MacWidget 检测到新版运行时时只显示托盘提示，不会打断当前
桌面；可在托盘浮层选择“重新启动 MacWidget”以安全交接并应用更新。

安装、升级和卸载都保留 `%LOCALAPPDATA%\MacWidget` 中的布局与偏好。安装器升级时会先退出旧版本，
然后等待单实例锁释放并自动恢复新版本。

## 系统要求与隐私

- Windows 10 1903+ 或 Windows 11，x64。
- Microsoft Edge WebView2 Runtime（安装器会在缺失时引导安装）。
- 建议 8 GB 以上内存；实际占用随组件数量、照片和媒体内容变化。

没有账号、遥测或自建后端。天气请求会直接发送到 MET Norway；其余组件数据均在本机读取。
天气数据来源为 [MET Norway Locationforecast 2.0](https://api.met.no/weatherapi/locationforecast/2.0/compact)，
遵循其 CC-BY 4.0 许可与请求频率要求。

安装目录中的 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) 列出 WebView2 SDK 的再发布通知，以及
MET Norway 天气数据的完整署名与许可链接。

## 可选：配合 MacDesk

MacDesk 与 MacWidget 可以独立安装。两者都在运行时，MacWidget 会向 MacDesk 发送组件占用的物理像素
矩形，MacDesk 可让桌面图标避让这些区域。该开关位于 MacDesk 设置中的 MacWidget 联动页；MacDesk
也会检测已安装的 MacWidget，并在桌面右键菜单提供打开组件库的入口。

## 从源码构建

```bash
# 发布 x64 Windows 构建
~/.dotnet/dotnet publish src/WidgetProto -c Release -r win-x64 --no-self-contained -o publish

# 部署到已配置的 Windows 测试机
./deploy.sh 192.168.1.8
```

安装器脚本位于 `installer/macwidget.iss`。其中的 WebView2 引导器来源、哈希和刷新规则见
[installer/WEBVIEW2.md](installer/WEBVIEW2.md)。产品行为与测试记录请参阅：

- [TESTPLAN.md](TESTPLAN.md) — 测试机操作和实验方法。
- [RESULTS.md](RESULTS.md) — 早期技术验证结果。
- [docs/store/](docs/store/) — 商店文案、定价和发布清单。

开发/实验命令行参数仍保留在 `Options.cs`，不建议普通用户使用；MacDesk 拉起组件库使用
`MacWidget.exe --edit-widgets`。
