using System.Threading;
using System.Windows;

namespace WidgetProto;

/// <summary>
/// 每个登录会话只保留一组桌面组件。第二次启动不会复制整组窗口：带 --edit-widgets 时
/// 转发给已运行实例打开组件库，安装器则可用 --quit 进行无 UI 的优雅升级/卸载。
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = "MacWidget.SingleInstance.v1";
    private const string QuitEventName = "MacWidget.Command.Quit.v1";
    private static Mutex? _mutex;
    private static EventWaitHandle? _quit;

    public static bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        if (created) return true;
        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    public static void StartQuitListener()
    {
        _quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        var worker = new Thread(() =>
        {
            while (true)
            {
                try { _quit.WaitOne(); }
                catch { return; }
                try { Application.Current.Dispatcher.BeginInvoke(Program.RequestShutdown); }
                catch { return; }
            }
        }) { IsBackground = true, Name = "macwidget-quit" };
        worker.Start();
    }

    public static bool SignalQuit()
    {
        try
        {
            using var evt = EventWaitHandle.OpenExisting(QuitEventName);
            return evt.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
