using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace WidgetProto;

/// <summary>
/// 显示器热插拔、分辨率或系统 DPI 变化后的安全交接。
/// WPF/WebView2 活体窗口在 PMv2 下不能可靠地原地重挂到新拓扑；保存当前物理布局后
/// 用带等待机制的子实例重新建窗，反而能保证尺寸与坐标都由新显示配置初始化。
/// </summary>
public static class TopologyWatcher
{
    const int WmDisplayChange = 0x007E;

    static HwndSource? _messageWindow;
    static System.Windows.Threading.DispatcherTimer? _debounce;
    static bool _started;
    static bool _restarting;

    public static void Start()
    {
        if (_started) return;
        _started = true;
        _debounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            RestartNow("display topology changed");
        };

        var p = new HwndSourceParameters("MacWidgetTopologyWatcher")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ExtendedWindowStyle = unchecked((int)Native.WS_EX_TOOLWINDOW),
        };
        _messageWindow = new HwndSource(p);
        _messageWindow.AddHook(WndProc);
        SystemEvents.DisplaySettingsChanged += OnSystemDisplayChanged;
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;
        SystemEvents.DisplaySettingsChanged -= OnSystemDisplayChanged;
        _debounce?.Stop();
        _debounce = null;
        _messageWindow?.Dispose();
        _messageWindow = null;
    }

    public static void RequestRestart(string reason)
    {
        if (Application.Current == null) return;
        _ = Application.Current.Dispatcher.BeginInvoke(() => RestartNow(reason));
    }

    /// <summary>启动期等到连续两次枚举相同，规避热插拔完成前 User32 短暂返回旧拓扑。</summary>
    public static async Task WaitForStableTopologyAsync()
    {
        string? previous = null;
        for (int i = 0; i < 10; i++)
        {
            var now = string.Join("|", DisplayTopology.GetAll().Select(d =>
                $"{d.Key}:{d.Physical.Left:F0},{d.Physical.Top:F0},{d.Physical.Width:F0}x{d.Physical.Height:F0}@{d.Dpi}"));
            if (now == previous)
            {
                Program.Log($"display topology stable: {now}");
                return;
            }
            previous = now;
            await Task.Delay(150);
        }
        Program.Log($"display topology settling timeout; proceeding with: {previous}");
    }

    static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDisplayChange)
        {
            Schedule();
            handled = true;
        }
        return IntPtr.Zero;
    }

    static void OnSystemDisplayChanged(object? sender, EventArgs e)
        => Application.Current?.Dispatcher.BeginInvoke(Schedule);

    static void Schedule()
    {
        if (_restarting || _debounce == null) return;
        _debounce.Stop();
        _debounce.Start();
    }

    static void RestartNow(string reason)
    {
        var app = Application.Current;
        if (_restarting || app?.Dispatcher.HasShutdownStarted == true) return;
        _restarting = true;
        try
        {
            // 普通重启同步落盘；若 Windows 已切到新拓扑，Layout 会拒绝把旧窗口误写进新工作区。
            // 子实例随后按稳定后的单屏/多屏工作区做身份映射和坐标自适应。
            Layout.SaveImmediately();
            WidgetLink.Send(force: true);

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
            {
                Program.Log($"topology restart aborted (no process path): {reason}");
                _restarting = false;
                return;
            }
            Program.Log($"topology restart requested: {reason}");
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, Arguments = "--restart-child" });
            app!.Shutdown();
        }
        catch (Exception ex)
        {
            Program.Log("topology restart FAIL: " + ex.Message);
            _restarting = false;
        }
    }
}
