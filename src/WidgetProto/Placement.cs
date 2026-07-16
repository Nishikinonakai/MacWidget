using System.Windows;

namespace WidgetProto;

/// <summary>
/// 摆位引擎 v2 —— 按 macOS 实测模型（research/notes/macos-widget-摆放与图标避让-调研.md）：
/// · 自由摆放是常态：不越界、不压件、不邻近 → 原样落地，无虚影；
/// · 并组吸附：与最近组件净距 ≤ JoinGap（重叠也算）→ 吸到它的单元网格相邻格位（格距=帧尺寸，零间隙）；
/// · 纯越界（远离组件）→ 钳制进安全边；
/// · 组格位优先于纯钳制（macOS exp3 实测：右缘溢出落点选择了入组而非贴边）。
/// </summary>
public static class Placement
{
    public const double Unit = 170;        // Small 单元 = 帧尺寸（缝隙烙在帧内衬里，组内零间隙）
    public const double EdgeMargin = 16;   // 屏幕安全边
    public const double JoinGap = 20;      // 邻近判定净距（粗筛：找锚组件）
    public const double SnapDist = 26;     // 真吸附门槛：最近格位距生手位置 ≤ 此值才吸（机主反馈修正：
                                           // 否则两组件间整条 20 走廊都成吸附区，自由区有"网格感"）

    public readonly record struct Result(double L, double T, bool Corrected);

    public static Result Resolve(Rect self, IReadOnlyList<Rect> others, Rect work)
    {
        var safe = new Rect(work.X + EdgeMargin, work.Y + EdgeMargin,
                            Math.Max(0, work.Width - EdgeMargin * 2), Math.Max(0, work.Height - EdgeMargin * 2));

        // 1) 邻近判定：净距 ≤ JoinGap（重叠 = 负净距，也入组）
        int anchor = -1; double best = double.MaxValue;
        for (int i = 0; i < others.Count; i++)
        {
            var g = GapXY(self, others[i]);
            double d = Math.Max(g.dx, g.dy);           // 各轴净距都在阈值内才算“邻近”
            if (g.dx <= JoinGap && g.dy <= JoinGap && d < best) { best = d; anchor = i; }
        }
        if (anchor >= 0)
        {
            var cell = NearestCell(self, others[anchor], others, safe);
            if (cell is { } c)
            {
                bool overlapped = self.IntersectsWith(others[anchor]);
                double dist = Math.Sqrt((c.X - self.X) * (c.X - self.X) + (c.Y - self.Y) * (c.Y - self.Y));
                // 重叠必须解算；净距邻近仅当"基本已在格位上"（纠正量小）才吸——其余情况自由
                if (overlapped || dist <= SnapDist) return new Result(c.X, c.Y, Corrected: true);
            }
        }

        // 2) 自由落地 / 越界钳制
        double l = Math.Clamp(self.X, safe.Left, Math.Max(safe.Left, safe.Right - self.Width));
        double t = Math.Clamp(self.Y, safe.Top, Math.Max(safe.Top, safe.Bottom - self.Height));
        var clamped = new Rect(l, t, self.Width, self.Height);

        // 3) 钳制后压到组件 → 以被压者为锚做格位解算
        for (int i = 0; i < others.Count; i++)
        {
            if (clamped.IntersectsWith(others[i]))
            {
                var cell = NearestCell(clamped, others[i], others, safe);
                if (cell is { } c) return new Result(c.X, c.Y, Corrected: true);
            }
        }
        return new Result(l, t, Corrected: l != self.X || t != self.Y);
    }

    /// <summary>锚组件单元网格上，距 self 最近的无冲突格位（self 按 w/h 占多个单元）</summary>
    static Point? NearestCell(Rect self, Rect anchorRect, IReadOnlyList<Rect> others, Rect safe)
    {
        int selfCols = (int)Math.Round(self.Width / Unit), selfRows = (int)Math.Round(self.Height / Unit);
        int anchorCols = (int)Math.Round(anchorRect.Width / Unit), anchorRows = (int)Math.Round(anchorRect.Height / Unit);
        Point? best = null; double bd = double.MaxValue;
        for (int i = -selfCols; i <= anchorCols; i++)
        {
            for (int j = -selfRows; j <= anchorRows; j++)
            {
                // 跳过与锚重叠的格位；只取环绕锚的位置
                bool overlapsAnchor = i > -selfCols && i < anchorCols && j > -selfRows && j < anchorRows;
                if (overlapsAnchor) continue;
                var p = new Point(anchorRect.X + i * Unit, anchorRect.Y + j * Unit);
                var r = new Rect(p.X, p.Y, self.Width, self.Height);
                if (p.X < safe.Left || p.Y < safe.Top || r.Right > safe.Right || r.Bottom > safe.Bottom) continue;
                bool clash = false;
                foreach (var o in others) if (r.IntersectsWith(Shrink(o, 0.5))) { clash = true; break; }
                if (clash) continue;
                double d = (p.X - self.X) * (p.X - self.X) + (p.Y - self.Y) * (p.Y - self.Y);
                if (d < bd) { bd = d; best = p; }
            }
        }
        return best;
    }

    static Rect Shrink(Rect r, double e) => new(r.X + e, r.Y + e, Math.Max(0, r.Width - 2 * e), Math.Max(0, r.Height - 2 * e));

    static (double dx, double dy) GapXY(Rect a, Rect b)
    {
        double dx = Math.Max(0, Math.Max(b.Left - a.Right, a.Left - b.Right));
        double dy = Math.Max(0, Math.Max(b.Top - a.Bottom, a.Top - b.Bottom));
        return (dx, dy);
    }
}
