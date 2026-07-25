using System.Windows;
using Microsoft.Win32;

namespace WidgetProto;

/// <summary>
/// Automatic 着色状态机（macOS 实测语义，refs 06/07）：
///   Widget style = full → 恒全彩；mono → 恒 mono；auto →（默认）每显示器独立判定——
///   本显示器上存在任意"普通应用窗口"→ 该屏全体组件渐变 mono；纯桌面可见 → 全彩。
/// 实现 = 500ms 轮询 EnumWindows（可见+非最小化+非斗篷+有标题+非工具窗+非 shell/桌面族+非自家），
/// 按 MonitorFromWindow 归屏；深浅外观同 tick 读 AppsUseLightTheme。变化才广播（0.3s 渐变在 CSS 侧）。
/// </summary>
public static class ColorMode
{
    public static bool Dark { get; private set; } = true;

    static readonly HashSet<IntPtr> _busy = new();
    static readonly Dictionary<uint, bool> _pidExcluded = new();
    static uint _selfPid;

    // 桌面族/外壳窗口：无论有无标题都不算"应用窗口"
    static readonly HashSet<string> _shellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow", "XamlExplorerHostIslandWindow",
    };
    // 进程级豁免：桌面基础设施同伙（MacDesk 主窗盖满桌面、WE 渲染窗——它们不是"用户开了窗口"）
    static readonly string[] _peerProcs = { "widgetproto", "macdesk", "wallpaper64", "wallpaper32", "wallpaperservice" };

    public static void Start()
    {
        _selfPid = (uint)Environment.ProcessId;
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        t.Tick += (_, _) => Tick();
        t.Start();
        Tick();
    }

    public static bool IsMono(IntPtr monitor) => Program.Opts.Style switch
    {
        "mono" => true,
        "full" => false,
        _ => _busy.Contains(monitor),
    };

    static void Tick()
    {
        bool dark = ReadDark();
        var (busy, occluders) = Scan();
        bool changed = dark != Dark || !busy.SetEquals(_busy);
        Dark = dark;
        _busy.Clear();
        foreach (var m in busy) _busy.Add(m);

        // 和 Automatic 着色共用一次窗口枚举：仅单个普通窗口完整覆盖组件时才允许挂起。
        // 多个窗口拼出来的覆盖不猜，避免误伤露在缝里的组件。
        foreach (Window w in Application.Current.Windows)
            if (w is WidgetWindow ww)
                ww.SetOccluded(occluders.Any(r => Covers(r, ww.PhysicalBounds)));

        if (!changed) return;
        Program.Log($"colormode: dark={dark} busyMonitors={busy.Count}");
        foreach (Window w in Application.Current.Windows)
            (w as WidgetWindow)?.PushState();
        PanelWindow.Existing?.PushState();
    }

    static bool ReadDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return Dark; }
    }

    static readonly char[] _clsBuf = new char[64];

    static bool Covers(Rect cover, Rect target)
    {
        const double epsilon = 2; // DWM 阴影/边框可能让真实窗口矩形差 1px
        return !target.IsEmpty && cover.Left <= target.Left + epsilon && cover.Top <= target.Top + epsilon &&
               cover.Right >= target.Right - epsilon && cover.Bottom >= target.Bottom - epsilon;
    }

    static (HashSet<IntPtr> Busy, List<Rect> Occluders) Scan()
    {
        var busy = new HashSet<IntPtr>();
        var occluders = new List<Rect>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) return true;
            var ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return true;
            if (Native.GetWindowTextLength(hwnd) == 0) return true;
            int n = Native.GetClassName(hwnd, _clsBuf, _clsBuf.Length);
            if (n > 0 && _shellClasses.Contains(new string(_clsBuf, 0, n))) return true;
            if (Dwm.IsCloaked(hwnd)) return true;
            // 尺寸下限 120×80：滤掉工具类小窗（如 PowerToys ColorPickerUI 的 166×61 隐藏窗一族），
            // 真实应用窗口不会小于这个
            if (!Native.GetWindowRect(hwnd, out var r) || r.Right - r.Left < 120 || r.Bottom - r.Top < 80) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == _selfPid || IsPeerProcess(pid)) return true;
            busy.Add(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST));
            occluders.Add(new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            return true;
        }, IntPtr.Zero);
        return (busy, occluders);
    }

    static bool IsPeerProcess(uint pid)
    {
        if (_pidExcluded.TryGetValue(pid, out bool cached)) return cached;
        if (_pidExcluded.Count > 512) _pidExcluded.Clear();   // pid 复用防陈旧
        var img = Native.ProcessImageName(pid);
        var name = System.IO.Path.GetFileNameWithoutExtension(img);
        bool peer = _peerProcs.Any(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        _pidExcluded[pid] = peer;
        return peer;
    }
}
