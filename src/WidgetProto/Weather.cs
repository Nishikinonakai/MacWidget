using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace WidgetProto;

/// <summary>
/// 天气数据源（参数化 topic "weather@lat,lon"）：MET Norway Locationforecast 2.0 compact。
/// 许可已核实（api.met.no/doc/TermsOfService）：CC-BY 4.0、**商用允许**，要求 = 署名（组件角落）
/// + 识别性 User-Agent + 坐标≤4 位小数 + 礼貌频率（此处 15min/城市 + 订阅者门控，远低于红线）。
/// 失败走 DataHub error 信封（最后好数据 stale 保底）——联网源就是这套契约当初的设计目标。
/// BYO-key 预留：cfg 可带 {source,key}，将来在此按 source 分派（和风天气等），MET 恒为免 key 默认。
/// </summary>
public sealed class WeatherProvider : IParamProvider
{
    public string Prefix => "weather";
    public TimeSpan Interval => TimeSpan.FromMinutes(15);

    static readonly HttpClient _http = MakeClient();

    static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        // MET 要求可识别 UA（应用名 + 联系途径）
        h.DefaultRequestHeaders.UserAgent.ParseAdd("MacWidget/0.2");
        h.DefaultRequestHeaders.UserAgent.ParseAdd("(+https://github.com/Nishikinonakai/MacDesk)");
        return h;
    }

    public object Fetch(string param)
    {
        var parts = param.Split(',');
        double lat = Math.Round(double.Parse(parts[0], CultureInfo.InvariantCulture), 4);
        double lon = Math.Round(double.Parse(parts[1], CultureInfo.InvariantCulture), 4);
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.met.no/weatherapi/locationforecast/2.0/compact?lat={lat}&lon={lon}");

        using var resp = _http.GetAsync(url).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        var series = doc.RootElement.GetProperty("properties").GetProperty("timeseries");

        double now = double.NaN, hi = double.MinValue, lo = double.MaxValue;
        string sym = "";
        var hours = new List<object>();
        int n = series.GetArrayLength();
        for (int i = 0; i < n; i++)
        {
            var e = series[i];
            double t = e.GetProperty("data").GetProperty("instant").GetProperty("details")
                        .GetProperty("air_temperature").GetDouble();
            if (i == 0) { now = t; sym = SymbolOf(e); }
            if (i < 24) { hi = Math.Max(hi, t); lo = Math.Min(lo, t); }   // 今日高低 ≈ 未来 24h 包络
            if (i >= 1 && hours.Count < 6)                               // 前 48h 为逐小时序列
                hours.Add(new { time = e.GetProperty("time").GetString(), temp = Math.Round(t), sym = SymbolOf(e) });
            if (i >= 24 && hours.Count >= 6) break;
        }
        return new
        {
            temp = Math.Round(now), sym,
            hi = Math.Round(hi), lo = Math.Round(lo),
            hours, src = "MET Norway",
        };
    }

    static string SymbolOf(JsonElement e)
    {
        var d = e.GetProperty("data");
        foreach (var k in new[] { "next_1_hours", "next_6_hours", "next_12_hours" })
            if (d.TryGetProperty(k, out var nx) && nx.TryGetProperty("summary", out var su))
                return su.GetProperty("symbol_code").GetString() ?? "";
        return "";
    }
}
