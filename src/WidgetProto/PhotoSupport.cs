using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace WidgetProto;

/// <summary>
/// 照片组件宿主侧：cfg.folder（缺省=系统"图片"文件夹）→ 推文件清单；
/// 图走**同源**路径 https://w{i}.test/__photos/&lt;name&gt;，由 WebResourceRequested 直接回文件流。
/// ⚠️为什么不用第二个虚拟主机映射：真机实测跨虚拟主机的子资源请求（photos-wN.test）
/// 一律 img FAIL（WebView2 1.0.4078 + --process-per-site），同源拦截是确定性的路。
/// </summary>
public static class PhotoSupport
{
    static readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif" };

    /// <summary>
    /// Setup 时挂一次：photo 组件的**整个源**由宿主供流（页面文件走 WebDir、__photos/* 走照片夹）。
    /// ⚠️不能与 SetVirtualHostNameToFolderMapping 并用——真机实锤：被完整映射的源在更低层短路，
    /// WebResourceRequested 对该源一律不触发（hook installed 但 serve hit 永不出现）。
    /// </summary>
    public static void Hook(WidgetWindow w, CoreWebView2 core, string pageHost)
    {
        core.AddWebResourceRequestedFilter($"https://{pageHost}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, a) => Serve(w, a);
        Program.Log($"photo: origin https://{pageHost} host-served");
    }

    /// <summary>cfg 变更/导航完成时重放：解析生效文件夹并推清单。</summary>
    public static void Apply(WidgetWindow w, CoreWebView2 core, int id)
    {
        string? folder = null;
        if (w.Cfg is { ValueKind: JsonValueKind.Object } c &&
            c.TryGetProperty("folder", out var f) && f.GetString() is { Length: > 0 } fs &&
            Directory.Exists(fs))
            folder = fs;
        folder ??= Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        w.PhotoFolder = folder;

        var files = new List<string>();
        try
        {
            foreach (var p in Directory.EnumerateFiles(folder)
                         .Where(p => Exts.Contains(Path.GetExtension(p)))
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Take(60))
                files.Add(Path.GetFileName(p));
        }
        catch (Exception ex) { Program.Log($"photo {id}: enumerate FAIL: {ex.Message}"); }

        w.PostJson(JsonSerializer.Serialize(new { t = "photos", folder, files }));
        Program.Log($"photo {id}: {files.Count} files @ {folder}");
    }

    static void Serve(WidgetWindow w, CoreWebView2WebResourceRequestedEventArgs a)
    {
        try
        {
            var abs = Uri.UnescapeDataString(new Uri(a.Request.Uri).AbsolutePath);
            var name = Path.GetFileName(abs);   // 防目录穿越：只取文件名
            // __photos/* → 照片夹；其余 → WebDir（页面/样式/脚本）
            var root = abs.StartsWith("/__photos/", StringComparison.Ordinal) ? w.PhotoFolder : Program.WebDir;
            string? path = root == null || name.Length == 0 ? null : Path.Combine(root, name);
            if (path == null || !File.Exists(path))
            {
                a.Response = Program.Env!.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            // 流交给响应对象，WebView2 读完自行处置
            a.Response = Program.Env!.CreateWebResourceResponse(
                File.OpenRead(path), 200, "OK", $"Content-Type: {Mime(name)}");
        }
        catch (Exception ex)
        {
            Program.Log($"photo serve FAIL: {ex.Message}");
            try { a.Response = Program.Env!.CreateWebResourceResponse(null, 500, "Error", ""); } catch { }
        }
    }

    static string Mime(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "application/javascript; charset=utf-8",
        _ => "application/octet-stream",
    };
}
