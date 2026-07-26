using System.IO;
using System.Text.Json;
using System.Windows;

namespace WidgetProto;

/// <summary>组件目录：kind → 支持的尺寸档与帧尺寸（s 180 / m 360×180 / l 360×360）。</summary>
public static class WidgetRegistry
{
    public static readonly string[] Kinds = { "clock", "calendar", "monitor", "weather", "photo", "music", "battery" };

    /// <summary>kind 支持的尺寸档（macOS 语义：组件自报支持档）。</summary>
    public static string[] SizesOf(string kind) => kind switch
    {
        "clock"    => new[] { "s", "m" },   // m = 世界时钟四表盘
        "calendar" => new[] { "s", "m" },   // m = 月历网格
        "monitor"  => new[] { "s", "m" },
        "music"    => new[] { "s", "m" },
        "weather"  => new[] { "m" },
        "photo"    => new[] { "l" },
        _          => new[] { "s" },
    };

    /// <summary>有"编辑小组件"配置脸的 kind（菜单据此显示入口）。</summary>
    public static bool Configurable(string kind) => kind is "photo" or "weather";

    public static string DefaultSize(string kind) => kind switch
    {
        "weather" => "m",
        "music"   => "m",
        "photo"   => "l",
        _         => "s",
    };

    public static (double W, double H) Size(string kind, string size) => size switch
    {
        "m" => (Placement.Unit * 2, Placement.Unit),
        "l" => (Placement.Unit * 2, Placement.Unit * 2),
        _   => (Placement.Unit, Placement.Unit),
    };
}

/// <summary>
/// 布局持久化：按"稳定显示器标识 × 物理分辨率"分档（对齐 macOS DesktopWidgetPlacementStorage
/// 的语义）。坐标是相对该显示器左上的物理 px，屏幕在虚拟桌面中换边/混合 DPI 时仍可复原。
/// widgets.json 在 %LOCALAPPDATA%\MacWidget，避免应用升级覆盖用户布局；首次产品化启动会从旧 exe 旁迁移。
/// 实验模式（--n/--widget）不读不写，保护机主的正式摆位。
/// </summary>
public static class Layout
{
    /// <summary>Size 为 null = 老档案（尺寸档之前）；Display 为 null = v1 主屏 DIU 坐标，载入时迁移。</summary>
    public sealed record Entry(string Kind, double X, double Y, string? Size = null, JsonElement? Cfg = null,
                               string? Display = null);

    static string PathOf => System.IO.Path.Combine(Program.DataDir, "widgets.json");
    static string LegacyPath => System.IO.Path.Combine(Program.BaseDir, "widgets.json");
    // v1 的 bucket 是主屏工作区 DIU；只用于逐桶无损迁移。
    static string LegacyKey() => $"{SystemParameters.WorkArea.Width:F0}x{SystemParameters.WorkArea.Height:F0}";

    public static List<Entry> LoadOrDefault()
    {
        try
        {
            if (!File.Exists(PathOf) && File.Exists(LegacyPath))
            {
                File.Copy(LegacyPath, PathOf);
                Program.Log("layout migrated from app directory");
            }
            if (File.Exists(PathOf))
            {
                var doc = Read();
                bool changed = false;
                var list = new List<Entry>();
                foreach (var screen in DisplayTopology.GetAll())
                {
                    changed |= MigrateAmbiguousDuplicateBucket(doc, screen);
                    changed |= MigrateLegacyPrimaryBucket(doc, screen);
                    if (doc.TryGetValue(screen.LayoutKey, out var bucket)) list.AddRange(bucket);
                }
                if (changed) Write(doc);
                if (list.Count > 0)
                {
                    Program.Log($"layout loaded: {list.Count} widgets across {DisplayTopology.GetAll().Count} display(s)");
                    return list;
                }
            }
        }
        catch (Exception ex) { Program.Log("layout load FAIL (falling back to default): " + ex.Message); }

        // 默认演示组：主屏右上角 时钟+日历 并组，天气 Medium 垫底。
        var display = DisplayTopology.Primary();
        double u = Placement.Unit * display.Scale, edge = Placement.EdgeMargin * display.Scale;
        double bx = display.Work.Right - display.Physical.Left - edge - u * 2;
        double by = display.Work.Top - display.Physical.Top + edge;
        Program.Log($"layout default seeded @ {display.LayoutKey}");
        return new List<Entry>
        {
            new("clock", bx, by, Display: display.Key),
            new("calendar", bx + u, by, Display: display.Key),
            new("weather", bx, by + u, Display: display.Key),
        };
    }

