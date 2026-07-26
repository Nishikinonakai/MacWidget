using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WidgetProto;

/// <summary>
/// 通知区入口的原生 WPF 浮层。NotifyIcon 只保留用来挂到 Windows 通知区，
/// 实际交互不再退回 WinForms ContextMenuStrip；位置按鼠标所在显示器的工作区计算。
/// </summary>
public sealed class TrayPopupWindow : Window
{
    static TrayPopupWindow? _open;
    bool _closing;

    const double WidthDiu = 278;
    const double HeightDiu = 371;
    const double Pad = 12;

    public static void Toggle()
    {
        if (_open is { IsVisible: true })
        {
            _open.SafeClose();
            return;
        }

        _open = new TrayPopupWindow();
        _open.ShowAtCursor();
    }

    public static void CloseIfOpen() => _open?.SafeClose();

    TrayPopupWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;
        ShowInTaskbar = false;
        Topmost = true;
        Width = WidthDiu;
        Height = HeightDiu;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "MacWidget";

        bool dark = ColorMode.Dark;
        var fg = new SolidColorBrush(dark ? Color.FromRgb(0xF2, 0xF2, 0xF7) : Color.FromRgb(0x1D, 0x1D, 0x1F));
        var muted = new SolidColorBrush(dark ? Color.FromRgb(0xA0, 0xA0, 0xAA) : Color.FromRgb(0x68, 0x68, 0x73));
        var card = new StackPanel { Margin = new Thickness(8, 8, 8, 9) };

        card.Children.Add(Header(fg, muted));
        card.Children.Add(Hairline(dark, 7, 7));
        card.Children.Add(ActionRow("编辑小组件…", "打开组件库并调整桌面布局", fg, muted, primary: true, EditMode.Enter));

        bool autostart = Autostart.IsEnabled();
        card.Children.Add(ActionRow("开机启动", autostart ? "已开启" : "已关闭", fg, muted, primary: false, () =>
        {
            bool next = !Autostart.IsEnabled();
            if (!Autostart.SetEnabled(next)) Program.Log("tray popup: autostart toggle failed");
        }, ToggleState(autostart)));

        card.Children.Add(ActionRow("重新启动 MacWidget", "应用已下载的 WebView2 安全更新", fg, muted,
            primary: false, () => TopologyWatcher.RequestRestart("tray restart")));
        card.Children.Add(ActionRow("隐私与数据", "查看随应用提供的本地隐私说明", fg, muted,
            primary: false, Program.OpenPrivacyNotice));

