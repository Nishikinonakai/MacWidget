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
    System.Windows.Threading.DispatcherTimer? _deactivateCheck;
    System.Windows.Threading.DispatcherTimer? _windowAnimation;
    Rect _shownRect;

    PanelWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        // The HWND itself moves with the panel animation. This keeps the native
        // acrylic surface and the visible rounded panel in the same rectangle.
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
            Dwm.ExtendIntoClient(h);
            Dwm.SetDark(h, ColorMode.Dark);
            Dwm.SetBackdrop(h, ColorMode.TransparencyEnabled ? PanelMaterial() : "none");
            Dwm.SetSquareCorners(h);
            Native.ApplyRoundedRegion(h, 16);
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
            if (pid != (uint)Environment.ProcessId) { EditMode.Exit(); return; }
            // Explorer occasionally becomes foreground one dispatcher turn after
            // WPF reports deactivation. Recheck once to make blank-desktop
            // clicks deterministic without closing for our own widget windows.
            _deactivateCheck?.Stop();
            _deactivateCheck = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(55) };
            _deactivateCheck.Tick += (_, _) =>
            {
                _deactivateCheck!.Stop();
                if (!EditMode.On || _pick != null || !IsVisible) return;
                Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint latePid);
                if (latePid != (uint)Environment.ProcessId) EditMode.Exit();
            };
            _deactivateCheck.Start();
        };
    }

    async void OnLoaded(object? s, RoutedEventArgs e)
    {
        try
        {
            var wv = new Microsoft.Web.WebView2.Wpf.WebView2CompositionControl { DefaultBackgroundColor = System.Drawing.Color.Transparent };
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
        _windowAnimation?.Stop();
        // 编辑是从当前鼠标所在桌面进入的；面板应留在那块屏幕，而不是总回主屏。
        Native.GetCursorPos(out var cursor);
        var display = DisplayTopology.ForPoint(new Point(cursor.X, cursor.Y));
        double workW = display.Work.Width / display.Scale, workH = display.Work.Height / display.Scale;
        Width = Math.Max(560, Math.Min(1020, workW - 64));
        Height = Math.Max(340, Math.Round(workH * 0.472));
        double pw = Width * display.Scale, ph = Height * display.Scale;
        double px = display.Work.Left + Math.Round((display.Work.Width - pw) / 2);
        double py = display.Work.Bottom - ph;
        _shownRect = new Rect(px, py, pw, ph);
        bool wasVisible = IsVisible;
        Left = 0; Top = 0;
        Show();
        if (PresentationSource.FromVisual(this) is HwndSource src)
        {
            var from = wasVisible ? DisplayTopology.RectOf(src.Handle)
                : new Rect(px, display.Work.Bottom, pw, ph);
            MovePanel(src.Handle, from);
            AnimatePanel(src.Handle, from, _shownRect, hideAtEnd: false);
        }
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
        if (!IsVisible || PresentationSource.FromVisual(this) is not HwndSource src) { Hide(); return; }
        var from = DisplayTopology.RectOf(src.Handle);
        var to = new Rect(_shownRect.Left, _shownRect.Bottom, _shownRect.Width, _shownRect.Height);
        AnimatePanel(src.Handle, from, to, hideAtEnd: true);
    }

    public void PushState()
    {
        if (PresentationSource.FromVisual(this) is HwndSource src)
        {
            Dwm.SetDark(src.Handle, ColorMode.Dark);
            Dwm.SetBackdrop(src.Handle, ColorMode.TransparencyEnabled ? PanelMaterial() : "none");
            Native.ApplyRoundedRegion(src.Handle, 16);
        }
        var installed = Application.Current.Windows.OfType<WidgetWindow>()
            .Where(w => w.IsVisible).Select(w => w.Kind).Distinct().ToArray();
        Post(System.Text.Json.JsonSerializer.Serialize(new
        {
            t = "state",
            dark = ColorMode.Dark,
            effects = ColorMode.TransparencyEnabled,
            accent = MacDeskAppearance.PanelAccentCss(),
            lang = ProductSettings.English ? "en" : "zh",
            installed,
        }));
    }

    void Post(string json) { try { _core?.PostWebMessageAsJson(json); } catch { } }

    static string PanelMaterial() => ColorMode.Dark ? "wca" : "wcalight";

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
        Native.GetCursorPos(out var pt);
        var display = DisplayTopology.ForPoint(new Point(pt.X, pt.Y));
        var pos = new DisplayTopology.Position(display.Key,
            pt.X - display.Physical.Left - w * display.Scale / 2,
            pt.Y - display.Physical.Top - h * display.Scale / 2);
        var ww = new WidgetWindow(Program.NextId(), kind, size, pos, lifted: true);
        ww.Show();
        _pick = ww;
        Post("""{"t":"pickup"}""");
        AnimateForPickup(hide: true);
        Program.Log($"panel pickup {kind}");
        _pickTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _pickTimer.Tick += (_, _) => PickTick();
        _pickTimer.Start();
    }

    void PickTick()
    {
        if (_pick == null) { _pickTimer?.Stop(); return; }
        Native.GetCursorPos(out var pt);
        var current = _pick.PhysicalBounds;
        var candidate = _pick.RectAt(pt.X - current.Width / 2, pt.Y - current.Height / 2);
        bool down = (Native.GetAsyncKeyState(0x01 /*VK_LBUTTON*/) & 0x8000) != 0;
        _pick.MoveToPhysical(candidate);
        WidgetLink.Send();
        var res = _pick.Resolve(candidate);
        if (down)
        {
            if (res.Corrected) GhostWindow.Instance.ShowAt(_pick.RectAt(res.L, res.T));
            else GhostWindow.Instance.HideGhost();
            return;
        }
        // 松手：面板范围内 = 取消（拖回收回），其余按引擎落位
        _pickTimer!.Stop(); _pickTimer = null;
        GhostWindow.Instance.HideGhost();
        var panel = _shownRect;
        bool overPanel = IsVisible && panel.Contains(new Point(pt.X, pt.Y));
        if (overPanel)
        {
            Program.Log("panel pickup canceled (dropped back on panel)");
            _pick.ByeAndClose();
        }
        else
        {
            Program.Log($"panel pickup drop at ({candidate.Left:f0},{candidate.Top:f0})px corrected={res.Corrected}");
            _pick.SettleFromPickup(res);
            Layout.Save();
        }
        _pick = null;
        Post("""{"t":"drop"}""");
        AnimateForPickup(hide: false);
        PushState(); // 放置或取消后刷新 Suggestions，优先推荐尚未摆到桌面的组件。
    }

    void AnimateForPickup(bool hide)
    {
        if (!IsVisible || PresentationSource.FromVisual(this) is not HwndSource src || _shownRect.IsEmpty) return;
        var current = DisplayTopology.RectOf(src.Handle);
        var hidden = new Rect(_shownRect.Left, _shownRect.Bottom, _shownRect.Width, _shownRect.Height);
        AnimatePanel(src.Handle, current, hide ? hidden : _shownRect, hideAtEnd: false);
    }

    void AnimatePanel(IntPtr hwnd, Rect from, Rect to, bool hideAtEnd)
    {
        _windowAnimation?.Stop();
        var started = DateTime.UtcNow;
        _windowAnimation = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _windowAnimation.Tick += (_, _) =>
        {
            double t = Math.Clamp((DateTime.UtcNow - started).TotalMilliseconds / 280.0, 0, 1);
            double eased = 1 - Math.Pow(1 - t, 3);
            var frame = new Rect(
                from.Left + (to.Left - from.Left) * eased,
                from.Top + (to.Top - from.Top) * eased,
                to.Width, to.Height);
            MovePanel(hwnd, frame);
            if (t < 1) return;
            _windowAnimation!.Stop();
            if (hideAtEnd) Hide();
        };
        _windowAnimation.Start();
    }

    static void MovePanel(IntPtr hwnd, Rect frame)
    {
        Native.MoveWindow(hwnd, (int)Math.Round(frame.Left), (int)Math.Round(frame.Top),
            (int)Math.Round(frame.Width), (int)Math.Round(frame.Height), true);
        Native.ApplyRoundedRegion(hwnd, 16);
    }

}
