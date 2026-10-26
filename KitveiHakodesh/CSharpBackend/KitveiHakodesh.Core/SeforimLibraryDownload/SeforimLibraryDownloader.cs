using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KitveiHakodesh.Core.Common;
using KitveiHakodesh.Core.Settings;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// Gets Otzaria's library onto this machine when none is installed, and keeps the copy it
    /// downloaded up to date. The entry point every host uses; the jobs themselves are
    /// <see cref="SeforimLibraryFullDownload"/> and <see cref="SeforimLibraryPatchUpdate"/>.
    ///
    /// INSTALL. <see cref="StartAsync"/> when the user asks. The request is persisted in
    /// <see cref="SeforimLibraryDownloadPendingFlag"/>, so a download cut short by a shutdown
    /// carries on from its bytes on disk the next time any host runs
    /// (<see cref="WatchPendingFlagAsync"/>).
    ///
    /// UPDATE. <see cref="UpdateIfDueAsync"/>, at most once a day, and only for the library
    /// this app downloaded and is using — never Otzaria's or Zayit's own copy, which their apps
    /// update. Patches apply in place; when no patch route exists the whole library downloads
    /// to a staged file that <see cref="SeforimLibraryStagedDatabase"/> swaps in at the next start.
    ///
    /// One run at a time per process.
    /// </summary>
    public sealed class SeforimLibraryDownloader : IDisposable
    {
        private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromDays(1);

        private readonly SeforimLibraryFullDownload _fullDownload;
        private readonly SeforimLibraryPatchUpdate _patchUpdate;
        private readonly SeforimLibraryDownloadProgressReporter _progress = new SeforimLibraryDownloadProgressReporter();
        private readonly object _gate = new object();

        private CancellationTokenSource? _running;
        private RegistryValueWatcher? _pendingFlagWatcher;

        public SeforimLibraryDownloader(HttpClient http)
        {
            _fullDownload = new SeforimLibraryFullDownload(http);
            _patchUpdate = new SeforimLibraryPatchUpdate(http);
            _progress.Changed += () => Changed?.Invoke(Progress);
        }

        /// <summary>Raised on every phase change and, throttled, as bytes move. Handlers must be quick.</summary>
        public event Action<SeforimLibraryDownloadProgress>? Changed;

        /// <summary>
        /// Raised when a new library file is in place or staged. The host must re-resolve the
        /// library — the service restarts, which also swaps in a staged file. Not raised for a
        /// patch: that changes the open database in place, and the index watcher picks it up.
        /// </summary>
        public event Action? Completed;

        /// <summary>A copy of the current state.</summary>
        public SeforimLibraryDownloadProgress Progress
        {
            get
            {
                var snapshot = _progress.Snapshot();
                snapshot.Pending = SeforimLibraryDownloadPendingFlag.IsSet;
                snapshot.Running = IsBusy;
                return snapshot;
            }
        }

        /// <summary>True while a run is executing — a host must not shut down under it.</summary>
        public bool IsBusy { get { lock (_gate) return _running != null; } }

        // ── Install ─────────────────────────────────────────────────────────────────

        /// <summary>Records the user's request and runs it. Returns at once when a run is already going.</summary>
        public Task StartAsync(CancellationToken hostStopping)
        {
            // A library already resolves (ours finished, or Otzaria/Zayit appeared): nothing to
            // download, and downloading would end by moving a file over one that is open.
            if (SeforimDbPathResolver.Resolve().Found)
            {
                SeforimLibraryDownloadPendingFlag.Clear();
                return Task.CompletedTask;
            }
            SeforimLibraryDownloadPendingFlag.Set();
            return RunExclusiveAsync(InstallAsync, hostStopping);
        }

        /// <summary>
        /// Resumes now if a download is pending, and again whenever the pending flag is set
        /// from outside this process — the installer's checkbox, written while the service is
        /// already running. The flag shares the Database registry key with the DB path; that
        /// watcher compares resolved paths, so setting the flag never restarts anything.
        /// </summary>
        public Task WatchPendingFlagAsync(CancellationToken hostStopping)
        {
            _pendingFlagWatcher ??= new RegistryValueWatcher(
                SeforimDbPathResolver.RegistryKeyPath,
                () => _ = ResumeIfPendingAsync(hostStopping),
                "khs-library-download-watch");
            _pendingFlagWatcher.Start();
            return ResumeIfPendingAsync(hostStopping);
        }

        private Task ResumeIfPendingAsync(CancellationToken hostStopping)
        {
            if (!SeforimLibraryDownloadPendingFlag.IsSet) return Task.CompletedTask;
            if (SeforimDbPathResolver.Resolve().Found)
            {
                // Finished last time but stopped before clearing the flag, or another library
                // was installed since it was set.
                SeforimLibraryDownloadPendingFlag.Clear();
                return Task.CompletedTask;
            }
            return RunExclusiveAsync(InstallAsync, hostStopping);
        }

        private async Task InstallAsync(CancellationToken cancellationToken)
        {
            if (await _fullDownload.RunAsync(SeforimLibraryDownloadLocations.DatabasePath, _progress, cancellationToken).ConfigureAwait(false))
            {
                SeforimLibraryDownloadPendingFlag.Clear();
                _progress.Phase(SeforimLibraryDownloadPhase.Completed);
                Completed?.Invoke();
                return;
            }
            // A failure retrying cannot fix (no release, no space, bad bytes) drops the request:
            // otherwise every service start — and every write to the Database registry key the
            // flag watcher sees — would silently fetch gigabytes again. The user asks again.
            SeforimLibraryDownloadPendingFlag.Clear();
        }

        /// <summary>
        /// Stops the run and drops the request. The bytes already fetched stay on disk, so asking
        /// again later resumes rather than restarts.
        /// </summary>
        public bool Cancel()
        {
            SeforimLibraryDownloadPendingFlag.Clear();
            lock (_gate)
            {
                if (_running == null) return false;
                try { _running.Cancel(); } catch (ObjectDisposedException) { }
                return true;
            }
        }

        // ── Update ──────────────────────────────────────────────────────────────────

        /// <summary>Updates the library this app downloaded, when it is the one in use and a day
        /// has passed since the last check. A no-op otherwise, and while another run is going.</summary>
        public Task UpdateIfDueAsync(CancellationToken hostStopping)
        {
            string ours = SeforimLibraryDownloadLocations.DatabasePath;
            string? inUse = SeforimDbPathResolver.Resolve().Path;
            if (inUse == null || !string.Equals(Path.GetFullPath(inUse), ours, StringComparison.OrdinalIgnoreCase)) return Task.CompletedTask;
            if (SeforimLibraryStagedDatabase.Exists) return Task.CompletedTask; // a newer one is already waiting
            if (DateTime.UtcNow - LastUpdateCheck() < UpdateCheckInterval) return Task.CompletedTask;
            return RunExclusiveAsync(ct => UpdateAsync(ours, ct), hostStopping);
        }

        private async Task UpdateAsync(string dbPath, CancellationToken cancellationToken)
        {
            var outcome = await _patchUpdate.RunAsync(dbPath, _progress, cancellationToken).ConfigureAwait(false);
            if (outcome == SeforimLibraryUpdateOutcome.NeedsFullDownload)
            {
                if (!await _fullDownload.RunAsync(SeforimLibraryDownloadLocations.StagedPath, _progress, cancellationToken).ConfigureAwait(false))
                    return;
                RecordUpdateCheck();
                _progress.Phase(SeforimLibraryDownloadPhase.Completed);
                Completed?.Invoke();
                return;
            }
            RecordUpdateCheck();
            if (outcome == SeforimLibraryUpdateOutcome.Patched)
            {
                // Patched in place: the host's open connections and caches still hold the old
                // release, so it re-resolves exactly as after a new file.
                _progress.Phase(SeforimLibraryDownloadPhase.Completed);
                Completed?.Invoke();
                return;
            }
            _progress.Phase(SeforimLibraryDownloadPhase.Idle);
        }

        private static DateTime LastUpdateCheck()
        {
            string stamp = AppSettingsRegistry.Get("KitveiHakodesh", "Database", "LibraryUpdateCheckedUtc", "");
            return DateTime.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var when)
                ? when
                : DateTime.MinValue;
        }

        private static void RecordUpdateCheck() =>
            AppSettingsRegistry.Set("KitveiHakodesh", "Database", "LibraryUpdateCheckedUtc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

        // ── One run at a time ───────────────────────────────────────────────────────

        private async Task RunExclusiveAsync(Func<CancellationToken, Task> run, CancellationToken hostStopping)
        {
            CancellationTokenSource cancellation;
            lock (_gate)
            {
                if (_running != null) return;
                cancellation = _running = CancellationTokenSource.CreateLinkedTokenSource(hostStopping);
            }

            try
            {
                await run(cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (hostStopping.IsCancellationRequested)
            {
                // The host is stopping; the pending flag stays and the next start resumes.
            }
            catch (OperationCanceledException)
            {
                _progress.Fail(SeforimLibraryDownloadFailure.Cancelled, null);
            }
            catch (HttpRequestException ex)
            {
                _progress.Fail(SeforimLibraryDownloadFailure.Network, ex.ToString());
            }
            catch (InvalidDataException ex)
            {
                _progress.Fail(SeforimLibraryDownloadFailure.CorruptArchive, ex.ToString());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                _progress.Fail(SeforimLibraryDownloadFailure.DiskError, ex.ToString());
            }
            catch (Exception ex)
            {
                // Anything else (a proxy's HTML for JSON, a malformed resume record) must end
                // this run, not escape: the host awaits some runs directly, and an exception
                // out of a hosted service stops the whole host.
                _progress.Fail(SeforimLibraryDownloadFailure.Unexpected, ex.ToString());
            }
            finally
            {
                lock (_gate) _running = null;
                cancellation.Dispose();
                _progress.Notify();
            }
        }

        public void Dispose() => _pendingFlagWatcher?.Dispose();
    }
}
