using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WidgetProto;

/// <summary>
/// Builds one small, genuinely Gaussian-blurred wallpaper texture per monitor.
/// Desktop widgets sit behind application windows, so wallpaper is their real
/// backdrop; caching the blur avoids a live GPU effect per WebView and prevents
/// foreground windows from being baked into a stale screenshot.
/// </summary>
public static class WallpaperBackdrop
{
    public sealed record Alignment(string Url, double Width, double Height, double X, double Y);

    sealed record Material(string DisplayKey, string FileName, long Version);

    static IReadOnlyDictionary<string, Material> _materials =
        new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
    static IReadOnlyList<DisplayTopology.Display> _displays = Array.Empty<DisplayTopology.Display>();

    public static async Task InitializeAsync()
    {
        try
        {
            var displays = DisplayTopology.GetAll();
            var materials = await Task.Run(() => Build(displays));
            _displays = displays;
            _materials = materials.ToDictionary(m => m.DisplayKey, StringComparer.OrdinalIgnoreCase);
            Program.Log($"wallpaper backdrop ready: {_materials.Count} monitor texture(s)");
        }
        catch (Exception ex)
        {
            _materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            _displays = Array.Empty<DisplayTopology.Display>();
            Program.Log($"wallpaper backdrop FAIL: {ex}");
        }
    }

    public static Alignment? ForWidget(System.Windows.Rect frame, double cardInsetDiu)
    {
        if (frame.IsEmpty) return null;
        var center = new System.Windows.Point(
            frame.Left + frame.Width / 2,
            frame.Top + frame.Height / 2);
        var display = _displays.FirstOrDefault(item => item.Physical.Contains(center))
            ?? NearestDisplay(center);
        if (display == null) return null;
        if (!_materials.TryGetValue(display.Key, out var material)) return null;

        double cardLeft = frame.Left - display.Physical.Left + cardInsetDiu * display.Scale;
        double cardTop = frame.Top - display.Physical.Top + cardInsetDiu * display.Scale;
        return new Alignment(
            $"https://material.test/{material.FileName}?v={material.Version}",
            display.Physical.Width / display.Scale,
            display.Physical.Height / display.Scale,
            -cardLeft / display.Scale,
            -cardTop / display.Scale);
    }

    static DisplayTopology.Display? NearestDisplay(System.Windows.Point point)
        => _displays.OrderBy(display =>
        {
            double x = Math.Clamp(point.X, display.Physical.Left, display.Physical.Right);
            double y = Math.Clamp(point.Y, display.Physical.Top, display.Physical.Bottom);
            double dx = point.X - x, dy = point.Y - y;
            return dx * dx + dy * dy;
        }).FirstOrDefault();

