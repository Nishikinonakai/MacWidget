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
    static string? _balloonTargetUrl;

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
        _icon.BalloonTipClicked += (_, _) =>
        {
            var target = _balloonTargetUrl;
            _balloonTargetUrl = null;
            if (target != null) ExternalLaunch.OpenHttp(target, "update notification");
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
                ? $"MacWidget {AppUpdate.DisplayVersion} — {widgets} widget(s) on desktop"
                : $"MacWidget {AppUpdate.DisplayVersion} — 桌面上有 {widgets} 个组件", enabled: false);
            menu.Separator();
            menu.Add(2, Ui.T("编辑小组件…", "Edit Widgets…"));
            menu.Add(3, Ui.T("开机启动", "Launch at sign-in"), isChecked: Autostart.IsEnabled());
            menu.Separator();
            menu.Add(4, Ui.T("检查更新…", "Check for Updates…"));
            menu.Add(5, Ui.T("重新启动 MacWidget", "Restart MacWidget"));
            menu.Add(6, Ui.T("隐私与数据", "Privacy && Data"));
            menu.Add(7, ProductSettings.English ? "语言 / Language：English" : "语言 / Language：简体中文");
            menu.Separator();
            menu.Add(8, Ui.T("退出 MacWidget", "Quit MacWidget"));

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
                    _ = AppUpdate.CheckFromTrayAsync();
                    break;
                case 5:
                    TopologyWatcher.RequestRestart("tray restart");
                    break;
                case 6:
                    Program.OpenPrivacyNotice();
                    break;
                case 7:
                    ProductSettings.ToggleLanguage();
                    TopologyWatcher.RequestRestart("language changed");
                    break;
                case 8:
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
                    _balloonTargetUrl = null;
                    _icon.ShowBalloonTip(8000, "MacWidget 可重启更新",
                        "WebView2 Runtime 已更新。打开托盘浮层并选择“重新启动 MacWidget”以应用安全更新。",
                        System.Windows.Forms.ToolTipIcon.Info);
                }
                catch { }
            });
        }
        catch { }
    }

    public static void ShowUpdateResult(UpdateCheckResult result)
    {
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_icon == null) return;
                string title, message;
                var icon = System.Windows.Forms.ToolTipIcon.Info;
                switch (result.Status)
                {
                    case UpdateCheckStatus.UpdateAvailable:
                        title = Ui.T($"发现 MacWidget {result.LatestVersion}", $"MacWidget {result.LatestVersion} is available");
                        message = Ui.T("单击此通知打开 GitHub Release 下载页。", "Click this notification to open the GitHub Release download page.");
                        _balloonTargetUrl = result.ReleaseUrl;
                        break;
                    case UpdateCheckStatus.Current:
                        title = Ui.T("MacWidget 已是最新版", "MacWidget is up to date");
                        message = Ui.T($"当前版本：{result.CurrentVersion}", $"Current version: {result.CurrentVersion}");
                        _balloonTargetUrl = null;
                        break;
                    default:
                        title = Ui.T("暂时无法检查更新", "Could not check for updates");
                        message = result.Error ?? Ui.T("请稍后重试。", "Try again later.");
                        icon = System.Windows.Forms.ToolTipIcon.Warning;
                        _balloonTargetUrl = null;
                        break;
                }
                _icon.ShowBalloonTip(9000, title, message, icon);
            });
        }
        catch { }
    }

    public static void ShowTimerFinished()
    {
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (_icon == null) return;
                _balloonTargetUrl = null;
                _icon.ShowBalloonTip(9000,
                    Ui.T("专注计时结束", "Focus timer complete"),
                    Ui.T("时间到了。休息一下，或者开始下一轮。", "Time is up. Take a break or start another round."),
                    System.Windows.Forms.ToolTipIcon.Info);
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
