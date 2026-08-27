using DocumentLocator.Client;
using KitveiHakodeshLib.Bridge;
using System;
using System.Collections.Generic;
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
    ///
    ///   openExcludedFoldersManager
    ///     — Opens the ExcludedFoldersForm WinForms dialog on the UI thread.
    ///       Replies with { saved: true } after the user confirms, or { saved: false }
    ///       if the user cancels. Persists changes via AppSettings.
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
            // Cancel but do NOT dispose: the superseded task is still reading cts.Token
            // below, and Token throws ObjectDisposedException once the source is gone —
            // which is not an OperationCanceledException, so it would skip the "superseded"
            // catches and reply with a spurious error. Each task disposes its own in the
            // finally below.
            //
            // Keep our OWN instance instead of re-reading the field afterwards: a concurrent
            // search can supersede us and its finally can null the field or dispose what it
            // points at, so a re-read yields null or a disposed source — and a disposed
            // source's Token throws the same spurious error all over again.
            var cts = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _currentSearch, cts);
            // Already disposed means that search finished on its own — nothing to cancel.
            try { previous?.Cancel(); } catch (ObjectDisposedException) { }

            Task.Run(async () =>
            {
                try
                {
                    // Start-on-demand: start the service if it has stopped, then wait
                    // until its index is ready. No-op if the service is already running
                    // and the index is ready. This blocks the search reply until ready,
                    // which is intentional — Vue's loading animation covers the wait.
                    ServiceBridge.StopIfStale();
                    // mayPromptForInstall: the user typed a query, so if the service was never
                    // registered this is the moment to ask for the elevation that registers it.
                    await _adapter.WaitUntilReadyAsync(cts.Token, _ => { }, mayPromptForInstall: true)
                        .ConfigureAwait(false);

                    if (cts.Token.IsCancellationRequested) return;

                    var (results, total) = await _adapter.SearchAsync(query, max, cts.Token)
                        .ConfigureAwait(false);

                    if (cts.Token.IsCancellationRequested) return;

                    var reply = new System.Collections.Generic.List<object>(results.Count);
                    foreach (var r in results)
                        reply.Add(new { fileName = r.FileName, path = r.Path, modifiedDate = r.ModifiedDate, addinName = r.AddinName });

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
                finally
                {
                    // Ours to dispose — we are the only reader of this token, and we are done
                    // with it. Clear the field first so a superseding search cannot pick it up.
                    Interlocked.CompareExchange(ref _currentSearch, null, cts);
                    cts.Dispose();
                }
            });
        }

        // ── Reindex ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Wipes and rebuilds the DocumentLocator index from scratch.
        /// Replies immediately with {}. Progress is not pushed to Vue — the next
        /// search call will block in WaitUntilReadyAsync until the rebuild finishes.
        ///
        /// The early reply is deliberate, and matches SearchHandler.HandleDeleteIndex. The app
        /// reset awaits this call, so replying only on completion would stall the whole reset
        /// behind a full MFT re-crawl — minutes — to no benefit. Nothing the reload does can
        /// collide with the rebuild: it owns its own Lucene index, and readers wait on
        /// WaitUntilReadyAsync rather than reading a half-built one.
        /// </summary>
        public void HandleReindex(string id)
        {
            // Cancel only — see HandleSearch: the superseded reindex still reads this token.
            // Own instance, not a field re-read — see HandleSearch.
            var cts = new CancellationTokenSource();
            var previous = Interlocked.Exchange(ref _reindexCts, cts);
            try { previous?.Cancel(); } catch (ObjectDisposedException) { } // see HandleSearch
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
                finally
                {
                    Interlocked.CompareExchange(ref _reindexCts, null, cts);
                    cts.Dispose();
                }
            });
            // NOTE: deliberately NOT Task.Run(..., cts.Token). That form hands the token to
            // the SCHEDULER, so a token already cancelled before the pool dequeues the
            // delegate makes the task go straight to Canceled and the body — including the
            // finally above — never runs, leaking the CTS and leaving the field stale. The
            // body observes cancellation through ReindexAsync(cts.Token) instead.
        }

        // ── Excluded folders manager ──────────────────────────────────────────────

        /// <summary>
        /// A reference to the host control used to marshal UI work onto the UI thread.
        /// Set by AppViewer after construction.
        /// </summary>
        public System.Windows.Forms.Control UiControl { get; set; }

        /// <summary>
        /// Opens the ExcludedFoldersForm dialog on the UI thread.
        /// Fetches the current list from the service first, then saves the updated
        /// list back to the service (and thus to excluded_folders.json) if the user
        /// confirms. Replies with { saved: true/false }.
        /// </summary>
        public void HandleOpenExcludedFoldersManager(string id)
        {
            Task.Run(async () =>
            {
                try
                {
                    // Just ensure the service process is running — we only need the
                    // pipe to be available for getExcludedFolders / setExcludedFolders.
                    // We do NOT wait for the index to be ready; that would block the
                    // dialog from opening while a full MFT crawl is in progress.
                    ServiceBridge.StopIfStale();
                    try { ServiceBridge.StartService(); } catch { /* already running */ }

                    // Give the pipe a moment to become available if the service just started.
                    await Task.Delay(600).ConfigureAwait(false);

                    var currentFolders = await ServiceBridge
                        .GetExcludedFoldersAsync(CancellationToken.None)
                        .ConfigureAwait(false);

                    // Show the WinForms dialog on the UI thread.
                    bool saved = false;
                    List<string> updatedFolders = null;

                    var control = UiControl;
                    if (control == null || control.IsDisposed)
                    {
                        _bridge.Reply(id, new { error = "UI context not available" });
                        return;
                    }

                    // BeginInvoke + TaskCompletionSource so we can await the dialog result
                    // without blocking the thread-pool thread.
                    var tcs = new TaskCompletionSource<bool>();
                    control.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            using (var form = new ExcludedFoldersForm(currentFolders))
                            {
                                var result = form.ShowDialog(control.FindForm());
                                if (result == System.Windows.Forms.DialogResult.OK)
                                {
                                    updatedFolders = form.ExcludedFolders;
                                    saved = true;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[FileSystemSearch] ExcludedFoldersForm error: " + ex.Message);
                        }
                        finally
                        {
                            tcs.TrySetResult(saved);
                        }
                    }));

                    saved = await tcs.Task.ConfigureAwait(false);

                    if (saved && updatedFolders != null)
                    {
                        await ServiceBridge
                            .SetExcludedFoldersAsync(updatedFolders, CancellationToken.None)
                            .ConfigureAwait(false);
                    }

                    _bridge.Reply(id, new { saved });
                }
                catch (Exception ex)
                {
                    _bridge.Reply(id, new { error = Unwrap(ex).Message });
                }
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static Exception Unwrap(Exception ex)
        {
            while (ex is AggregateException ae && ae.InnerException != null)
                ex = ae.InnerException;
            return ex;
        }

        /// <summary>
        /// Cancels any in-flight search/reindex. Deliberately does NOT dispose their sources:
        /// the tasks are still reading those tokens and each disposes its own in its finally.
        /// Disposing here would throw ObjectDisposedException out of the task instead of a
        /// clean cancellation — and out of THIS method if a task got there first, which would
        /// skip the rest of the caller's teardown.
        /// </summary>
        public void Dispose()
        {
            var reindexCts = Interlocked.Exchange(ref _reindexCts, null);
            try { reindexCts?.Cancel(); } catch (ObjectDisposedException) { }

            var searchCts = Interlocked.Exchange(ref _currentSearch, null);
            try { searchCts?.Cancel(); } catch (ObjectDisposedException) { }
        }
    }
}
