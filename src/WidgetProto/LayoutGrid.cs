using System.Windows;

namespace WidgetProto;

/// <summary>
/// 摆位网格（macOS 风格：从工作区右上角起的列网格）。
/// 与 WidgetWindow 初始摆位同一套公式；吸附 = 在所有合法格位里找与当前中心最近的。
/// </summary>
public static class LayoutGrid
{
    public const double Cell = 356;    // 340 组件 + 16 间距
    public const double Margin = 16;

    public static (double L, double T) Snap(double l, double t, double w, double h)
    {
        var wa = SystemParameters.WorkArea;
        int cols = Math.Max(1, (int)((wa.Width - Margin * 2) / Cell));
        double best = double.MaxValue, bl = l, bt = t;
        double cx = l + w / 2, cy = t + h / 2;
        for (int k = 0; k < cols; k++)
        {
            double x = wa.Right - Margin - Cell * (k + 1) + (Cell - w);
            for (int r = 0; ; r++)
            {
                double y = wa.Top + Margin + Cell * r;
                if (y > wa.Bottom - 120) break;   // 放不下最小组件高度就不再往下排
                double d = (x + w / 2 - cx) * (x + w / 2 - cx) + (y + h / 2 - cy) * (y + h / 2 - cy);
                if (d < best) { best = d; bl = x; bt = y; }
            }
        }
        return (bl, bt);
    }
}
