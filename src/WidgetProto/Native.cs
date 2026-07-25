using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WidgetProto;

public static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const long WS_EX_TRANSPARENT = 0x00000020;

    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    // ---- Automatic 着色状态机 / 面板拖出 所需 ----

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    public static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hwnd, char[] buf, int max);

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT p);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);

    [DllImport("kernel32.dll")]
    public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    public static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr h, uint flags, char[] buf, ref int size);

    // ---- 系统计数器（SysMonProvider）----

    [DllImport("kernel32.dll")]
    public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buf);

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;   // 秒；-1 = 未知
    }

    [DllImport("kernel32.dll")]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

    public static string ProcessImageName(uint pid)
    {
        var h = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
        if (h == IntPtr.Zero) return "";
        try
        {
            var buf = new char[512]; int len = buf.Length;
            return QueryFullProcessImageName(h, 0, buf, ref len) ? new string(buf, 0, len) : "";
        }
        finally { CloseHandle(h); }
    }
}

public static class Dwm
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    /// <summary>UWP/云挂起窗口 IsWindowVisible 仍为 true，但 DWM 侧被斗篷遮蔽——判"真窗口"必须过这道。</summary>
    public static bool IsCloaked(IntPtr hwnd)
        => DwmGetWindowAttribute(hwnd, 14 /*DWMWA_CLOAKED*/, out int v, 4) == 0 && v != 0;

    [StructLayout(LayoutKind.Sequential)]
    struct MARGINS { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;   // DWMWCP_ROUND = 2
    const int DWMWA_SYSTEMBACKDROP_TYPE = 38;        // 1 none / 2 mica / 3 acrylic / 4 tabbed

    public static void ExtendIntoClient(IntPtr hwnd)
    {
        var m = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        var hr = DwmExtendFrameIntoClientArea(hwnd, ref m);
        Program.Log($"extendframe hr=0x{hr:x}");
    }

    public static void SetRoundCorners(IntPtr hwnd)
    {
        int v = 2;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref v, 4);
    }

    public static void SetDark(IntPtr hwnd, bool on)
    {
        int v = on ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, 4);
    }

    public static void SetBackdrop(IntPtr hwnd, string kind)
    {
        if (kind is "wca" or "wcablur") { Wca.Apply(hwnd, kind); return; }
        int v = kind switch { "mica" => 2, "acrylic" => 3, "tabbed" => 4, _ => 1 };
        var hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref v, 4);
        Program.Log($"backdrop {kind}({v}) hr=0x{hr:x}");
    }
}

/// <summary>
/// 未公开路线：SetWindowCompositionAttribute 的 accent 模糊（TranslucentTB/Rainmeter 同款）。
/// 不依赖窗口激活态；若 DWMSBT 被证实绑激活，这条是产品候选。
/// </summary>
public static class Wca
{
    [StructLayout(LayoutKind.Sequential)]
    struct ACCENT_POLICY { public int State; public int Flags; public uint GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    struct WINCOMPATTRDATA { public int Attribute; public IntPtr Data; public int Size; }

    [DllImport("user32.dll")]
    static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINCOMPATTRDATA data);

    public static void Apply(IntPtr hwnd, string kind)
    {
        // wca = ACCENT_ENABLE_ACRYLICBLURBEHIND(4)，wcablur = ACCENT_ENABLE_BLURBEHIND(3)
        var pol = new ACCENT_POLICY
        {
            State = kind == "wcablur" ? 3 : 4,
            Flags = 2,
            GradientColor = 0x40202020,   // AABBGGRR：25% 深灰 tint（acrylic 态要求非零 alpha）
        };
        var pin = Marshal.AllocHGlobal(Marshal.SizeOf<ACCENT_POLICY>());
        try
        {
            Marshal.StructureToPtr(pol, pin, false);
            var data = new WINCOMPATTRDATA { Attribute = 19 /*WCA_ACCENT_POLICY*/, Data = pin, Size = Marshal.SizeOf<ACCENT_POLICY>() };
            var r = SetWindowCompositionAttribute(hwnd, ref data);
            Program.Log($"wca {kind} ret={r}");
        }
        finally { Marshal.FreeHGlobal(pin); }
    }
}

/// <summary>
/// 贴桌面层：SetWindowPos(HWND_BOTTOM) + WM_WINDOWPOSCHANGING 钩子把任何抬升改回 HWND_BOTTOM。
/// Rainmeter OnDesktop 同款，公开 API，不碰 WorkerW（24H2 已证明 WorkerW 时序会被微软无预告改动）。
/// </summary>
public static class BottomPin
{
    const int WM_WINDOWPOSCHANGING = 0x0046;
    static readonly IntPtr HWND_BOTTOM = new(1);
    static readonly IntPtr HWND_TOP = IntPtr.Zero;
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

    static readonly HashSet<IntPtr> _suspended = new();

    /// <summary>拖拽期把组件提离桌面带（macOS 实测同款：拖拽中的组件升层渲染），结束后 Drop 回钉底部。</summary>
    public static void Lift(IntPtr hwnd)
    {
        lock (_suspended) _suspended.Add(hwnd);
        Native.SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public static void Drop(IntPtr hwnd)
    {
        lock (_suspended) _suspended.Remove(hwnd);
        Native.SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int x, y, cx, cy;
        public uint flags;
    }

    public static void Install(HwndSource src)
    {
        Native.SetWindowPos(src.Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        src.AddHook(Hook);
    }

    static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING)
        {
            lock (_suspended) { if (_suspended.Contains(hwnd)) return IntPtr.Zero; }
            var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((wp.flags & SWP_NOZORDER) == 0)
            {
                wp.hwndInsertAfter = HWND_BOTTOM;
                Marshal.StructureToPtr(wp, lParam, false);
            }
        }
        return IntPtr.Zero;
    }
}
