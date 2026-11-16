using Windows.Media.Control;
using Windows.Storage.Streams;

namespace SpotifyToast;

internal sealed class TrackInfo : IDisposable
{
    public string Title { get; }
    public string Artist { get; }
    public string Album { get; }
    public Bitmap? Cover { get; }

    public TrackInfo(string title, string artist, string album, Bitmap? cover)
    {
        Title = title; Artist = artist; Album = album; Cover = cover;
    }

    public void Dispose() => Cover?.Dispose();
}

/// <summary>
/// Escuta o controle de mídia do Windows (o mesmo do painel de volume). Não faz polling:
/// só acorda quando o Spotify avisa que algo mudou.
/// </summary>
internal sealed class MediaWatcher : IDisposable
{
    readonly SynchronizationContext ui;
    readonly System.Threading.Timer debounce;
    readonly Dictionary<string, GlobalSystemMediaTransportControlsSession> sessions = new();
    readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> onProps;
    readonly Windows.Foundation.TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> onPlayback;

    GlobalSystemMediaTransportControlsSessionManager? manager;
    string? lastKey;
    bool primed, checking, recheck;

    public event Action<TrackInfo>? TrackChanged;
    public event Action<TrackInfo>? TrackKnown;

    public MediaWatcher(SynchronizationContext ui)
    {
        this.ui = ui;
        debounce = new System.Threading.Timer(_ => this.ui.Post(_ => _ = CheckAsync(), null));
        onProps = (_, _) => Schedule(450);
        onPlayback = (_, _) => Schedule(450);
    }

    public async Task StartAsync()
    {
        manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        manager.SessionsChanged += (_, _) => ui.Post(_ => RefreshSessions(), null);
        RefreshSessions();
    }

    static bool IsSpotify(string id) => id.Contains("spotify", StringComparison.OrdinalIgnoreCase);

    void RefreshSessions()
    {
        if (manager == null) return;
        Unsubscribe();
        foreach (var s in manager.GetSessions())
        {
            string id;
            try { id = s.SourceAppUserModelId ?? ""; } catch { continue; }
            if (!IsSpotify(id) || sessions.ContainsKey(id)) continue;
            sessions[id] = s;
            s.MediaPropertiesChanged += onProps;
            s.PlaybackInfoChanged += onPlayback;
        }
        Schedule(250);
    }

    void Unsubscribe()
    {
        foreach (var s in sessions.Values)
        {
            try { s.MediaPropertiesChanged -= onProps; s.PlaybackInfoChanged -= onPlayback; } catch { }
        }
        sessions.Clear();
    }

    void Schedule(int ms) => debounce.Change(ms, Timeout.Infinite);

    async Task CheckAsync()
    {
        if (checking) { recheck = true; return; }
        checking = true;
        try
        {
            foreach (var s in sessions.Values.ToList())
            {
                GlobalSystemMediaTransportControlsSessionMediaProperties? props;
                try
                {
                    if (s.GetPlaybackInfo()?.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) continue;
                    props = await s.TryGetMediaPropertiesAsync();
                }
                catch { continue; }
                if (props == null || string.IsNullOrWhiteSpace(props.Title)) continue;

                string key = props.Title + "\u0001" + props.Artist;
                if (key == lastKey) return;
                lastKey = key;

                // música que já tocava quando o app abriu: só guarda, sem popup
                if (!primed)
                {
                    primed = true;
                    TrackKnown?.Invoke(new TrackInfo(props.Title, props.Artist ?? "", props.AlbumTitle ?? "", await ReadCover(props.Thumbnail)));
                    return;
                }

                var cover = await ReadCover(props.Thumbnail);
                if (cover == null)
                {
                    // o Spotify às vezes manda a capa um pouco depois do título
                    await Task.Delay(700);
                    try
                    {
                        var again = await s.TryGetMediaPropertiesAsync();
                        if (again != null && again.Title + "\u0001" + again.Artist == key)
                            cover = await ReadCover(again.Thumbnail);
                    }
                    catch { }
                }
                TrackChanged?.Invoke(new TrackInfo(props.Title, props.Artist ?? "", props.AlbumTitle ?? "", cover));
                return;
            }
            primed = true;
        }
        finally
        {
            checking = false;
            if (recheck) { recheck = false; Schedule(200); }
        }
    }

    static async Task<Bitmap?> ReadCover(IRandomAccessStreamReference? reference)
    {
        if (reference == null) return null;
        try
        {
            using var ras = await reference.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            if (ms.Length == 0) return null;
            ms.Position = 0;
            using var img = Image.FromStream(ms);
            // guarda uma cópia pequena: o popup nunca usa mais que isso
            return Fx.SquareCover(img, Math.Min(256, Math.Min(img.Width, img.Height)));
        }
        catch { return null; }
    }

    public void Dispose()
    {
        debounce.Dispose();
        Unsubscribe();
    }
}
