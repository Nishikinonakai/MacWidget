using System.IO;
using System.Text.Json;
using System.Windows;

namespace WidgetProto;

/// <summary>
/// v4 layout semantics: one rolling workspace containing the user's latest arrangement.
/// Display identity helps map surfaces, while resolution/work-area changes only adapt and compact that arrangement.
/// A topology that reappears must never resurrect an older single/multi-display archive.
/// </summary>
internal static class AdaptiveLayout
{
    const int Version = 4;
    static string PathOf => System.IO.Path.Combine(Program.DataDir, "layout.json");

    sealed class Document
    {
        public int Version { get; set; } = AdaptiveLayout.Version;
        public Profile? Current { get; set; }
    }

    // v3 wrote two independent histories. Upgrade by choosing the one the user touched most recently.
    sealed class V3Document
    {
        public int Version { get; set; }
        public Profile? Single { get; set; }
        public Profile? Multi { get; set; }
    }

    sealed record Profile(DateTime UpdatedUtc, List<SavedDisplay> Displays, List<Layout.Entry> Widgets);

    sealed record SavedDisplay(
        string Key,
        bool IsPrimary,
        double PhysicalX,
        double PhysicalY,
        double PhysicalWidth,
        double PhysicalHeight,
        double WorkX,
        double WorkY,
        double WorkWidth,
        double WorkHeight,
        uint Dpi);

    sealed record LegacyBucket(string LayoutKey, string DisplayKey, double Width, double Height,
                               List<Layout.Entry> Widgets, int Order);

    public static string Fingerprint(IReadOnlyList<DisplayTopology.Display> displays)
        => string.Join("|", displays
            .OrderBy(display => display.Physical.Left)
            .ThenBy(display => display.Physical.Top)
            .Select(display => $"{display.Key}:{display.Physical.Left:F0},{display.Physical.Top:F0}," +
                               $"{display.Physical.Width:F0}x{display.Physical.Height:F0};" +
                               $"work={display.Work.Left:F0},{display.Work.Top:F0}," +
                               $"{display.Work.Width:F0}x{display.Work.Height:F0}@{display.Dpi}" +
                               (display.IsPrimary ? "*" : "")));

    /// <summary>Load and adapt the latest workspace, irrespective of the current display count.</summary>
    public static bool TryLoad(IReadOnlyList<DisplayTopology.Display> displays, out List<Layout.Entry> entries)
    {
        entries = new();
        if (!File.Exists(PathOf)) return false;
        try
        {
            var adapted = AdaptSerialized(File.ReadAllText(PathOf), displays);
            if (adapted == null) return false;
            entries = adapted;
            return true; // An empty profile intentionally means an empty desktop.
        }
        catch (Exception ex)
        {
            Program.Log("adaptive layout load FAIL (trying legacy): " + ex.Message);
            return false;
        }
    }

    /// <summary>Pure document adapter shared with the executable topology regression scenarios.</summary>
    internal static List<Layout.Entry>? AdaptSerialized(
        string json, IReadOnlyList<DisplayTopology.Display> displays)
    {
        using var root = JsonDocument.Parse(json);
        if (!root.RootElement.TryGetProperty("Version", out var versionValue) ||
            versionValue.ValueKind != JsonValueKind.Number) return null;

        int version = versionValue.GetInt32();
        Profile? profile = version switch
        {
            Version => JsonSerializer.Deserialize<Document>(json)?.Current,
            3 => LatestV3Profile(JsonSerializer.Deserialize<V3Document>(json)),
            _ => null,
        };
        return profile == null || profile.Displays.Count == 0 ? null : AdaptProfile(profile, displays);
    }

