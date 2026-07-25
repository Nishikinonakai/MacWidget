using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace WidgetProto;

public static class Program
{
    public static Options Opts = new();
    public static CoreWebView2Environment? Env;
    public static readonly string BaseDir = AppContext.BaseDirectory;
    public static readonly string WebDir = Path.Combine(AppContext.BaseDirectory, "web");

    static int _nextId;
    public static int NextId() => _nextId++;

    [STAThread]
    public static int Main(string[] args)
    {
        Opts = Options.Parse(args);
        Log($"=== start: lab={Opts.LabMode} n={Opts.N} control={Opts.Control} backdrop={Opts.Backdrop} " +
            $"origin={Opts.Origin} pin={Opts.Pin} widget={Opts.Widget} glass={Opts.Glass} style={Opts.Style} " +
            $"procpersite={Opts.ProcPerSite} noactivate={Opts.NoActivate}");
        Log($"    raw cmdline: {Environment.CommandLine}");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { Log("UNHANDLED: " + e.Exception); e.Handled = true; };
        app.Startup += async (_, _) =>
        {
            try
            {
                DataHub.Register(new SysMonProvider());   // 数据源注册（有订阅者才开采样）

                if (Opts.Control != "native")
                {
                    // 全部组件共享同一个 Environment（同一 udf）→ 共享 browser/GPU 进程；
                    // --process-per-site 合并同 site renderer（内存实验定案的产品配方）
                    var udf = Path.Combine(BaseDir, "udf");
                    var envOpts = new CoreWebView2EnvironmentOptions();
                    if (Opts.ProcPerSite) envOpts.AdditionalBrowserArguments = "--process-per-site";
                    Env = await CoreWebView2Environment.CreateAsync(null, udf, envOpts);
                    Log($"webview2 env ready, runtime={Env.BrowserVersionString} procpersite={Opts.ProcPerSite}");
                }

                if (Opts.LabMode)
                {
                    // 实验模式：--n/--widget 栅格铺开（内存/材质对照实验用），不碰持久化布局
                    var kinds = WidgetRegistry.Kinds;
                    for (int i = 0; i < Opts.N; i++)
                    {
                        var kind = Opts.Widget == "mixed" ? kinds[i % kinds.Length] : Opts.Widget;
                        new WidgetWindow(NextId(), kind).Show();
                    }
                }
                else
                {
                    // 产品模式：恢复本分辨率档的摆位（无档 = 默认演示组）
                    foreach (var it in Layout.LoadOrDefault())
                        new WidgetWindow(NextId(), it.Kind, it.Size, it.X, it.Y).Show();
                }
                Log("all windows shown");

                ColorMode.Start();   // Automatic 着色状态机（含深浅外观跟随）

                // MacDesk 联动：初始占用矩形（等一拍让窗口全部落位）+ 3s 心跳
                // （心跳兜住 MacDesk 重启后的重连——管道断开时对方已清空，重连即恢复避让）
                _ = Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    () => WidgetLink.Send(force: true));
                var beat = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                beat.Tick += (_, _) => WidgetLink.Send(force: true);
                beat.Start();
            }
            catch (Exception ex)
            {
                Log("STARTUP FAIL: " + ex);
                Application.Current.Shutdown(1);
            }
        };
        return app.Run();
    }

    static readonly object _logLock = new();
    public static void Log(string msg)
    {
        try
        {
            lock (_logLock)
                File.AppendAllText(Path.Combine(BaseDir, "proto.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} {msg}\r\n");
        }
        catch { /* 日志失败不致命 */ }
    }
}
