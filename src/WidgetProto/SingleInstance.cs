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
    private const string RestartEventName = "MacWidget.Command.Restart.v1";
    private static Mutex? _mutex;
    private static EventWaitHandle? _quit;
    private static EventWaitHandle? _restart;

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

    public static void StartRestartListener()
    {
        _restart = new EventWaitHandle(false, EventResetMode.AutoReset, RestartEventName);
        var worker = new Thread(() =>
        {
            while (true)
            {
                try { _restart.WaitOne(); }
                catch { return; }
                TopologyWatcher.RequestRestart("command");
            }
        }) { IsBackground = true, Name = "macwidget-restart" };
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

    public static bool SignalRestart()
    {
        try
        {
            using var evt = EventWaitHandle.OpenExisting(RestartEventName);
            return evt.Set();
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>显示拓扑交接的子实例最多等十秒，避免旧实例尚在释放 WebView2/互斥锁时丢启动。</summary>
    public static bool TryAcquireForRestart(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        do
        {
            if (TryAcquire()) return true;
            Thread.Sleep(100);
        } while (DateTime.UtcNow < until);
        return false;
    }
}
