using System.Text.Json;
using System.Windows;

namespace WidgetProto;

/// <summary>
/// 宿主→组件数据桥。契约第一天就按异步长成（将来天气等联网 provider 照插，不改协议）：
/// 组件页 mw.subscribe(topic) → host.js 上报 {t:'sub'} → 这里登记并立即回放最近信封；
/// provider 仅在有可见订阅者时按 Interval 采样（订阅者门控；被全屏遮挡的组件会随 WebView2 挂起停表）；
/// 信封 {t:'data',topic,status:'ok'|'loading'|'error',stale,ts,data,error}——
/// 采样失败保留最后一份好数据照发 + stale:true，组件永远有东西可画、也不撒谎。
/// Fetch 在线程池跑（慢计数器/联网不卡 UI），结果投回 UI 线程再派发。
/// </summary>
public interface IDataProvider
{
    string Topic { get; }
    TimeSpan Interval { get; }
    /// <summary>后台线程调用；返回可 JSON 序列化对象（字段名小写）；抛异常 = error 信封。</summary>
    object Fetch();
}

/// <summary>数据桥反向通道：组件页 mw.send(topic, cmd) → provider（播控等）。UI 线程调用。</summary>
public interface ICommandSink
{
    void Command(string cmd);
}

/// <summary>
/// 参数化数据源：topic = "{Prefix}@{param}"，每个不同 param 一份独立采样/快照/生命周期
/// （如 weather@30.25,120.17——多个天气组件各配各的城市）。订阅者门控照常。
/// </summary>
public interface IParamProvider
{
    string Prefix { get; }
    TimeSpan Interval { get; }
    /// <summary>后台线程调用；抛异常 = error 信封。</summary>
    object Fetch(string param);
}

public static class DataHub
{
    sealed class Topic
    {
        public required IDataProvider Provider;
        public System.Windows.Threading.DispatcherTimer? Timer;
        public object? LastGood;       // 最后一份成功数据（error 信封里照发）
        public string? LastEnvelope;   // 新订阅者立即回放
        public bool Busy;              // 上次采样未归 → 跳过本 tick（慢 provider 不叠罗汉）
        public int Fails;
    }

    static readonly Dictionary<string, Topic> _topics = new();
    static readonly Dictionary<WidgetWindow, HashSet<string>> _subs = new();
    static readonly List<IParamProvider> _paramProviders = new();

    public static void Register(IDataProvider p) => _topics[p.Topic] = new Topic { Provider = p };
    public static void Register(IParamProvider p) => _paramProviders.Add(p);

    /// <summary>参数化 topic 的实例化适配（每个完整 topic 串一份状态）。</summary>
    sealed class ParamAdapter : IDataProvider
    {
        readonly IParamProvider _p; readonly string _topic, _param;
        public ParamAdapter(IParamProvider p, string topic, string param) { _p = p; _topic = topic; _param = param; }
        public string Topic => _topic;
        public TimeSpan Interval => _p.Interval;
        public object Fetch() => _p.Fetch(_param);
    }

    /// <summary>UI 线程（WebMessage 回调）。重复订阅幂等——页面每次导航都会重发 sub。</summary>
    public static void Subscribe(WidgetWindow w, string topic)
    {
        if (!_topics.TryGetValue(topic, out var t))
        {
            var pp = _paramProviders.FirstOrDefault(p =>
                topic.StartsWith(p.Prefix + "@", StringComparison.Ordinal) && topic.Length > p.Prefix.Length + 1);
            if (pp != null)
                _topics[topic] = t = new Topic { Provider = new ParamAdapter(pp, topic, topic[(pp.Prefix.Length + 1)..]) };
        }
        if (t == null)
        {
            Program.Log($"datahub: unknown topic '{topic}' from {w.Kind}");
            return;
        }
        if (!_subs.TryGetValue(w, out var set)) _subs[w] = set = new();
        if (set.Add(topic)) Program.Log($"datahub: {w.Kind} sub {topic}");
        // 回放快照（或 loading 占位）——新文档总是先有东西画
        w.PostJson(t.LastEnvelope ?? Env(topic, "loading", stale: false, data: null, error: null));
        EnsureRunning(t);
    }

