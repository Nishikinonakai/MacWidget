using System.Text.Json;
using System.Windows;
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

Console.WriteLine("Layout scenarios passed: v3 latest, same-resolution replacement, compaction, no resurrection.");
