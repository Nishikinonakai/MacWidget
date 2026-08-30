using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace WidgetProto;

public enum UpdateCheckStatus { Current, UpdateAvailable, Unavailable }

public sealed record UpdateCheckResult(UpdateCheckStatus Status, string CurrentVersion,
    string? LatestVersion = null, string? ReleaseUrl = null, string? Error = null);

/// <summary>Manual, read-only update check against the latest public GitHub Release.</summary>
public static class AppUpdate
{
    const string ApiUrl = "https://api.github.com/repos/Nishikinonakai/MacWidget/releases/latest";
    const string ReleasesUrl = "https://github.com/Nishikinonakai/MacWidget/releases";
    static readonly HttpClient Http = MakeClient();
    static int _checking;

    public static string DisplayVersion => CurrentVersion().ToString(3);

    static HttpClient MakeClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MacWidget/{DisplayVersion}");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        return client;
    }

    public static async Task CheckFromTrayAsync()
    {
        if (Interlocked.Exchange(ref _checking, 1) != 0)
        {
            Tray.ShowUpdateResult(new(UpdateCheckStatus.Unavailable, DisplayVersion,
                Error: Ui.T("正在检查，请稍候。", "An update check is already running.")));
            return;
        }
        try
        {
            Program.Log("product update check started");
            var result = await CheckAsync(CurrentVersion());
            Program.Log($"product update check: {result.Status} current={result.CurrentVersion} latest={result.LatestVersion ?? "n/a"}");
            Tray.ShowUpdateResult(result);
        }
        finally { Interlocked.Exchange(ref _checking, 0); }
    }

    internal static async Task<UpdateCheckResult> CheckAsync(Version current)
    {
        try
        {
            using var response = await Http.GetAsync(ApiUrl);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new(UpdateCheckStatus.Unavailable, current.ToString(3), Error:
                    Ui.T("暂时没有公开版本。", "No public release is available yet."));
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            string tag = root.TryGetProperty("tag_name", out var tagNode) ? tagNode.GetString() ?? "" : "";
            string url = root.TryGetProperty("html_url", out var urlNode) ? urlNode.GetString() ?? "" : "";
            if (!TryParseReleaseVersion(tag, out var latest) || !IsTrustedReleaseUrl(url))
                return new(UpdateCheckStatus.Unavailable, current.ToString(3), Error:
                    Ui.T("版本信息格式无法识别。", "The release metadata was not recognized."));
            return latest > current
                ? new(UpdateCheckStatus.UpdateAvailable, current.ToString(3), latest.ToString(3), url)
                : new(UpdateCheckStatus.Current, current.ToString(3), latest.ToString(3), ReleasesUrl);
        }
        catch (Exception ex)
        {
            Program.Log("product update check failed: " + ex.Message);
            return new(UpdateCheckStatus.Unavailable, current.ToString(3), Error:
                Ui.T("无法连接 GitHub，请稍后重试。", "Could not reach GitHub. Try again later."));
        }
    }

    public static bool TryParseReleaseVersion(string? tag, out Version version)
    {
        version = null!;
        tag = tag?.Trim();
        if (string.IsNullOrEmpty(tag)) return false;
        if (tag[0] is 'v' or 'V') tag = tag[1..];
        if (!Version.TryParse(tag, out var parsed) || parsed.Major < 0 || parsed.Minor < 0) return false;
        version = Normalize(parsed);
        return true;
    }

    public static bool IsNewerRelease(string? tag, Version current)
        => TryParseReleaseVersion(tag, out var latest) && latest > Normalize(current);

    static bool IsTrustedReleaseUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
           uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
           uri.AbsolutePath.StartsWith("/Nishikinonakai/MacWidget/releases/", StringComparison.OrdinalIgnoreCase);

    static Version CurrentVersion()
        => Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    static Version Normalize(Version version)
        => new(Math.Max(0, version.Major), Math.Max(0, version.Minor), Math.Max(0, version.Build));
}
