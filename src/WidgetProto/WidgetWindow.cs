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
    CoreWebView2? _core;
    bool _removing;

    public string Kind { get; }

    /// <param name="x">帧左上（DIU）；空 = 实验模式栅格位</param>
    /// <param name="lifted">出生即升层（面板拖出中的新组件，不先钉底）</param>
    public WidgetWindow(int i, string kind, double? x = null, double? y = null, bool lifted = false)
    {
        _i = i;
        Kind = kind;
        _startLifted = lifted;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;   // 铁律：layered 与 WPF D3D/DWM backdrop 互斥（macdesk-pitfalls）
        ShowInTaskbar = false;
        ShowActivated = !Program.Opts.NoActivate;
        Background = Brushes.Transparent;
        Title = $"WidgetProto {i} {kind}";

        // 窗口 = 帧（摆放/避让/组格距的原子单位）；可视卡由页面 CSS 画（内衬 8、圆角 20、阴影）
        (Width, Height) = WidgetRegistry.Size(kind);

        WindowStartupLocation = WindowStartupLocation.Manual;
        if (x is { } xl && y is { } yt) { Left = xl; Top = yt; }
        else
        {
            // 实验模式：刻意不规整的栅格铺开（自由摆放是一等公民，便于验证非网格落点）
            var wa = SystemParameters.WorkArea;
            int col = i % 2, row = i / 2;
            Left = wa.Right - 24 - (Width + 36) * (col + 1) + (col == 0 ? 0 : 13);
            Top = wa.Top + 20 + row * (Placement.Unit + 40) + i * 7;
        }

        SourceInitialized += OnSourceInit;
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            if (!_removing) return;
            WidgetLink.Send(force: true);   // 管道对面即时回位
            Layout.Save();
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
        Dwm.SetDark(h, Program.Opts.Dark);
        if (Program.Opts.Backdrop != "none") Dwm.SetBackdrop(h, Program.Opts.Backdrop);   // 实验对照保留

        if (Program.Opts.Pin == "bottom") BottomPin.Install(src);
        if (_startLifted) BottomPin.Lift(h);
        if (Program.Opts.NoActivate) src.AddHook(NoActivateHook);
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
        core.SetVirtualHostNameToFolderMapping(host, Program.WebDir, CoreWebView2HostResourceAccessKind.Allow);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.NavigationCompleted += (_, a) =>
        {
            Program.Log($"widget {_i} ({Kind}) nav done ok={a.IsSuccess}");
            PushState(forcePost: true);   // 注入快照与导航之间的状态变化在这兜住
        };
        core.WebMessageReceived += OnWebMessage;
        // 宿主运行时注入（拖拽/状态类/徽章/右键），组件作者零感知（产品同款设计）
        await core.AddScriptToExecuteOnDocumentCreatedAsync(HostJs());
    }

    // ---- 状态推送（着色状态机 + 编辑模式 → 页面 CSS 类） ----

    static string? _hostJs;
    string HostJs()
    {
        _hostJs ??= File.Exists(Path.Combine(Program.WebDir, "host.js"))
            ? File.ReadAllText(Path.Combine(Program.WebDir, "host.js"))
            : "";
        if (_hostJs.Length == 0) Program.Log("WARN host.js missing");
        var (dark, mono) = StateNow();
        return $"window.__mwInit={{dark:{(dark ? "true" : "false")},mono:{(mono ? "true" : "false")},editing:{(EditMode.On ? "true" : "false")}}};\n" + _hostJs;
    }

    (bool dark, bool mono) StateNow()
    {
        var mon = PresentationSource.FromVisual(this) is HwndSource src
            ? Native.MonitorFromWindow(src.Handle, Native.MONITOR_DEFAULTTONEAREST)
            : IntPtr.Zero;
        return (ColorMode.Dark, ColorMode.IsMono(mon));
    }

    (bool dark, bool mono, bool edit) _pushed;
    bool _pushedOnce;

    public void PushState(bool forcePost = false)
    {
        if (_core == null) return;
        var (dark, mono) = StateNow();
        var s = (dark, mono, EditMode.On);
        if (!forcePost && _pushedOnce && s == _pushed) return;
        _pushed = s; _pushedOnce = true;
        try
        {
            _core.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(
                new { t = "state", dark, mono, editing = EditMode.On }));
        }
        catch (Exception ex) { Program.Log($"widget {_i} poststate FAIL: {ex.Message}"); }
    }

    /// <summary>移除：页面缩退动画（bye）→ 收窗 → 持久化/联动即时更新。</summary>
    public void ByeAndClose()
    {
        if (_removing) return;
        _removing = true;
        try { _core?.PostWebMessageAsJson("""{"t":"bye"}"""); } catch { }
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(230) };
        t.Tick += (_, _) => { t.Stop(); Close(); };
        t.Start();
    }

    // ---- 拖拽摆位 + 并组吸附 + 落点虚影（引擎 v2，帧制参数） ----

    double _origL, _origT;
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
                case "edit":
                    EditMode.Toggle();
                    break;
                case "remove":
                    ByeAndClose();
                    break;
                case "dragstart":
                    _anim?.Stop();
                    _dragging = true;
                    _origL = Left; _origT = Top;
                    BottomPin.Lift(Hwnd());          // macOS 同款：拖拽中的组件升层
                    Program.Log($"widget {_i} dragstart at ({Left:f0},{Top:f0})");
                    break;
                case "drag":
                {
                    if (!_dragging) break;
                    // Chromium 的 screenX/Y 是 DIP，与 WPF DIU 同标度（同 DPI 下），直接相加
                    double nl = _origL + root.GetProperty("dx").GetDouble();
                    double nt = _origT + root.GetProperty("dy").GetDouble();
                    MoveTo(nl, nt);                  // 自由跟手，不吸不拦
                    WidgetLink.Send();               // MacDesk 图标实时避让（~30Hz 节流）
                    var res = Resolve(nl, nt);
                    if (res.Corrected) GhostWindow.Instance.ShowAt(res.L, res.T, Width, Height);
                    else GhostWindow.Instance.HideGhost();   // 合法位置 → 无虚影（自由摆放是常态）
                    break;
                }
                case "dragend":
                {
                    if (!_dragging) break;
                    _dragging = false;
                    GhostWindow.Instance.HideGhost();
                    BottomPin.Drop(Hwnd());
                    var res = Resolve(Left, Top);
                    if (res.Corrected)
                    {
                        Program.Log($"widget {_i} dragend at ({Left:f0},{Top:f0}) -> corrected ({res.L:f0},{res.T:f0})");
                        AnimateTo(res.L, res.T);     // 违规才纠正动画
                    }
                    else
                    {
                        Program.Log($"widget {_i} dragend free at ({Left:f0},{Top:f0})");
                    }
                    WidgetLink.Send(force: true);
                    Layout.Save();
                    break;
                }
            }
        }
        catch (Exception ex) { Program.Log($"widget {_i} webmsg FAIL: {ex.Message}"); }
    }

    IntPtr Hwnd() => ((HwndSource)PresentationSource.FromVisual(this)!).Handle;

    public Placement.Result Resolve(double l, double t)
    {
        var others = new List<Rect>();
        foreach (Window w in Application.Current.Windows)
            if (w is WidgetWindow ww && !ReferenceEquals(ww, this) && ww.IsVisible)
                others.Add(new Rect(ww.Left, ww.Top, ww.Width, ww.Height));
        return Placement.Resolve(new Rect(l, t, Width, Height), others, SystemParameters.WorkArea);
    }

    public void MoveTo(double l, double t)
    {
        var src = (HwndSource)PresentationSource.FromVisual(this)!;
        double k = src.CompositionTarget.TransformToDevice.M11;
        Native.MoveWindow(src.Handle, (int)Math.Round(l * k), (int)Math.Round(t * k),
            (int)Math.Round(Width * k), (int)Math.Round(Height * k), true);
    }

    /// <summary>面板拖出松手后的落位（PanelWindow 的光标循环调用）。</summary>
    public void SettleFromPickup(Placement.Result res)
    {
        BottomPin.Drop(Hwnd());
        if (res.Corrected) AnimateTo(res.L, res.T);
        else { WidgetLink.Send(force: true); Layout.Save(); }
    }

    void AnimateTo(double l, double t)
    {
        _anim?.Stop();
        double fl = Left, ft = Top;
        int frame = 0; const int frames = 10;   // ~160ms cubic ease-out
        _anim = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _anim.Tick += (_, _) =>
        {
            frame++;
            double p = 1 - Math.Pow(1 - frame / (double)frames, 3);
            MoveTo(fl + (l - fl) * p, ft + (t - ft) * p);
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
