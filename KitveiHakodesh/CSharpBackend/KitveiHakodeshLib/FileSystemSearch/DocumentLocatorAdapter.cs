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

        // How many consecutive failed status polls (pipe timeouts, service-not-installed,
        // etc.) we tolerate before giving up and surfacing the error to the caller.
        // While the service IS responding with "building", we wait indefinitely —
        // a first-time MFT crawl can legitimately take minutes.
        private const int MaxConsecutiveStatusFailures = 5;

        public async Task WaitUntilReadyAsync(CancellationToken ct, Action<string> onProgress)
        {
            // Remember a start failure (e.g. service not installed) so the eventual
            // error message names the root cause, not just "no response". The status
            // poll below still runs — the service may already be up despite this throw
            // (e.g. the exe was launched manually and is serving the pipe).
            Exception startError = null;
            try { ServiceBridge.StartService(); }
            catch (Exception ex) { startError = ex; }

            // Definitive failures (not installed / disabled / blocked / exe missing)
            // won't heal by polling — give up after a single failed status check.
            int maxFailures = IsDefinitiveStartFailure(startError)
                ? 1
                : MaxConsecutiveStatusFailures;

            int consecutiveFailures = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                ServiceBridge.StatusResult status;
                try
                {
                    status = await ServiceBridge.GetStatusAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (AggregateException ae) when (Unwrap(ae) is OperationCanceledException)
                {
                    throw new OperationCanceledException(ct);
                }
                catch (Exception ex)
                {
                    if (++consecutiveFailures >= maxFailures)
                        throw new InvalidOperationException(
                            DescribeUnavailable(startError ?? ex), Unwrap(ex));
                    onProgress("ממתין לשירות…");
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                    continue;
                }

                if (status == null)
                {
                    if (++consecutiveFailures >= maxFailures)
                        throw new InvalidOperationException(
                            startError != null ? DescribeUnavailable(startError) : "שירות החיפוש אינו מגיב");
                    onProgress("ממתין לשירות…");
                    await Task.Delay(1000, ct).ConfigureAwait(false);
                    continue;
                }

                consecutiveFailures = 0;

                switch (status.State)
                {
                    case "ready":
                        return;
                    case "error":
                        // Propagate — must NOT be swallowed by a retry loop, otherwise
                        // the caller waits forever while the index is broken.
                        throw new InvalidOperationException("שגיאת אינדקס: " + status.Message);
                    default: // "building"
                        onProgress(status.Message ?? "בונה אינדקס…");
                        await Task.Delay(500, ct).ConfigureAwait(false);
                        break;
                }
            }
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
                throw new InvalidOperationException(
                    !string.IsNullOrEmpty(result.Message)
                        ? result.Message
                        : "שגיאה בחיפוש (" + (result.Status ?? "ללא סטטוס") + ")");

            var list = new List<FileSystemSearchResult>(result.Entries.Count);
            foreach (var entry in result.Entries)
            {
                ct.ThrowIfCancellationRequested();
                list.Add(new FileSystemSearchResult(
                    System.IO.Path.GetFileName(entry.Path),
                    System.IO.Path.GetDirectoryName(entry.Path) ?? entry.Path,
                    entry.DateMs,
                    entry.AddinName));
            }

            return (list, result.Total);
        }

        private static bool IsDefinitiveStartFailure(Exception ex)
        {
            var sse = Unwrap(ex) as ServiceBridge.ServiceStartException;
            return sse != null && sse.Reason != ServiceBridge.ServiceStartFailure.Other;
        }

        /// <summary>
        /// Builds the user-facing (Hebrew) message for a service that could not be
        /// reached, keyed on the classified start-failure reason when available.
        /// </summary>
        private static string DescribeUnavailable(Exception ex)
        {
            var cause = Unwrap(ex);
            if (cause is ServiceBridge.ServiceStartException sse)
            {
                switch (sse.Reason)
                {
                    case ServiceBridge.ServiceStartFailure.NotInstalled:
                        return "שירות החיפוש (DocumentLocator) אינו מותקן במחשב זה. " +
                               "הפעל את היישום מחדש כדי להתקינו.";
                    case ServiceBridge.ServiceStartFailure.Disabled:
                        return "שירות החיפוש (DocumentLocator) מושבת. " +
                               "יש להפעיל אותו דרך ניהול השירותים של Windows (services.msc).";
                    case ServiceBridge.ServiceStartFailure.AccessDenied:
                        return "הגישה לשירות החיפוש נחסמה — ייתכן על ידי מדיניות אבטחה או תוכנת אנטי־וירוס.";
                    case ServiceBridge.ServiceStartFailure.ExeMissing:
                        return "קובץ שירות החיפוש חסר בתיקיית היישום. יש להתקין את היישום מחדש.";
                }
            }
            return "שירות החיפוש אינו זמין: " + cause.Message;
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
        public string FileName     { get; }
        public string Path         { get; }
        /// <summary>Last-write time as Unix milliseconds. 0 if not available.</summary>
        public long   ModifiedDate { get; }
        /// <summary>
        /// Non-empty only for Otzaria addin entry-point files.
        /// Value is "תוסף אוצריא: {name}" as stored in the index.
        /// </summary>
        public string AddinName    { get; }
        public FileSystemSearchResult(string fileName, string path, long modifiedDate = 0, string addinName = "")
        { FileName = fileName; Path = path; ModifiedDate = modifiedDate; AddinName = addinName ?? ""; }
    }
}
