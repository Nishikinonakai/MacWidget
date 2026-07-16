# WidgetProto — MacWidget 技术原型

MacWidget（Windows 上的 macOS 风格桌面小组件，规划中的付费产品）立项前的三项技术验证：

1. **内存**：C# 宿主 + 共享 CoreWebView2Environment，N 个 web 组件的内存曲线与边际成本；
2. **视觉**：宿主窗口 DWM 云母/亚克力（公开 API `DWMWA_SYSTEMBACKDROP_TYPE`）+ 透明背景 WebView2 的叠加效果；
3. **贴底**：`HWND_BOTTOM` + `WM_WINDOWPOSCHANGING` 贴桌面层（不碰 WorkerW）与全屏应用/桌面操作的相处。

结论直接决定 MacWidget 用不用「WPF 宿主 + WebView2（组件=HTML/CSS/JS）」这条架构。
测试步骤、home-win 操作手册、结果模板见 **[TESTPLAN.md](TESTPLAN.md)**。

## 结构

```
src/WidgetProto/        net10.0-windows WPF 宿主（无 XAML，代码构 UI）
  Program.cs            入口 + 共享 WebView2 环境 + 日志(proto.log)
  WidgetWindow.cs       组件窗：无边框/透明直通/材质/贴底/不抢焦点
  Native.cs             DWM + BottomPin P/Invoke
  web/*.html            四个示例组件（时钟rAF/CPU canvas 10Hz/天气/照片KenBurns）
tools/*.ps1             home-win 侧：内存采样、单轮矩阵、z 序 dump
deploy.sh               Mac 侧构建（绕代理）+ scp 到 home-win C:\work\widgetproto
```

## 命令行

```
WidgetProto.exe --n 4 --control comp --backdrop acrylic --origin same --pin bottom --widget mixed
  --control  hwnd | comp | native     comp=WebView2CompositionControl（本命方案）
  --backdrop none | mica | acrylic | tabbed
  --origin   same | multi             multi=每组件独立 site，强制拆 renderer
  --pin      bottom | none
  --widget   mixed | clock | monitor | weather | photo
  --glass    extend | none            DwmExtendFrameIntoClientArea(-1) 开关
  --light / --activate                浅色 / 允许抢焦点
```

与 MacDesk 完全独立的代码库；将来 MacWidget 若立项，此仓库只作技术档案，不直接演化成产品。
