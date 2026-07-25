using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace WidgetProto;

/// <summary>
/// 组件库面板（refs 03）：底部滑出、水平居中、高 ≈ 47% 工作区、宽 ≈ 1020（小屏自适应收窄）。
/// 窗口即最终矩形（透明表面），滑入滑出动画在页面内做（translateY，GPU 合成）。
/// 拖出放置 = 页面只报一次 pickup，宿主用真实光标循环（GetCursorPos + GetAsyncKeyState）接管——
/// 不依赖网页指针捕获跨窗口的行为细节，松手即按摆位引擎落位。
/// </summary>
public sealed class PanelWindow : Window
{
    public static PanelWindow? Existing { get; private set; }
    public static PanelWindow Get() => Existing ??= new PanelWindow();

    CoreWebView2? _core;
    bool _pendingShow;
    System.Windows.Threading.DispatcherTimer? _hideTimer;

    PanelWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        ShowInTaskbar = false;
        ShowActivated = true;      // 搜索框要键盘
        Topmost = true;
        Background = Brushes.Transparent;
        Title = "MacWidget Panel";
        WindowStartupLocation = WindowStartupLocation.Manual;

        SourceInitialized += (_, _) =>
        {
            var src = (HwndSource)PresentationSource.FromVisual(this)!;
            src.CompositionTarget.BackgroundColor = Colors.Transparent;
            var h = src.Handle;
            var ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
            ex |= Native.WS_EX_TOOLWINDOW;
            Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex));
            Dwm.ExtendIntoClient(h);   // 透明表面防黑底
        };
        Loaded += OnLoaded;
        Closed += (_, _) => Existing = null;
        // macOS 语义：点 Done 或点桌面退出编辑模式（点桌面/切走 = 面板失活；拖出进行中除外——
        // 拖出全程鼠标不点别处，不会触发失活）。
        // ⚠️点组件（徽章/拖拽）时 WebView2 子窗会抢激活——同进程内的焦点腾挪不算"离开"，
        // 否则徽章在 pointerup 前就被 editing=false 藏掉，click 永远不成立（真机踩过）。
        Deactivated += (_, _) =>
        {
            if (!EditMode.On || _pick != null || !IsVisible) return;
            var fg = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(fg, out uint pid);
            if (pid == (uint)Environment.ProcessId) return;
            EditMode.Exit();
        };
    }

    async void OnLoaded(object? s, RoutedEventArgs e)
    {
        try
        {
            var wv = new Microsoft.Web.WebView2.Wpf.WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
            Content = wv;
            await wv.EnsureCoreWebView2Async(Program.Env);
            _core = wv.CoreWebView2;
            _core.SetVirtualHostNameToFolderMapping("panel.test", Program.WebDir, CoreWebView2HostResourceAccessKind.Allow);
            _core.Settings.AreDefaultContextMenusEnabled = false;
            _core.Settings.IsStatusBarEnabled = false;
            _core.Settings.IsZoomControlEnabled = false;
            _core.NavigationCompleted += (_, a) =>
            {
                Program.Log($"panel nav done ok={a.IsSuccess}");
                if (_pendingShow) { _pendingShow = false; PostShow(); }
            };
            _core.WebMessageReceived += OnWebMessage;
            wv.Source = new Uri("https://panel.test/panel.html");
        }
        catch (Exception ex) { Program.Log("panel FAIL: " + ex); }
    }

    public void ShowPanel()
    {
        _hideTimer?.Stop();
        var wa = SystemParameters.WorkArea;
        Width = Math.Max(560, Math.Min(1020, wa.Width - 64));
        Height = Math.Max(340, Math.Round(wa.Height * 0.472));
        Left = wa.Left + Math.Round((wa.Width - Width) / 2);
        Top = wa.Bottom - Height;
        Show();
        Activate();
        if (_core == null) { _pendingShow = true; return; }
        PostShow();
    }

    void PostShow()
    {
        PushState();
        Post("""{"t":"show"}""");
    }

    public void HidePanel()
    {
        Post("""{"t":"hide"}""");
        _hideTimer?.Stop();
        _hideTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _hideTimer.Tick += (_, _) => { _hideTimer!.Stop(); Hide(); };
        _hideTimer.Start();
    }

    public void PushState()
        => Post($"{{\"t\":\"state\",\"dark\":{(ColorMode.Dark ? "true" : "false")}}}");

    void Post(string json) { try { _core?.PostWebMessageAsJson(json); } catch { } }

    void OnWebMessage(object? s, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            switch (root.GetProperty("t").GetString())
            {
                case "done": EditMode.Exit(); break;
                case "pickup": StartPickup(root.GetProperty("kind").GetString() ?? ""); break;
            }
        }
        catch (Exception ex) { Program.Log("panel webmsg FAIL: " + ex.Message); }
    }

    // ---- 拖出放置：宿主光标循环 ----

    WidgetWindow? _pick;
    System.Windows.Threading.DispatcherTimer? _pickTimer;

    void StartPickup(string kind)
    {
        if (_pick != null || !WidgetRegistry.Kinds.Contains(kind)) return;
        var size = WidgetRegistry.DefaultSize(kind);
        var (w, h) = WidgetRegistry.Size(kind, size);
        double k = Dpi();
        Native.GetCursorPos(out var pt);
        var ww = new WidgetWindow(Program.NextId(), kind, size, pt.X / k - w / 2, pt.Y / k - h / 2, lifted: true);
        ww.Show();
        _pick = ww;
        Program.Log($"panel pickup {kind}");
        _pickTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _pickTimer.Tick += (_, _) => PickTick(w, h);
        _pickTimer.Start();
    }

    void PickTick(double w, double h)
    {
        if (_pick == null) { _pickTimer?.Stop(); return; }
        double k = Dpi();
        Native.GetCursorPos(out var pt);
        double l = pt.X / k - w / 2, t = pt.Y / k - h / 2;
        bool down = (Native.GetAsyncKeyState(0x01 /*VK_LBUTTON*/) & 0x8000) != 0;
        _pick.MoveTo(l, t);
        WidgetLink.Send();
        var res = _pick.Resolve(l, t);
        if (down)
        {
            if (res.Corrected) GhostWindow.Instance.ShowAt(res.L, res.T, w, h);
            else GhostWindow.Instance.HideGhost();
            return;
        }
        // 松手：面板范围内 = 取消（拖回收回），其余按引擎落位
        _pickTimer!.Stop(); _pickTimer = null;
        GhostWindow.Instance.HideGhost();
        double cx = pt.X / k, cy = pt.Y / k;
        bool overPanel = IsVisible && cx >= Left && cx <= Left + Width && cy >= Top && cy <= Top + Height;
        if (overPanel)
        {
            Program.Log("panel pickup canceled (dropped back on panel)");
            _pick.ByeAndClose();
        }
        else
        {
            Program.Log($"panel pickup drop at ({l:f0},{t:f0}) corrected={res.Corrected}");
            _pick.SettleFromPickup(res);
            Layout.Save();
        }
        _pick = null;
    }

    double Dpi()
        => PresentationSource.FromVisual(this) is HwndSource src
            ? src.CompositionTarget.TransformToDevice.M11 : 1.0;
}
