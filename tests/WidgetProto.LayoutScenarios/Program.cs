using System.Text.Json;
using System.Windows;
using QRCoder;
using WidgetProto;

static DisplayTopology.Display Display(string key, int handle, double width, double height,
    bool primary = true, double left = 0, double top = 0, uint dpi = 96)
    => new(key, new IntPtr(handle), new Rect(left, top, width, height),
        new Rect(left, top, width, height - 40), dpi, primary, $@"\\.\DISPLAY{handle}");

static Dictionary<string, object?> Saved(DisplayTopology.Display display) => new()
{
    ["Key"] = display.Key,
    ["IsPrimary"] = display.IsPrimary,
    ["PhysicalX"] = display.Physical.Left,
    ["PhysicalY"] = display.Physical.Top,
    ["PhysicalWidth"] = display.Physical.Width,
    ["PhysicalHeight"] = display.Physical.Height,
    ["WorkX"] = display.Work.Left - display.Physical.Left,
    ["WorkY"] = display.Work.Top - display.Physical.Top,
    ["WorkWidth"] = display.Work.Width,
    ["WorkHeight"] = display.Work.Height,
    ["Dpi"] = display.Dpi,
};

static Dictionary<string, object?> Profile(IEnumerable<DisplayTopology.Display> displays,
    IEnumerable<Layout.Entry> widgets, DateTime updated) => new()
{
    ["UpdatedUtc"] = updated,
    ["Displays"] = displays.Select(Saved).ToList(),
    ["Widgets"] = widgets.ToList(),
};

static string V4(IEnumerable<DisplayTopology.Display> displays, IEnumerable<Layout.Entry> widgets)
    => JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["Version"] = 4,
        ["Current"] = Profile(displays, widgets, DateTime.UtcNow),
    });

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static bool Overlaps(Rect left, Rect right)
    => left.Left < right.Right && left.Right > right.Left &&
       left.Top < right.Bottom && left.Bottom > right.Top;

var source = Display("OLD", 1, 1920, 1080);
var replacement = Display("NEW", 2, 1920, 1080);
var sameResolution = AdaptiveLayout.AdaptSerialized(V4(new[] { source }, new[]
{
    new Layout.Entry("clock", 120, 90, "s", Display: source.Key),
}), new[] { replacement })!;
Require(sameResolution.Count == 1, "same-resolution migration lost a widget");
Require(sameResolution[0].Display == replacement.Key && sameResolution[0].X == 120 && sameResolution[0].Y == 90,
    "same-resolution replacement did not preserve relative coordinates");

var now = DateTime.UtcNow;
string v3 = JsonSerializer.Serialize(new Dictionary<string, object?>
{
    ["Version"] = 3,
    ["Single"] = Profile(new[] { source }, new[]
    {
        new Layout.Entry("clock", 10, 10, "s", Display: source.Key),
    }, now.AddMinutes(-5)),
    ["Multi"] = Profile(new[] { source }, new[]
    {
        new Layout.Entry("calendar", 20, 20, "s", Display: source.Key),
    }, now),
});
var upgraded = AdaptiveLayout.AdaptSerialized(v3, new[] { source })!;
Require(upgraded.Count == 1 && upgraded[0].Kind == "calendar", "v3 upgrade did not choose the newest profile");

var secondary = Display("SECONDARY", 2, 1920, 1080, primary: false, left: 1920);
var compactTarget = Display("OLD", 1, 1280, 720);
var folded = AdaptiveLayout.AdaptSerialized(V4(new[] { source, secondary }, new[]
{
    new Layout.Entry("photo", 1544, 16, "l", Display: source.Key),
    new Layout.Entry("weather", 1544, 16, "m", Display: secondary.Key),
    new Layout.Entry("clock", 1724, 196, "s", Display: secondary.Key),
}), new[] { compactTarget })!;
Require(folded.Count == 3 && folded.All(entry => entry.Display == compactTarget.Key),
    "removed-display widgets were not folded onto the remaining display");

var occupied = new List<Rect>();
var safe = new Rect(16, 16, compactTarget.Work.Width - 32, compactTarget.Work.Height - 32);
foreach (var entry in folded)
{
    var size = WidgetRegistry.Size(entry.Kind, entry.Size ?? WidgetRegistry.DefaultSize(entry.Kind));
    var rect = new Rect(entry.X, entry.Y, size.W * compactTarget.Scale, size.H * compactTarget.Scale);
    Require(rect.Left >= safe.Left && rect.Top >= safe.Top && rect.Right <= safe.Right && rect.Bottom <= safe.Bottom,
        $"{entry.Kind} remained outside the compact work area");
    Require(!occupied.Any(other => Overlaps(rect, other)), $"{entry.Kind} still overlaps after compaction");
    occupied.Add(rect);
}

var reattached = AdaptiveLayout.AdaptSerialized(V4(new[] { compactTarget }, folded),
    new[] { compactTarget, secondary })!;
Require(reattached.All(entry => entry.Display == compactTarget.Key),
    "reattaching a display resurrected an obsolete multi-display placement");

