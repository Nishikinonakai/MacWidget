using System.Drawing;
using System.Windows;

namespace WidgetProto;

/// <summary>
/// 托盘常驻入口。图标取自 EXE 内嵌的正式应用图标；NotifyIcon 仅负责通知区挂钩，
/// 点击后由 TrayPopupWindow 提供与产品其余界面一致的 WPF 浮层。
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
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button is System.Windows.Forms.MouseButtons.Left or System.Windows.Forms.MouseButtons.Right)
                Application.Current.Dispatcher.BeginInvoke(TrayPopupWindow.Toggle);
        };
        Program.Log("tray ready");
    }

    public static void Uninstall()
    {
        try
        {
            TrayPopupWindow.CloseIfOpen();
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
