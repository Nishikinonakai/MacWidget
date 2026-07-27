using System.Drawing;
using System.Windows;
using System.Windows.Interop;

namespace WidgetProto;

/// <summary>
/// 托盘常驻入口。图标取自 EXE 内嵌的正式应用图标；NotifyIcon 仅负责通知区挂钩，
/// 点击后使用 USER32 原生菜单；Windows 负责主题、圆角、阴影、DPI 与键盘交互。
/// </summary>
public static class Tray
{
    static System.Windows.Forms.NotifyIcon? _icon;
    static HwndSource? _menuOwner;
    static bool _menuOpen;
    static bool _runtimeUpdateNoticePending;

    public static void Install()
    {
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = "MacWidget",
            Visible = true,
        };
        _menuOwner = new HwndSource(new HwndSourceParameters("MacWidget Tray Menu Owner")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = (int)Native.WS_EX_TOOLWINDOW,
            Width = 1,
            Height = 1,
            PositionX = -32000,
            PositionY = -32000,
        });
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button is System.Windows.Forms.MouseButtons.Left or System.Windows.Forms.MouseButtons.Right)
                Application.Current.Dispatcher.BeginInvoke(OpenNativeMenu);
        };
        Program.Log("tray ready");
        if (_runtimeUpdateNoticePending) ShowRuntimeUpdateNotice();
    }

    public static void Uninstall()
    {
        try
        {
            if (_icon != null) { _icon.Visible = false; _icon.Dispose(); _icon = null; }
            _menuOwner?.Dispose();
            _menuOwner = null;
        }
        catch { }
    }

    static void OpenNativeMenu()
    {
        if (_menuOpen || _menuOwner == null) return;
        _menuOpen = true;
        try
        {
            using var menu = new NativePopupMenu();
            int widgets = Application.Current.Windows.OfType<WidgetWindow>().Count(w => w.IsVisible);
            menu.Add(1, ProductSettings.English
                ? $"MacWidget — {widgets} widget(s) on desktop"
                : $"MacWidget — 桌面上有 {widgets} 个组件", enabled: false);
            menu.Separator();
            menu.Add(2, Ui.T("编辑小组件…", "Edit Widgets…"));
            menu.Add(3, Ui.T("开机启动", "Launch at sign-in"), isChecked: Autostart.IsEnabled());
            menu.Add(4, Ui.T("重新启动 MacWidget", "Restart MacWidget"));
            menu.Add(5, Ui.T("隐私与数据", "Privacy && Data"));
            menu.Add(6, ProductSettings.English ? "语言 / Language：English" : "语言 / Language：简体中文");
            menu.Separator();
            menu.Add(7, Ui.T("退出 MacWidget", "Quit MacWidget"));

            switch (menu.Show(_menuOwner.Handle))
            {
                case 2:
                    EditMode.Enter();
                    break;
                case 3:
                    if (!Autostart.SetEnabled(!Autostart.IsEnabled()))
                        Program.Log("tray menu: autostart toggle failed");
                    break;
                case 4:
                    TopologyWatcher.RequestRestart("tray restart");
                    break;
                case 5:
                    Program.OpenPrivacyNotice();
                    break;
                case 6:
                    ProductSettings.ToggleLanguage();
                    TopologyWatcher.RequestRestart("language changed");
                    break;
                case 7:
                    Program.RequestShutdown();
                    break;
            }
        }
        finally { _menuOpen = false; }
    }

    /// <summary>Evergreen WebView2 已更新时的非打扰提示；Windows 可能因用户的通知设置而不显示，日志仍保留。</summary>
    public static void ShowRuntimeUpdateNotice()
    {
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    if (_icon == null)
                    {
                        _runtimeUpdateNoticePending = true;
                        return;
                    }
                    _runtimeUpdateNoticePending = false;
                    _icon.ShowBalloonTip(8000, "MacWidget 可重启更新",
                        "WebView2 Runtime 已更新。打开托盘浮层并选择“重新启动 MacWidget”以应用安全更新。",
                        System.Windows.Forms.ToolTipIcon.Info);
                }
                catch { }
            });
        }
        catch { }
    }

    static Icon LoadApplicationIcon()
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                using var extracted = Icon.ExtractAssociatedIcon(executable);
                if (extracted != null) return (Icon)extracted.Clone();
            }
        }
        catch { }

        return (Icon)SystemIcons.Application.Clone();
    }
}
