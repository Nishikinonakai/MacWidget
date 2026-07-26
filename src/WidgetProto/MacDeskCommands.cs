using System.Windows;

namespace WidgetProto;

/// <summary>
/// MacDesk → MacWidget 的极小控制面：只请求进入编辑模式。命名事件不传敏感数据、没有
/// 服务端依赖，且 MacDesk 不必引用本项目程序集。事件存在仅代表当前用户会话中的实例。
/// </summary>
public static class MacDeskCommands
{
    public const string EditWidgetsEventName = "MacWidget.Command.EditWidgets.v1";
    static EventWaitHandle? _editWidgets;

    public static void Start()
    {
        if (Program.Opts.WithoutMacDesk) return;
        if (_editWidgets != null) return;
        _editWidgets = new EventWaitHandle(false, EventResetMode.AutoReset, EditWidgetsEventName);
        var worker = new Thread(() =>
        {
            while (true)
            {
                try { _editWidgets.WaitOne(); }
                catch { return; }
                try { Application.Current.Dispatcher.BeginInvoke(EditMode.Enter); }
                catch { return; }
            }
        }) { IsBackground = true, Name = "macdesk-edit-widgets" };
        worker.Start();
        Program.Log("MacDesk edit command ready");
    }

    public static bool RequestEditor()
    {
        if (Program.Opts.WithoutMacDesk) return false;
        try
        {
            using var evt = EventWaitHandle.OpenExisting(EditWidgetsEventName);
            return evt.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
