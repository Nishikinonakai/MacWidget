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
        ShowActivated = false;
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

    void Setup(CoreWebView2 core, string host)
    {
        core.SetVirtualHostNameToFolderMapping(host, Program.WebDir, CoreWebView2HostResourceAccessKind.Allow);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.NavigationCompleted += (_, a) => Program.Log($"widget {_i} ({_kind}) nav done ok={a.IsSuccess}");
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
