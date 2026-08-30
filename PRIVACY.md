# MacWidget Privacy Notice / 隐私说明

**Effective date / 生效日期：2026-08-30**

MacWidget is a Windows desktop-widget application. This notice describes the data flows in the
current application build. It is not a substitute for legal advice.

MacWidget 是一款 Windows 桌面小组件应用。本说明描述当前构建中的数据流，不构成法律意见。

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

The Quick Links widget stores the labels and HTTP(S) addresses you enter in the same local layout
file. MacWidget contacts a configured site only after you click its shortcut; the destination then
receives normal browser request data under that site's own policies.

快捷网址组件会在同一份本地布局文件中保存你填写的名称和 HTTP(S) 地址。只有在你单击对应快捷方式后，
MacWidget 才会交给默认浏览器访问该站点；目标网站随后会按其自身政策收到常规浏览器请求数据。

The Local Note widget stores its title and plain-text body in the same local layout file. The Focus
Timer stores its selected duration, remaining time, and end timestamp there so a running timer can
survive an application restart. Neither widget sends this data over the network.

本地速记组件会在同一份本地布局文件中保存标题和纯文本正文。专注计时器会在其中保存所选时长、剩余时间和
结束时间，以便应用重启后继续计时。这两个组件都不会把这些数据发送到网络。

The Keep Awake widget stores its selected duration, optional end timestamp, and display preference
locally, then uses the Windows execution-state API; it does not contact a service. The Offline QR
widget stores the text you enter in the local layout file and generates the QR image locally. The
text is not uploaded by MacWidget, although another device that scans the result may act on it.

防休眠组件会在本机保存所选时长、可选结束时间和屏幕偏好，并调用 Windows 执行状态 API，不连接网络服务。
离线二维码组件会把你输入的文字保存在本地布局文件中，并完全在本机生成二维码；MacWidget 不会上传该文字，
但扫描二维码的其他设备可能会按其中内容执行操作。

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

## Manual update checks / 手动检查更新

MacWidget does not check for product updates in the background. When you choose “Check for Updates”
from the tray menu, the app requests the latest public release metadata from GitHub's HTTPS API.
GitHub receives the repository path, the MacWidget version in the User-Agent, and normal network
metadata such as your IP address. No widget configuration, desktop layout, or photo data is sent.

MacWidget 不会在后台检查产品更新。只有当你从托盘菜单选择“检查更新”时，应用才会通过 HTTPS 请求 GitHub
最新公开 Release 的元数据。GitHub 会收到固定的仓库路径、User-Agent 中的 MacWidget 版本，以及包括 IP 地址
在内的常规网络元数据；组件配置、桌面布局和照片数据不会被发送。

## Changes and contact / 更新与联系

If this notice changes materially, the updated version will be distributed with a later application
build. For privacy questions or to report a problem, use the project's issue tracker:
<https://github.com/Nishikinonakai/MacWidget/issues>.

如本说明发生重大变化，更新版本会随之后的应用构建发布。如有隐私问题或需报告问题，请使用项目 issue：
<https://github.com/Nishikinonakai/MacWidget/issues>。
