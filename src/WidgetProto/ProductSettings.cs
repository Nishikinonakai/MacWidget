using System.IO;
using System.Text.Json;

namespace WidgetProto;

/// <summary>
/// 产品级偏好（与自适应工作区 layout.json 分开）：安装目录可被升级覆盖，所有用户状态
/// 一律住在 %LOCALAPPDATA%\MacWidget。当前只保存自启选择，后续产品偏好在这里扩展。
/// </summary>
internal static class ProductSettings
{
    private sealed class Data
    {
        public bool Autostart { get; set; }
        public string Language { get; set; } = "auto";
    }

    private static readonly string FilePath = Path.Combine(Program.DataDir, "settings.json");
    public static bool AutostartEnabled { get; private set; }
    public static string Language { get; private set; } = "auto";
    public static bool English => Language == "en" || (Language == "auto" &&
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase));

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data();
            AutostartEnabled = data.Autostart;
            Language = data.Language is "zh" or "en" ? data.Language : "auto";
        }
        catch (Exception ex) { Program.Log("settings load failed: " + ex.Message); }
    }

    public static void SetAutostart(bool enabled)
    {
        AutostartEnabled = enabled;
        try
        {
            Directory.CreateDirectory(Program.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data { Autostart = enabled, Language = Language }, new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
        }
        catch (Exception ex) { Program.Log("settings save failed: " + ex.Message); }
    }

    public static void ToggleLanguage()
    {
        Language = English ? "zh" : "en";
        try
        {
            Directory.CreateDirectory(Program.DataDir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data { Autostart = AutostartEnabled, Language = Language }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Program.Log("language save failed: " + ex.Message); }
    }
}
