using System.Runtime.InteropServices;
using System.Windows;

namespace WidgetProto;

/// <summary>
/// 显示器拓扑的唯一入口。这里的 Rect 一律是 Windows 虚拟桌面的物理像素；WPF 的
/// Window.Left/Top 是随 DPI 变化的逻辑坐标，不能拿来跨屏持久化或传给 MacDesk。
/// </summary>
public static class DisplayTopology
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfoEx
    {
        public int cbSize;
        public Native.RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Native.RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplayDevicesW(string? device, uint index, ref DisplayDevice display, uint flags);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    const uint DisplayDeviceActive = 0x1;

    public sealed record Display(
        string Key,
        IntPtr Handle,
        Rect Physical,
        Rect Work,
        uint Dpi,
        bool IsPrimary,
        string Device)
    {
        public double Scale => Dpi / 96.0;
        public string LayoutKey => $"v2:{Key}:{Physical.Width:F0}x{Physical.Height:F0}";
    }

    /// <summary>按显示器相对物理 px 存的位置；显示器换到虚拟桌面另一侧时仍能还原。</summary>
    public readonly record struct Position(string DisplayKey, double X, double Y);

    public static IReadOnlyList<Display> GetAll()
    {
        var raw = new List<(IntPtr Handle, MonitorInfoEx Info)>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfoW(h, ref info)) raw.Add((h, info));
            return true;
        }, IntPtr.Zero);

        // 同一台电视的多个 HDMI 输入会给 Windows 相同 EDID。旧代码按枚举顺序追加 #2，
        // 而热插拔后的枚举顺序没有稳定性，可能把两路输入的组件布局对调。DISPLAYn 是
        // Windows 当前图形路径的稳定连接标识；只在 EDID 重复时纳入 key，单屏旧 key 不变。
        var baseKeys = raw.Select(item => EdidKey(item.Info.szDevice) ?? DeviceKey(item.Info.szDevice)).ToList();
        var duplicateCounts = baseKeys
            .GroupBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var displays = new List<Display>();
        for (int i = 0; i < raw.Count; i++)
        {
            var (handle, info) = raw[i];
            uint dpi = 96;
            if (GetDpiForMonitor(handle, 0 /* MDT_EFFECTIVE_DPI */, out uint x, out _) == 0) dpi = x;
            string baseKey = baseKeys[i];
            string key = duplicateCounts[baseKey] > 1 ? $"{baseKey}@{DeviceKey(info.szDevice)}" : baseKey;
            displays.Add(new Display(key, handle,
                ToRect(info.rcMonitor), ToRect(info.rcWork), dpi, (info.dwFlags & 1) != 0, info.szDevice));
        }
        return displays.OrderByDescending(d => d.IsPrimary).ToList();
    }

    public static Display Primary()
        => GetAll().FirstOrDefault(d => d.IsPrimary) ?? throw new InvalidOperationException("No display is available");

    public static Display ByKey(string? key)
    {
        var all = GetAll();
        return all.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? all.First(d => d.IsPrimary);
    }

    public static Display ForPoint(Point point)
    {
        var h = Native.MonitorFromPoint(new Native.POINT { X = (int)Math.Round(point.X), Y = (int)Math.Round(point.Y) },
            Native.MONITOR_DEFAULTTONEAREST);
        return GetAll().FirstOrDefault(d => d.Handle == h) ?? Primary();
    }

    public static Display ForRect(Rect rect)
        => ForPoint(new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2));

    public static Display ForWindow(IntPtr hwnd)
    {
        var h = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        return GetAll().FirstOrDefault(d => d.Handle == h) ?? Primary();
    }

    public static Rect RectOf(IntPtr hwnd)
        => Native.GetWindowRect(hwnd, out var r) ? ToRect(r) : Rect.Empty;

    static Rect ToRect(Native.RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    static string? EdidKey(string adapterDevice)
    {
        string? fallback = null;
        for (uint i = 0; i < 8; i++)
        {
            var display = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevicesW(adapterDevice, i, ref display, 0)) break;
            var parts = display.DeviceID.Split('\\');
            string? key = parts.Length >= 2 && parts[1].Length > 0 ? parts[1] : null;
            if (key == null) continue;
            if ((display.StateFlags & DisplayDeviceActive) != 0) return key;
            fallback ??= key;
        }
        return fallback;
    }

    static string DeviceKey(string device)
    {
        var key = device.Trim().TrimStart('\\', '.');
        return string.IsNullOrEmpty(key) ? "DISPLAY" : key;
    }
}
