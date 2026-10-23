using KitveiHakodesh.Core.LocalFiles;

namespace KitveiHakodeshService.LocalFiles;

/// <summary>
/// Transport wrapper over <see cref="LocalFileIndexCoordinator"/> — the local file index, its
/// build/rebuild lifecycle, excluded folders and search are all Core's. What stays here is DI
/// lifetime (one per service, disposed on shutdown) and the log hookup.
/// </summary>
public sealed class LocalFileIndexService : IDisposable
{
    private readonly LocalFileIndexCoordinator _coordinator;

    public LocalFileIndexService(ILogger<LocalFileIndexService> logger)
    {
        _coordinator = new LocalFileIndexCoordinator((level, message, ex) =>
        {
            switch (level)
            {
                case LocalFileIndexLogLevel.Error: logger.LogError(ex, "{Message}", message); break;
                case LocalFileIndexLogLevel.Warning: logger.LogWarning(ex, "{Message}", message); break;
                case LocalFileIndexLogLevel.Debug: logger.LogDebug(ex, "{Message}", message); break;
                default: logger.LogInformation(ex, "{Message}", message); break;
            }
        });
    }

    public List<string> GetExcludedFolders() => _coordinator.GetExcludedFolders();

    public void SetExcludedFolders(IEnumerable<string> folders) => _coordinator.SetExcludedFolders(folders);

    public LocalFileIndexStatusResult GetIndexStatus() => _coordinator.GetIndexStatus();

    public global::KitveiHakodesh.Core.Common.IndexBuildReason? LastBuildReason => _coordinator.LastBuildReason;

    public Task<LocateDocumentsResult> LocateAsync(string query, int max, CancellationToken ct) =>
        _coordinator.LocateAsync(query, max, ct);

    public Task ReindexAsync(CancellationToken ct) => _coordinator.ReindexAsync(ct);

    public void Warmup() => _coordinator.Warmup();

    /// <summary>True while the local-file index is building, searching or applying a live
    /// update — the idle memory trimmer must not run then.</summary>
    public bool IsBusy => _coordinator.IsBusy;

    public string DescribeResidentState() => _coordinator.DescribeResidentState();

    /// <summary>Idle memory trim: close the Lucene index and its live watchers; reopens and
    /// catches up from the USN journal on the next search (see Core).</summary>
    public bool ReleaseIdleResources() => _coordinator.ReleaseIdleResources();

    public void Dispose() => _coordinator.Dispose();
}
