using System.IO;
using Microsoft.Win32;

namespace WidgetProto;

/// <summary>
/// 单用户自启。MacWidget 是普通桌面应用，不需要服务权限：Run 键足够稳定、在任务管理器
/// 启动应用页也可由用户一眼管理；实际开关与产品设置同步，卸载器会清理此项。
/// </summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MacWidget";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch { return false; }
    }

    public static bool Enable()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return false;
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key?.SetValue(ValueName, Quote(exe));
            Program.Log("autostart enabled: " + exe);
            return true;
        }
        catch (Exception ex)
        {
            Program.Log("autostart enable failed: " + ex.Message);
            return false;
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            Program.Log("autostart disabled");
        }
        catch (Exception ex) { Program.Log("autostart disable failed: " + ex.Message); }
    }

    public static bool SetEnabled(bool enabled)
    {
        if (!enabled) { Disable(); ProductSettings.SetAutostart(false); return true; }
        bool ok = Enable();
        ProductSettings.SetAutostart(ok);
        return ok;
    }

    /// <summary>设置写着开启但注册表被用户/清理工具移除时，在下次应用启动恢复。</summary>
    public static void EnsureConfigured()
    {
        if (ProductSettings.AutostartEnabled && !IsEnabled())
            ProductSettings.SetAutostart(Enable());
    }

    private static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;
}
