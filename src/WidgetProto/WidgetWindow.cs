using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace WidgetProto;

public sealed class WidgetWindow : Window
{
    readonly int _i;
    readonly bool _startLifted;
    readonly DisplayTopology.Position? _initialPosition;
    CoreWebView2? _core;
    bool _removing;
    bool _occluded;
    bool _dataSuspended;

    public string Kind { get; }
    public string SizeClass { get; private set; }
    /// <summary>组件实例配置（widget 自定形状，宿主只存取不解释）；null = 未配置过。</summary>
    public System.Text.Json.JsonElement? Cfg { get; private set; }
    /// <summary>photo 专用：当前生效的照片文件夹（PhotoSupport 维护，供流时查）。</summary>
    public string? PhotoFolder { get; set; }

    /// <param name="size">尺寸档 s/m/l；null 或不支持 = kind 默认档（老档案兼容）</param>
    /// <param name="initialPosition">帧左上（相对显示器的物理 px）；空 = 实验模式栅格位</param>
    /// <param name="lifted">出生即升层（面板拖出中的新组件，不先钉底）</param>
    public WidgetWindow(int i, string kind, string? size = null, DisplayTopology.Position? initialPosition = null, bool lifted = false,
                        System.Text.Json.JsonElement? cfg = null)
    {
        Cfg = cfg;
        _i = i;
        Kind = kind;
        SizeClass = size != null && WidgetRegistry.SizesOf(kind).Contains(size)
            ? size : WidgetRegistry.DefaultSize(kind);
        _startLifted = lifted;
        _initialPosition = initialPosition;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;   // 铁律：layered 与 WPF D3D/DWM backdrop 互斥（macdesk-pitfalls）
        ShowInTaskbar = false;
        ShowActivated = !Program.Opts.NoActivate;
        Background = Brushes.Transparent;
        Title = $"MacWidget {i} {kind}";

        // 窗口 = 帧（摆放/避让/组格距的原子单位）；可视卡由页面 CSS 画（内衬 8、圆角 20、阴影）
        (Width, Height) = WidgetRegistry.Size(kind, SizeClass);

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0; Top = 0; // hWnd 建好后统一走物理像素定位，混合 DPI 不能用 Left/Top 跨屏。
        if (_initialPosition == null)
        {
            // 实验模式：刻意不规整的栅格铺开（自由摆放是一等公民，便于验证非网格落点）
            var display = DisplayTopology.Primary();
            double k = display.Scale;
            int col = i % 2, row = i / 2;
            double x = display.Work.Right - display.Physical.Left - 24 * k - (Width + 36) * k * (col + 1) + (col == 0 ? 0 : 13 * k);
            double y = display.Work.Top - display.Physical.Top + 20 * k + row * (Placement.Unit + 40) * k + i * 7 * k;
            _initialPosition = new DisplayTopology.Position(display.Key, x, y);
        }

        SourceInitialized += OnSourceInit;
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            DataHub.Drop(this);             // 订阅随窗口生命周期，最后一个走人即停采样
            if (!_removing) return;
            WidgetLink.Send(force: true);   // 管道对面即时回位
            Layout.Save();
            PanelWindow.Existing?.PushState();
        };
    }

    void OnSourceInit(object? s, EventArgs e)
    {
        var src = (HwndSource)PresentationSource.FromVisual(this)!;
        // MacDesk 透明直通同款：WPF 表面清除色透明，卡外区域直透桌面
        src.CompositionTarget.BackgroundColor = Colors.Transparent;

        var h = src.Handle;
        var ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
        ex |= Native.WS_EX_TOOLWINDOW;
        if (Program.Opts.NoActivate) ex |= Native.WS_EX_NOACTIVATE;
        Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex));

        // 透明表面防黑底必须 extend（macwidget 已踩）；圆角不再走 DWM（系统 8px 与卡 20pt 不符，CSS 接管）
        if (Program.Opts.Glass == "extend") Dwm.ExtendIntoClient(h);
        Dwm.SetDark(h, Program.Opts.Appearance == "light" ? false : Program.Opts.Dark);
        Dwm.SetRoundCorners(h);
        Native.ApplyRoundedInsetRegion(h, 8, 20);
        if (Program.Opts.Backdrop != "none") Dwm.SetBackdrop(h, Program.Opts.Backdrop);   // 实验对照保留

        if (Program.Opts.Pin == "bottom") BottomPin.Install(src);
        if (_startLifted) BottomPin.Lift(h);
        if (Program.Opts.NoActivate) src.AddHook(NoActivateHook);
        SizeChanged += (_, _) => Native.ApplyRoundedInsetRegion(h, 8, 20);
        if (_initialPosition is { } pos) MoveToPhysical(RectFor(pos));
    }

    static IntPtr NoActivateHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEACTIVATE = 0x0021;
        const int MA_NOACTIVATE = 3;
        if (msg == WM_MOUSEACTIVATE) { handled = true; return new IntPtr(MA_NOACTIVATE); }
        return IntPtr.Zero;
    }

    async void OnLoaded(object? s, RoutedEventArgs e)
    {
        try
        {
            if (Program.Opts.Control == "native")
            {
                Content = BuildNativeCard();
                return;
            }

            // same: 全部同一虚拟主机；multi: 每组件独立 site（--process-per-site 下 renderer 仍合并）
            var host = Program.Opts.Origin == "same" ? "widgets.test" : $"w{_i}.test";
            var url = new Uri($"https://{host}/{Kind}.html");

            if (Program.Opts.Control == "comp")
            {
                var wv = new WebView2CompositionControl { DefaultBackgroundColor = System.Drawing.Color.Transparent };
                Content = wv;
                await wv.EnsureCoreWebView2Async(Program.Env);
                await Setup(wv.CoreWebView2, host);
                wv.Source = url;
            }
            else
            {
                var wv = new Microsoft.Web.WebView2.Wpf.WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
                Content = wv;
                await wv.EnsureCoreWebView2Async(Program.Env);
                await Setup(wv.CoreWebView2, host);
                wv.Source = url;
            }
        }
        catch (Exception ex)
        {
            Program.Log($"widget {_i} ({Kind}) FAIL: {ex}");
        }
    }

    async Task Setup(CoreWebView2 core, string host)
    {
        _core = core;
        // photo 的源整个由宿主供流（Hook 里说明为什么不能用映射）；其余 kind 走文件夹映射
        if (Kind != "photo")
            core.SetVirtualHostNameToFolderMapping(host, Program.WebDir, CoreWebView2HostResourceAccessKind.Allow);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        // 订阅随文档走：新导航启动 = 旧文档作废，订阅清零（reload 换 topic 不留孤儿采样）。
        // ⚠️必须在 Starting 清而不是 Completed——新文档的 sub 消息先于 nav done 到达（真机踩过：
        // 放 Completed 会把刚订完的全灭，四路 sub 后紧跟四路 idle stopped）。
        core.NavigationStarting += (_, _) => DataHub.Drop(this);
        core.NavigationCompleted += (_, a) =>
        {
            Program.Log($"widget {_i} ({Kind}) nav done ok={a.IsSuccess}");
            PushState(forcePost: true);   // 注入快照与导航之间的状态变化在这兜住
            if (Kind == "photo") PhotoSupport.Apply(this, core, _i);
        };
        core.WebMessageReceived += OnWebMessage;
        if (Kind == "photo") PhotoSupport.Hook(this, core, host);
        // 宿主运行时注入（拖拽/状态类/徽章/右键），组件作者零感知（产品同款设计）
        _initScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(HostJs());
    }

    // ---- 状态推送（着色状态机 + 编辑模式 → 页面 CSS 类） ----

    string? _initScriptId;

    /// <summary>cfg 变更后重注册文档创建注入（快照是注册时冻结的，不重注册则 reload 读到旧 cfg）。</summary>
    async Task RefreshInitScript()
    {
        if (_core == null) return;
        try
        {
            var old = _initScriptId;
            _initScriptId = await _core.AddScriptToExecuteOnDocumentCreatedAsync(HostJs());
            if (old != null) _core.RemoveScriptToExecuteOnDocumentCreated(old);
        }
        catch (Exception ex) { Program.Log($"widget {_i} initscript refresh FAIL: {ex.Message}"); }
    }

    static string? _hostJs;
    string HostJs()
    {
        _hostJs ??= File.Exists(Path.Combine(Program.WebDir, "host.js"))
            ? File.ReadAllText(Path.Combine(Program.WebDir, "host.js"))
            : "";
        if (_hostJs.Length == 0) Program.Log("WARN host.js missing");
        var (dark, mono, effects) = StateNow();
        var cfgJson = Cfg is { } c ? c.GetRawText() : "null";
        return $"window.__mwInit={{dark:{(dark ? "true" : "false")},mono:{(mono ? "true" : "false")},effects:{(effects ? "true" : "false")},editing:{(EditMode.On ? "true" : "false")},lang:'{(ProductSettings.English ? "en" : "zh")}',cfg:{cfgJson}}};\n" + _hostJs;
    }

    (bool dark, bool mono, bool effects) StateNow()
    {
        var mon = PresentationSource.FromVisual(this) is HwndSource src
            ? Native.MonitorFromWindow(src.Handle, Native.MONITOR_DEFAULTTONEAREST)
            : IntPtr.Zero;
        return (ColorMode.Dark, ColorMode.IsMono(mon), ColorMode.TransparencyEnabled);
    }

    (bool dark, bool mono, bool effects, bool edit) _pushed;
    bool _pushedOnce;

    public void PushState(bool forcePost = false)
    {
        if (_core == null) return;
        var (dark, mono, effects) = StateNow();
        if (PresentationSource.FromVisual(this) is HwndSource src)
        {
            Dwm.SetDark(src.Handle, dark);
            // In mono the native Mica surface supplies the sampled material;
            // widget.css deliberately leaves the card translucent so it is
            // visible instead of being hidden behind an opaque color fill.
            Dwm.SetBackdrop(src.Handle, effects && mono ? "mica" : Program.Opts.Backdrop);
        }
        var s = (dark, mono, effects, EditMode.On);
        if (!forcePost && _pushedOnce && s == _pushed) return;
        _pushed = s; _pushedOnce = true;
        try
        {
            _core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(
                new { t = "state", dark, mono, effects, editing = EditMode.On }));
        }
        catch (Exception ex) { Program.Log($"widget {_i} poststate FAIL: {ex.Message}"); }
    }

    /// <summary>数据桥投递（DataHub 调）；core 未就绪静默丢——订阅回放兜住后续。</summary>
    public void PostJson(string json)
    {
        try { _core?.PostWebMessageAsJson(json); }
        catch (Exception ex) { Program.Log($"widget {_i} postjson FAIL: {ex.Message}"); }
    }

    /// <summary>
    /// ColorMode every 500ms reports complete occlusion.  Keep the WebView
    /// visible and only gate polling: suspending it required hiding the surface,
    /// which made a just-uncovered widget wake one frame late.
    /// </summary>
    public void SetOccluded(bool covered)
    {
        bool shouldGateData = covered && !EditMode.On && !_dragging && !_removing;
        if (_occluded == shouldGateData) return;
        _occluded = shouldGateData;
        _dataSuspended = shouldGateData;
        DataHub.SetSuspended(this, shouldGateData);
        Program.Log($"widget {_i} ({Kind}) {(shouldGateData ? "data gated (occluded)" : "data resumed (visible)")}");
    }

    internal bool IsDataSuspended => _dataSuspended;

    /// <summary>切尺寸档（菜单调）：帧改尺寸、左上角锚定，违规才纠正动画（与拖拽落位同引擎）。</summary>
    public void ApplySize(string size)
    {
        if (size == SizeClass || !WidgetRegistry.SizesOf(Kind).Contains(size)) return;
        SizeClass = size;
        (Width, Height) = WidgetRegistry.Size(Kind, size);
        Program.Log($"widget {_i} ({Kind}) size -> {size}");
        // WPF 会在本轮布局后才把 DIU 尺寸落实到当前屏的物理尺寸。
        _ = Dispatcher.BeginInvoke(() =>
        {
            var now = PhysicalBounds;
            var candidate = RectAt(now.Left, now.Top);
            var res = Resolve(candidate);
            if (res.Corrected) AnimateTo(res);
            else { MoveToPhysical(candidate); WidgetLink.Send(force: true); Layout.Save(); }
        });
    }

    /// <summary>移除：页面缩退动画（bye）→ 收窗 → 持久化/联动即时更新。</summary>
    public void ByeAndClose()
    {
        if (_removing) return;
        _removing = true;
        Program.Log($"widget {_i} ({Kind}) removing");
        try { _core?.PostWebMessageAsJson("""{"t":"bye"}"""); } catch { }
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(230) };
        t.Tick += (_, _) => { t.Stop(); Close(); };
        t.Start();
    }

    // ---- 拖拽摆位 + 并组吸附 + 落点虚影（引擎 v2，帧制参数） ----

    Rect _origBounds;
    double _dragScale;
    bool _dragging;
    System.Windows.Threading.DispatcherTimer? _anim;

    void OnWebMessage(object? s, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            switch (root.GetProperty("t").GetString())
            {
                case "hello":
                    Program.Log($"widget {_i} ({Kind}) host.js alive");
                    break;
                case "dbg":
                    // 组件页 mw.log(...)（开发排障设施：页面无 console 出口，宿主日志是唯一喉舌）
                    Program.Log($"widget {_i} ({Kind}) dbg: {root.GetProperty("m").GetString()}");
                    break;
                case "edit":
                    EditMode.Toggle();
                    break;
                case "menu":
                    // 右键出 macOS 式菜单（尺寸/编辑/移除）；坐标=屏幕 DIP，与 DIU 同标度
                    MenuWindow.Open(this, root.GetProperty("x").GetDouble(), root.GetProperty("y").GetDouble());
                    break;
                case "sub":
                    if (root.GetProperty("topic").GetString() is { Length: > 0 } topic)
                        DataHub.Subscribe(this, topic);
                    break;
                case "unsub":
                    if (root.GetProperty("topic").GetString() is { Length: > 0 } untopic)
                        DataHub.Unsubscribe(this, untopic);
                    break;
                case "cmd":
                    DataHub.Command(root.GetProperty("topic").GetString() ?? "",
                                    root.GetProperty("cmd").GetString() ?? "");
                    break;
                case "cfg":
                    // 页面 mw.saveCfg：存快照（Clone 脱离 JsonDocument 生命周期）→ 持久化 → kind 侧钩子
                    Cfg = root.GetProperty("cfg").Clone();
                    Layout.Save();
                    Program.Log($"widget {_i} ({Kind}) cfg saved");
                    if (Kind == "photo" && _core != null) PhotoSupport.Apply(this, _core, _i);
                    _ = RefreshInitScript();   // 注入快照跟上新 cfg（否则下次导航读到陈旧 cfg——真机踩过）
                    break;
                case "placeSearch":
                    if (Kind != "weather") break;
                    var query = root.TryGetProperty("q", out var q) ? q.GetString() ?? "" : "";
                    _ = SearchPlacesAsync(query);
                    break;
                case "pickfolder":
                {
                    var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择照片文件夹" };
                    if (Cfg is { ValueKind: System.Text.Json.JsonValueKind.Object } cc &&
                        cc.TryGetProperty("folder", out var fp) && fp.GetString() is { Length: > 0 } cur &&
                        System.IO.Directory.Exists(cur))
                        dlg.InitialDirectory = cur;
                    bool? ok = dlg.ShowDialog(this);
                    PostJson(System.Text.Json.JsonSerializer.Serialize(
                        new { t = "folder", path = ok == true ? dlg.FolderName : null }));
                    break;
                }
                case "remove":
                    ByeAndClose();
                    break;
                case "dragstart":
                    _anim?.Stop();
                    _dragging = true;
                    _origBounds = PhysicalBounds;
                    _dragScale = DpiScale();
                    BottomPin.Lift(Hwnd());          // macOS 同款：拖拽中的组件升层
                    Program.Log($"widget {_i} dragstart at ({_origBounds.Left:f0},{_origBounds.Top:f0})px");
                    break;
                case "drag":
                {
                    if (!_dragging) break;
                    // Chromium 的 screenX/Y 是该窗的 DIP；拖拽开始时换成物理 px，
                    // 之后可跨过不同 DPI 的显示器而不把虚拟桌面原点缩放错。
                    double nl = _origBounds.Left + root.GetProperty("dx").GetDouble() * _dragScale;
                    double nt = _origBounds.Top + root.GetProperty("dy").GetDouble() * _dragScale;
                    var candidate = RectAt(nl, nt);
                    MoveToPhysical(candidate);       // 自由跟手，不吸不拦
                    WidgetLink.Send();               // MacDesk 图标实时避让（~30Hz 节流）
                    var res = Resolve(candidate);
                    if (res.Corrected) GhostWindow.Instance.ShowAt(RectAt(res.L, res.T));
                    else GhostWindow.Instance.HideGhost();   // 合法位置 → 无虚影（自由摆放是常态）
                    break;
                }
                case "dragend":
                {
                    if (!_dragging) break;
                    _dragging = false;
                    GhostWindow.Instance.HideGhost();
                    BottomPin.Drop(Hwnd());
                    var current = PhysicalBounds;
                    var res = Resolve(current);
                    if (res.Corrected)
                    {
                        Program.Log($"widget {_i} dragend at ({current.Left:f0},{current.Top:f0})px -> corrected ({res.L:f0},{res.T:f0})px");
                        AnimateTo(res);              // 违规才纠正动画
                    }
                    else
                    {
                        Program.Log($"widget {_i} dragend free at ({current.Left:f0},{current.Top:f0})px");
                    }
                    WidgetLink.Send(force: true);
                    Layout.Save();
                    break;
                }
            }
        }
        catch (Exception ex) { Program.Log($"widget {_i} webmsg FAIL: {ex.Message}"); }
    }

    async Task SearchPlacesAsync(string query)
    {
        try
        {
            var results = await WeatherSearch.FindAsync(query);
            PostJson(System.Text.Json.JsonSerializer.Serialize(new { t = "placeResults", q = query, results }));
        }
        catch (Exception ex)
        {
            Program.Log($"weather place search FAIL: {ex.Message}");
            PostJson(System.Text.Json.JsonSerializer.Serialize(new { t = "placeResults", q = query, results = Array.Empty<object>(), error = true }));
        }
    }

    IntPtr Hwnd() => ((HwndSource)PresentationSource.FromVisual(this)!).Handle;

    /// <summary>当前帧的真实虚拟桌面物理矩形；这是避让、跨屏摆位与持久化的共同坐标系。</summary>
    public Rect PhysicalBounds => PresentationSource.FromVisual(this) is HwndSource src
        ? DisplayTopology.RectOf(src.Handle) : Rect.Empty;

    double DpiScale()
        => PresentationSource.FromVisual(this) is HwndSource src ? src.CompositionTarget.TransformToDevice.M11 : 1.0;

    Rect RectFor(DisplayTopology.Position position)
    {
        var display = DisplayTopology.ByKey(position.DisplayKey);
        return new Rect(display.Physical.Left + position.X, display.Physical.Top + position.Y,
            Width * display.Scale, Height * display.Scale);
    }

    /// <summary>给定物理左上，按落点所在显示器的 DPI 计算帧的物理尺寸。</summary>
    public Rect RectAt(double left, double top)
    {
        var current = PhysicalBounds;
        var probe = new Rect(left, top,
            current.IsEmpty ? Width * DpiScale() : current.Width,
            current.IsEmpty ? Height * DpiScale() : current.Height);
        var display = DisplayTopology.ForRect(probe);
        return new Rect(left, top, Width * display.Scale, Height * display.Scale);
    }

    public Placement.Result Resolve(Rect self)
    {
        var display = DisplayTopology.ForRect(self);
        self = new Rect(self.Left, self.Top, Width * display.Scale, Height * display.Scale);
        var others = new List<Rect>();
        foreach (Window w in Application.Current.Windows)
            if (w is WidgetWindow ww && !ReferenceEquals(ww, this) && ww.IsVisible)
            {
                var other = ww.PhysicalBounds;
                if (!other.IsEmpty && DisplayTopology.ForRect(other).Handle == display.Handle) others.Add(other);
            }
        return Placement.Resolve(self, others, display.Work,
            Placement.Unit * display.Scale, Placement.EdgeMargin * display.Scale);
    }

    public void MoveToPhysical(Rect rect)
    {
        var src = (HwndSource)PresentationSource.FromVisual(this)!;
        Native.MoveWindow(src.Handle, (int)Math.Round(rect.Left), (int)Math.Round(rect.Top),
            (int)Math.Round(rect.Width), (int)Math.Round(rect.Height), true);
    }

    /// <summary>面板拖出松手后的落位（PanelWindow 的光标循环调用）。</summary>
    public void SettleFromPickup(Placement.Result res)
    {
        BottomPin.Drop(Hwnd());
        if (res.Corrected) AnimateTo(res);
        else { WidgetLink.Send(force: true); Layout.Save(); }
    }

    void AnimateTo(Placement.Result res)
    {
        _anim?.Stop();
        var from = PhysicalBounds;
        var to = RectAt(res.L, res.T);
        int frame = 0; const int frames = 10;   // ~160ms cubic ease-out
        _anim = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _anim.Tick += (_, _) =>
        {
            frame++;
            double p = 1 - Math.Pow(1 - frame / (double)frames, 3);
            MoveToPhysical(new Rect(
                from.Left + (to.Left - from.Left) * p,
                from.Top + (to.Top - from.Top) * p,
                from.Width + (to.Width - from.Width) * p,
                from.Height + (to.Height - from.Height) * p));
            WidgetLink.Send(force: frame >= frames);   // 纠正动画期间也持续联动
            if (frame >= frames) { _anim!.Stop(); Layout.Save(); }
        };
        _anim.Start();
    }

    /// <summary>纯 WPF 卡片（对照组：排除 WebView2，单独验证透明表面配方）</summary>
    UIElement BuildNativeCard()
    {
        var time = new TextBlock
        {
            FontSize = 42, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var sub = new TextBlock
        {
            FontSize = 13, Text = "WPF NATIVE",
            Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(time);
        panel.Children.Add(sub);
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 46, 46, 46)),
            CornerRadius = new CornerRadius(20),
            Margin = new Thickness(8),
            Child = panel,
        };
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        t.Tick += (_, _) => time.Text = DateTime.Now.ToString("HH:mm:ss");
        t.Start();
        return card;
    }
}
