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
    readonly string _kind;

    public WidgetWindow(int i, string kind)
    {
        _i = i;
        _kind = kind;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;   // 铁律：layered 与 WPF D3D/DWM backdrop 互斥（macdesk-pitfalls）
        ShowInTaskbar = false;
        ShowActivated = !Program.Opts.NoActivate;   // --activate 时真激活（验证 DWM 材质的非激活回退）
        Background = Brushes.Transparent;
        Title = $"WidgetProto {i} {kind}";
        Width = 340;
        Height = kind == "photo" ? 340 : 170;

        // 从工作区右上角起排布，3 列网格（macOS 摆位习惯）
        WindowStartupLocation = WindowStartupLocation.Manual;
        var wa = SystemParameters.WorkArea;
        int col = i % 3, row = i / 3;
        Left = wa.Right - 16 - 356 * (col + 1) + (356 - Width);
        Top = wa.Top + 16 + row * 356;

        SourceInitialized += OnSourceInit;
        Loaded += OnLoaded;
    }

    void OnSourceInit(object? s, EventArgs e)
    {
        var src = (HwndSource)PresentationSource.FromVisual(this)!;
        // MacDesk 透明直通同款：WPF 表面清除色透明，DWM 把 backdrop 材质透上来
        src.CompositionTarget.BackgroundColor = Colors.Transparent;

        var h = src.Handle;
        var ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
        ex |= Native.WS_EX_TOOLWINDOW;
        if (Program.Opts.NoActivate) ex |= Native.WS_EX_NOACTIVATE;
        Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex));

        if (Program.Opts.Glass == "extend") Dwm.ExtendIntoClient(h);
        Dwm.SetRoundCorners(h);
        Dwm.SetDark(h, Program.Opts.Dark);
        Dwm.SetBackdrop(h, Program.Opts.Backdrop);

        if (Program.Opts.Pin == "bottom") BottomPin.Install(src);
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

            // same: 全部同一虚拟主机(同 site，renderer 可合并)；multi: 每组件独立 site，强制拆 renderer
            var host = Program.Opts.Origin == "same" ? "widgets.test" : $"w{_i}.test";
            var url = new Uri($"https://{host}/{_kind}.html");

            if (Program.Opts.Control == "comp")
            {
                var wv = new WebView2CompositionControl { DefaultBackgroundColor = System.Drawing.Color.Transparent };
                Content = wv;
                await wv.EnsureCoreWebView2Async(Program.Env);
                Setup(wv.CoreWebView2, host);
                wv.Source = url;
            }
            else
            {
                var wv = new Microsoft.Web.WebView2.Wpf.WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
                Content = wv;
                await wv.EnsureCoreWebView2Async(Program.Env);
                Setup(wv.CoreWebView2, host);
                wv.Source = url;
            }
        }
        catch (Exception ex)
        {
            Program.Log($"widget {_i} ({_kind}) FAIL: {ex}");
        }
    }

    async void Setup(CoreWebView2 core, string host)
    {
        core.SetVirtualHostNameToFolderMapping(host, Program.WebDir, CoreWebView2HostResourceAccessKind.Allow);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.NavigationCompleted += (_, a) => Program.Log($"widget {_i} ({_kind}) nav done ok={a.IsSuccess}");
        core.WebMessageReceived += OnWebMessage;
        // 拖拽由宿主注入，组件作者零感知（产品同款设计）
        await core.AddScriptToExecuteOnDocumentCreatedAsync(DragJs);
    }

    // ---- 拖拽摆位 + 网格吸附 + 落点虚影 ----

    const string DragJs = """
        (function(){
          if (window.__mwDrag) return; window.__mwDrag = true;
          let sx=0, sy=0, armed=false, dragging=false;
          addEventListener('pointerdown', e => {
            if (e.button !== 0) return;
            armed = true; dragging = false; sx = e.screenX; sy = e.screenY;
          }, true);
          addEventListener('pointermove', e => {
            if (!armed) return;
            const dx = e.screenX - sx, dy = e.screenY - sy;
            if (!dragging && Math.hypot(dx, dy) > 4) {
              dragging = true;
              window.chrome.webview.postMessage({ t: 'dragstart' });
            }
            if (dragging) window.chrome.webview.postMessage({ t: 'drag', dx: dx, dy: dy });
          }, true);
          const end = () => {
            if (dragging) window.chrome.webview.postMessage({ t: 'dragend' });
            armed = false; dragging = false;
          };
          addEventListener('pointerup', end, true);
          addEventListener('pointercancel', end, true);
        })();
        """;

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
                case "dragstart":
                    _anim?.Stop();
                    _dragging = true;
                    _origL = Left; _origT = Top;
                    var (gl, gt) = LayoutGrid.Snap(Left, Top, Width, Height);
                    GhostWindow.Instance.ShowAt(gl, gt, Width, Height);
                    Program.Log($"widget {_i} dragstart at ({Left:f0},{Top:f0})");
                    break;
                case "drag":
                    if (!_dragging) break;
                    // Chromium 的 screenX/Y 是 DIP，与 WPF DIU 同标度（同 DPI 下），直接相加
                    double nl = _origL + root.GetProperty("dx").GetDouble();
                    double nt = _origT + root.GetProperty("dy").GetDouble();
                    MoveTo(nl, nt);
                    var (sl, st) = LayoutGrid.Snap(nl, nt, Width, Height);
                    GhostWindow.Instance.MoveTo(sl, st);
                    break;
                case "dragend":
                    if (!_dragging) break;
                    _dragging = false;
                    GhostWindow.Instance.HideGhost();
                    var (tl, tt) = LayoutGrid.Snap(Left, Top, Width, Height);
                    Program.Log($"widget {_i} dragend at ({Left:f0},{Top:f0}) -> snap ({tl:f0},{tt:f0})");
                    AnimateTo(tl, tt);
                    break;
            }
        }
        catch (Exception ex) { Program.Log($"widget {_i} webmsg FAIL: {ex.Message}"); }
    }

    void MoveTo(double l, double t)
    {
        var src = (HwndSource)PresentationSource.FromVisual(this)!;
        double k = src.CompositionTarget.TransformToDevice.M11;
        Native.MoveWindow(src.Handle, (int)Math.Round(l * k), (int)Math.Round(t * k),
            (int)Math.Round(Width * k), (int)Math.Round(Height * k), true);
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
            if (frame >= frames) _anim!.Stop();
        };
        _anim.Start();
    }

    /// <summary>纯 WPF 卡片（对照组：排除 WebView2，单独验证 backdrop 配方是否成立）</summary>
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
            Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            CornerRadius = new CornerRadius(14),
            Child = panel,
        };
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        t.Tick += (_, _) => time.Text = DateTime.Now.ToString("HH:mm:ss");
        t.Start();
        return card;
    }
}
