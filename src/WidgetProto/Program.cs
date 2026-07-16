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

    [STAThread]
    public static int Main(string[] args)
    {
        Opts = Options.Parse(args);
        Log($"=== start: n={Opts.N} control={Opts.Control} backdrop={Opts.Backdrop} origin={Opts.Origin} " +
            $"pin={Opts.Pin} widget={Opts.Widget} glass={Opts.Glass} dark={Opts.Dark} noactivate={Opts.NoActivate}");
        Log($"    raw cmdline: {Environment.CommandLine}");

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { Log("UNHANDLED: " + e.Exception); e.Handled = true; };
        app.Startup += async (_, _) =>
        {
            try
            {
                if (Opts.Control != "native")
                {
                    // 全部组件共享同一个 Environment（同一 udf）→ 共享 browser/GPU 进程，是内存实验的前提
                    var udf = Path.Combine(BaseDir, "udf");
                    var envOpts = new CoreWebView2EnvironmentOptions();
                    if (Opts.ProcPerSite) envOpts.AdditionalBrowserArguments = "--process-per-site";
                    Env = await CoreWebView2Environment.CreateAsync(null, udf, envOpts);
                    Log($"webview2 env ready, runtime={Env.BrowserVersionString} procpersite={Opts.ProcPerSite}");
                }
                var kinds = new[] { "clock", "monitor", "weather", "photo" };
                for (int i = 0; i < Opts.N; i++)
                {
                    var kind = Opts.Widget == "mixed" ? kinds[i % kinds.Length] : Opts.Widget;
                    new WidgetWindow(i, kind).Show();
                }
                Log("all windows shown");
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
