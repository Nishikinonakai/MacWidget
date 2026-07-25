using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace WidgetProto;

/// <summary>
/// MacDesk 联动客户端：把全体组件的占用矩形（屏幕物理 px）推给 MacDesk 的
/// 图标避让管道（MacDesk.WidgetAvoid.v1）。MacDesk 不在场时静默哑火，进程退出
/// （管道断开）= MacDesk 侧自动清空 = 图标回位。
/// </summary>
public static class WidgetLink
{
    static NamedPipeClientStream? _pipe;
    static StreamWriter? _writer;
    static DateTime _lastSend = DateTime.MinValue;
    static string? _pending;
    static bool _pumping;
    static readonly object _lk = new();

    /// <summary>UI 线程调用。默认 ~30Hz 节流；force 用于落定/关闭等必达时刻。</summary>
    public static void Send(bool force = false)
    {
        if (!force && (DateTime.Now - _lastSend).TotalMilliseconds < 33) return;
        _lastSend = DateTime.Now;

        var rects = new List<double[]>();
        foreach (Window w in Application.Current.Windows)
        {
            if (w is not WidgetWindow ww || !ww.IsVisible) continue;
            // GetWindowRect 已是虚拟桌面物理 px。不能再用 Left/Top × 单一 DPI：
            // 副屏在左/上或混合缩放时，那个换算会把矩形送到错误的 MacDesk 屏幕。
            var r = ww.PhysicalBounds;
            if (!r.IsEmpty) rects.Add(new[] { r.Left, r.Top, r.Width, r.Height });
        }
        var line = JsonSerializer.Serialize(new { rects });

        lock (_lk)
        {
            _pending = line;                       // 只保留最新一帧，堆积无意义
            if (_pumping) return;
            _pumping = true;
        }
        System.Threading.Tasks.Task.Run(Pump);
    }

    static void Pump()
    {
        while (true)
        {
            string line;
            lock (_lk)
            {
                if (_pending == null) { _pumping = false; return; }
                line = _pending;
                _pending = null;
            }
            try
            {
                if (_writer == null)
                {
                    var p = new NamedPipeClientStream(".", "MacDesk.WidgetAvoid.v1", PipeDirection.Out);
                    p.Connect(300);
                    _pipe = p;
                    _writer = new StreamWriter(p) { AutoFlush = true };
                    Program.Log("widgetlink connected to MacDesk");
                }
                _writer.WriteLine(line);
            }
            catch
            {
                try { _writer?.Dispose(); _pipe?.Dispose(); } catch { }
                _writer = null; _pipe = null;
                lock (_lk) { _pending = null; _pumping = false; }   // MacDesk 不在场：放弃本轮
                return;
            }
        }
    }
}