    /// <summary>
    /// Best-effort upgrade from v2 buckets, which did not record which buckets belonged to the same topology.
    /// Exact loads happen in Layout first; here identity, resolution and recency are hints, never hard gates.
    /// </summary>
    public static bool TryAdaptLegacy(Dictionary<string, List<Layout.Entry>> document,
                                      IReadOnlyList<DisplayTopology.Display> displays,
                                      out List<Layout.Entry> entries)
    {
        entries = new();
        var candidates = document.Select((pair, index) => ParseLegacy(pair.Key, pair.Value, index))
            .Where(candidate => candidate != null)
            .Cast<LegacyBucket>()
            .ToList();
        if (candidates.Count == 0) return false;

        var unused = new List<LegacyBucket>(candidates);
        var assignments = new Dictionary<IntPtr, LegacyBucket>();
        var orderedTargets = displays.OrderByDescending(display => display.IsPrimary).ToList();

        // Reserve exact buckets globally before heuristic matching so the primary cannot consume a secondary's archive.
        foreach (var target in orderedTargets)
        {
            var exact = unused.FirstOrDefault(candidate =>
                string.Equals(candidate.LayoutKey, target.LayoutKey, StringComparison.OrdinalIgnoreCase));
            if (exact == null) continue;
            assignments[target.Handle] = exact;
            unused.Remove(exact);
        }
        foreach (var target in orderedTargets.Where(target => !assignments.ContainsKey(target.Handle)))
        {
            var sameIdentity = unused.Where(candidate =>
                    string.Equals(candidate.DisplayKey, target.Key, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => LegacyScore(candidate, target)).FirstOrDefault();
            if (sameIdentity == null) continue;
            assignments[target.Handle] = sameIdentity;
            unused.Remove(sameIdentity);
        }
        foreach (var target in orderedTargets.Where(target => !assignments.ContainsKey(target.Handle)))
        {
            if (unused.Count == 0) break;
            var best = unused.OrderByDescending(candidate => LegacyScore(candidate, target)).First();
            assignments[target.Handle] = best;
            unused.Remove(best);
        }

        foreach (var target in orderedTargets)
        {
            if (!assignments.TryGetValue(target.Handle, out var source)) continue;
            var sourceDisplay = new SavedDisplay(source.DisplayKey, false, 0, 0, source.Width, source.Height,
                0, 0, source.Width, source.Height, target.Dpi);
            entries.AddRange(AdaptEntries(source.Widgets, sourceDisplay, target));
        }

        entries = Settle(entries, displays);
        return true;
    }

    public static void Save(IReadOnlyList<DisplayTopology.Display> displays, List<Layout.Entry> widgets)
    {
        var document = new Document
        {
            Version = Version,
            Current = new Profile(DateTime.UtcNow, displays.Select(FromDisplay).ToList(), widgets),
        };
        WriteAtomically(PathOf, JsonSerializer.Serialize(document, JsonOptions));
    }

    static Profile? LatestV3Profile(V3Document? document)
    {
        if (document?.Version != 3) return null;
        if (document.Single == null) return document.Multi;
        if (document.Multi == null) return document.Single;
        return document.Single.UpdatedUtc >= document.Multi.UpdatedUtc ? document.Single : document.Multi;
    }

    static List<Layout.Entry> AdaptProfile(Profile profile, IReadOnlyList<DisplayTopology.Display> targets)
    {
        var mapping = MapDisplays(profile.Displays, targets);
        var sourceByKey = profile.Displays.ToDictionary(display => display.Key, StringComparer.OrdinalIgnoreCase);
        var primarySource = profile.Displays.FirstOrDefault(display => display.IsPrimary) ?? profile.Displays[0];
        var result = new List<Layout.Entry>();

        foreach (var entry in profile.Widgets)
        {
            SavedDisplay source = entry.Display != null && sourceByKey.TryGetValue(entry.Display, out var found)
                ? found : primarySource;
            DisplayTopology.Display target = mapping.TryGetValue(source.Key, out var mapped)
                ? mapped : targets.First(display => display.IsPrimary);
            result.Add(AdaptEntry(entry, source, target));
        }
        return Settle(result, targets);
    }

    static Dictionary<string, DisplayTopology.Display> MapDisplays(
        IReadOnlyList<SavedDisplay> sources, IReadOnlyList<DisplayTopology.Display> targets)
    {
        var result = new Dictionary<string, DisplayTopology.Display>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<IntPtr>();
        var targetPrimary = targets.First(display => display.IsPrimary);

        if (targets.Count == 1)
        {
            foreach (var source in sources) result[source.Key] = targetPrimary;
            return result;
        }

        // Stable EDID serial identity wins when available.
        foreach (var source in sources)
        {
            var exact = targets.FirstOrDefault(target => !used.Contains(target.Handle) &&
                string.Equals(target.Key, source.Key, StringComparison.OrdinalIgnoreCase));
            if (exact == null) continue;
            result[source.Key] = exact;
            used.Add(exact.Handle);
        }

        // Primary desktop is a semantic role, independent of which cable or panel currently carries it.
        var sourcePrimary = sources.FirstOrDefault(source => source.IsPrimary);
        if (sourcePrimary != null && !result.ContainsKey(sourcePrimary.Key) && !used.Contains(targetPrimary.Handle))
        {
            result[sourcePrimary.Key] = targetPrimary;
            used.Add(targetPrimary.Handle);
        }

        // Remaining surfaces follow their left-to-right/top-to-bottom geometry role.
        var remainingSources = sources.Where(source => !result.ContainsKey(source.Key))
            .OrderBy(source => source.PhysicalX).ThenBy(source => source.PhysicalY).ToList();
        var remainingTargets = targets.Where(target => !used.Contains(target.Handle))
            .OrderBy(target => target.Physical.Left).ThenBy(target => target.Physical.Top).ToList();
        for (int i = 0; i < Math.Min(remainingSources.Count, remainingTargets.Count); i++)
            result[remainingSources[i].Key] = remainingTargets[i];

        // A removed display folds onto the primary instead of making its widgets disappear.
        foreach (var source in remainingSources.Skip(remainingTargets.Count))
            result[source.Key] = targetPrimary;
        return result;
    }

    static IEnumerable<Layout.Entry> AdaptEntries(IEnumerable<Layout.Entry> entries, SavedDisplay source,
                                                   DisplayTopology.Display target)
    {
        foreach (var entry in entries) yield return AdaptEntry(entry, source, target);
    }

    static Layout.Entry AdaptEntry(Layout.Entry entry, SavedDisplay source, DisplayTopology.Display target)
    {
        string size = entry.Size != null && WidgetRegistry.SizesOf(entry.Kind).Contains(entry.Size)
            ? entry.Size : WidgetRegistry.DefaultSize(entry.Kind);
        var dimensions = WidgetRegistry.Size(entry.Kind, size);
        double sourceScale = Math.Max(1, source.Dpi) / 96.0;
        double targetScale = target.Scale;
        double sourceWidth = dimensions.W * sourceScale, sourceHeight = dimensions.H * sourceScale;
        double targetWidth = dimensions.W * targetScale, targetHeight = dimensions.H * targetScale;
        double targetWorkX = target.Work.Left - target.Physical.Left;
        double targetWorkY = target.Work.Top - target.Physical.Top;

        double x = AdaptAxis(entry.X, source.WorkX, source.WorkWidth, sourceWidth,
            targetWorkX, target.Work.Width, targetWidth, targetScale / sourceScale);
        double y = AdaptAxis(entry.Y, source.WorkY, source.WorkHeight, sourceHeight,
            targetWorkY, target.Work.Height, targetHeight, targetScale / sourceScale);
        return entry with { X = x, Y = y, Display = target.Key };
    }

    /// <summary>Keep edge distances at the edges; use proportional travel only through the middle band.</summary>
    static double AdaptAxis(double position, double sourceStart, double sourceExtent, double sourceWidgetExtent,
                            double targetStart, double targetExtent, double targetWidgetExtent, double dpiRatio)
    {
        double sourceTravel = Math.Max(0, sourceExtent - sourceWidgetExtent);
        double targetTravel = Math.Max(0, targetExtent - targetWidgetExtent);
        double local = Math.Clamp(position - sourceStart, 0, sourceTravel);
        double ratio = sourceTravel > 0 ? local / sourceTravel : 0;
        double adapted = ratio switch
        {
            <= 0.35 => local * dpiRatio,
            >= 0.65 => targetTravel - (sourceTravel - local) * dpiRatio,
            _ => ratio * targetTravel,
        };
        return targetStart + Math.Clamp(adapted, 0, targetTravel);
    }

    static List<Layout.Entry> Settle(List<Layout.Entry> entries, IReadOnlyList<DisplayTopology.Display> displays)
    {
        var displayByKey = displays.ToDictionary(display => display.Key, StringComparer.OrdinalIgnoreCase);
        var occupied = displays.ToDictionary(display => display.Key, _ => new List<Rect>(), StringComparer.OrdinalIgnoreCase);
        var result = new List<Layout.Entry>(entries.Count);

        foreach (var entry in entries)
        {
            var display = entry.Display != null && displayByKey.TryGetValue(entry.Display, out var found)
                ? found : displays.First(item => item.IsPrimary);
            string size = entry.Size != null && WidgetRegistry.SizesOf(entry.Kind).Contains(entry.Size)
                ? entry.Size : WidgetRegistry.DefaultSize(entry.Kind);
            var dimensions = WidgetRegistry.Size(entry.Kind, size);
            var candidate = new Rect(display.Physical.Left + entry.X, display.Physical.Top + entry.Y,
                dimensions.W * display.Scale, dimensions.H * display.Scale);
            double margin = Placement.EdgeMargin * display.Scale;
            var safe = new Rect(display.Work.Left + margin, display.Work.Top + margin,
                Math.Max(0, display.Work.Width - margin * 2), Math.Max(0, display.Work.Height - margin * 2));
            bool inBounds = candidate.Left >= safe.Left && candidate.Top >= safe.Top &&
                            candidate.Right <= safe.Right && candidate.Bottom <= safe.Bottom;
            bool clashes = occupied[display.Key].Any(other => candidate.IntersectsWith(other));
            if (!inBounds || clashes)
            {
                var settled = Placement.Resolve(candidate, occupied[display.Key], display.Work,
                    Placement.Unit * display.Scale, margin);
                candidate = new Rect(settled.L, settled.T, candidate.Width, candidate.Height);
                if (!Fits(candidate, safe, occupied[display.Key]))
                {
                    var free = FindNearestFree(candidate, safe, occupied[display.Key],
                        Placement.Unit * display.Scale);
                    if (free is { } position) candidate = position;
                    else Program.Log($"adaptive layout capacity exhausted on {display.Key}; " +
                                     $"keeping {entry.Kind} visible with unavoidable overlap");
                }
            }
            occupied[display.Key].Add(candidate);
            result.Add(entry with
            {
                X = candidate.Left - display.Physical.Left,
                Y = candidate.Top - display.Physical.Top,
                Display = display.Key,
            });
        }
        return result;
    }

    static bool Fits(Rect candidate, Rect safe, IReadOnlyList<Rect> occupied)
        => candidate.Left >= safe.Left && candidate.Top >= safe.Top &&
           candidate.Right <= safe.Right && candidate.Bottom <= safe.Bottom &&
           !occupied.Any(other => candidate.IntersectsWith(Shrink(other, 0.5)));

    /// <summary>
    /// A disconnected display can fold several widgets onto a smaller work area. Placement.Resolve intentionally
    /// searches only around the nearest group; migration needs a whole-work-area fallback so widgets compact into
    /// any free cell before accepting an unavoidable overlap.
    /// </summary>
    static Rect? FindNearestFree(Rect desired, Rect safe, IReadOnlyList<Rect> occupied, double unit)
    {
        int columns = (int)Math.Floor((safe.Width - desired.Width) / unit) + 1;
        int rows = (int)Math.Floor((safe.Height - desired.Height) / unit) + 1;
        if (columns <= 0 || rows <= 0) return null;

        Rect? best = null;
        double bestDistance = double.MaxValue;
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            var candidate = new Rect(safe.Left + column * unit, safe.Top + row * unit,
                desired.Width, desired.Height);
            if (!Fits(candidate, safe, occupied)) continue;
            double dx = candidate.Left - desired.Left, dy = candidate.Top - desired.Top;
            double distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;
            best = candidate;
            bestDistance = distance;
        }
        return best;
    }

