using DocumentLocator.Client;
using KitveiHakodeshLib.Bridge;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.FileSystemSearch
{
    /// <summary>
    /// Bridge between the Vue frontend and DocumentLocatorAdapter.
    ///
    /// Push events sent to Vue:
    ///   fileSystemIndexingStatus { isIndexing: bool, message?: string }
    ///     — pushed during index build with progress messages, and once with
    ///       isIndexing=false when the index becomes ready.
    ///
    /// Actions:
    ///   fileSystemSearchPageLoad
    ///     — Vue sends on page load (only after user has consented to install).
    ///       Replies immediately with { isReady: bool }.
    ///       All blocking work (StopIfStale, IsReady, EnsureInstalled, EnsureReady)
    ///       runs on a background thread — never on the WebMessageReceived thread.
    ///
    ///   fileSystemSearch
    ///     — Vue sends a query. C# passes the limit straight through to the
    ///       DocumentLocator pipe so Lucene caps the result set server-side.
    ///       Replies with { results, total, error? }.
    /// </summary>
    public class FileSystemSearchHandler : IDisposable
    {
        // Must match MAX_RESULTS in useLocalFileSearch.ts
        private const int DefaultMaxResults = 5000;

        private readonly WebBridge _bridge;
        private readonly DocumentLocatorAdapter _adapter;
        private CancellationTokenSource _currentSearch;
        private CancellationTokenSource _ensureReadyCts;

        public FileSystemSearchHandler(WebBridge bridge)
        {
            _bridge  = bridge;
            _adapter = new DocumentLocatorAdapter();
        }

        // ── Page load: check ready ────────────────────────────────────────────────

        /// <summary>
        /// Vue sends this on page load to find out if the index is ready.
        /// This is only called after the user has consented to install the service.
        ///
        /// Replies immediately with { isReady: false } and moves ALL blocking work
        /// (StopIfStale, IsInstalled, IsReady, EnsureInstalled, EnsureReady) to a
        /// background thread so the WebMessageReceived handler returns immediately.
        /// </summary>
        public void HandlePageLoad(string id)
        {
            // Cancel any previous ensure-ready loop (e.g. user navigated away and back).
            var previous = Interlocked.Exchange(ref _ensureReadyCts, new CancellationTokenSource());
            previous?.Cancel();
            previous?.Dispose();

            var cts = _ensureReadyCts;

            // Reply immediately — everything else is background work.
            // We always say isReady=false here and let the background thread push
            // fileSystemIndexingStatus { isIndexing: false } when actually ready.
            // This avoids the complexity of racing a synchronous IsReady check
            // against the WebMessageReceived thread constraint.
            _bridge.Reply(id, new { isReady = false });

            Task.Run(() => RunEnsureReadyLoop(cts.Token), cts.Token);
        }

        private void RunEnsureReadyLoop(CancellationToken ct)
        {
            try
            {
                // Stop a stale running service binary so the fresh exe is picked up.
                ServiceBridge.StopIfStale();

                // Install the service (UAC prompt once) if it isn't registered yet.
                if (!ServiceBridge.IsInstalled())
                {
                    PushIndexingStatus(isIndexing: true, message: "מתקין את שירות האינדקס…");
                    bool installed = ServiceBridge.EnsureInstalled();
                    if (!installed)
                    {
                        // User cancelled the UAC prompt.
                        PushIndexingStatus(isIndexing: true, message: "ההתקנה בוטלה על ידי המשתמש.");
                        return;
                    }
                }

                // Mirror WaitForIndexAsync from the demo — polls GetStatusAsync in a loop,
                // forwarding progress messages to Vue via push events.
                _adapter.WaitUntilReadyAsync(ct, message =>
                    PushIndexingStatus(isIndexing: true, message: message))
                    .GetAwaiter().GetResult();

                // Index is ready.
                PushIndexingStatus(isIndexing: false, message: null);
            }
            catch (OperationCanceledException) { /* page closed or superseded — fine */ }
            catch (AggregateException ae) when (Unwrap(ae) is OperationCanceledException) { }
            catch (Exception ex)
            {
                PushIndexingStatus(isIndexing: true, message: "שגיאה: " + Unwrap(ex).Message);
            }
        }

        private static Exception Unwrap(Exception ex)
        {
            while (ex is AggregateException ae && ae.InnerException != null)
                ex = ae.InnerException;
            return ex;
        }

        private void PushIndexingStatus(bool isIndexing, string message)
        {
            if (message != null)
                _bridge.PushEvent(new { @event = "fileSystemIndexingStatus", isIndexing, message });
            else
                _bridge.PushEvent(new { @event = "fileSystemIndexingStatus", isIndexing });
        }

        // ── Reindex ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Vue sends this when the user requests a full DocumentLocator index rebuild.
        /// Replies immediately with {} and runs the reindex + wait-until-ready loop
        /// on a background thread, forwarding progress via fileSystemIndexingStatus
        /// push events — the same events the search page already listens to.
        /// </summary>
        public void HandleReindex(string id)
        {
            // Cancel any previous ensure-ready loop so it doesn't race.
            var previous = Interlocked.Exchange(ref _ensureReadyCts, new CancellationTokenSource());
            previous?.Cancel();
            previous?.Dispose();

            var cts = _ensureReadyCts;

            _bridge.Reply(id, new { });

            Task.Run(async () =>
            {
                try
                {
                    PushIndexingStatus(isIndexing: true, message: "שולח בקשת בנייה מחדש…");
                    await _adapter.ReindexAsync(cts.Token).ConfigureAwait(false);
                    await _adapter.WaitUntilReadyAsync(cts.Token, message =>
                        PushIndexingStatus(isIndexing: true, message: message))
                        .ConfigureAwait(false);
                    PushIndexingStatus(isIndexing: false, message: null);
                }
                catch (OperationCanceledException) { }
                catch (AggregateException ae) when (Unwrap(ae) is OperationCanceledException) { }
                catch (Exception ex)
                {
                    PushIndexingStatus(isIndexing: true, message: "שגיאה: " + Unwrap(ex).Message);
                }
            }, cts.Token);
        }

        // ── Search ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Vue sends this when it has a query to run.
        /// The limit is passed straight through to the service so Lucene caps the
        /// result set server-side (faster than fetching everything and truncating here).
        /// Replies with { results, total, error? }.
        /// total reflects the full match count from the index, even when results are capped.
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

            Task.Run(async () =>
            {
                try
                {
                    var (results, total) = await _adapter.SearchAsync(query, max, cts.Token)
                        .ConfigureAwait(false);

                    if (cts.Token.IsCancellationRequested) return;

                    var reply = new System.Collections.Generic.List<object>(results.Count);
                    foreach (var r in results)
                        reply.Add(new { fileName = r.FileName, path = r.Path });

                    _bridge.Reply(id, new { results = reply, total });
                }
                catch (OperationCanceledException)
                {
                    // Superseded by a newer search — no reply needed.
                }
                catch (Exception ex)
                {
                    // If the service is no longer installed (user uninstalled it while
                    // the app was running), tell Vue so it can re-show the install prompt.
                    if (!ServiceBridge.IsInstalled())
                    {
                        _bridge.Reply(id, new { notInstalled = true });
                        return;
                    }
                    _bridge.Reply(id, new { error = ex.Message });
                }
            });
        }

        public void Dispose()
        {
            var ensureCts = Interlocked.Exchange(ref _ensureReadyCts, null);
            ensureCts?.Cancel();
            ensureCts?.Dispose();

            var searchCts = Interlocked.Exchange(ref _currentSearch, null);
            searchCts?.Cancel();
            searchCts?.Dispose();
        }
    }
}
