using Microsoft.Win32;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using KitveiHakodesh.Core.SeforimDbFullTextSearch;

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
    ///   FtsLib's index write lock makes a build exclusive - every build takes it, in
    ///   either leg - so there is nothing to coordinate here. This class only ASKS
    ///   (IsAnotherProcessBuilding) so the UI can show someone else's progress, and
    ///   polls the progress file for the numbers.
    /// </summary>
    internal sealed class FtsIndexState
    {
        private enum State { Idle, Building, Ready }

        // Cross-process build coordination.
        //
        // This used to be a named Mutex owned by this library. It was BOTH redundant and
        // wrong: redundant because FtsLib's index write lock - an OS file lock on write.lock
        // inside the index directory - is what actually makes a build exclusive, and every
        // build goes through it; wrong because the SERVICE never took the mutex, so while the
        // service was building, this side's probe answered "nobody is building" and the UI
        // showed no progress for the very case it exists to cover.
        //
        // So the question goes to the lock both legs really take. There is nothing to acquire
        // or release here any more - a builder takes the write lock by building.

        /// <summary>
        /// Whether a build is running - here or in another process - against this index.
        ///
        /// ADVISORY, for deciding what to SHOW. A builder does not consult this before
        /// starting; it starts, and the write lock refuses if someone else got there first.
        /// </summary>
        internal static bool IsAnotherProcessBuilding()
        {
            try { return SeforimDbFtsEngine.IsBuildInProgress(FtsIndexPath); }
            catch (Exception ex)
            {
                // Cannot tell. Say "no" - the write lock is the real correctness backstop, so
                // the worst case is a build that starts and is refused.
                Console.WriteLine("[FtsIndexState] build-in-progress check failed: " + ex.Message);
                return false;
            }
        }

        // Guards all field reads and writes. Never held during long-running I/O.
        private readonly object _lock = new object();

        private State                   _state = State.Idle;
        private string                  _dbPath;
        // Core's engine handle, not the engine itself: this project no longer references the
        // engine library. See KitveiHakodesh.Core.SeforimDbFullTextSearch.SeforimDbFtsEngine.
        private SeforimDbFtsEngine       _index;
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
        internal SeforimDbFtsEngine GetIndex()  { lock (_lock) { return _index; } }
        internal string       GetDbPath() { lock (_lock) { return _dbPath; } }

        // ── State transitions (single writer — all field mutations live here) ─────

        /// <summary>
        /// Sets the DB path and index object atomically. Called by the actor thread
        /// during OnDbReady before any state transition.
        /// </summary>
        internal void SetDatabase(string dbPath, SeforimDbFtsEngine index)
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

        internal static string BloomFolderPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters");

        // ── Stamps live in Core ───────────────────────────────────────────────────
        //
        // fts.ver / fts.src, the content fingerprint and the "is this stamp an older format"
        // test were all implemented here AND in the dev service AND in Core. They are now
        // Core's alone (SeforimDbFtsIndexer writes the stamps, SeforimDbContentFingerprint
        // computes them), so the hosted leg and the service can no longer disagree about
        // whether an index is complete, resumable or stale.
        //
        // The app-version stamp that used to live here is retired rather than moved. It forced
        // a rebuild on EVERY app update even when nothing about indexing had changed; the
        // precise question is Core's IndexFormatVersion, which is part of the stamp, and a
        // release that deliberately wants every user to reindex uses the installer's
        // -p:DeleteFtsIndex switch (see Build/Installer/Helpers/AddinInstaller.cs).

    }
}
