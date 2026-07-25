using System.IO;

namespace WidgetProto;

/// <summary>
/// 系统监视数据源（topic "sysmon"，数据桥探路者）：
/// CPU = GetSystemTimes 差分（kernel 含 idle），内存 = GlobalMemoryStatusEx，
/// 磁盘 = 系统盘已用比例，GPU = 性能计数器 "GPU Engine"(engtype_3D) 汇总。
/// 任何一项拿不到 = null（契约的 n/a 路径：精简系统/无此类目照样出卡，组件画 "—"）。
/// </summary>
public sealed class SysMonProvider : IDataProvider
{
    public string Topic => "sysmon";
    public TimeSpan Interval => TimeSpan.FromMilliseconds(1600);

    long _pIdle, _pKernel, _pUser;
    bool _primed;

    public object Fetch()
    {
        double? mem = null; double memUsedGb = 0, memTotalGb = 0;
        var ms = new Native.MEMORYSTATUSEX
        {
            Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.MEMORYSTATUSEX>(),
        };
        if (Native.GlobalMemoryStatusEx(ref ms) && ms.TotalPhys > 0)
        {
            memTotalGb = ms.TotalPhys / 1073741824.0;
            memUsedGb = (ms.TotalPhys - ms.AvailPhys) / 1073741824.0;
            mem = Math.Clamp(memUsedGb / memTotalGb, 0, 1);
        }

        double? disk = null; double diskFreeGb = 0;
        try
        {
            var di = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
            disk = Math.Clamp(1 - (double)di.TotalFreeSpace / di.TotalSize, 0, 1);
            diskFreeGb = di.TotalFreeSpace / 1073741824.0;
        }
        catch { /* 拿不到就 n/a */ }

        return new
        {
            cpu = CpuNow(), mem, disk, gpu = Gpu.Read(),
            memUsedGb = Math.Round(memUsedGb, 1),
            memTotalGb = Math.Round(memTotalGb, 1),
            diskFreeGb = Math.Round(diskFreeGb),
        };
    }

    double? CpuNow()
    {
        if (!Native.GetSystemTimes(out long idle, out long kernel, out long user)) return null;
        if (!_primed)
        {
            _primed = true;
            (_pIdle, _pKernel, _pUser) = (idle, kernel, user);
            return null;   // 首采无差分基线
        }
        double di = idle - _pIdle, total = (kernel - _pKernel) + (user - _pUser);
        (_pIdle, _pKernel, _pUser) = (idle, kernel, user);
        return total > 0 ? Math.Clamp(1 - di / total, 0, 1) : null;
    }

    /// <summary>
    /// GPU 占用：perf 类目 "GPU Engine" / "Utilization Percentage"，engtype_3D 实例求和（任务管理器近似）。
    /// 实例随进程生灭 → 周期重建；类目不存在探测一次即死心；瞬态异常丢弃本次、下 tick 重建。
    /// </summary>
    static class Gpu
    {
        static System.Diagnostics.PerformanceCounter[]? _counters;
        static int _age;
        static bool _dead;

        public static double? Read()
        {
            if (_dead) return null;
            try
            {
                if (_counters == null || ++_age >= 8) Rebuild();
                if (_dead || _counters == null || _counters.Length == 0) return null;
                double sum = 0;
                foreach (var c in _counters) sum += c.NextValue();
                return Math.Clamp(sum / 100.0, 0, 1);
            }
            catch
            {
                DisposeAll();
                return null;
            }
        }

        static void Rebuild()
        {
            _age = 0;
            DisposeAll();
            try
            {
                var cat = new System.Diagnostics.PerformanceCounterCategory("GPU Engine");
                _counters = cat.GetInstanceNames()
                    .Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    .Select(n => new System.Diagnostics.PerformanceCounter("GPU Engine", "Utilization Percentage", n, readOnly: true))
                    .ToArray();
            }
            catch (InvalidOperationException) { _dead = true; }   // 类目/计数器不存在：此机永远 n/a
        }

        static void DisposeAll()
        {
            if (_counters == null) return;
            foreach (var c in _counters) { try { c.Dispose(); } catch { } }
            _counters = null;
        }
    }
}
