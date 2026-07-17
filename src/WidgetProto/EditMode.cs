using System.Windows;

namespace WidgetProto;

/// <summary>
/// 编辑模式（macOS Edit Widgets）：组件左上角出减号徽章 + 组件库面板滑出。
/// 进入 = 任意组件右键；退出 = 面板"完成"/Esc/再次右键。徽章渲染在组件页内（host.js），
/// 这里只做全局开关与广播。
/// </summary>
public static class EditMode
{
    public static bool On { get; private set; }

    public static void Toggle() { if (On) Exit(); else Enter(); }

    public static void Enter()
    {
        if (On) return;
        On = true;
        Program.Log("editmode ON");
        Broadcast();
        PanelWindow.Get().ShowPanel();
    }

    public static void Exit()
    {
        if (!On) return;
        On = false;
        Program.Log("editmode OFF");
        Broadcast();
        PanelWindow.Existing?.HidePanel();
    }

    static void Broadcast()
    {
        foreach (Window w in Application.Current.Windows)
            (w as WidgetWindow)?.PushState();
    }
}
