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

        /// <summary>
        /// <paramref name="mayPromptForInstall"/> decides whether an unregistered service is
        /// allowed to raise a UAC prompt. Only a search the user actually asked for passes
        /// true: the warmup call runs on page load, and a UAC dialog appearing over Word
        /// because a pane opened is not something the user asked for.
        /// </summary>
        public async Task WaitUntilReadyAsync(
            CancellationToken ct, Action<string> onProgress, bool mayPromptForInstall = false)
        {
            // Remember a start failure (e.g. service not installed) so the eventual
            // error message names the root cause, not just "no response". The status
            // poll below still runs — the service may already be up despite this throw
            // (e.g. the exe was launched manually and is serving the pipe).
            Exception startError = null;
            try { ServiceBridge.StartService(); }
            catch (Exception ex) { startError = ex; }

            // A service that was never registered cannot be started, and cannot be
            // registered from here either: that writes to HKLM, and the VSTO runs inside
            // Word as a normal user. EnsureInstalled re-launches the service exe with the
            // "runas" verb so Windows prompts for elevation once — the same thing the
            // installer does — then we retry the start that just failed.
            //
            // ExeMissing takes the same route: the registration points at an exe that is
            // not there, which no amount of retrying fixes and which the user cannot act
            // on from a message. --install re-registers at the exe next to this assembly,
            // so it repairs a registration left pointing somewhere stale.
            //
            // It only helps when a good exe actually sits beside the add-in — EnsureInstalled
            // needs that exe to run --install at all. When the files themselves are gone
            // it throws, TryInstallService returns false, and we fall through to the
            // "reinstall the application" message, which is the right advice then.
            //
            // Disabled is deliberately NOT here. That is a startup type someone set in
            // services.msc, and quietly re-enabling a service the user turned off is not
            // ours to do — that case keeps its explanatory message.
            bool justInstalled = false;
            if (mayPromptForInstall
                && (IsStartFailure(startError, ServiceBridge.ServiceStartFailure.NotInstalled)
                 || IsStartFailure(startError, ServiceBridge.ServiceStartFailure.ExeMissing))
                && TryInstallService())
            {
                justInstalled = true;
                startError = null;
                try { ServiceBridge.StartService(); }
                catch (Exception ex) { startError = ex; }
            }

            // AccessDenied has its own repair. A registration made by an older --install
            // never granted SERVICE_START to authenticated users, so every start from
            // this process is denied — permanently, until someone re-applies the DACL.
            // --fixdacl does exactly that (one elevation), then the start is retried;
            // the retry is also the only way to know whether the grant took. If the
            // denial came from policy or security software instead, the retry fails the
            // same way and the existing message about that stands.
            //
            // Not attempted right after a successful install: --install just wrote that
            // same grant, so a denial now is not the stale-DACL case and a second UAC
            // prompt in the same search could not help.
            if (mayPromptForInstall
                && !justInstalled
                && IsStartFailure(startError, ServiceBridge.ServiceStartFailure.AccessDenied)
                && TryFixServiceDacl())
            {
                startError = null;
                try { ServiceBridge.StartService(); }
                catch (Exception ex) { startError = ex; }
            }

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

        private static bool IsStartFailure(Exception ex, ServiceBridge.ServiceStartFailure reason)
        {
            var sse = Unwrap(ex) as ServiceBridge.ServiceStartException;
            return sse != null && sse.Reason == reason;
        }

        /// <summary>
        /// Set once the user dismisses the elevation prompt, so we stop asking for the
        /// lifetime of the host process. Re-prompting on the next keystroke of a file search
        /// would be its own kind of broken. Written from the thread pool, hence Volatile.
        /// </summary>
        private static int _userDeclinedElevation;

        /// <summary>
        /// Asks ServiceBridge to register the service, which raises a UAC prompt.
        /// Returns true only if the service is registered afterwards.
        ///
        /// Blocks while the prompt is up, so this must stay on a background thread —
        /// WaitUntilReadyAsync is already called from one.
        /// </summary>
        private static bool TryInstallService()
        {
            if (Volatile.Read(ref _userDeclinedElevation) != 0) return false;

            try
            {
                if (ServiceBridge.EnsureInstalled()) return true;
                Volatile.Write(ref _userDeclinedElevation, 1); // user clicked No
                return false;
            }
            catch
            {
                // Throws when the exe is missing, or when --install ran but the registration
                // never appeared. Neither is recoverable here, and neither should take down
                // the search that asked — fall through to the usual unavailable message.
                return false;
            }
        }

        /// <summary>
        /// One per process, whatever the outcome: unlike --install, the DACL repair is
        /// definitive — if the grant didn't cure the denial, the cause is policy or
        /// security software and repeating the prompt on every search cures nothing.
        /// Interlocked also collapses stacked keystrokes into a single UAC prompt.
        /// </summary>
        private static int _fixDaclAttempted;

        /// <summary>
        /// Runs DocumentLocator.Service.exe --fixdacl elevated, which re-grants
        /// SERVICE_START to authenticated users. Repairs registrations made by an older
        /// --install that never wrote that grant, leaving every non-elevated start
        /// denied.
        ///
        /// Lives here rather than in ServiceBridge because that file belongs to the
        /// DocumentLocator sub-repository, which this repository does not commit into.
        /// The exe is resolved the same way ServiceBridge resolves it: next to the
        /// DocumentLocator.Client assembly.
        ///
        /// Shares the declined-elevation latch with TryInstallService — a user who said
        /// No to one elevation prompt is not asked a different one on the next search.
        /// Returns true when the elevated process ran; whether the grant took is not
        /// observable from here, so the caller's start retry is the verification.
        /// </summary>
        private static bool TryFixServiceDacl()
        {
            if (Volatile.Read(ref _userDeclinedElevation) != 0) return false;
            if (Interlocked.Exchange(ref _fixDaclAttempted, 1) != 0) return false;

            try
            {
                string exe = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(ServiceBridge).Assembly.Location) ?? ".",
                    "DocumentLocator.Service.exe");
                if (!System.IO.File.Exists(exe)) return false;

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = exe,
                    Arguments       = "--fixdacl",
                    Verb            = "runas",
                    UseShellExecute = true,
                    WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden,
                };

                using (var proc = System.Diagnostics.Process.Start(psi))
                    proc?.WaitForExit(30_000);

                return true;
            }
            catch (System.ComponentModel.Win32Exception ex)
                when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED — user clicked No
            {
                Volatile.Write(ref _userDeclinedElevation, 1);
                return false;
            }
            catch
            {
                // Anything else — can't run the repair; fall through to the usual
                // unavailable message.
                return false;
            }
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
                    // Reaching here means the install prompt was declined or failed —
                    // WaitUntilReadyAsync always attempts the elevated install first.
                    case ServiceBridge.ServiceStartFailure.NotInstalled:
                        return "שירות החיפוש (DocumentLocator) אינו מותקן במחשב זה. " +
                               "חפש שוב ואשר את בקשת ההרשאה כדי להתקינו.";
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
