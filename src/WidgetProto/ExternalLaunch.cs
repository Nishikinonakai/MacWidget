using System.Diagnostics;

namespace WidgetProto;

/// <summary>Only user-initiated HTTP(S) destinations may leave the app.</summary>
internal static class ExternalLaunch
{
    public static bool TryNormalizeHttpUri(string? value, out Uri uri)
    {
        uri = null!;
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
        if (string.IsNullOrWhiteSpace(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo)) return false;
        uri = parsed;
        return true;
    }

    public static bool OpenHttp(string? value, string source)
    {
        if (!TryNormalizeHttpUri(value, out var uri))
        {
            Program.Log($"external URL rejected ({source})");
            return false;
        }
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            Program.Log($"external URL opened ({source}): {uri.Host}");
            return true;
        }
        catch (Exception ex)
        {
            Program.Log($"external URL open failed ({source}): {ex.Message}");
            return false;
        }
    }
}