    /// <summary>组件命令（UI 线程）。执行后 250ms 快拍一帧让 UI 跟手（给目标应用反应时间）。</summary>
    public static void Command(string topic, string cmd)
    {
        if (cmd.Length == 0 || !_topics.TryGetValue(topic, out var t)) return;
        if (t.Provider is not ICommandSink sink) return;
        try { sink.Command(cmd); }
        catch (Exception ex) { Program.Log($"datahub: {topic} cmd '{cmd}' FAIL: {ex.Message}"); }
        var once = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        once.Tick += (_, _) => { once.Stop(); Sample(t); };
        once.Start();
    }

    /// <summary>单 topic 退订（页面 mw.unsubscribe：换城市等运行期换挡）。</summary>
    public static void Unsubscribe(WidgetWindow w, string topic)
    {
        if (!_subs.TryGetValue(w, out var set) || !set.Remove(topic)) return;
        if (_topics.TryGetValue(topic, out var t)) StopIfIdle(t);
    }

    /// <summary>窗口关闭时调用；最后一个订阅者离场即停表。</summary>
    public static void Drop(WidgetWindow w)
    {
        if (!_subs.Remove(w)) return;
        foreach (var t in _topics.Values) StopIfIdle(t);
    }

    /// <summary>WebView2 成功挂起后，组件仍保留订阅关系但不再让无人可见的 topic 采样。</summary>
    public static void SetSuspended(WidgetWindow w, bool suspended)
    {
        if (!_subs.TryGetValue(w, out var set)) return;
        foreach (var topic in set)
            if (_topics.TryGetValue(topic, out var t))
            {
                if (suspended) StopIfIdle(t);
                else EnsureRunning(t); // 恢复立即快拍，组件不等下一个 Interval
            }
    }

    static void EnsureRunning(Topic t)
    {
        if (t.Timer != null) return;
        t.Timer = new System.Windows.Threading.DispatcherTimer { Interval = t.Provider.Interval };
        t.Timer.Tick += (_, _) => Sample(t);
        t.Timer.Start();
        Sample(t);   // 首个订阅者不等首个 Interval
    }

    static void StopIfIdle(Topic t)
    {
        if (t.Timer == null) return;
        foreach (var (widget, set) in _subs)
            if (set.Contains(t.Provider.Topic) && !widget.IsDataSuspended) return;
        t.Timer.Stop(); t.Timer = null;
        Program.Log($"datahub: {t.Provider.Topic} idle or occluded, sampling stopped");
    }

    static void Sample(Topic t)
    {
        if (t.Busy) return;
        t.Busy = true;
        Task.Run(() =>
        {
            object? data = null; string? err = null;
            try { data = t.Provider.Fetch(); }
            catch (Exception ex)
            {
                // COM 断连类异常 Message 常为空（真机：杀播放器瞬间的 GSMTC 调用），带上类型名
                err = ex.Message.Length > 0 ? ex.Message : ex.GetType().Name;
            }
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                t.Busy = false;
                string env;
                if (err == null)
                {
                    t.LastGood = data; t.Fails = 0;
                    env = Env(t.Provider.Topic, "ok", stale: false, data, error: null);
                }
                else
                {
                    t.Fails++;
                    if (t.Fails <= 3 || t.Fails % 20 == 0)
                        Program.Log($"datahub: {t.Provider.Topic} fetch FAIL #{t.Fails}: {err}");
                    env = Env(t.Provider.Topic, "error", stale: t.LastGood != null, t.LastGood, err);
                }
                t.LastEnvelope = env;
                foreach (var (w, set) in _subs)
                    if (set.Contains(t.Provider.Topic)) w.PostJson(env);
            });
        });
    }

    static string Env(string topic, string status, bool stale, object? data, string? error)
        => JsonSerializer.Serialize(new
        {
            t = "data", topic, status, stale,
            ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            data, error,
        });
}
