using FtsLib.SeforimDb;
using Microsoft.Win32;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace KezayitLib.Search
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
    /// Background tasks (build, merge) run on Task.Run threads and communicate back
    /// only through the named transition methods below.
    ///
    /// State machine:
    ///   Idle     → Building : TryStartBuilding()
    ///   Building → Ready    : TryMarkReady()
    ///   Building → Idle     : TryMarkIdle()
    ///   Ready    → Merging  : TryStartMerging()
    ///   Merging  → Ready    : MarkMergeComplete()
    ///   Any      → Idle     : StopAll()
    /// </summary>
    internal sealed class FtsIndexState
    {
        private enum State { Idle, Building, Ready, Merging }

        // Guards all field reads and writes. Never held during long-running I/O.
        private readonly object _lock = new object();

        private State                   _state = State.Idle;
        private string                  _dbPath;
        private SeforimIndex            _index;
        private Task                    _indexingTask;
        private Task                    _mergeTask;
        private CancellationTokenSource _indexingCts;

        // ── Read-only snapshots (safe to call from any thread) ────────────────────

        internal bool IsReady
        {
            get { lock (_lock) { return _state == State.Ready || _state == State.Merging; } }
        }

        internal bool IsIndexing
        {
            get { lock (_lock) { return _state == State.Building || _indexingCts != null; } }
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
                _indexingCts = null;
            }
        }

        /// <summary>
        /// Transitions Ready → Merging and returns the current index snapshot.
        /// Returns false if not Ready or a merge is already running.
        /// </summary>
        internal bool TryStartMerging(out SeforimIndex index)
        {
            lock (_lock)
            {
                if (_state != State.Ready || _mergeTask != null)
                {
                    index = null;
                    return false;
                }
                _state = State.Merging;
                index  = _index;
                return true;
            }
        }

        /// <summary>
        /// Records the Task for the current merge so StopAll can wait for it.
        /// Called immediately after TryStartMerging succeeds.
        /// </summary>
        internal void SetMergeTask(Task task)
        {
            lock (_lock) { _mergeTask = task; }
        }

        /// <summary>
        /// Transitions Merging → Ready. Called from the merge task's finally block.
        /// </summary>
        internal void MarkMergeComplete()
        {
            lock (_lock)
            {
                if (_state == State.Merging) _state = State.Ready;
                _mergeTask = null;
            }
        }

        /// <summary>
        /// Marks the index as Ready without going through a build.
        /// Used by the actor thread when the index is already complete on disk.
        /// </summary>
        internal void MarkReadyDirect()
        {
            lock (_lock) { _state = State.Ready; }
        }

        // ── StopAll ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Cancels any running build, waits for it and any running merge to fully stop,
        /// then resets all state to Idle. Safe to call from any thread.
        /// After this returns, no background work is touching the index directory.
        /// </summary>
        internal void StopAll()
        {
            Task indexingTask, mergeTask;
            CancellationTokenSource cts;
            lock (_lock)
            {
                cts          = _indexingCts;
                indexingTask = _indexingTask;
                mergeTask    = _mergeTask;
            }

            cts?.Cancel();

            if (indexingTask != null) { try { indexingTask.Wait(15000); } catch { } }
            if (mergeTask    != null) { try { mergeTask.Wait(30000);    } catch { } }

            lock (_lock)
            {
                _state        = State.Idle;
                _indexingTask = null;
                _mergeTask    = null;
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

        internal static bool MergeNeeded()
        {
            try
            {
                if (!Directory.Exists(FtsIndexPath)) return false;
                return Directory.GetFiles(FtsIndexPath, "seg_*.dat").Length > 1;
            }
            catch { return false; }
        }

        // ── Paths ─────────────────────────────────────────────────────────────────

        internal static string FtsIndexPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FtsIndex");

        internal static string FtsVersionStampPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FtsIndex", "fts.ver");

        internal static string BloomFolderPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters");

        // ── Version stamp ─────────────────────────────────────────────────────────

        internal static string GetInstalledAppVersion()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\KleiKodesh"))
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
    }
}
