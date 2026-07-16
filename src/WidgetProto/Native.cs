using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WidgetProto;

public static class Native
{
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}

public static class Dwm
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

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
        int v = kind switch { "mica" => 2, "acrylic" => 3, "tabbed" => 4, _ => 1 };
        var hr = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref v, 4);
        Program.Log($"backdrop {kind}({v}) hr=0x{hr:x}");
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
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;

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
