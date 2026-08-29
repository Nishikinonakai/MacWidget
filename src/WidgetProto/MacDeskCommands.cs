using System.Windows;

namespace WidgetProto;

/// <summary>
/// MacDesk → MacWidget 的极小控制面：请求进入编辑模式或切换三态着色。命名事件不传敏感数据、没有
/// 服务端依赖，且 MacDesk 不必引用本项目程序集。事件存在仅代表当前用户会话中的实例。
/// </summary>
public static class MacDeskCommands
{
    public const string EditWidgetsEventName = "MacWidget.Command.EditWidgets.v1";
    public const string StyleAutoEventName = "MacWidget.Command.Style.Auto.v1";
    public const string StyleMonoEventName = "MacWidget.Command.Style.Mono.v1";
    public const string StyleFullEventName = "MacWidget.Command.Style.Full.v1";
    static EventWaitHandle? _editWidgets;
    static EventWaitHandle? _styleAuto;
    static EventWaitHandle? _styleMono;
    static EventWaitHandle? _styleFull;

    public static void Start()
    {
        if (Program.Opts.WithoutMacDesk) return;
        if (_editWidgets != null) return;
        try
        {
            _editWidgets = new EventWaitHandle(false, EventResetMode.AutoReset, EditWidgetsEventName);
            _styleAuto = new EventWaitHandle(false, EventResetMode.AutoReset, StyleAutoEventName);
            _styleMono = new EventWaitHandle(false, EventResetMode.AutoReset, StyleMonoEventName);
            _styleFull = new EventWaitHandle(false, EventResetMode.AutoReset, StyleFullEventName);
        }
        catch (Exception ex)
        {
            try { _editWidgets?.Dispose(); _styleAuto?.Dispose(); _styleMono?.Dispose(); _styleFull?.Dispose(); }
            catch { }
            _editWidgets = _styleAuto = _styleMono = _styleFull = null;
            Program.Log("MacDesk commands unavailable: " + ex.Message);
            return; // 可选联动失败不得阻止小组件启动。
        }
        var worker = new Thread(() =>
        {
            WaitHandle[] commands = { _editWidgets, _styleAuto, _styleMono, _styleFull };
            while (true)
            {
                int command;
                try { command = WaitHandle.WaitAny(commands); }
                catch { return; }
                try
                {
                    _ = Application.Current.Dispatcher.BeginInvoke(command switch
                    {
                        0 => EditMode.Enter,
                        1 => () => ColorMode.ApplyStyle("auto"),
                        2 => () => ColorMode.ApplyStyle("mono"),
                        _ => () => ColorMode.ApplyStyle("full"),
                    });
                }
                catch { return; }
            }
        }) { IsBackground = true, Name = "macdesk-commands" };
        worker.Start();
        Program.Log("MacDesk edit/style commands ready");
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