var automaticWork = new Rect(0, 0, 1280, 680);
var automaticOccupied = new List<Rect>
{
    new(16, 16, 180, 180),
    new(196, 16, 360, 180),
};
var automatic = Placement.FindAutomaticPosition(new Size(360, 360), automaticOccupied,
    automaticWork, Placement.Unit, Placement.EdgeMargin);
var automaticRect = new Rect(automatic, new Size(360, 360));
Require(automaticRect.Left >= 16 && automaticRect.Top >= 16 &&
        automaticRect.Right <= automaticWork.Right - 16 && automaticRect.Bottom <= automaticWork.Bottom - 16,
    "automatic placement put a widget outside the safe work area");
Require(!automaticOccupied.Any(other => Overlaps(automaticRect, other)),
    "automatic placement overlapped an existing widget despite available space");

var crowded = Placement.FindAutomaticPosition(new Size(360, 360),
    new[] { new Rect(16, 16, 900, 600) }, automaticWork, Placement.Unit, Placement.EdgeMargin);
var crowdedRect = new Rect(crowded, new Size(360, 360));
Require(crowdedRect.Left >= 16 && crowdedRect.Top >= 16 &&
        crowdedRect.Right <= automaticWork.Right - 16 && crowdedRect.Bottom <= automaticWork.Bottom - 16,
    "crowded automatic placement failed to keep the widget visible");

Require(AppUpdate.IsNewerRelease("v0.4.1", new Version(0, 4, 0)),
    "update comparison missed a newer tagged release");
Require(!AppUpdate.IsNewerRelease("v0.4.0", new Version(0, 4, 0)),
    "update comparison treated the current release as newer");
Require(!AppUpdate.IsNewerRelease("nightly", new Version(0, 4, 0)),
    "update comparison accepted a non-version tag");
Require(ExternalLaunch.TryNormalizeHttpUri("example.com/docs", out var normalizedLink) &&
        normalizedLink.AbsoluteUri == "https://example.com/docs",
    "quick-link URL normalization failed");
Require(!ExternalLaunch.TryNormalizeHttpUri("javascript:alert(1)", out _),
    "quick-link validation accepted a non-HTTP scheme");
Require(WidgetRegistry.Kinds.Contains("calculator") && WidgetRegistry.Kinds.Contains("links") &&
        WidgetRegistry.Configurable("links"),
    "original utility widgets are missing from the registry");
Require(WidgetRegistry.SizesOf("calculator").SequenceEqual(new[] { "l" }) &&
        WidgetRegistry.DefaultSize("calculator") == "l" &&
        WidgetRegistry.Size("calculator", "l") == (360d, 360d),
    "calculator must remain a Large-only widget");
Require(WidgetRegistry.Kinds.Contains("timer") && WidgetRegistry.Kinds.Contains("note") &&
        WidgetRegistry.SizesOf("timer").SequenceEqual(new[] { "m" }) &&
        WidgetRegistry.SizesOf("note").SequenceEqual(new[] { "l" }) &&
        WidgetRegistry.Configurable("note"),
    "local focus timer and note widgets are not registered with their intended sizes");
Require(WidgetRegistry.Kinds.Contains("awake") && WidgetRegistry.Kinds.Contains("qr") &&
        WidgetRegistry.SizesOf("awake").SequenceEqual(new[] { "m" }) &&
        WidgetRegistry.SizesOf("qr").SequenceEqual(new[] { "l" }) &&
        WidgetRegistry.Configurable("qr"),
    "keep-awake and offline QR widgets are not registered with their intended sizes");

using (var awakeDoc = JsonDocument.Parse("""{"active":true,"endUtc":"2026-08-30T12:30:00Z","keepDisplay":true}"""))
{
    Require(KeepAwakeManager.TryRead(awakeDoc.RootElement, out var awakeRequest) &&
            awakeRequest.EndUtc == DateTimeOffset.Parse("2026-08-30T12:30:00Z") && awakeRequest.KeepDisplayOn,
        "timed keep-awake configuration could not be restored");
}
using (var invalidAwakeDoc = JsonDocument.Parse("""{"active":true,"endUtc":"not-a-date"}"""))
    Require(!KeepAwakeManager.TryRead(invalidAwakeDoc.RootElement, out _),
        "keep-awake accepted an invalid end timestamp");

using (var qrData = QRCodeGenerator.GenerateQrCode("https://github.com/Nishikinonakai/MacWidget", QRCodeGenerator.ECCLevel.Q))
using (var qr = new PngByteQRCode(qrData))
{
    var png = qr.GetGraphic(2);
    Require(png.Length > 8 && png[0] == 0x89 && png[1] == 0x50 && png[2] == 0x4e && png[3] == 0x47,
        "offline QR generator did not produce a PNG image");
}

Console.WriteLine("Scenarios passed: layout migration/compaction, automatic placement, update versions, safe links, utility registry.");

if (args.Contains("--check-update-network", StringComparer.OrdinalIgnoreCase))
{
    var update = await AppUpdate.CheckAsync(new Version(0, 4, 0));
    Console.WriteLine($"GitHub update endpoint: {update.Status}; latest={update.LatestVersion ?? "n/a"}; error={update.Error ?? "none"}");
}
