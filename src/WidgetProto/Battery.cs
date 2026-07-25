using System.IO;
using System.Text.Json;

namespace WidgetProto;

/// <summary>
/// 电池数据源（topic "battery"）：GetSystemPowerStatus 快照；台机无电池 = hasBattery:false
/// （真实 n/a 路径，组件画"无电池"占位）。
/// 模拟座（无电池机器测 UI 全态，机主拍板的测法）：exe 旁 simbatt.json 存在即接管——
///   {"pct":67,"ac":false,"charging":false,"remainMin":143}
/// 运行期可改可删（2s tick 现读），删除立即回真实路径；文件损坏忽略并走真实值。
/// </summary>
public sealed class BatteryProvider : IDataProvider
{
    public string Topic => "battery";
    public TimeSpan Interval => TimeSpan.FromMilliseconds(2000);

    bool _simLogged;

    public object Fetch()
    {
        var simPath = Path.Combine(Program.BaseDir, "simbatt.json");
        if (File.Exists(simPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(simPath));
                var r = doc.RootElement;
                if (!_simLogged) { _simLogged = true; Program.Log("battery: simbatt.json active (simulated)"); }
                double pct = r.TryGetProperty("pct", out var p) ? p.GetDouble() / 100.0 : 1.0;
                bool ac = r.TryGetProperty("ac", out var a) && a.GetBoolean();
                bool charging = r.TryGetProperty("charging", out var c) && c.GetBoolean();
                int? remain = r.TryGetProperty("remainMin", out var m) ? m.GetInt32() : null;
                return Snap(true, Math.Clamp(pct, 0, 1), ac, charging, remain);
            }
            catch (Exception ex) { Program.Log("battery: simbatt.json bad, using real: " + ex.Message); }
        }
        else _simLogged = false;

        if (!Native.GetSystemPowerStatus(out var s)) return Snap(false, null, true, false, null);
        bool has = s.BatteryFlag != 255 && (s.BatteryFlag & 128) == 0;   // 128 = NO_SYSTEM_BATTERY
        if (!has) return Snap(false, null, s.ACLineStatus == 1, false, null);
        return Snap(true,
            s.BatteryLifePercent <= 100 ? s.BatteryLifePercent / 100.0 : null,
            s.ACLineStatus == 1,
            (s.BatteryFlag & 8) != 0,
            s.BatteryLifeTime >= 0 ? s.BatteryLifeTime / 60 : null);
    }

    static object Snap(bool hasBattery, double? pct, bool ac, bool charging, int? remainMin)
        => new { hasBattery, pct, ac, charging, remainMin };
}