    /// <summary>把持久化的相对物理坐标展开成虚拟桌面物理坐标。</summary>
    public static DisplayTopology.Position PositionOf(Entry entry)
    {
        var display = DisplayTopology.ByKey(entry.Display);
        if (entry.Display == null) // 只会发生在异常/旧文件直接调用时，保持可恢复。
            return new(display.Key, entry.X * display.Scale, entry.Y * display.Scale);
        return new(display.Key, entry.X, entry.Y);
    }

    static System.Windows.Threading.DispatcherTimer? _debounce;

    /// <summary>UI 线程调用；500ms 防抖合并连环落定。</summary>
    public static void Save()
    {
        if (Program.Opts.LabMode) return;
        _debounce ??= MakeTimer();
        _debounce.Stop(); _debounce.Start();
    }

    /// <summary>显示拓扑交接/退出前必须同步落盘，不能等 500ms 防抖计时器。</summary>
    public static void SaveImmediately()
    {
        if (Program.Opts.LabMode) return;
        _debounce?.Stop();
        SaveNow();
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
            var doc = Read();
            var displays = DisplayTopology.GetAll();
            foreach (var display in displays) MigrateLegacyPrimaryBucket(doc, display);
            var buckets = displays.ToDictionary(d => d.LayoutKey, _ => new List<Entry>());
            foreach (Window w in Application.Current.Windows)
                if (w is WidgetWindow ww && ww.IsVisible)
                {
                    var rect = ww.PhysicalBounds;
                    if (rect.IsEmpty) continue;
                    var display = DisplayTopology.ForRect(rect);
                    buckets[display.LayoutKey].Add(new Entry(ww.Kind,
                        rect.Left - display.Physical.Left, rect.Top - display.Physical.Top,
                        ww.SizeClass, ww.Cfg, display.Key));
                }
            // 已断开的显示器桶不触碰；当前屏空桶要写回，才会正确记住"此屏已清空"。
            foreach (var (key, bucket) in buckets) doc[key] = bucket;
            Write(doc);
            Program.Log($"layout saved: {buckets.Sum(b => b.Value.Count)} widgets across {buckets.Count} display(s)");
        }
        catch (Exception ex) { Program.Log("layout save FAIL: " + ex.Message); }
    }

    static Dictionary<string, List<Entry>> Read()
        => File.Exists(PathOf)
            ? JsonSerializer.Deserialize<Dictionary<string, List<Entry>>>(File.ReadAllText(PathOf)) ?? new()
            : new();

    static void Write(Dictionary<string, List<Entry>> doc)
    {
        Directory.CreateDirectory(Program.DataDir);
        File.WriteAllText(PathOf, JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        }));
    }

    /// <summary>
    /// v2.0 以前，相同 EDID 的多路连接以 `SKG5500` / `SKG5500#2` 区分，后缀来自枚举顺序。
    /// 新 key 用 `@DISPLAYn` 锁定 Windows 的连接路径。迁移时复制而不移除旧桶：断开第二屏回到
    /// 单屏后，旧版本和当前版本仍都能找回原来的摆放。
    /// </summary>
    static bool MigrateAmbiguousDuplicateBucket(Dictionary<string, List<Entry>> doc, DisplayTopology.Display display)
    {
        int marker = display.Key.LastIndexOf("@DISPLAY", StringComparison.OrdinalIgnoreCase);
        if (marker <= 0 || doc.ContainsKey(display.LayoutKey)) return false;

        string baseKey = display.Key[..marker];
        string device = display.Key[(marker + 1)..]; // DISPLAY1 / DISPLAY2
        int ordinal = 1;
        if (device.Length > "DISPLAY".Length)
            _ = int.TryParse(device["DISPLAY".Length..], out ordinal);
        if (ordinal < 1) ordinal = 1;

        string oldKey = ordinal == 1 ? baseKey : $"{baseKey}#{ordinal}";
        string oldLayoutKey = $"v2:{oldKey}:{display.Physical.Width:F0}x{display.Physical.Height:F0}";
        if (!doc.TryGetValue(oldLayoutKey, out var oldBucket)) return false;

        doc[display.LayoutKey] = oldBucket.Select(entry => entry with { Display = display.Key }).ToList();
        Program.Log($"layout migrated: duplicate {oldLayoutKey} -> {display.LayoutKey}");
        return true;
    }

    static bool MigrateLegacyPrimaryBucket(Dictionary<string, List<Entry>> doc, DisplayTopology.Display display)
    {
        if (!display.IsPrimary || doc.ContainsKey(display.LayoutKey)) return false;
        if (!doc.TryGetValue(LegacyKey(), out var legacy)) return false;
        doc[display.LayoutKey] = legacy.Select(entry => entry with
        {
            X = entry.X * display.Scale,
            Y = entry.Y * display.Scale,
            Display = display.Key,
        }).ToList();
        doc.Remove(LegacyKey());
        Program.Log($"layout migrated: v1 {LegacyKey()} -> {display.LayoutKey}");
        return true;
    }
}
