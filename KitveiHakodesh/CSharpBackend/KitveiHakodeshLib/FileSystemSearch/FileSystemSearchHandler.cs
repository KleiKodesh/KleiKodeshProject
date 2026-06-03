using KitveiHakodeshLib.Bridge;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.FileSystemSearch
{
    /// <summary>
    /// Bridge between the Vue frontend and EverythingSearchClient via EverythingSearchAdapter.
    ///
    /// Three actions:
    ///
    ///   fileSystemSearchPageLoad — Vue asks: is Everything ready?
    ///     Reply immediately with { isReady: bool }.
    ///     If not ready, start EnsureReady on a background thread and push
    ///     fileSystemIndexingStatus = false when it completes.
    ///
    ///   fileSystemSearch — Vue sends a query.
    ///     Run the search, reply with results.
    ///     Vue only sends this when it already knows isReady = true.
    /// </summary>
    public class FileSystemSearchHandler : IDisposable
    {
        private const int DefaultMaxResults = 200;

        private readonly WebBridge _bridge;
        private readonly EverythingSearchAdapter _adapter;
        private CancellationTokenSource _currentSearch;

        public FileSystemSearchHandler(WebBridge bridge)
        {
            _bridge  = bridge;
            _adapter = new EverythingSearchAdapter { FilterToDocumentTypes = true };
        }

        // ── Page load: check ready ────────────────────────────────────────────

        /// <summary>
        /// Vue sends this on page load to find out if Everything is ready.
        /// Replies immediately with { isReady: bool }.
        /// If not ready, kicks off EnsureReady on a background thread and
        /// pushes fileSystemIndexingStatus { isIndexing: false } to Vue when done.
        /// </summary>
        public void HandlePageLoad(string id)
        {
            bool isReady = _adapter.IsReady();
            _bridge.Reply(id, new { isReady });

            if (!isReady)
                StartEnsureReady();
        }

        private void StartEnsureReady()
        {
            Task.Run(() =>
            {
                try
                {
                    _adapter.EnsureReady(CancellationToken.None);
                    _bridge.PushEvent(new { @event = "fileSystemIndexingStatus", isIndexing = false });
                }
                catch (Exception)
                {
                    // Everything failed to start — leave Vue showing the spinner.
                    // Nothing more we can do without user action.
                }
            });
        }

        // ── Search ────────────────────────────────────────────────────────────

        /// <summary>
        /// Vue sends this when it has a query to run.
        /// Vue only sends searches when isReady = true, so no readiness check needed here.
        /// </summary>
        public void HandleSearch(JsonElement root, string id)
        {
            string query = root.TryGetProperty("query", out var q) ? (q.GetString() ?? "") : "";
            int max = root.TryGetProperty("max", out var m) && m.TryGetInt32(out int mv)
                ? mv
                : DefaultMaxResults;

            // Cancel previous in-flight search so rapid keystrokes don't stack up.
            var previous = Interlocked.Exchange(ref _currentSearch, new CancellationTokenSource());
            previous?.Cancel();
            previous?.Dispose();

            var cts = _currentSearch;

            Task.Run(() =>
            {
                try
                {
                    var results = _adapter.Search(query, max, cts.Token);

                    if (cts.Token.IsCancellationRequested) return;

                    // Project to anonymous objects with camelCase keys to match Vue expectations.
                    var reply = new System.Collections.Generic.List<object>(results.Count);
                    foreach (var r in results)
                        reply.Add(new { fileName = r.FileName, path = r.Path });

                    _bridge.Reply(id, new { results = reply, total = reply.Count });
                }
                catch (OperationCanceledException)
                {
                    // Superseded by a newer search — no reply needed.
                }
                catch (Exception ex)
                {
                    _bridge.Reply(id, new { error = ex.Message });
                }
            }, CancellationToken.None);
        }
        public void Dispose()
        {
            var cts = Interlocked.Exchange(ref _currentSearch, null);
            cts?.Cancel();
            cts?.Dispose();
        }
    }
}
