using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;

namespace WidgetProto;

internal readonly record struct KeepAwakeRequest(DateTimeOffset? EndUtc, bool KeepDisplayOn);

/// <summary>
/// Aggregates every Keep Awake widget onto the WPF UI thread. Windows execution
/// state is thread-scoped, so one widget must never clear another widget's request.
/// </summary>
internal static class KeepAwakeManager
{
    const uint EsSystemRequired = 0x00000001;
    const uint EsDisplayRequired = 0x00000002;
    const uint EsContinuous = 0x80000000;

    static readonly Dictionary<WidgetWindow, KeepAwakeRequest> Active = new();
    static DispatcherTimer? _timer;
    static uint _appliedFlags;

    public static void Restore(WidgetWindow owner, JsonElement? cfg)
    {
        if (!TryRead(cfg, out var request)) { Remove(owner); return; }
        // An infinite request deliberately means "until this MacWidget process
        // exits". Timed requests may resume after a restart using their UTC end.
        if (request.EndUtc == null) { Remove(owner); owner.MarkKeepAwakeExpired(); return; }
        Update(owner, request);
    }

    public static void Update(WidgetWindow owner, JsonElement? cfg)
    {
        if (!TryRead(cfg, out var request)) { Remove(owner); return; }
        Update(owner, request);
    }

    static void Update(WidgetWindow owner, KeepAwakeRequest request)
    {
        if (request.EndUtc is { } end && end <= DateTimeOffset.UtcNow)
        {
            Active.Remove(owner);
            Apply();
            owner.MarkKeepAwakeExpired();
            return;
        }
        Active[owner] = request;
        EnsureTimer();
        Apply();
    }

    public static void Remove(WidgetWindow owner)
    {
        if (!Active.Remove(owner)) return;
        Apply();
        if (Active.Count == 0) { _timer?.Stop(); _timer = null; }
    }

    internal static bool TryRead(JsonElement? cfg, out KeepAwakeRequest request)
    {
        request = default;
        if (cfg is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("active", out var active) || active.ValueKind != JsonValueKind.True) return false;

        DateTimeOffset? end = null;
        if (value.TryGetProperty("endUtc", out var endNode) && endNode.ValueKind == JsonValueKind.String)
        {
            if (!DateTimeOffset.TryParse(endNode.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)) return false;
            end = parsed.ToUniversalTime();
        }
        bool display = value.TryGetProperty("keepDisplay", out var displayNode) &&
                       displayNode.ValueKind == JsonValueKind.True;
        request = new(end, display);
        return true;
    }

    static void EnsureTimer()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => ExpireDueRequests();
        _timer.Start();
    }

    static void ExpireDueRequests()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = Active.Where(pair => pair.Value.EndUtc is { } end && end <= now)
                            .Select(pair => pair.Key).ToArray();
        if (expired.Length == 0) return;
        foreach (var owner in expired) Active.Remove(owner);
        Apply();
        foreach (var owner in expired) owner.MarkKeepAwakeExpired();
        if (Active.Count == 0) { _timer?.Stop(); _timer = null; }
    }

    static void Apply()
    {
        uint flags = EsContinuous;
        if (Active.Count > 0) flags |= EsSystemRequired;
        if (Active.Values.Any(request => request.KeepDisplayOn)) flags |= EsDisplayRequired;
        if (flags == _appliedFlags) return;
        _appliedFlags = flags;
        if (SetThreadExecutionState(flags) == 0)
            Program.Log($"keep awake API failed flags=0x{flags:x8}");
        else
            Program.Log($"keep awake state: active={Active.Count} display={((flags & EsDisplayRequired) != 0)}");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint SetThreadExecutionState(uint esFlags);
}
