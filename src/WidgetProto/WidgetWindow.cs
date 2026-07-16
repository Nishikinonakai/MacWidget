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

        // 初始摆位刻意“不规整”（自由摆放是 v2 的一等公民，便于验证非网格落点）
        WindowStartupLocation = WindowStartupLocation.Manual;
        var wa = SystemParameters.WorkArea;
        int col = i % 2, row = i / 2;
        Left = wa.Right - 24 - (Width + 36) * (col + 1) + (col == 0 ? 0 : 13);
        Top = wa.Top + 20 + row * 220 + i * 7;

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
                    break;
                }
            }
        }
        catch (Exception ex) { Program.Log($"widget {_i} webmsg FAIL: {ex.Message}"); }
    }

    IntPtr Hwnd() => ((HwndSource)PresentationSource.FromVisual(this)!).Handle;

    Placement.Result Resolve(double l, double t)
    {
        var others = new List<Rect>();
        foreach (Window w in Application.Current.Windows)
            if (w is WidgetWindow ww && !ReferenceEquals(ww, this) && ww.IsVisible)
                others.Add(new Rect(ww.Left, ww.Top, ww.Width, ww.Height));
        return Placement.Resolve(new Rect(l, t, Width, Height), others, SystemParameters.WorkArea);
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
