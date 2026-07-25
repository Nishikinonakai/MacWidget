using System.IO;
using System.Text.Json;

namespace WidgetProto;

/// <summary>
/// 产品级偏好（与按分辨率分桶的 widgets.json 分开）：安装目录可被升级覆盖，所有用户状态
/// 一律住在 %LOCALAPPDATA%\MacWidget。当前只保存自启选择，后续产品偏好在这里扩展。
/// </summary>
internal static class ProductSettings
{
    private sealed class Data
    {
        public bool Autostart { get; set; }
    }

    private static readonly string FilePath = Path.Combine(Program.DataDir, "settings.json");
    public static bool AutostartEnabled { get; private set; }

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            AutostartEnabled = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath))?.Autostart ?? false;
        }
        catch (Exception ex) { Program.Log("settings load failed: " + ex.Message); }
    }

    public static void SetAutostart(bool enabled)
    {
        AutostartEnabled = enabled;
        try
        {
            Directory.CreateDirectory(Program.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data { Autostart = enabled }, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
        }
        catch (Exception ex) { Program.Log("settings save failed: " + ex.Message); }
    }
}