    static Rect Shrink(Rect rect, double amount) => new(
        rect.X + amount, rect.Y + amount,
        Math.Max(0, rect.Width - amount * 2), Math.Max(0, rect.Height - amount * 2));

    static SavedDisplay FromDisplay(DisplayTopology.Display display) => new(
        display.Key,
        display.IsPrimary,
        display.Physical.Left,
        display.Physical.Top,
        display.Physical.Width,
        display.Physical.Height,
        display.Work.Left - display.Physical.Left,
        display.Work.Top - display.Physical.Top,
        display.Work.Width,
        display.Work.Height,
        display.Dpi);

    static LegacyBucket? ParseLegacy(string key, List<Layout.Entry> widgets, int order)
    {
        if (!key.StartsWith("v2:", StringComparison.OrdinalIgnoreCase)) return null;
        int separator = key.LastIndexOf(':');
        if (separator <= 3) return null;
        string[] dimensions = key[(separator + 1)..].Split('x');
        if (dimensions.Length != 2 || !double.TryParse(dimensions[0], out double width) ||
            !double.TryParse(dimensions[1], out double height) || width <= 0 || height <= 0) return null;
        return new LegacyBucket(key, key[3..separator], width, height, widgets, order);
    }

    static double LegacyScore(LegacyBucket candidate, DisplayTopology.Display target)
    {
        double score = candidate.Order / 1000.0; // Later-created buckets win otherwise identical migrations.
        if (string.Equals(candidate.DisplayKey, target.Key, StringComparison.OrdinalIgnoreCase)) score += 1_000_000;
        if (Math.Abs(candidate.Width - target.Physical.Width) < 0.5 &&
            Math.Abs(candidate.Height - target.Physical.Height) < 0.5) score += 100_000;
        double candidateAspect = candidate.Width / candidate.Height;
        double targetAspect = target.Physical.Width / target.Physical.Height;
        score -= Math.Abs(candidateAspect - targetAspect) * 10_000;
        score -= (Math.Abs(candidate.Width - target.Physical.Width) +
                  Math.Abs(candidate.Height - target.Physical.Height)) / 10;
        return score;
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    static void WriteAtomically(string path, string contents)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, contents);
        File.Move(temporary, path, overwrite: true);
    }
}
