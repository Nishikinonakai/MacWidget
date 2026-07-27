using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace WidgetProto;

/// <summary>Small, privacy-preserving place search used only after the user types a query.</summary>
internal static class WeatherSearch
{
    static readonly HttpClient Http = MakeClient();

    static HttpClient MakeClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MacWidget/0.2 (+https://github.com/Nishikinonakai/MacWidget)");
        return client;
    }

    public static async Task<object[]> FindAsync(string query, string language)
    {
        query = query.Trim();
        if (query.Length is < 2 or > 120) return [];
        language = language == "en" ? "en" : "zh";
        var url = $"https://geocoding-api.open-meteo.com/v1/search?count=8&language={language}&format=json&name=" +
                  Uri.EscapeDataString(query);
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("results", out var results)) return [];
        return results.EnumerateArray().Select(r => (object)new
        {
            name = r.GetProperty("name").GetString() ?? "",
            admin = r.TryGetProperty("admin1", out var a) ? a.GetString() : null,
            country = r.TryGetProperty("country", out var c) ? c.GetString() : null,
            lat = Math.Round(r.GetProperty("latitude").GetDouble(), 4),
            lon = Math.Round(r.GetProperty("longitude").GetDouble(), 4),
            timezone = r.TryGetProperty("timezone", out var tz) ? tz.GetString() : "UTC",
        }).ToArray();
    }
}
