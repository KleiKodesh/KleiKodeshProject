using DocumentLocator.Client;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.FileSystemSearch
{
    /// <summary>
    /// Thin adapter over ServiceBridge (DocumentLocator.Client).
    /// Mirrors DocumentLocator.Demo\MainForm.cs exactly — progress messages are
    /// forwarded to a callback instead of updating a WinForms label.
    ///
    ///   IsReady()              — quick non-blocking poll.
    ///   WaitUntilReadyAsync()  — polls GetStatusAsync until ready, forwarding progress.
    ///   SearchAsync()          — sends the query with a result cap; returns (results, total).
    ///
    /// No extension filtering — the service only indexes document types to begin with.
    /// </summary>
    public class DocumentLocatorAdapter
    {
        // ── 1. IsReady ────────────────────────────────────────────────────────────

        public bool IsReady()
        {
            try
            {
                var task = ServiceBridge.GetStatusAsync(CancellationToken.None);
                if (!task.Wait(2000)) return false;
                var status = task.Result;
                return status != null && status.State == "ready";
            }
            catch { return false; }
        }

        // ── 2. WaitUntilReadyAsync ────────────────────────────────────────────────

        public async Task WaitUntilReadyAsync(CancellationToken ct, Action<string> onProgress)
        {
            try { ServiceBridge.StartService(); }
            catch { }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var status = await ServiceBridge.GetStatusAsync(ct).ConfigureAwait(false);

                    if (status == null)
                    {
                        onProgress("ממתין לשירות…");
                        await Task.Delay(1000, ct).ConfigureAwait(false);
                        continue;
                    }

                    switch (status.State)
                    {
                        case "ready":
                            return;
                        case "error":
                            throw new InvalidOperationException("שגיאת אינדקס: " + status.Message);
                        default: // "building"
                            onProgress(status.Message ?? "בונה אינדקס…");
                            await Task.Delay(500, ct).ConfigureAwait(false);
                            break;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (AggregateException ae) when (Unwrap(ae) is OperationCanceledException)
                {
                    throw new OperationCanceledException(ct);
                }
                catch (AggregateException ae) when (Unwrap(ae) is TimeoutException)
                {
                    onProgress("ממתין לצינור השירות…");
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    onProgress("שגיאה: " + Unwrap(ex).Message);
                    await Task.Delay(1500, ct).ConfigureAwait(false);
                }
            }

            ct.ThrowIfCancellationRequested();
        }

        // ── 3. ReindexAsync ───────────────────────────────────────────────────────

        /// <summary>
        /// Sends a reindex request to the DocumentLocator service, asking it to
        /// wipe its Lucene index and perform a full MFT rebuild from scratch.
        /// Starts the service if not already running.
        /// </summary>
        public async Task ReindexAsync(CancellationToken ct)
        {
            await ServiceBridge.ReindexAsync(ct).ConfigureAwait(false);
        }

        // ── 4. SearchAsync ────────────────────────────────────────────────────────

        /// <summary>
        /// Executes a search and returns (results, total).
        /// The limit is passed to the service so Lucene caps the result set server-side.
        /// total reflects the full match count; results.Count may be less when capped.
        /// </summary>
        public async Task<(List<FileSystemSearchResult> results, int total)> SearchAsync(
            string query, int max, CancellationToken ct)
        {
            var result = await ServiceBridge.SearchAsync(
                query, drive: null, ct: ct, limit: max)
                .ConfigureAwait(false);

            if (result.Status != "ok")
                return (new List<FileSystemSearchResult>(), 0);

            var list = new List<FileSystemSearchResult>(result.Paths.Count);
            foreach (string path in result.Paths)
            {
                ct.ThrowIfCancellationRequested();
                list.Add(new FileSystemSearchResult(
                    System.IO.Path.GetFileName(path),
                    System.IO.Path.GetDirectoryName(path) ?? path));
            }

            return (list, result.Total);
        }

        private static Exception Unwrap(Exception ex)
        {
            while (ex is AggregateException ae && ae.InnerException != null)
                ex = ae.InnerException;
            return ex;
        }
    }

    public sealed class FileSystemSearchResult
    {
        public string FileName { get; }
        public string Path     { get; }
        public FileSystemSearchResult(string fileName, string path)
        { FileName = fileName; Path = path; }
    }
}
