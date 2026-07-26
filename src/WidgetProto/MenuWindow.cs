using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WidgetProto;

/// <summary>
/// 组件右键菜单（macOS refs 02：尺寸档打勾 + 编辑小组件… + 移除小组件）。
/// 纯 WPF——菜单小且要快，不为它起 WebView2；透明表面窗 + 圆角卡 + 表面内阴影
/// （AllowsTransparency 铁律不破，阴影呼吸空间烙在窗口边距里，与卡面同做法）。
/// 深浅跟随 ColorMode；任何点击离开（Deactivated）即收。菜单是同进程窗口，
/// 打开它不会触发编辑模式面板的"点了桌面"误判（面板已按前台进程豁免同进程）。
/// </summary>
public sealed class MenuWindow : Window
{
    static MenuWindow? _open;

    public static void Open(WidgetWindow target, double x, double y)
    {
        _open?.Close();
        _open = new MenuWindow(target, x, y);
        _open.Show();
        _open.Activate();
    }

    const double Pad = 0;

    readonly double _cx, _cy;   // 光标落点（DIU）＝可视菜单期望左上
    bool _closing;              // Close 进行中窗口失活会再触发 Deactivated→Close（真机踩过：双重 Close 抛 InvalidOperation）

    MenuWindow(WidgetWindow target, double x, double y)
    {
        _cx = x; _cy = y;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = x - Pad; Top = y - Pad;
        Title = "MacWidget Menu";

        bool dark = ColorMode.Dark;
        var fg = new SolidColorBrush(dark ? Color.FromRgb(0xF2, 0xF2, 0xF7) : Color.FromRgb(0x1D, 0x1D, 0x1F));
        var body = new StackPanel { Margin = new Thickness(5) };

        // macOS 菜单序：编辑本组件（配置脸）→ 尺寸档 → 编辑小组件…（全局编辑模式）→ 移除
        if (WidgetRegistry.Configurable(target.Kind))
        {
            body.Children.Add(Row(Ui.T("编辑「", "Edit “") + KindLabel(target.Kind) + Ui.T("」", "”"), fg, check: false,
                () => target.PostJson("""{"t":"editcfg"}""")));
            body.Children.Add(Hairline(dark));
        }
        var sizes = WidgetRegistry.SizesOf(target.Kind);
        if (sizes.Length > 1)
        {
            foreach (var s in sizes)
            {
                var sz = s;   // foreach 变量捕获
                body.Children.Add(Row(SizeLabel(sz), fg, check: sz == target.SizeClass, () => target.ApplySize(sz)));
            }
            body.Children.Add(Hairline(dark));
        }
        body.Children.Add(Row(Ui.T("编辑小组件…", "Edit Widgets…"), fg, check: false, EditMode.Enter));
        body.Children.Add(Hairline(dark));
        body.Children.Add(Row(Ui.T("移除小组件", "Remove Widget"), new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)), check: false,
            target.ByeAndClose));

        bool transparency = ColorMode.TransparencyEnabled;
        Content = new Border
        {
            Background = new SolidColorBrush(transparency
                ? (dark ? Color.FromArgb(224, 45, 45, 48) : Color.FromArgb(226, 242, 242, 247))
                : (dark ? Color.FromRgb(45, 45, 48) : Color.FromRgb(242, 242, 247))),
            BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(30, 255, 255, 255) : Color.FromArgb(26, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(Pad),
            MinWidth = 178,
            Child = body,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 3, Direction = 270, Opacity = .38, Color = Colors.Black },
        };

        SourceInitialized += (_, _) =>
        {
            var src = (HwndSource)PresentationSource.FromVisual(this)!;
            src.CompositionTarget.BackgroundColor = Colors.Transparent;
            var h = src.Handle;
            var ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW));
            Dwm.ExtendIntoClient(h);   // 透明表面防黑底
            Dwm.SetDark(h, ColorMode.Dark);
            Dwm.SetBackdrop(h, ColorMode.TransparencyEnabled ? "acrylic" : "none"); // 短暂菜单用原生亚克力
            Dwm.SetRoundCorners(h);
            Native.ApplyRoundedRegion(h, 12);
        };
        SizeChanged += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource src)
                Native.ApplyRoundedRegion(src.Handle, 12);
        };
        Loaded += (_, _) =>
        {
            // 展开方向：默认右下；越界翻到光标另一侧，再兜底钳进工作区
            var wa = SystemParameters.WorkArea;
            double vw = ActualWidth - Pad * 2, vh = ActualHeight - Pad * 2;
            double lx = _cx, ty = _cy;
            if (lx + vw > wa.Right - 4) lx = Math.Max(wa.Left + 4, _cx - vw);
            if (ty + vh > wa.Bottom - 4) ty = Math.Max(wa.Top + 4, _cy - vh);
            Left = lx - Pad; Top = ty - Pad;
        };
        Closing += (_, _) => _closing = true;
        Deactivated += (_, _) => SafeClose();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) SafeClose(); };
        Closed += (_, _) => { if (_open == this) _open = null; };
    }

    void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    static string SizeLabel(string s) => ProductSettings.English ? s switch { "m" => "Medium", "l" => "Large", _ => "Small" } : s switch { "m" => "中", "l" => "大", _ => "小" };

    static string KindLabel(string kind) => kind switch
    {
        "photo" => Ui.T("照片", "Photos"), "clock" => Ui.T("时钟", "Clock"), "calendar" => Ui.T("日历", "Calendar"),
        "monitor" => Ui.T("系统监视", "System Monitor"), "weather" => Ui.T("天气", "Weather"), "music" => Ui.T("正在播放", "Now Playing"), "battery" => Ui.T("电池", "Battery"),
        _ => kind,
    };

    Border Row(string text, Brush normalFg, bool check, Action act)
    {
        var chk = new TextBlock
        {
            Text = check ? "✓" : "", Width = 17, FontSize = 12.5,
            Foreground = normalFg, VerticalAlignment = VerticalAlignment.Center,
        };
        var tb = new TextBlock
        {
            Text = text, FontSize = 13.5,
            Foreground = normalFg, VerticalAlignment = VerticalAlignment.Center,
        };
        var dock = new DockPanel();
        dock.Children.Add(chk);
        dock.Children.Add(tb);
        var row = new Border
        {
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(8, 5.5, 16, 5.5),
            Background = Brushes.Transparent,
            Child = dock,
        };
        row.MouseEnter += (_, _) =>
        {
            row.Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
            tb.Foreground = Brushes.White; chk.Foreground = Brushes.White;
        };
        row.MouseLeave += (_, _) =>
        {
            row.Background = Brushes.Transparent;
            tb.Foreground = normalFg; chk.Foreground = normalFg;
        };
        row.MouseLeftButtonUp += (_, _) =>
        {
            SafeClose();
            Dispatcher.BeginInvoke(act);   // 先收菜单再执行（编辑模式面板会抢激活）
        };
        return row;
    }

    static Border Hairline(bool dark) => new()
    {
        Height = 1,
        Margin = new Thickness(10, 4, 10, 4),
        Background = new SolidColorBrush(dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0)),
    };
}
