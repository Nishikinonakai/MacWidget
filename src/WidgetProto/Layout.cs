using System.IO;
using System.Text.Json;
using System.Windows;

namespace WidgetProto;

/// <summary>组件目录：kind → 帧尺寸（Small 180 / Medium 360×180 / Large 360×360）与默认演示组。</summary>
public static class WidgetRegistry
{
    public static readonly string[] Kinds = { "clock", "calendar", "monitor", "weather", "photo" };

    public static (double W, double H) Size(string kind) => kind switch
    {
        "weather" => (Placement.Unit * 2, Placement.Unit),      // Medium 2×1
        "photo" => (Placement.Unit * 2, Placement.Unit * 2),    // Large 2×2
        _ => (Placement.Unit, Placement.Unit),                  // Small
    };
}

/// <summary>
/// 布局持久化：按"工作区 DIU 尺寸"分档（对齐 macOS DesktopWidgetPlacementStorage 按显示器×分辨率分档
/// 的语义——同机 1080p@100% 与 4K@300% 的 DIU 不同，各存各的档）。widgets.json 在 exe 旁。
/// 实验模式（--n/--widget）不读不写，保护机主的正式摆位。
/// </summary>
public static class Layout
{
    public sealed record Entry(string Kind, double X, double Y);

    static string PathOf => System.IO.Path.Combine(Program.BaseDir, "widgets.json");
    static string Key()
    {
        var wa = SystemParameters.WorkArea;
        return $"{wa.Width:F0}x{wa.Height:F0}";
    }

    public static List<Entry> LoadOrDefault()
    {
        try
        {
            if (File.Exists(PathOf))
            {
                var doc = JsonSerializer.Deserialize<Dictionary<string, List<Entry>>>(File.ReadAllText(PathOf));
                if (doc != null && doc.TryGetValue(Key(), out var list) && list.Count > 0)
                {
                    Program.Log($"layout loaded: {list.Count} widgets @ {Key()}");
                    return list;
                }
            }
        }
        catch (Exception ex) { Program.Log("layout load FAIL (falling back to default): " + ex.Message); }

        // 默认演示组：右上角 时钟+日历 并组，天气 Medium 垫底 —— 正好展示帧贴合的 16 视觉缝
        var wa = SystemParameters.WorkArea;
        double u = Placement.Unit;
        double bx = wa.Right - Placement.EdgeMargin - u * 2, by = wa.Top + Placement.EdgeMargin;
        Program.Log($"layout default seeded @ {Key()}");
        return new List<Entry>
        {
            new("clock", bx, by),
            new("calendar", bx + u, by),
            new("weather", bx, by + u),
        };
    }

    static System.Windows.Threading.DispatcherTimer? _debounce;

    /// <summary>UI 线程调用；500ms 防抖合并连环落定。</summary>
    public static void Save()
    {
        if (Program.Opts.LabMode) return;
        _debounce ??= MakeTimer();
        _debounce.Stop(); _debounce.Start();
    }

    static System.Windows.Threading.DispatcherTimer MakeTimer()
    {
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        t.Tick += (_, _) => { t.Stop(); SaveNow(); };
        return t;
    }

    static void SaveNow()
    {
        try
        {
            Dictionary<string, List<Entry>> doc = new();
            if (File.Exists(PathOf))
                doc = JsonSerializer.Deserialize<Dictionary<string, List<Entry>>>(File.ReadAllText(PathOf)) ?? new();
            var list = new List<Entry>();
            foreach (Window w in Application.Current.Windows)
                if (w is WidgetWindow ww && ww.IsVisible)
                    list.Add(new Entry(ww.Kind, ww.Left, ww.Top));
            doc[Key()] = list;
            File.WriteAllText(PathOf, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            Program.Log($"layout saved: {list.Count} widgets @ {Key()}");
        }
        catch (Exception ex) { Program.Log("layout save FAIL: " + ex.Message); }
    }
}
