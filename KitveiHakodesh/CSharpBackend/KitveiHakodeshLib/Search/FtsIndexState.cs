using FtsLib.SeforimDb;
using Microsoft.Win32;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodeshLib.Search
{
    /// <summary>
    /// Owns all mutable state for the FTS index lifecycle.
    ///
    /// This is the SINGLE WRITER of all state fields. No other class reads or writes
    /// fields directly — all access goes through the methods below. The lock is an
    /// implementation detail; callers never acquire it.
    ///
    /// Lifecycle serialization (OnDbReady, ResetAndReindex, HandleDeleteIndex) is
    /// handled by the actor thread in SearchHandler — not by a semaphore here.
    /// Background tasks (build) run on Task.Run threads and communicate back
    /// only through the named transition methods below.
    ///
    /// State machine:
    ///   Idle     → Building : TryStartBuilding()
    ///   Building → Ready    : TryMarkReady()
    ///   Building → Idle     : TryMarkIdle()
    ///   Any      → Idle     : StopAll()
    ///
    /// Cross-process coordination:
    ///   A named system Mutex (FtsIndexBuildLock) ensures only one process builds
    ///   at a time. The building process acquires it before starting and releases it
    ///   when the build finishes or is cancelled. Other processes detect the held
    ///   mutex via TryAcquireBuildLock() and poll the progress file for display.
    /// </summary>
    internal sealed class FtsIndexState
    {
        private enum State { Idle, Building, Ready }

        // ── Cross-process build lock ──────────────────────────────────────────────
        // Named mutex scoped to the current user session (Local\ prefix) so it works
        // correctly when multiple Windows user sessions are active simultaneously.
        // The mutex name encodes the index path so two instances pointing at different
        // index directories do not block each other.
        private static Mutex _buildMutex;
        // Whether a builder IN THIS PROCESS holds the lock, as a count rather than a bool.
        //
        // This state is static while FtsIndexState is per-SearchHandler, and one process can
        // host several AppViewers (the VSTO pane plus a popped-out window). The old bool
        // short-circuited — "already ours, return true" — so a second local builder was told
        // it held a lock it had never acquired, and whichever build finished first released
        // the real OS mutex while the other was still writing, letting another PROCESS start
        // building the same directory.
        //
        // What actually happens now: a mutex is owned by a THREAD, and each builder waits on
        // its own pool thread, so a second local builder's WaitOne(0) simply FAILS while the
        // first holds it — it declines and marks itself idle. That is the correct outcome, and
        // it means the count never exceeds 1 in practice. It stays a count because it keeps
        // acquire and release symmetric per caller and cannot get stuck true the way the bool
        // could; in-process exclusion is the index write lock's job either way.
        private static int _buildLockHolders;
        private static bool BuildMutexOwned => _buildLockHolders > 0;
        private static readonly object _mutexLock = new object();

        private static string BuildMutexName
        {
            get
            {
                // Sanitise the path into a valid mutex name (no backslashes, colons, etc.)
                string sanitised = FtsIndexPath
                    .Replace('\\', '_').Replace('/', '_')
                    .Replace(':', '_').Replace(' ', '_');
                // Mutex names are limited to MAX_PATH (260) chars; truncate if needed.
                if (sanitised.Length > 200) sanitised = sanitised.Substring(sanitised.Length - 200);
                return @"Local\FtsIndexBuild_" + sanitised;
            }
        }

        /// <summary>
        /// Tries to acquire the cross-process build lock without blocking.
        /// Returns true if this process now owns the lock; false if another process
        /// already holds it.
        /// </summary>
        internal static bool TryAcquireBuildLock()
        {
            lock (_mutexLock)
            {
                try
                {
                    if (_buildMutex == null)
                        _buildMutex = new Mutex(false, BuildMutexName);

                    // Always waited, never short-circuited on "we already hold it". Ownership
                    // is per THREAD, so a second local builder on its own pool thread is
                    // refused here and declines — where the old short-circuit handed it a
                    // lock it did not own, whose first release then freed the mutex
                    // system-wide while the other builder was still writing.
                    bool acquired = _buildMutex.WaitOne(0); // non-blocking
                    if (acquired) _buildLockHolders++;
                    return acquired;
                }
                catch (AbandonedMutexException)
                {
                    // Previous owner crashed — WaitOne took ownership before throwing.
                    _buildLockHolders++;
                    return true;
                }
                catch (Exception ex)
                {
                    // Fail CLOSED. This used to return true "so a mutex error doesn't block
                    // the build", but the errors that actually land here are ownership and
                    // name-collision failures — cases where we demonstrably do NOT hold the
                    // lock. Claiming it anyway let two processes build one index directory,
                    // and made the build's finally call ReleaseMutex on a mutex it never
                    // acquired. Declining costs one skipped build: the caller watches the
                    // other builder instead, and the next OnDbReady retries.
                    Console.WriteLine("[FtsIndexState] TryAcquireBuildLock failed: " + ex.Message);
                    try { _buildMutex?.Dispose(); } catch { }
                    _buildMutex = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// Releases the cross-process build lock. Safe to call even if not owned.
        /// </summary>
        internal static void ReleaseBuildLock()
        {
            lock (_mutexLock)
            {
                if (!BuildMutexOwned) return;
                try
                {
                    _buildMutex?.ReleaseMutex();
                }
                catch (Exception ex)
                {
                    // A throw here means this thread is not the OS-recorded owner, so the
                    // mutex is STILL HELD by this process and the handle is now untrustworthy.
                    // Dropping only the flag (what this used to do) left the OS owning a lock
                    // nobody would ever release, and every later check re-entered it
                    // recursively and reported "nobody is building". Discarding the handle
                    // makes the next acquire open a fresh one and see the real state; the
                    // stale OS ownership dies with the process. Callers must acquire and
                    // release on the SAME thread — see FtsIndexBuilder.RunBuild.
                    Console.WriteLine("[FtsIndexState] ReleaseBuildLock failed: " + ex.Message);
                    try { _buildMutex?.Dispose(); } catch { }
                    _buildMutex = null;
                }
                finally
                {
                    if (_buildLockHolders > 0) _buildLockHolders--;
                }
            }
        }

        /// <summary>Releases a probe acquisition taken by IsAnotherProcessBuilding on THIS
        /// thread. Must be called from the same thread that acquired, and under _mutexLock.
        /// A failure means the handle can no longer be reasoned about, so it is discarded:
        /// the next acquire opens a fresh one and observes the true system-wide state.</summary>
        private static void ReleaseProbe()
        {
            try { _buildMutex.ReleaseMutex(); }
            catch (Exception ex)
            {
                Console.WriteLine("[FtsIndexState] build-lock probe release failed: " + ex.Message);
                try { _buildMutex?.Dispose(); } catch { }
                _buildMutex = null;
            }
        }

        /// <summary>
        /// Returns true if another process currently holds the build lock.
        /// Does not acquire the lock.
        /// </summary>
        internal static bool IsAnotherProcessBuilding()
        {
            lock (_mutexLock)
            {
                if (BuildMutexOwned) return false; // a builder here holds it — not another process

                // Probing by acquire-then-release must happen on ONE thread: the acquire
                // makes this thread the OS owner, and only that thread may release. Both
                // halves are inside this lock and this method, so they always pair. If a
                // release ever fails anyway, the handle is discarded rather than left
                // half-held — a leaked recursive acquisition is what previously made this
                // method answer "nobody is building" for the rest of the process lifetime.
                try
                {
                    if (_buildMutex == null)
                        _buildMutex = new Mutex(false, BuildMutexName);

                    bool acquired = _buildMutex.WaitOne(0);
                    if (acquired)
                    {
                        // We got it — release immediately, nobody else is building.
                        ReleaseProbe();
                        return false;
                    }
                    return true;
                }
                catch (AbandonedMutexException)
                {
                    // Previous owner crashed — WaitOne took ownership before throwing, so
                    // this thread now holds it and must release it here.
                    ReleaseProbe();
                    return false;
                }
                catch (Exception ex)
                {
                    // Cannot tell — assume nobody is building (the index write lock is the
                    // real correctness backstop) and drop the unusable handle.
                    Console.WriteLine("[FtsIndexState] IsAnotherProcessBuilding check failed: " + ex.Message);
                    try { _buildMutex?.Dispose(); } catch { }
                    _buildMutex = null;
                    return false;
                }
            }
        }

        // Guards all field reads and writes. Never held during long-running I/O.
        private readonly object _lock = new object();

        private State                   _state = State.Idle;
        private string                  _dbPath;
        private SeforimIndex            _index;
        private Task                    _indexingTask;
        private CancellationTokenSource _indexingCts;

        // ── Read-only snapshots (safe to call from any thread) ────────────────────

        internal bool IsReady
        {
            get { lock (_lock) { return _state == State.Ready; } }
        }

        internal bool IsIndexing
        {
            get { lock (_lock) { return _state == State.Building || _indexingCts != null; } }
        }

        /// <summary>
        /// Both flags from ONE lock acquisition. Reading IsReady and IsIndexing separately
        /// can straddle a transition and observe "not ready, not indexing" — the state the
        /// UI reads as "nothing is happening" — for a build that is merely between phases.
        /// </summary>
        internal void GetStatus(out bool ready, out bool indexing)
        {
            lock (_lock)
            {
                ready    = _state == State.Ready;
                indexing = _state == State.Building || _indexingCts != null;
            }
        }

        /// <summary>
        /// Returns a snapshot of the current index object. Callers that need a stable
        /// reference for a long operation should capture this once — the field may be
        /// replaced by a concurrent SetDatabase call on the actor thread.
        /// </summary>
        internal SeforimIndex GetIndex()  { lock (_lock) { return _index; } }
        internal string       GetDbPath() { lock (_lock) { return _dbPath; } }

        // ── State transitions (single writer — all field mutations live here) ─────

        /// <summary>
        /// Sets the DB path and index object atomically. Called by the actor thread
        /// during OnDbReady before any state transition.
        /// </summary>
        internal void SetDatabase(string dbPath, SeforimIndex index)
        {
            lock (_lock) { _dbPath = dbPath; _index = index; }
        }

        /// <summary>
        /// Transitions Idle → Building. Returns false if already building.
        /// Out parameter receives the CancellationTokenSource for this build session —
        /// passed back to TryMarkReady/TryMarkIdle as a stale-task guard.
        /// </summary>
        internal bool TryStartBuilding(out CancellationTokenSource cts)
        {
            lock (_lock)
            {
                if (_state == State.Building) { cts = null; return false; }
                _state       = State.Building;
                _indexingCts = new CancellationTokenSource();
                cts          = _indexingCts;
                return true;
            }
        }

        /// <summary>
        /// Records the Task for the current build so StopAll can wait for it.
        /// Called immediately after TryStartBuilding succeeds.
        /// </summary>
        internal void SetIndexingTask(Task task)
        {
            lock (_lock) { _indexingTask = task; }
        }

        /// <summary>
        /// Transitions Building → Ready if this CTS is still the active one.
        /// Also accepts Ready state (already transitioned via MarkReadyDirect during
        /// partial-index detection) — just clears the CTS in that case.
        /// Returns true if the index is now Ready (false = stale task, ignore).
        /// </summary>
        internal bool TryMarkReady(CancellationTokenSource cts)
        {
            lock (_lock)
            {
                if (_indexingCts != cts) return false;
                // Accept both Building (normal path) and Ready (already marked ready
                // mid-build when first segment was flushed).
                if (_state != State.Building && _state != State.Ready) return false;
                _state       = State.Ready;
                // The build that owned this CTS is finished, so nothing reads its token any
                // more. Without the Dispose we leak one finalizable source per build.
                _indexingCts?.Dispose();
                _indexingCts = null;
                return true;
            }
        }

        /// <summary>
        /// Transitions Building → Idle if this CTS is still the active one.
        /// If the build was partially ready (MarkReadyDirect was called mid-build)
        /// the state will be Ready — leave it Ready in that case, just clear the CTS.
        /// </summary>
        internal void TryMarkIdle(CancellationTokenSource cts)
        {
            lock (_lock)
            {
                if (_indexingCts != cts) return;
                // If we already transitioned to Ready mid-build, keep it Ready.
                // Only reset to Idle if we never became searchable.
                if (_state == State.Building)
                    _state = State.Idle;
                _indexingCts?.Dispose(); // build over — see TryMarkReady
                _indexingCts = null;
            }
        }

        /// <summary>
        /// Marks the index as Ready without waiting for the build to finish — the index is
        /// already complete on disk, or a build has flushed its first searchable segment.
        ///
        /// <paramref name="cts"/> is the caller's build session, and is REQUIRED for the
        /// same reason TryMarkReady/TryMarkIdle take one: a build cancelled by StopAll keeps
        /// running until it notices, and its progress callbacks used to be able to force
        /// Ready over an index that a reset had just wiped — leaving search convinced a
        /// deleted corpus was queryable. Pass null only from the actor thread, which owns
        /// the lifecycle and has no build session to be stale against.
        /// </summary>
        /// <returns>True if the state is now Ready; false when this build is stale.</returns>
        internal bool MarkReadyDirect(CancellationTokenSource cts)
        {
            lock (_lock)
            {
                if (cts != null && _indexingCts != cts) return false;
                _state = State.Ready;
                return true;
            }
        }

        // ── StopAll ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Cancels any running build, waits for it to fully stop, then resets all
        /// state to Idle. Safe to call from any thread.
        /// After this returns, no background work is touching the index directory.
        /// </summary>
        internal void StopAll()
        {
            Task indexingTask;
            CancellationTokenSource cts;
            lock (_lock)
            {
                cts          = _indexingCts;
                indexingTask = _indexingTask;
            }

            // Guarded: we read cts under the lock and then released it, so the build thread
            // can reach TryMarkReady/TryMarkIdle and dispose this very source before we get
            // here. Already disposed means the build already finished — there is nothing to
            // cancel, and letting the exception out would abort the rest of the lifecycle
            // action (the cache deletes / DeleteFtsIndex + reindex never run, so the user's
            // "delete index" or "reset" would silently do nothing).
            try { cts?.Cancel(); } catch (ObjectDisposedException) { }

            // Wait WITHOUT a timeout for the build task to stop.
            //
            // BuildIndex() blocks on the calling thread until IndexWriter.Dispose()
            // has called WaitForMerge() and the entire flush+merge pipeline has drained.
            // That means when the task returns, no background work is touching the
            // index directory — safe to wipe or modify it immediately after.
            //
            // The previous 60-second timeout was the root cause of the corruption bug:
            // if a merge took longer than 60s, StopAll returned while the pipeline was
            // still writing, and a subsequent DeleteFtsIndex() would race with those
            // writes. Orphan files or a stale build.progress in the recreated directory
            // caused the next build to resume from the wrong line ID, permanently
            // skipping lines 1..N.
            if (indexingTask != null) { try { indexingTask.Wait(); } catch { } }

            lock (_lock)
            {
                _state        = State.Idle;
                _indexingTask = null;
                // Safe to dispose: we waited for indexingTask above, so the build that read
                // this token has fully drained. TryMarkReady/TryMarkIdle may have disposed
                // and nulled it already — Dispose is idempotent and this is null-guarded.
                _indexingCts?.Dispose();
                _indexingCts  = null;
            }
        }

        // ── Index directory ───────────────────────────────────────────────────────

        internal static void DeleteFtsIndex()
        {
            try
            {
                if (Directory.Exists(FtsIndexPath))
                {
                    Directory.Delete(FtsIndexPath, recursive: true);
                    Console.WriteLine("[SearchHandler] Deleted FTS index directory");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SearchHandler] Failed to delete FTS index: " + ex.Message);
            }
        }

        /// <summary>
        /// Deletes the Word→PDF and HebrewBooks PDF caches on a background thread.
        /// Called by the full app reset flow, alongside DeleteFtsIndex and
        /// DeleteBloomIndexIfPresent — but unlike those two, these folders have nothing to do
        /// with the index lifecycle, so they must NOT be deleted on the lifecycle actor:
        /// gigabytes of cached PDFs used to be deleted between the index wipe and the reloaded
        /// page's OnDbReady, holding up the FTS rebuild for as long as the PDFs took to delete.
        /// That serial delete is why the app reset's index rebuild started so much later than
        /// the standalone "reset search index" button's, which never touches these folders.
        ///
        /// The WebView2 webcache is deliberately NOT in this list. It used to be, and it never
        /// once worked: it is the live user-data folder of the very WebView2 that issued the
        /// reset, so Directory.Delete fails on the open handles and the exception is swallowed
        /// below. The folder name was wrong on top of that — the standalone app passes
        /// "webcache-standalone" to the AppViewer constructor, so the hardcoded "webcache"
        /// pointed at a directory that does not exist there. Browser storage is cleared by
        /// AppViewer.ClearWebViewBrowsingDataAsync through the WebView2 profile API instead,
        /// which is the only supported way to clear a cache that is currently mounted.
        /// </summary>
        internal static void DeletePdfCachesInBackground()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] cacheDirs =
            {
                Path.Combine(baseDir, "KitveiHakodesh", "word-cache"),
                Path.Combine(baseDir, "KitveiHakodesh", "hebrewbooks-cache"),
            };

            Task.Run(() =>
            {
                foreach (string dir in cacheDirs)
                {
                    try
                    {
                        if (Directory.Exists(dir))
                        {
                            Directory.Delete(dir, recursive: true);
                            Console.WriteLine("[SearchHandler] Deleted cache: " + dir);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[SearchHandler] Failed to delete cache " + dir + ": " + ex.Message);
                    }
                }
            });
        }

        internal static void DeleteBloomIndexIfPresent()
        {
            try
            {
                if (Directory.Exists(BloomFolderPath))
                {
                    Directory.Delete(BloomFolderPath, recursive: true);
                    Console.WriteLine("[SearchHandler] Deleted legacy Bloom index folder");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SearchHandler] Failed to delete Bloom folder: " + ex.Message);
            }
        }

        // ── Cross-process progress reading ───────────────────────────────────────

        /// <summary>
        /// Reads the progress file written by the building process and returns
        /// display data (percentage, processed, total) without needing a live
        /// SeforimIndex instance. Used by the watcher thread in the non-building
        /// process to push progress events to its own frontend.
        /// Returns false if no progress file exists or it cannot be read.
        /// </summary>
        internal static bool TryReadProgressFile(out double percentage, out int processed, out int total)
        {
            percentage = 0;
            processed  = 0;
            total      = 0;
            try
            {
                // The progress file is "build.progress" in the index directory.
                // Format: 3 newline-separated integers — lineId, totalLines, resumeOffset.
                // Written by IndexingPipeline.WriteProgressFile.
                string progressPath = Path.Combine(FtsIndexPath, "build.progress");
                if (!File.Exists(progressPath)) return false;

                string[] lines = File.ReadAllText(progressPath).Trim().Split('\n');
                // lines[0] = last flushed lineId (not needed for display)
                // lines[1] = total lines in the database
                // lines[2] = count of lines indexed so far (resumeOffset)
                long cachedTotal  = 0;
                long cachedOffset = 0;
                if (lines.Length >= 2) long.TryParse(lines[1].Trim(), out cachedTotal);
                if (lines.Length >= 3) long.TryParse(lines[2].Trim(), out cachedOffset);

                if (cachedTotal <= 0) return false;

                total      = (int)Math.Min(cachedTotal, int.MaxValue);
                processed  = (int)Math.Min(cachedOffset, int.MaxValue);
                percentage = Math.Min(99.9, cachedOffset * 100.0 / cachedTotal);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ── Validation ────────────────────────────────────────────────────────────

        internal static string ValidateFtsIndex()
        {
            try
            {
                if (!Directory.Exists(FtsIndexPath)) return "index directory missing";
                if (Directory.GetFiles(FtsIndexPath, "*.dat").Length == 0)
                    return "no segment files found";
                return null;
            }
            catch (Exception ex) { return "validation error: " + ex.Message; }
        }

        // ── Paths ─────────────────────────────────────────────────────────────────

        internal static string FtsIndexPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FtsIndex");

        internal static string FtsVersionStampPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FtsIndex", "fts.ver");

        internal static string FtsSourceStampPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FtsIndex", "fts.src");

        internal static string BloomFolderPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters");

        // ── Version stamp ─────────────────────────────────────────────────────────

        internal static string GetInstalledAppVersion()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\KleiKodesh"))
                    return key?.GetValue("Version")?.ToString();
            }
            catch { return null; }
        }

        internal static string ReadVersionStamp()
        {
            try
            {
                return File.Exists(FtsVersionStampPath)
                    ? File.ReadAllText(FtsVersionStampPath).Trim()
                    : null;
            }
            catch { return null; }
        }

        internal static void WriteVersionStamp(string version)
        {
            try
            {
                Directory.CreateDirectory(FtsIndexPath);
                File.WriteAllText(FtsVersionStampPath, version ?? "");
            }
            catch { }
        }

        // ── Source-DB stamp (build provenance) ───────────────────────────────────
        // fts.ver (the app-version stamp above) is written only when a build COMPLETES,
        // so an interrupted build used to carry no record of which DB it was indexing —
        // resuming it after a DB switch permanently skipped every line below the old
        // watermark. fts.src records a fingerprint of the source DB's CONTENT at build
        // START; ExecuteOnDbReady refuses to resume (and invalidates a completed index)
        // when it no longer matches the current DB.
        //
        // The fingerprint deliberately describes the CONTENT, not the file. An earlier
        // version used path + size + mtime + the -wal sidecar's size/mtime, which made
        // the stamp track journal churn rather than content: anything that touched the
        // file without changing a single row — a stray `PRAGMA journal_mode` on open, a
        // copy, a restore, a reinstall — produced a mismatch and wiped the whole index
        // on the next launch. The queries below are all index seeks or header reads, so
        // this stays a startup-cheap check; COUNT(*) on `line` is a multi-second full
        // scan and is deliberately NOT part of it.

        /// <summary>Content fingerprint of the seforim DB: path plus the identity of the
        /// `line` and `book` tables (extremal ids and the schema cookie). Read-only, and
        /// invariant under anything that rewrites the file without changing its rows.
        /// Falls back to the file's size alone when the DB cannot be queried, so a
        /// genuinely different DB still mismatches. Returns null on error.</summary>
        internal static string ComputeDbStamp(string dbPath)
        {
            try
            {
                if (string.IsNullOrEmpty(dbPath)) return null;
                // Carries StampPrefix like every other branch — without it this stamp would
                // read as the older format and be excused rather than compared.
                if (!File.Exists(dbPath)) return StampPrefix + dbPath.ToLowerInvariant() + "|missing";

                string content = TryComputeDbContentStamp(dbPath);
                if (content != null) return StampPrefix + dbPath.ToLowerInvariant() + "|" + content;

                // The DB is present but unreadable (locked, corrupt, unexpected schema).
                // Size alone still separates two different databases, and unlike mtime it
                // does not move when the file is merely rewritten in place.
                return StampPrefix + dbPath.ToLowerInvariant() + "|len=" + new FileInfo(dbPath).Length;
            }
            catch { return null; }
        }

        /// <summary>Version tag on every stamp this build writes. Bump it whenever the
        /// fingerprint's format changes, so old stamps are recognised as a DIFFERENT
        /// FORMAT rather than compared as a different DB — see IsLegacySourceStamp.</summary>
        private const string StampPrefix = "v2|";

        /// <summary>True for an fts.src written by an app version whose stamp format
        /// predates this one. Such a stamp can never compare equal to a current stamp, so
        /// treating it as a mismatch would wipe a perfectly good index on the first launch
        /// after every format change. Callers treat it as "provenance unknown" instead:
        /// a completed index is kept (and re-stamped on its next build), while an
        /// interrupted build is still wiped rather than resumed on an unverifiable
        /// watermark.</summary>
        internal static bool IsLegacySourceStamp(string stamp)
        {
            return stamp != null && !stamp.StartsWith(StampPrefix, StringComparison.Ordinal);
        }

        /// <summary>Queries the DB for a content fingerprint. Returns null if it cannot be
        /// read, so the caller can fall back. Opens READ-ONLY: this must never write the
        /// header or create a -wal sidecar (that is the bug this stamp exists to survive).</summary>
        private static string TryComputeDbContentStamp(string dbPath)
        {
            try
            {
                using (var conn = new System.Data.SQLite.SQLiteConnection(
                           "Data Source=" + dbPath + ";Version=3;Read Only=True;FailIfMissing=True;"))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        // schema_version changes on any DDL, so a rebuilt DB with coincidentally
                        // identical extremal ids still mismatches.
                        cmd.CommandText = "PRAGMA schema_version";
                        object schemaVer = cmd.ExecuteScalar();

                        // MIN/MAX over an INTEGER PRIMARY KEY are index seeks, not scans.
                        cmd.CommandText = "SELECT MIN(id), MAX(id) FROM line";
                        long lineMin = 0, lineMax = 0;
                        using (var r = cmd.ExecuteReader())
                            if (r.Read() && !r.IsDBNull(0))
                            { lineMin = r.GetInt64(0); lineMax = r.GetInt64(1); }

                        cmd.CommandText = "SELECT MIN(id), MAX(id) FROM book";
                        long bookMin = 0, bookMax = 0;
                        using (var r = cmd.ExecuteReader())
                            if (r.Read() && !r.IsDBNull(0))
                            { bookMin = r.GetInt64(0); bookMax = r.GetInt64(1); }

                        // A DB swapped for one with the same id ranges but different text would
                        // slip past the ids alone; the last line's content pins the actual rows.
                        cmd.CommandText = "SELECT content FROM line WHERE id = @id";
                        cmd.Parameters.AddWithValue("@id", lineMax);
                        object lastLine = cmd.ExecuteScalar();
                        int lastLineLen = lastLine is string s ? s.Length : -1;

                        return "schema=" + schemaVer
                             + "|line=" + lineMin + ":" + lineMax
                             + "|book=" + bookMin + ":" + bookMax
                             + "|tail=" + lastLineLen;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FtsIndexState] Content stamp unavailable, falling back: " + ex.Message);
                return null;
            }
        }

        internal static string ReadSourceStamp()
        {
            try
            {
                return File.Exists(FtsSourceStampPath)
                    ? File.ReadAllText(FtsSourceStampPath).Trim()
                    : null;
            }
            catch { return null; }
        }

        internal static void WriteSourceStamp(string stamp)
        {
            try
            {
                Directory.CreateDirectory(FtsIndexPath);
                File.WriteAllText(FtsSourceStampPath, stamp ?? "");
            }
            catch { }
        }
    }
}
