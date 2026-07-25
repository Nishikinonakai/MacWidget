# MacWidget Steam 视觉素材 brief

状态：2026-07-26。已有首个无文字主视觉概念稿
[`assets/macwidget-capsule-concept-v1.png`](assets/macwidget-capsule-concept-v1.png)，它是方向验证，**不是**
可直接上传的 capsule。最终素材必须在名称、标题字形和法务风险确认后制作。

## 截图背景（已备好，尚未应用）

[`assets/macwidget-neutral-wallpaper-v1.png`](assets/macwidget-neutral-wallpaper-v1.png) 是一张 3840×2160 的
无文字、中性深灰桌面壁纸，中心保持低细节与低对比，专供拍摄商店截图时衬托浅色和深色组件。它不是
capsule，也不包含产品标识，不应上传到 Steam。

实际拍摄前须取得机主授权；先记录当前壁纸及其显示方式，再临时应用该文件，完成 4K 截图和所需的
1920×1080 导出后立即恢复原壁纸并目视确认。不要在未授权或机器正在使用时更改桌面设置。

仓库中的 `tools/stage-store-wallpaper.ps1` 将这个过程做成显式的可恢复会话：不带参数时只报告当前状态；
`-Apply` 会备份当前有本地文件路径的壁纸后再切换；`-Restore` 会恢复原图与样式并删除备份会话。先把候选
壁纸复制到测试机的本地路径，再在获授权的交互会话中依次运行：

```powershell
.\tools\stage-store-wallpaper.ps1
.\tools\stage-store-wallpaper.ps1 -Apply -WallpaperPath 'C:\work\macwidget-store\macwidget-neutral-wallpaper-v1.png'
# 拍摄并导出截图
.\tools\stage-store-wallpaper.ps1 -Restore
```

若当前壁纸不是可恢复的本地文件，脚本会拒绝自动切换，避免覆盖幻灯片、主题或在线壁纸状态。

## 已定视觉方向

- 深靛蓝到紫色的夜间桌面氛围；半透明玻璃组件卡片表达时钟、日历、天气、音乐四个核心场景。
- 仅一个暖琥珀四角星作为聚焦点，呼应应用图标；保持蓝紫主色，不使用苹果标志、SF 字体、macOS 窗口框或
  Windows 标志。
- 主视觉靠左，右侧预留低细节暗区给可读的产品标题；小尺寸时优先让四张卡片和标题可辨。
- 当前概念稿为 1672×941、约 16:9，无文字。它可作为主图/横幅的构图参考，制作 1232×706 主 capsule 时需
  按官方模板重新裁切和排版。

## 官方交付规格

以下是 Steam 当前模板要求，而不是旧的 616×353 / 231×87 规格：

| 类型 | 尺寸 | 内容约束 |
| --- | ---: | --- |
| Store Header Capsule | 920×430 | 主视觉 + 清晰的产品名称 |
| Store Small Capsule | 462×174 | 主视觉 + 清晰的产品名称 |
| Store Main Capsule | 1232×706 | 主视觉 + 清晰的产品名称 |
| Store Vertical Capsule | 748×896 | 主视觉 + 清晰的产品名称 |
| Store screenshots | 至少 1920×1080，16:9 | 真机产品画面；中性壁纸拍摄 |
| Library Capsule | 600×900 | 主视觉 + 产品名称 |
| Library Header | 920×430 | 主视觉 + 产品名称 |
| Library Hero | 3840×1240 PNG | 只放主视觉，不能有文字；关键物置于中央 860×380 安全区 |
| Library Logo | 宽 1280 或高 720 PNG | 仅产品标题字标（可加标记），透明背景 |

## 文案和合规边界

基础 Store capsule 只能包含产品名、可选正式副标题和产品美术；不要放折扣、评价分数、奖项、功能清单、
“Early Access”、平台图标或其他营销文案。Library Hero 不放文字，Library Logo 只放标题字标。所有素材都要
PG-13，并且产品名称必须在 capsule 中清晰可读。

产品目前暂定名 **MacWidget**。标题字标落稿前需完成名称/商标风险确认；若改名，应同步更新 Store 文案、
安装器、应用标识和全部素材。标题建议只用 `MacWidget`，不加 “macOS”、Apple 或 Windows 等字样。

## 下一次制作清单

1. 机主确认产品名称与该蓝紫玻璃方向。
2. 用真实的标题字标覆盖到主视觉右侧安全留白；先输出 Main Capsule，再同源裁切 Header、Small 和 Vertical。
3. 从无文字主视觉另出 Library Hero 和透明 Library Logo；不要把标题烘焙进 Hero。
4. 经机主授权后，临时使用上述中性壁纸，补拍 1920×1080 的七组件、编辑模式、天气/音乐、Automatic
   对比、照片配置和 MacDesk 避让联动素材；结束时恢复原壁纸。
5. 逐张套 Steam 当期官方模板检查裁切、安全区和小尺寸可读性，再上传 Steamworks。

## 依据

- Steamworks 的 [Graphical Assets Overview](https://partner.steamgames.com/doc/store/assets) 说明 2024 年起多数
  capsule 使用新尺寸，旧尺寸不再接受。
- [Store Graphical Assets](https://partner.steamgames.com/doc/store/assets/standard) 列出 Store Main Capsule 的
  1232×706 规格和标题可读性要求。
- [Graphical Asset Rules](https://partner.steamgames.com/doc/store/assets/rules) 规定基础 capsule 的可用文字范围；
  [Library Assets](https://partner.steamgames.com/doc/store/assets/libraryassets) 规定 Hero 与 Logo 的分层规则。