        card.Children.Add(Hairline(dark, 7, 6));
        card.Children.Add(ActionRow("退出 MacWidget", "组件会从桌面隐藏", new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)), muted,
            primary: false, Program.RequestShutdown));

        bool transparency = ColorMode.TransparencyEnabled;
        Content = new Border
        {
            Margin = new Thickness(Pad),
            Padding = new Thickness(8),
            Background = new SolidColorBrush(transparency
                ? (dark ? Color.FromArgb(224, 42, 42, 46) : Color.FromArgb(226, 246, 246, 250))
                : (dark ? Color.FromRgb(42, 42, 46) : Color.FromRgb(246, 246, 250))),
            BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(31, 255, 255, 255) : Color.FromArgb(28, 0, 0, 0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(15),
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = .36, Color = Colors.Black },
            Child = card,
        };

        SourceInitialized += (_, _) =>
        {
            var src = (HwndSource)PresentationSource.FromVisual(this)!;
            src.CompositionTarget.BackgroundColor = Colors.Transparent;
            var ex = Native.GetWindowLongPtr(src.Handle, Native.GWL_EXSTYLE).ToInt64();
            Native.SetWindowLongPtr(src.Handle, Native.GWL_EXSTYLE, new IntPtr(ex | Native.WS_EX_TOOLWINDOW));
            Dwm.ExtendIntoClient(src.Handle);
            Dwm.SetDark(src.Handle, ColorMode.Dark);
            Dwm.SetBackdrop(src.Handle, ColorMode.TransparencyEnabled ? "mica" : "none");
        };
        Deactivated += (_, _) => SafeClose();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) SafeClose(); };
        Closing += (_, _) => _closing = true;
        Closed += (_, _) => { if (_open == this) _open = null; };
    }

    void ShowAtCursor()
    {
        Native.GetCursorPos(out var cursor);
        var display = DisplayTopology.ForPoint(new Point(cursor.X, cursor.Y));
        double scale = display.Scale;
        double width = WidthDiu * scale, height = HeightDiu * scale, gap = 8 * scale;
        var work = display.Work;
        var physical = display.Physical;

        bool bottomTaskbar = work.Bottom < physical.Bottom - 1;
        bool topTaskbar = work.Top > physical.Top + 1;
        bool leftTaskbar = work.Left > physical.Left + 1;
        bool rightTaskbar = work.Right < physical.Right - 1;
        double left, top;
        if (bottomTaskbar || topTaskbar)
        {
            left = Math.Clamp(cursor.X - width + 22 * scale, work.Left + gap, work.Right - width - gap);
            top = bottomTaskbar ? work.Bottom - height - gap : work.Top + gap;
        }
        else
        {
            left = leftTaskbar ? work.Left + gap : work.Right - width - gap;
            top = Math.Clamp(cursor.Y - height + 22 * scale, work.Top + gap, work.Bottom - height - gap);
        }

        Left = 0;
        Top = 0;
        Show();
        if (PresentationSource.FromVisual(this) is HwndSource src)
            Native.MoveWindow(src.Handle, (int)Math.Round(left), (int)Math.Round(top),
                (int)Math.Round(width), (int)Math.Round(height), true);
        Activate();
        Program.Log("tray popup shown");
    }

    UIElement Header(Brush fg, Brush muted)
    {
        var mark = new Grid { Width = 32, Height = 32, Margin = new Thickness(4, 2, 10, 2) };
        mark.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10),
            Background = new LinearGradientBrush(Color.FromRgb(0x4F, 0x46, 0xE5), Color.FromRgb(0x9B, 0x5D, 0xE8), 48),
        });
        var cells = new UniformGrid { Rows = 2, Columns = 2, Margin = new Thickness(8), IsHitTestVisible = false };
        for (int i = 0; i < 4; i++)
            cells.Children.Add(new Border { Margin = new Thickness(1), CornerRadius = new CornerRadius(2), Background = Brushes.White });
        mark.Children.Add(cells);
        mark.Children.Add(new Border
        {
            Width = 6, Height = 6, CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x2F)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 2, 0),
        });

        int widgets = Application.Current.Windows.OfType<WidgetWindow>().Count(w => w.IsVisible);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = "MacWidget", Foreground = fg, FontSize = 14.5, FontWeight = FontWeights.SemiBold });
        labels.Children.Add(new TextBlock { Text = $"{widgets} 个组件正在桌面上", Foreground = muted, FontSize = 11.5, Margin = new Thickness(0, 1, 0, 0) });
        var row = new DockPanel { Margin = new Thickness(4, 4, 4, 5) };
        DockPanel.SetDock(mark, Dock.Left);
        row.Children.Add(mark);
        row.Children.Add(labels);
        return row;
    }

    Border ActionRow(string title, string subtitle, Brush titleBrush, Brush subtitleBrush, bool primary, Action action, UIElement? trailing = null)
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, Foreground = primary ? Brushes.White : titleBrush, FontSize = 13.2, FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal });
        texts.Children.Add(new TextBlock { Text = subtitle, Foreground = primary ? new SolidColorBrush(Color.FromArgb(214, 255, 255, 255)) : subtitleBrush, FontSize = 10.8, Margin = new Thickness(0, 1, 0, 0) });
        var row = new DockPanel();
        if (trailing != null)
        {
            DockPanel.SetDock(trailing, Dock.Right);
            row.Children.Add(trailing);
        }
        row.Children.Add(texts);
        var border = new Border
        {
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(11, 8, 10, 8),
            MinHeight = 45,
            CornerRadius = new CornerRadius(10),
            Background = primary ? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)) : Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = row,
        };
        if (!primary)
        {
            border.MouseEnter += (_, _) => border.Background = new SolidColorBrush(Color.FromArgb(22, 0x0A, 0x84, 0xFF));
            border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        }
        border.MouseLeftButtonUp += (_, _) =>
        {
            SafeClose();
            Dispatcher.BeginInvoke(action);
        };
        return border;
    }

    static UIElement ToggleState(bool on) => new Border
    {
        Width = 30,
        Height = 18,
        CornerRadius = new CornerRadius(9),
        Background = new SolidColorBrush(on ? Color.FromRgb(0x34, 0xC7, 0x59) : Color.FromRgb(0x77, 0x77, 0x80)),
        Child = new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(7),
            Background = Brushes.White,
            Margin = new Thickness(on ? 14 : 2, 2, 2, 2),
            HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        },
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 1, 0),
    };

    static Border Hairline(bool dark, double top, double bottom) => new()
    {
        Height = 1,
        Margin = new Thickness(10, top, 10, bottom),
        Background = new SolidColorBrush(dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0)),
    };

    void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }
}
