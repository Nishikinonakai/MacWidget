using System.Runtime.InteropServices;

namespace WidgetProto;

/// <summary>
/// Widget context menu backed by a USER32 HMENU. Windows owns layout, DPI,
/// theme, shadows, corners, keyboard navigation, and accessibility.
/// </summary>
public static class MenuWindow
{
    public static void Open(WidgetWindow target, double _, double __)
    {
        using var menu = new NativePopupMenu();
        int editConfig = 0;
        int nextId = 100;

        if (WidgetRegistry.Configurable(target.Kind))
        {
            editConfig = nextId++;
            menu.Add(editConfig, Ui.T("编辑“", "Edit “") + KindLabel(target.Kind) + Ui.T("”", "”"));
            menu.Separator();
        }

        var sizeCommands = new Dictionary<int, string>();
        var sizes = WidgetRegistry.SizesOf(target.Kind);
        if (sizes.Length > 1)
        {
            foreach (var size in sizes)
            {
                int id = nextId++;
                sizeCommands[id] = size;
                menu.Add(id, SizeLabel(size), isChecked: size == target.SizeClass);
            }
            menu.Separator();
        }

        int editWidgets = nextId++;
        int remove = nextId++;
        menu.Add(editWidgets, Ui.T("编辑小组件…", "Edit Widgets…"));
        menu.Separator();
        menu.Add(remove, Ui.T("移除小组件", "Remove Widget"));

        int command = menu.Show(target.NativeHandle);
        if (command == 0) return;
        if (command == editConfig) target.BeginConfiguration();
        else if (sizeCommands.TryGetValue(command, out var size)) target.ApplySize(size);
        else if (command == editWidgets) EditMode.Enter();
        else if (command == remove) target.ByeAndClose();
    }

    static string SizeLabel(string size) => ProductSettings.English
        ? size switch { "m" => "Medium", "l" => "Large", _ => "Small" }
        : size switch { "m" => "中", "l" => "大", _ => "小" };

    static string KindLabel(string kind) => kind switch
    {
        "photo" => Ui.T("照片", "Photos"),
        "clock" => Ui.T("时钟", "Clock"),
        "calendar" => Ui.T("日历", "Calendar"),
        "monitor" => Ui.T("系统监视", "System Monitor"),
        "weather" => Ui.T("天气", "Weather"),
        "music" => Ui.T("正在播放", "Now Playing"),
        "battery" => Ui.T("电池", "Battery"),
        _ => kind,
    };
}

/// <summary>A small lifetime-safe wrapper around the native popup-menu API.</summary>
internal sealed class NativePopupMenu : IDisposable
{
    const uint MF_STRING = 0x0000;
    const uint MF_GRAYED = 0x0001;
    const uint MF_CHECKED = 0x0008;
    const uint MF_SEPARATOR = 0x0800;
    const uint TPM_RIGHTBUTTON = 0x0002;
    const uint TPM_RETURNCMD = 0x0100;

    IntPtr _menu = CreatePopupMenu();

    public void Add(int command, string text, bool isChecked = false, bool enabled = true)
    {
        if (_menu == IntPtr.Zero) return;
        uint flags = MF_STRING | (isChecked ? MF_CHECKED : 0) | (enabled ? 0 : MF_GRAYED);
        _ = AppendMenu(_menu, flags, new UIntPtr((uint)command), text);
    }

    public void Separator()
    {
        if (_menu != IntPtr.Zero)
            _ = AppendMenu(_menu, MF_SEPARATOR, UIntPtr.Zero, null);
    }

    public int Show(IntPtr owner)
    {
        if (_menu == IntPtr.Zero || owner == IntPtr.Zero) return 0;
        Native.GetCursorPos(out var cursor);
        _ = SetForegroundWindow(owner);
        int command = TrackPopupMenuEx(_menu, TPM_RIGHTBUTTON | TPM_RETURNCMD,
            cursor.X, cursor.Y, owner, IntPtr.Zero);
        // Required by TrackPopupMenu to make a subsequent click reliably dismiss it.
        _ = PostMessage(owner, 0, IntPtr.Zero, IntPtr.Zero);
        return command;
    }

    public void Dispose()
    {
        if (_menu == IntPtr.Zero) return;
        _ = DestroyMenu(_menu);
        _menu = IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr reserved);

    [DllImport("user32.dll")]
    static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
}
