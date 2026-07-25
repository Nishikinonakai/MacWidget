# MacWidget Privacy Notice / 隐私说明

**Effective date / 生效日期：2026-07-26**

MacWidget is a Windows desktop-widget application. This notice describes the data flows in the
current application build. It is not a substitute for product-specific legal advice in the
territories where the application is offered.

MacWidget 是一款 Windows 桌面小组件应用。本说明描述当前构建中的数据流；在实际销售地区发布前，
仍应按适用法律取得产品专属的法律意见。

## What MacWidget does not operate / 不提供的服务

MacWidget has no account system, developer-operated cloud backend, advertising SDK, or first-party
analytics/telemetry service. It does not upload the contents of your photos, media library, desktop
layout, battery data, system-monitor readings, or current media session to a MacWidget-operated
server.

MacWidget 没有账号系统、自建云端后端、广告 SDK 或第一方分析/遥测服务。它不会把你的照片内容、
媒体库内容、桌面布局、电池数据、系统监视数据或当前媒体会话上传到 MacWidget 运营的服务器。

## Data stored locally / 本地保存的数据

The app stores its layout, preferences, widget configuration, log, and WebView2 user-data folder
under `%LOCALAPPDATA%\MacWidget`. A photo widget may store the folder path you choose and reads
the names and contents of supported images from that folder solely to display them on your desktop.
System, battery, and media widgets read data from Windows locally.

应用会在 `%LOCALAPPDATA%\MacWidget` 保存布局、偏好、组件配置、日志和 WebView2 用户数据。照片组件可保存你
选择的文件夹路径，并仅为显示桌面照片而读取该目录中受支持图片的名称和内容。系统、电池和媒体组件均在
Windows 本机读取数据。

To remove this local application data, quit MacWidget and delete `%LOCALAPPDATA%\MacWidget`.
Uninstalling the app intentionally preserves this directory so that an upgrade does not erase your
layout; it does not delete your original photo files.

如需删除本地应用数据，请先退出 MacWidget，再删除 `%LOCALAPPDATA%\MacWidget`。卸载会有意保留此目录，
以免升级时清空布局；它不会删除你的原始照片文件。

## Weather data / 天气数据

If you add and use a weather widget, MacWidget sends a direct HTTPS request to MET Norway's
Locationforecast API. The request includes the configured city coordinates (rounded to four decimal
places) and an identifying application User-Agent. Because the request is direct, MET Norway also
receives normal network metadata such as your IP address. MacWidget does not use device GPS or send
your photos, media data, desktop layout, or account information to MET Norway.

如果你添加并使用天气组件，MacWidget 会直接通过 HTTPS 请求 MET Norway 的 Locationforecast API。请求包含已配置
城市的坐标（四位小数）和可识别的应用 User-Agent。由于请求为客户端直连，MET Norway 也会收到包括 IP 地址在内的
常规网络元数据。MacWidget 不使用设备 GPS，也不会向 MET Norway 发送照片、媒体数据、桌面布局或账号信息。

MET Norway states that its API access logs store the client IP address and any geocoordinates used
in requests. Its terms and privacy statement govern that processing:

- [MET Weather API Terms of Service](https://api.met.no/doc/TermsOfService)
- [MET Norway Privacy Policy](https://www.met.no/en/About-us/privacy)

MET Norway 表示，其 API 访问日志会保存客户端 IP 地址及请求中的坐标；相关处理受其条款和隐私声明约束，链接如上。
删除天气组件即可停止该组件的数据请求；也可以在组件设置中改用其他城市。

## Microsoft Edge WebView2 / Microsoft Edge WebView2

MacWidget uses the Microsoft Edge WebView2 Evergreen Runtime to render widget interfaces. The
runtime is supplied and updated by Microsoft, and Microsoft may process data according to its own
terms and privacy statement. MacWidget's bundled notice identifies the runtime and its distribution
terms in `THIRD-PARTY-NOTICES.md`.

MacWidget 使用 Microsoft 提供和更新的 Edge WebView2 Evergreen Runtime 渲染组件界面；Microsoft 对该运行时的
处理受其自身条款和隐私声明约束。随安装包提供的 `THIRD-PARTY-NOTICES.md` 说明了该运行时及其分发条款。

## Changes and contact / 更新与联系

If this notice changes materially, the updated version will be distributed with a later application
build. For privacy questions or to report a problem, use the project's issue tracker:
<https://github.com/Nishikinonakai/MacWidget/issues>.

如本说明发生重大变化，更新版本会随之后的应用构建发布。如有隐私问题或需报告问题，请使用项目 issue：
<https://github.com/Nishikinonakai/MacWidget/issues>。
