using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WidgetProto;

/// <summary>
/// 正在播放数据源（topic "music"）：GSMTC 系统媒体会话——任何集成 SMTC 的播放器
/// （Spotify/新 Media Player/浏览器…）都上报，零第三方依赖、零联网。
/// 轮询 1s 快照（事件驱动后续再说）；封面按曲目键缓存、变了才重编码 data URI；
/// 播放中用 LastUpdatedTime 漂移校正进度（部分应用 Timeline 只稀疏更新）。
/// 反向通道：playpause / next / prev（fire-and-forget，250ms 后 DataHub 快拍跟手）。
/// </summary>
public sealed class MusicProvider : IDataProvider, ICommandSink
{
    public string Topic => "music";
    public TimeSpan Interval => TimeSpan.FromMilliseconds(1000);

    GlobalSystemMediaTransportControlsSessionManager? _mgr;
    string? _artKey, _artData;

    public object Fetch()
    {
        _mgr ??= GlobalSystemMediaTransportControlsSessionManager.RequestAsync().GetAwaiter().GetResult();
        var s = _mgr.GetCurrentSession();
        if (s == null)
        {
            _artKey = null; _artData = null;
            return new { hasSession = false };
        }

        var props = s.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
        var pb = s.GetPlaybackInfo();
        var tl = s.GetTimelineProperties();
        bool playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        string key = props.Title + "" + props.Artist + "" + props.AlbumTitle;
        if (key != _artKey)
        {
            _artKey = key;
            _artData = ReadThumb(props.Thumbnail);
        }

        // 进度：Timeline 稀疏更新的应用（如浏览器）靠 LastUpdatedTime 外推
        double dur = tl.EndTime.TotalSeconds;
        double pos = tl.Position.TotalSeconds;
        if (playing) pos += (DateTimeOffset.UtcNow - tl.LastUpdatedTime).TotalSeconds;
        if (dur > 0) pos = Math.Clamp(pos, 0, dur);

        return new
        {
            hasSession = true, playing,
            title = props.Title, artist = props.Artist, album = props.AlbumTitle,
            posSec = Math.Round(pos, 1), durSec = Math.Round(dur, 1),
            app = s.SourceAppUserModelId,
            art = _artData,
        };
    }

    static string? ReadThumb(IRandomAccessStreamReference? r)
    {
        if (r == null) return null;
        try
        {
            using var stream = r.OpenReadAsync().GetAwaiter().GetResult();
            var size = (uint)Math.Min(stream.Size, 512 * 1024);   // 封面上限 512K，够 SMTC 缩略图
            if (size == 0) return null;
            var buf = new Windows.Storage.Streams.Buffer(size);
            stream.ReadAsync(buf, size, InputStreamOptions.ReadAhead).GetAwaiter().GetResult();
            var bytes = new byte[buf.Length];
            using var dr = DataReader.FromBuffer(buf);
            dr.ReadBytes(bytes);
            var mime = string.IsNullOrEmpty(stream.ContentType) ? "image/png" : stream.ContentType;
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch { return null; }   // 封面拿不到不拦整帧
    }

    public void Command(string cmd)
    {
        var s = _mgr?.GetCurrentSession();
        if (s == null) return;
        switch (cmd)
        {
            case "playpause": _ = s.TryTogglePlayPauseAsync(); break;
            case "next": _ = s.TrySkipNextAsync(); break;
            case "prev": _ = s.TrySkipPreviousAsync(); break;
        }
    }
}
