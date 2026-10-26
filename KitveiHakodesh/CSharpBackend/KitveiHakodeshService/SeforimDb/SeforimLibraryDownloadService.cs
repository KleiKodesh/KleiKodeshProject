using System.Threading.Channels;
using KitveiHakodesh.Core.SeforimLibraryDownload;

namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// Transport over Core's <see cref="SeforimLibraryDownloader"/>: starts it when the app asks,
/// resumes it when the service starts with a download pending (the installer's checkbox, or a
/// run cut short by a shutdown), checks for library updates, streams progress, and restarts
/// the service once a new library file is in place so every reader re-resolves it.
///
/// The download policy — which release, resume, verify, unpack, where — is all Core's.
/// </summary>
public sealed class SeforimLibraryDownloadService(
    HttpClient http,
    IHostApplicationLifetime lifetime,
    ILogger<SeforimLibraryDownloadService> logger) : BackgroundService
{
    private readonly SeforimLibraryDownloader _downloader = new(http);
    private readonly List<Channel<bool>> _subscribers = new();
    private CancellationToken _stopping;

    /// <summary>A download is running; the service must not idle-stop under it (Http/OwnerWatcher).</summary>
    public bool IsBusy => _downloader.IsBusy;

    public SeforimLibraryDownloadProgress Progress => _downloader.Progress;

    /// <summary>How often to ask whether an update check is due. Core decides whether one
    /// is (once a day); this only keeps a long-running service asking.</summary>
    private static readonly TimeSpan UpdateCheckPoll = TimeSpan.FromHours(6);

    /// <summary>Let start-up settle before the first update check competes for the disk.</summary>
    private static readonly TimeSpan UpdateStartupDelay = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        _downloader.Changed += OnChanged;
        _downloader.Completed += OnCompleted;
        await _downloader.WatchPendingFlagAsync(stoppingToken);

        try
        {
            await Task.Delay(UpdateStartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await _downloader.UpdateIfDueAsync(stoppingToken);
                await Task.Delay(UpdateCheckPoll, stoppingToken);
            }
        }
        catch (OperationCanceledException) { /* stopping */ }
    }

    public override void Dispose()
    {
        _downloader.Dispose();
        base.Dispose();
    }

    /// <summary>Starts (or resumes) the download in the background. Returns at once.</summary>
    public void Start()
    {
        logger.LogInformation("Library download requested");
        _ = Task.Run(() => _downloader.StartAsync(_stopping));
    }

    public bool Cancel() => _downloader.Cancel();

    /// <summary>A snapshot now and one per change, until the client disconnects.</summary>
    public async Task StreamProgressAsync(Func<SeforimLibraryDownloadProgress, Task> emit, CancellationToken ct)
    {
        var changed = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        lock (_subscribers) _subscribers.Add(changed);
        try
        {
            await emit(_downloader.Progress);
            while (true)
            {
                await changed.Reader.ReadAsync(ct);
                await emit(_downloader.Progress);
            }
        }
        catch (OperationCanceledException) { /* client left */ }
        catch (IOException) { /* client disconnected mid-stream */ }
        catch (ObjectDisposedException) { /* connection torn down under us */ }
        finally
        {
            lock (_subscribers) _subscribers.Remove(changed);
        }
    }

    private void OnChanged(SeforimLibraryDownloadProgress progress)
    {
        if (progress.Phase == SeforimLibraryDownloadPhase.Failed)
            logger.LogWarning("Library download failed: {Failure} {Detail}", progress.Failure, progress.Detail);
        lock (_subscribers)
            foreach (var subscriber in _subscribers) subscriber.Writer.TryWrite(true);
    }

    private void OnCompleted()
    {
        logger.LogInformation("New library downloaded; restarting to open it");
        // The same clean restart a DB switch uses (Common/DbChangeWatcher): the fresh process
        // resolves the new database. A short delay lets the Completed frame reach the apps first.
        _ = Task.Run(async () =>
        {
            await Task.Delay(1000);
            lifetime.StopApplication();
        });
    }
}
