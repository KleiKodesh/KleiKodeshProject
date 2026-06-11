using SearchEngine.SeforimDb;
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
        private static bool  _buildMutexOwned;
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
                if (_buildMutexOwned) return true; // already ours

                try
                {
                    if (_buildMutex == null)
                        _buildMutex = new Mutex(false, BuildMutexName);

                    bool acquired = _buildMutex.WaitOne(0); // non-blocking
                    _buildMutexOwned = acquired;
                    return acquired;
                }
                catch (AbandonedMutexException)
                {
                    // Previous owner crashed — we now own it.
                    _buildMutexOwned = true;
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[FtsIndexState] TryAcquireBuildLock failed: " + ex.Message);
                    return true; // fail open — don't block the build on mutex errors
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
                if (!_buildMutexOwned) return;
                try
                {
                    _buildMutex?.ReleaseMutex();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[FtsIndexState] ReleaseBuildLock failed: " + ex.Message);
                }
                finally
                {
                    _buildMutexOwned = false;
                }
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
                if (_buildMutexOwned) return false; // we own it — no other process

                try
                {
                    if (_buildMutex == null)
                        _buildMutex = new Mutex(false, BuildMutexName);

                    bool acquired = _buildMutex.WaitOne(0);
                    if (acquired)
                    {
                        // We got it — release immediately, nobody else is building.
                        _buildMutex.ReleaseMutex();
                        return false;
                    }
                    return true;
                }
                catch (AbandonedMutexException)
                {
                    // Previous owner crashed — mutex is now ours; release it.
                    try { _buildMutex?.ReleaseMutex(); } catch { }
                    return false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[FtsIndexState] IsAnotherProcessBuilding check failed: " + ex.Message);
                    return false; // fail open
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
            SeforimIndex oldIndex;
            lock (_lock)
            {
                oldIndex = _index;
                _dbPath  = dbPath;
                _index   = index;
            }
            // No Dispose needed — FtsLib's SeforimIndex holds no unmanaged resources.
            _ = oldIndex;
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
        /// Marks the index as Ready without going through a build.
        /// Used by the actor thread when the index is already complete on disk.
        /// </summary>
        internal void MarkReadyDirect()
        {
            lock (_lock) { _state = State.Ready; }
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

            cts?.Cancel();

            if (indexingTask != null) { try { indexingTask.Wait(15000); } catch { } }

            lock (_lock)
            {
                _state        = State.Idle;
                _indexingTask = null;
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
        /// Deletes all user-generated cache folders under the app's base directory:
        /// FTS index, Bloom filters, Word→PDF cache, HebrewBooks cache, and WebView2 webcache.
        /// Called by the full app reset flow (איפוס האפליקציה).
        /// </summary>
        internal static void DeleteAllCaches()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] cacheDirs =
            {
                FtsIndexPath,
                BloomFolderPath,
                Path.Combine(baseDir, "KitveiHakodesh", "cache", "word"),
                Path.Combine(baseDir, "KitveiHakodesh", "cache", "hebrewbooks"),
                Path.Combine(baseDir, "KitveiHakodesh", "webcache"),
            };

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
                // Lucene index is valid when at least one committed segments file exists
                // and there is no interrupted build (progress file present means resumable).
                bool hasSegments = false;
                foreach (var f in Directory.GetFiles(FtsIndexPath))
                {
                    string name = Path.GetFileName(f);
                    if (name.StartsWith("segments_") && name != "segments.gen")
                    { hasSegments = true; break; }
                }
                if (!hasSegments) return "no segment files found";
                return null;
            }
            catch (Exception ex) { return "validation error: " + ex.Message; }
        }

        // ── Paths ─────────────────────────────────────────────────────────────────

        internal static string FtsIndexPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FtsIndex");

        internal static string FtsVersionStampPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FtsIndex", "fts.ver");

        internal static string BloomFolderPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters");

        // ── Database fingerprint (size + last-write time) ────────────────────────
        //
        // We previously used SHA256 of the entire DB file for change detection.
        // That approach had two problems:
        //   1. It reads the entire DB file on every app startup — several seconds
        //      of blocking I/O for large databases.
        //   2. SQLite WAL checkpoints write back to the main DB file during normal
        //      operation, changing the SHA256 hash even when the data hasn't changed
        //      in any way that would invalidate the FTS index. This caused spurious
        //      "database changed" rebuilds on every reload after any query activity.
        //
        // The replacement uses file size + last-write UTC ticks — two values that
        // are read from the filesystem metadata in a single stat() call (no file I/O).
        // A WAL checkpoint does change the main file's size and mtime, but only when
        // actual data pages are written back. In practice this is a reliable signal:
        // if the DB content changed enough to checkpoint, the FTS index may be stale.
        // If a checkpoint happens without any new books being added, the rebuild is
        // a false positive — but it is rare and far less disruptive than the old
        // SHA256 approach which triggered on every session.

        internal static string FtsDbHashPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FtsIndex", "fts.dbhash");

        /// <summary>
        /// Returns a fingerprint string for the database file based on its size and
        /// last-write time. The operation is instant — no file content is read.
        /// Returns null if the file does not exist or the metadata cannot be read.
        /// </summary>
        internal static string ComputeDbHash(string dbPath)
        {
            try
            {
                if (!File.Exists(dbPath)) return null;
                var info = new FileInfo(dbPath);
                // Format: "{size}:{lastWriteUtcTicks}"
                return info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            }
            catch { return null; }
        }

        /// <summary>
        /// Reads the stored database fingerprint from the stamp file.
        /// Returns null if the file does not exist or cannot be read.
        /// </summary>
        internal static string ReadDbHashStamp()
        {
            try
            {
                return File.Exists(FtsDbHashPath)
                    ? File.ReadAllText(FtsDbHashPath).Trim()
                    : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Writes the database fingerprint to the stamp file.
        /// </summary>
        internal static void WriteDbHashStamp(string dbHash)
        {
            try
            {
                Directory.CreateDirectory(FtsIndexPath);
                File.WriteAllText(FtsDbHashPath, dbHash ?? "");
            }
            catch { }
        }
    }
}
