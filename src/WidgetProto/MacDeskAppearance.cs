using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace WidgetProto;

/// <summary>
/// 可选地复用 MacDesk 的强调色。两个产品不共享程序集、也不要求同时升级：只有确认本机仍装有
/// MacDesk 时才读取它的用户设置，卸载后留下的 settings.json 不会污染 MacWidget 的默认外观。
/// </summary>
internal static class MacDeskAppearance
{
    const string DefaultAccent = "#0A84FF"; // Windows / macOS 的安全默认蓝

    static readonly IReadOnlyDictionary<string, string> AccentColors =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["blue"] = "#2B63D9",
            ["purple"] = "#953D96",
            ["pink"] = "#F74F9E",
            ["red"] = "#E0383E",
            ["orange"] = "#F7821B",
            ["yellow"] = "#E6B300",
            ["green"] = "#62BA46",
            ["graphite"] = "#797979",
        };

    public static string PanelAccentCss()
    {
        if (Program.Opts.WithoutMacDesk || !IsInstalled()) return DefaultAccent;

        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MacDesk", "settings.json");
            if (!File.Exists(settingsPath)) return DefaultAccent;

            using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
            if (!doc.RootElement.TryGetProperty("AccentColor", out var value) ||
                value.ValueKind != JsonValueKind.String) return DefaultAccent;

            return AccentColors.TryGetValue(value.GetString() ?? "", out var css)
                ? css : DefaultAccent;
        }
        catch
        {
            // 联动外观绝不能影响组件启动；遇到被占用/手改的设置文件时安静回退。
            return DefaultAccent;
        }
    }

    static bool IsInstalled()
    {
        Process[] running = Array.Empty<Process>();
        try
        {
            running = Process.GetProcessesByName("MacDesk");
            if (running.Length > 0) return true; // 运行中的同名正式副本足以证明已安装/可用。
        }
        catch { }
        finally { foreach (var process in running) process.Dispose(); }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (new[]
        {
            Path.Combine(local, "Programs", "MacDesk", "MacDesk.exe"),
            Path.Combine(programFiles, "MacDesk", "MacDesk.exe"),
        }.Any(File.Exists)) return true;

        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\MacDesk.exe");
                if (key?.GetValue(null) is string path && File.Exists(path)) return true;
            }
            catch { }
        }
        return false;
    }
}
