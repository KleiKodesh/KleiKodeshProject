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
    /// Actions:
    ///   fileSystemSearch
    ///     — Vue sends a query. C# starts the service on demand if it is stopped,
    ///       waits until the index is ready, then executes the search and replies.
    ///       Vue shows its own loading animation during the wait — no push events needed.
    ///       Replies with { results, total } on success or { error } on failure.
    ///
    ///   ResetDocumentLocatorIndex
    ///     — Wipes and rebuilds the index from scratch. Replies immediately with {}.
    /// </summary>
    public class FileSystemSearchHandler : IDisposable
    {
        // Must match MAX_RESULTS in useLocalFileSearch.ts
        private const int DefaultMaxResults = 5000;

        private readonly WebBridge _bridge;
        private readonly DocumentLocatorAdapter _adapter;
        private CancellationTokenSource _currentSearch;
        private CancellationTokenSource _reindexCts;

        public FileSystemSearchHandler(WebBridge bridge)
        {
            _bridge  = bridge;
            _adapter = new DocumentLocatorAdapter();
        }

        // ── Search ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Vue sends this on page load to warm up the service in the background.
        /// Replies immediately with {} so the RPC completes, then starts the service
        /// and waits for the index on a background thread — silently. No push events.
        /// </summary>
        public void HandleWarmup(string id)
        {
            _bridge.Reply(id, new { });

            Task.Run(() =>
            {
                try
                {
                    ServiceBridge.StopIfStale();
                    _adapter.WaitUntilReadyAsync(CancellationToken.None, _ => { })
                        .GetAwaiter().GetResult();
                }
                catch { /* warmup is best-effort — silently ignore any failure */ }
            });
        }

        // ── Search ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Executes a search. Starts the service on demand if it has stopped, waits
        /// until the index is ready, then replies with { results, total } or { error }.
        /// Rapid keystrokes cancel the previous in-flight call.
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
                    // Start-on-demand: start the service if it has stopped, then wait
                    // until its index is ready. No-op if the service is already running
                    // and the index is ready. This blocks the search reply until ready,
                    // which is intentional — Vue's loading animation covers the wait.
                    ServiceBridge.StopIfStale();
                    await _adapter.WaitUntilReadyAsync(cts.Token, _ => { })
                        .ConfigureAwait(false);

                    if (cts.Token.IsCancellationRequested) return;

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
                catch (AggregateException ae) when (Unwrap(ae) is OperationCanceledException)
                {
                    // Same — superseded.
                }
                catch (Exception ex)
                {
                    _bridge.Reply(id, new { error = Unwrap(ex).Message });
                }
            });
        }

        // ── Reindex ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Wipes and rebuilds the DocumentLocator index from scratch.
        /// Replies immediately with {}. Progress is not pushed to Vue — the next
        /// search call will block in WaitUntilReadyAsync until the rebuild finishes.
        /// </summary>
        public void HandleReindex(string id)
        {
            var previous = Interlocked.Exchange(ref _reindexCts, new CancellationTokenSource());
            previous?.Cancel();
            previous?.Dispose();

            var cts = _reindexCts;
            _bridge.Reply(id, new { });

            Task.Run(async () =>
            {
                try
                {
                    await _adapter.ReindexAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (AggregateException ae) when (Unwrap(ae) is OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.WriteLine("[FileSystemSearch] Reindex error: " + Unwrap(ex).Message);
                }
            }, cts.Token);
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Exception Unwrap(Exception ex)
        {
            while (ex is AggregateException ae && ae.InnerException != null)
                ex = ae.InnerException;
            return ex;
        }

        public void Dispose()
        {
            var reindexCts = Interlocked.Exchange(ref _reindexCts, null);
            reindexCts?.Cancel();
            reindexCts?.Dispose();

            var searchCts = Interlocked.Exchange(ref _currentSearch, null);
            searchCts?.Cancel();
            searchCts?.Dispose();
        }
    }
}