    static List<Material> Build(IReadOnlyList<DisplayTopology.Display> displays)
    {
        Directory.CreateDirectory(Program.DataDir);
        var settings = ReadSettings();
        using var wallpaper = LoadWallpaper(settings.Path);
        var results = new List<Material>();

        for (int i = 0; i < displays.Count; i++)
        {
            var display = displays[i];
            int width = Math.Max(1, (int)Math.Round(display.Physical.Width / 4));
            int height = Math.Max(1, (int)Math.Round(display.Physical.Height / 4));
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(settings.Background);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (wallpaper != null)
                    DrawWallpaper(g, wallpaper, width, height, settings.Style, settings.Tile);
            }

            GaussianBlur(bitmap, sigma: 5.5);
            string fileName = $"wallpaper-blur-{i}.png";
            string finalPath = Path.Combine(Program.DataDir, fileName);
            string temporaryPath = Path.Combine(Program.DataDir, $"wallpaper-blur-{i}.tmp.png");
            bitmap.Save(temporaryPath, ImageFormat.Png);
            File.Move(temporaryPath, finalPath, true);
            results.Add(new Material(display.Key, fileName, File.GetLastWriteTimeUtc(finalPath).Ticks));
        }
        return results;
    }

    sealed record Settings(string Path, int Style, bool Tile, Color Background);

    static Settings ReadSettings()
    {
        using var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        string path = Convert.ToString(desktop?.GetValue("WallPaper")) ?? "";
        if (!File.Exists(path))
        {
            string transcoded = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
            if (File.Exists(transcoded)) path = transcoded;
        }
        _ = int.TryParse(Convert.ToString(desktop?.GetValue("WallpaperStyle")), out int style);
        bool tile = Convert.ToString(desktop?.GetValue("TileWallpaper")) == "1";

        Color background = Color.Black;
        using var colors = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
        var parts = (Convert.ToString(colors?.GetValue("Background")) ?? "").Split(' ');
        if (parts.Length == 3 &&
            byte.TryParse(parts[0], out byte r) &&
            byte.TryParse(parts[1], out byte g) &&
            byte.TryParse(parts[2], out byte b))
            background = Color.FromArgb(r, g, b);
        return new Settings(path, style, tile, background);
    }

    static Image? LoadWallpaper(string path)
    {
        if (!File.Exists(path)) return null;
        // Detach from the source file so a slideshow can replace it later.
        using var source = Image.FromFile(path);
        return new Bitmap(source);
    }

    static void DrawWallpaper(Graphics g, Image image, int width, int height, int style, bool tile)
    {
        if (tile)
        {
            int tileWidth = Math.Max(1, image.Width / 4);
            int tileHeight = Math.Max(1, image.Height / 4);
            for (int y = 0; y < height; y += tileHeight)
                for (int x = 0; x < width; x += tileWidth)
                    g.DrawImage(image, new Rectangle(x, y, tileWidth, tileHeight));
            return;
        }

        RectangleF destination;
        if (style == 2) // Stretch
        {
            destination = new RectangleF(0, 0, width, height);
        }
        else
        {
            double sx = width / (double)image.Width;
            double sy = height / (double)image.Height;
            double scale = style == 6 ? Math.Min(sx, sy) : // Fit
                style is 10 or 22 ? Math.Max(sx, sy) :     // Fill / Span fallback
                .25;                                       // Center
            float drawWidth = (float)(image.Width * scale);
            float drawHeight = (float)(image.Height * scale);
            destination = new RectangleF(
                (width - drawWidth) / 2,
                (height - drawHeight) / 2,
                drawWidth,
                drawHeight);
        }
        g.DrawImage(image, destination);
    }

    static void GaussianBlur(Bitmap bitmap, double sigma)
    {
        int radius = Math.Max(1, (int)Math.Ceiling(sigma * 3));
        var kernel = new double[radius * 2 + 1];
        double sum = 0;
        for (int i = -radius; i <= radius; i++)
        {
            double value = Math.Exp(-(i * i) / (2 * sigma * sigma));
            kernel[i + radius] = value;
            sum += value;
        }
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;

        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            int stride = Math.Abs(data.Stride);
            var source = new byte[stride * bitmap.Height];
            var horizontal = new byte[source.Length];
            var output = new byte[source.Length];
            Marshal.Copy(data.Scan0, source, 0, source.Length);

            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    ConvolvePixel(source, horizontal, stride, bitmap.Width, bitmap.Height,
                        x, y, radius, kernel, horizontalPass: true);

            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    ConvolvePixel(horizontal, output, stride, bitmap.Width, bitmap.Height,
                        x, y, radius, kernel, horizontalPass: false);

            Marshal.Copy(output, 0, data.Scan0, output.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    static void ConvolvePixel(
        byte[] source, byte[] target, int stride, int width, int height,
        int x, int y, int radius, double[] kernel, bool horizontalPass)
    {
        double blue = 0, green = 0, red = 0, alpha = 0;
        for (int k = -radius; k <= radius; k++)
        {
            int sampleX = horizontalPass ? Math.Clamp(x + k, 0, width - 1) : x;
            int sampleY = horizontalPass ? y : Math.Clamp(y + k, 0, height - 1);
            int sample = sampleY * stride + sampleX * 4;
            double weight = kernel[k + radius];
            blue += source[sample] * weight;
            green += source[sample + 1] * weight;
            red += source[sample + 2] * weight;
            alpha += source[sample + 3] * weight;
        }
        int targetIndex = y * stride + x * 4;
        target[targetIndex] = (byte)Math.Clamp((int)Math.Round(blue), 0, 255);
        target[targetIndex + 1] = (byte)Math.Clamp((int)Math.Round(green), 0, 255);
        target[targetIndex + 2] = (byte)Math.Clamp((int)Math.Round(red), 0, 255);
        target[targetIndex + 3] = (byte)Math.Clamp((int)Math.Round(alpha), 0, 255);
    }
}
