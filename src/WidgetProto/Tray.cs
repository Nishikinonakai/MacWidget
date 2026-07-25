using System.Drawing;
using System.Windows;

namespace WidgetProto;

/// <summary>
/// 托盘常驻入口：编辑小组件… / 退出（原型先能用——机主日常跑这台机，不该靠 taskkill 退出）。
/// 图标运行期自画（渐变圆角块 + W），正式图标与 WPF 弹层菜单产品期再换。
/// </summary>
public static class Tray
{
    static System.Windows.Forms.NotifyIcon? _icon;

    public static void Install()
    {
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "MacWidget（原型）",
            Visible = true,
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("编辑小组件…", null, (_, _) =>
            Application.Current.Dispatcher.BeginInvoke(EditMode.Enter));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _icon!.Visible = false;
            Application.Current.Shutdown();
        }));
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => Application.Current.Dispatcher.BeginInvoke(EditMode.Toggle);
        Program.Log("tray ready");
    }

    static Icon MakeIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = Rounded(new Rectangle(2, 2, 28, 28), 10);
        using var grad = new System.Drawing.Drawing2D.LinearGradientBrush(
            new Rectangle(0, 0, 32, 32),
            Color.FromArgb(255, 64, 156, 255), Color.FromArgb(255, 175, 82, 222), 55f);
        g.FillPath(grad, path);
        using var f = new Font("Segoe UI", 15, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        var sz = g.MeasureString("W", f);
        g.DrawString("W", f, Brushes.White, (32 - sz.Width) / 2, (32 - sz.Height) / 2 + 1);
        return Icon.FromHandle(bmp.GetHicon());   // 托盘常驻整个进程生命周期，句柄不回收
    }

    static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int rad)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddArc(r.X, r.Y, rad, rad, 180, 90);
        p.AddArc(r.Right - rad, r.Y, rad, rad, 270, 90);
        p.AddArc(r.Right - rad, r.Bottom - rad, rad, rad, 0, 90);
        p.AddArc(r.X, r.Bottom - rad, rad, rad, 90, 90);
        p.CloseFigure();
        return p;
    }
}
