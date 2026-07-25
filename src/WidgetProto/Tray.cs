using System.Drawing;
using System.Windows;

namespace WidgetProto;

/// <summary>
/// 托盘常驻入口：编辑小组件… / 自启 / 退出。
/// 图标取自 EXE 内嵌的正式应用图标，确保托盘、快捷方式与安装器一致。
/// </summary>
public static class Tray
{
    static System.Windows.Forms.NotifyIcon? _icon;

    public static void Install()
    {
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadApplicationIcon(),
            Text = "MacWidget",
            Visible = true,
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("编辑小组件…", null, (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(EditMode.Enter));
        var autostart = new System.Windows.Forms.ToolStripMenuItem("开机启动")
        {
            Checked = Autostart.IsEnabled(),
            CheckOnClick = false,
        };
        autostart.Click += (_, _) =>
        {
            bool next = !Autostart.IsEnabled();
            bool ok = Autostart.SetEnabled(next);
            autostart.Checked = ok && next;
            if (!ok) Program.Log("autostart toggle failed");
        };
        menu.Items.Add(autostart);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Program.RequestShutdown();
        }));
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => Application.Current.Dispatcher.BeginInvoke(EditMode.Toggle);
        Program.Log("tray ready");
    }

    public static void Uninstall()
    {
        try
        {
            if (_icon != null) { _icon.Visible = false; _icon.Dispose(); _icon = null; }
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
