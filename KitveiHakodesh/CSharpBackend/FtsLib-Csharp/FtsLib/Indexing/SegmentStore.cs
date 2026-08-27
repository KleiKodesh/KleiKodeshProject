using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Orchestrates the segment lifecycle: flush pipeline and crash recovery.
    ///
    /// Delegates to:
    ///   <see cref="SegmentLiveState"/> — thread-safe registry of live segments
    ///   <see cref="SegmentWriter"/>    — stateless .dat + .db I/O
    ///   <see cref="SegmentMerger"/>    — LSM merge logic
    ///   <see cref="SegmentWal"/>       — write-ahead log for crash safety
    ///
    /// Flush pipeline (fully non-blocking on the indexing thread):
    ///   Flush() hands a completed RamIndex to a background task and returns.
    ///   A depth-1 SemaphoreSlim provides back-pressure: the next flush cannot
    ///   start until the previous flush write AND any triggered merge both finish,
    ///   keeping at most one RamIndex queued in memory at any time.
    ///   After the write, MergeIfNeeded runs on the same task — an LSM merge fires
    ///   only when a level reaches the fanout threshold (4 segments).
    ///   WaitForMerge() drains the entire pipeline.
    ///
    /// Search / merge exclusion (Lucene-style commit locking):
    ///   A ReaderWriterLockSlim (_searchMergeLock) protects only the merge COMMIT —
    ///   the rename/delete/live-swap step at the end of SegmentMerger.MergeLevel,
    ///   which takes milliseconds. The long k-way merge write runs WITHOUT the lock:
    ///   it only reads source segments and writes .tmp files, both safe under
    ///   concurrent readers. Searches therefore proceed freely during merges.
    ///   GetLiveSegmentPaths()/AcquireSearchLease() take the read lock (blocking
    ///   briefly if a commit is in flight); a SearchLease keeps it held for the
    ///   reader's lifetime so a commit can never delete files under an open reader.
    ///   Flush writes (segment writes that do not trigger a merge) do not need the
    ///   lock — they only add a new segment to the live set, which is safe to
    ///   observe mid-search.
    /// </summary>
    internal sealed class SegmentStore
    {
        internal const int Fanout = 4;

        internal readonly SegmentLiveState Live;
        internal readonly SegmentWal       Wal;

        private readonly SegmentMerger        _merger;
        private readonly ForceMerger          _forceMerger;
        private DeleteSet                     _deleteSet;
        private readonly string               _dir;
        internal string Dir                          => _dir;
        internal ReaderWriterLockSlim SearchMergeLock => _searchMergeLock;
        internal SegmentMerger        Merger          => _merger;

        // Excludes searches from observing a partially-merged live set.
        // Write lock: held only for the brief merge COMMIT (rename + source
        //             deletion + live-state swap) inside SegmentMerger.MergeLevel.
        // Read lock: held while snapshotting live segment paths for a search, and
        //            for the whole search via SearchLease so the commit's source
        //            deletion waits until no reader has the files open.
        //
        // SupportsRecursion is required because Task continuations in the flush
        // pipeline can be inlined onto a thread pool thread that already holds a
        // read lock from a concurrent search.  With the default NoRecursion policy
        // TryEnterReadLock throws LockRecursionException in that case.  Recursion
        // support is safe here because the read lock is always paired with a
        // matching ExitReadLock in a finally block or via SearchLease.Dispose().
        private readonly ReaderWriterLockSlim _searchMergeLock =
            new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        // ── Flush pipeline ────────────────────────────────────────────
        // _flushSlot: depth-1 semaphore — back-pressure gate between indexing and I/O.
        // _pipelineTask: tail of the flush+merge chain; WaitForMerge() waits on it.
        private readonly SemaphoreSlim _flushSlot    = new SemaphoreSlim(1, 1);
        private readonly object        _pipelineLock = new object();
        private Task                   _pipelineTask = Task.CompletedTask;

        // Written by the background flush task after the segment is fully on disk.
        // Read by the indexing thread — volatile for safe cross-thread visibility.
        internal volatile int LastFlushedLineId = int.MinValue;

        // When true, each flush+merge task runs its thread in Windows background
        // processing mode (lowest CPU + very-low I/O priority) for the task's duration —
        // see BackgroundPriorityScope. Set by SeforimIndex.BuildIndex for a
        // backgroundPriority build (and cleared when it ends); the scope is entered
        // per task because these run on pool threads the mode must never leak onto.
        internal volatile bool FlushInBackgroundPriority;

        // Observed by long-running LSM merges. When cancelled, an in-flight merge
        // aborts within a fraction of a second. Aborting is always safe: no source
        // segment is ever deleted before the merged target is fully committed, so
        // an abort leaves only .tmp garbage plus an ABORT_MERGE WAL record.
        // Set by IndexWriter from the build's CancellationToken.
        internal CancellationToken MergeAbortToken = CancellationToken.None;

        // First exception thrown by a background flush write (not merge aborts).
        // Once set, the next Flush() call rethrows on the indexing thread so the
        // build fails fast instead of silently skipping a batch of lines while
        // later flushes keep advancing LastFlushedLineId past the gap.
        private volatile Exception _pipelineFault;
        internal Exception PipelineFault => _pipelineFault;

        // Set to true when WipeIndexDirectory() is called during recovery.
        // SeforimIndex checks this after BuildIndex to know whether to reset the store.
        internal bool IsWiped { get; private set; }

        internal SegmentStore(string dir)
        {
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            Live         = new SegmentLiveState(dir);
            Wal          = new SegmentWal(dir);
            _merger      = new SegmentMerger(this);
            _forceMerger = new ForceMerger(this);
            _dir         = dir;
        }

        // ── Delete set (used during Purge) ────────────────────────────

        internal void SetDeleteSet(DeleteSet ds) => _deleteSet = ds;
        internal DeleteSet GetDeleteSet()        => _deleteSet;

        // ── Live segment paths (used by IndexReader) ──────────────────

        // Merges hold the write lock only for their millisecond commit step, so a
        // read acquisition normally succeeds instantly. The timeout is a deadlock
        // backstop (e.g. a leaked SearchLease) — searches then fail with an
        // actionable error instead of hanging forever.
        private const int ReadLockTimeoutMs = 60_000;

        /// <summary>
        /// Returns a consistent snapshot of all live segment paths.
        /// Blocks briefly if a merge commit is in flight. Throws
        /// <see cref="IndexMergingException"/> only if the lock cannot be acquired
        /// within the backstop timeout.
        /// </summary>
        public List<(string dat, string db)> GetLiveSegmentPaths()
        {
            if (!_searchMergeLock.TryEnterReadLock(ReadLockTimeoutMs))
                throw new IndexMergingException();
            try
            {
                return Live.GetLiveSegmentPaths();
            }
            finally
            {
                _searchMergeLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Returns a consistent snapshot of all live segment paths together with a
        /// search lease that holds the read lock for the caller's lifetime.
        ///
        /// The caller MUST dispose the returned <see cref="SearchLease"/> when the
        /// search is complete (i.e. when the <see cref="IndexReader"/> is disposed).
        /// While the lease is held, any merge commit that needs to delete source
        /// segment files will block on the write lock — guaranteeing that no file
        /// the reader has open is deleted from under it.
        ///
        /// Blocks briefly if a merge commit is in flight. Throws
        /// <see cref="IndexMergingException"/> only if the lock cannot be acquired
        /// within the backstop timeout.
        /// </summary>
        public SearchLease AcquireSearchLease(out List<(string dat, string db)> livePaths)
        {
            if (!_searchMergeLock.TryEnterReadLock(ReadLockTimeoutMs))
                throw new IndexMergingException();

            // Read lock is now held. Snapshot the live paths while holding it.
            // The lease will release the lock when disposed.
            try
            {
                livePaths = Live.GetLiveSegmentPaths();
                return new SearchLease(_searchMergeLock);
            }
            catch
            {
                _searchMergeLock.ExitReadLock();
                throw;
            }
        }

        // ── Recovery ─────────────────────────────────────────────────

        public void Recover()
        {
            FtsLog.Write("SegmentStore.Recover", "starting recovery in " + _dir);

            // Step 1: Scan all segment files (including .tmp) AND the WAL to find
            // the highest segment ID ever allocated, so _nextSegId is set correctly
            // even if a .tmp file or a WAL-mentioned segment is about to be deleted.
            int maxSegId = ScanMaxSegId();

            // Also scan the WAL for any target segment IDs mentioned in BEGIN_MERGE
            // entries — these may be higher than any file on disk if a merge was
            // interrupted before the target file was created.
            var walRecovery = Wal.Analyze();
            if (walRecovery.PendingMerge != null && walRecovery.PendingMerge.Target > maxSegId)
                maxSegId = walRecovery.PendingMerge.Target;

            // Step 2: Delete all .tmp files — these are incomplete writes.
            foreach (var tmp in Directory.GetFiles(_dir, "*.tmp"))
            {
                try { File.Delete(tmp); } catch { /* best-effort */ }
            }

            // Step 2b: Delete all .del tombstones left by a previous version of the code.
            // The current merge implementation uses plain File.Delete (no tombstones),
            // but clean up any that remain from an older build.
            foreach (var del in Directory.GetFiles(_dir, "*.del"))
            {
                try { File.Delete(del); } catch { /* best-effort */ }
            }

            // Step 2c: Clean up orphaned SQLite WAL files (left behind from deleted segments).
            // These are -shm and -wal files whose corresponding .db file no longer exists.
            foreach (var walFile in Directory.GetFiles(_dir, "*.db-shm").Concat(Directory.GetFiles(_dir, "*.db-wal")))
            {
                string dbFile = walFile.Replace("-shm", "").Replace("-wal", "");
                if (!File.Exists(dbFile))
                {
                    try { File.Delete(walFile); } catch { /* best-effort */ }
                }
            }

            // Step 3: Rebuild live state from disk, passing the max segment ID
            // so _nextSegId starts at maxSegId + 1.
            Live.RebuildFromDisk(maxSegId);
            FtsLog.Write("SegmentStore.Recover",
                $"live state rebuilt — maxSegId={maxSegId}");

            // Step 4: Validate all segment files — if any are corrupt, wipe the index.
            // Exception: if a segment is the target of the pending merge in the WAL,
            // it may be partially written — skip it here and let the merge recovery
            // in Step 5 delete and re-run it.
            int pendingTarget = walRecovery.PendingMerge != null ? walRecovery.PendingMerge.Target : -1;
            int pendingTargetLevel = walRecovery.PendingMerge != null ? walRecovery.PendingMerge.Level + 1 : -1;
            try
            {
                ValidateAllSegments(pendingTarget, pendingTargetLevel);
            }
            catch (InvalidDataException ex)
            {
                Console.WriteLine("[Recovery] Corrupt segment detected during validation — wiping index for rebuild: " + ex.Message);
                WipeIndexDirectory();
                throw new CorruptIndexException("Corrupt segment detected during validation — index wiped for rebuild.", ex);
            }

            // Step 5: Check for interrupted merge and redo it if needed.
            if (walRecovery.PendingMerge == null)
            {
                // No pending level merge — but the WAL file may still exist (e.g. truncated/garbage).
                // Clear it so the next startup doesn't re-enter recovery unnecessarily.
                // If a force merge was interrupted between level merges, resume it now.
                if (walRecovery.PendingForceMerge)
                {
                    FtsLog.Write("SegmentStore.Recover",
                        "PendingForceMerge with no pending level merge — resuming from between-pass kill point");
                    _forceMerger.ResumeForceMerge();
                    return;
                }
                Wal.Clear();
                FtsLog.Write("SegmentStore.Recover", "no pending merge in WAL — WAL cleared, recovery complete");
                return;
            }

            var op = walRecovery.PendingMerge;
            Console.WriteLine($"[Recovery] Interrupted merge: L{op.Level} → target {op.Target}");
            FtsLog.Write("SegmentStore.Recover",
                $"INTERRUPTED MERGE found in WAL: L{op.Level}→L{op.Level+1} sources=[{string.Join(",",op.Sources)}] target={op.Target}");

            string targetDat = Live.SegDatPath(op.Level + 1, op.Target);
            string targetDb  = Live.SegDbPath(op.Level + 1, op.Target);

            // Determine how far the merge got before the crash.
            // The commit order in SegmentMerger.MergeLevel is strict:
            //   step 1: rename BOTH .tmp files → final names
            //   step 2: delete source segments (one by one)
            //   step 3: write END_MERGE
            //
            // Therefore: if both target files exist at their FINAL names and pass
            // full validation, step 1 completed and the target contains ALL data
            // from every source — regardless of how many sources step 2 already
            // deleted. Trust the target (generalized Case B).
            //
            // The old logic only trusted the target when every source was gone.
            // A kill landing mid-step-2 (some sources deleted, some not) deleted
            // the complete target and re-merged only the surviving sources —
            // permanently losing the deleted sources' data ("results from early
            // segments disappear").
            //
            // Remaining cases:
            //   - target missing/invalid, ALL sources present → step 1 never
            //     completed, nothing was deleted; delete partial target, re-run.
            //   - target missing/invalid, ANY source missing  → unrecoverable;
            //     wipe and rebuild.
            bool targetExists   = File.Exists(targetDat) && File.Exists(targetDb);
            bool targetComplete = targetExists && ValidateSegmentFully(targetDat, targetDb);

            if (targetExists && !targetComplete)
                FtsLog.Write("SegmentStore.Recover",
                    $"target seg_{op.Level + 1}_{op.Target} exists at final path but FAILED full validation — treating as partial");

            if (targetComplete)
            {
                // Generalized Case B: the target is fully committed. Register it,
                // then finish step 2 by deleting whatever sources remain. Deleting
                // sources BEFORE writing END_MERGE keeps this idempotent: a crash
                // mid-cleanup lands back here on the next startup.
                Console.WriteLine($"[Recovery] Merge target is complete — registering target and removing leftover sources");
                FtsLog.Write("SegmentStore.Recover",
                    $"Generalized Case B: target seg_{op.Level + 1}_{op.Target} validated — registering as live, deleting leftover sources");
                Live.AddToLive(op.Level + 1, op.Target);
                foreach (int sid in op.Sources)
                {
                    string srcDat = Live.SegDatPath(op.Level, sid);
                    string srcDb  = Live.SegDbPath(op.Level, sid);
                    if (File.Exists(srcDat) || File.Exists(srcDb))
                        FtsLog.Write("SegmentStore.Recover",
                            $"  deleting leftover source seg_{op.Level}_{sid}");
                    DeleteIfExists(srcDat);
                    DeleteIfExists(srcDb);
                    DeleteIfExists(srcDb + "-shm");
                    DeleteIfExists(srcDb + "-wal");
                    Live.RemoveFromLive(op.Level, sid);
                }
                // Write END_MERGE so the WAL is well-formed, then clear it entirely
                // so the next startup does not re-enter recovery unnecessarily.
                Wal.Open();
                Wal.EndMerge(op.Level, op.Target);
                Wal.Clear();
                FtsLog.Write("SegmentStore.Recover", "Case B recovery complete — WAL cleared");

                // If this was part of an interrupted force merge, resume remaining levels.
                if (walRecovery.PendingForceMerge)
                {
                    FtsLog.Write("SegmentStore.Recover",
                        "Case B + PendingForceMerge — resuming remaining levels");
                    _forceMerger.ResumeForceMerge();
                }
                return;
            }

            // Target missing or invalid — it contributed nothing durable and step 2
            // (source deletion) cannot have started. Delete any partial target files.
            DeleteIfExists(targetDat);
            DeleteIfExists(targetDb);
            // Also delete SQLite's WAL files
            DeleteIfExists(targetDb + "-shm");
            DeleteIfExists(targetDb + "-wal");
            Live.RemoveFromLive(op.Level + 1, op.Target);

            // A re-run only reproduces the full target if EVERY source survived.
            bool allSourcesExist = true;
            foreach (int sid in op.Sources)
            {
                if (!File.Exists(Live.SegDatPath(op.Level, sid)) ||
                    !File.Exists(Live.SegDbPath(op.Level, sid)))
                {
                    allSourcesExist = false;
                    FtsLog.Write("SegmentStore.Recover",
                        $"source seg_{op.Level}_{sid} is missing (dat or db)");
                    break;
                }
            }

            if (!allSourcesExist)
            {
                // Some source data survives neither in a source file nor in a
                // complete target — the index is unrecoverable without data loss.
                Console.WriteLine("[Recovery] Merge target incomplete and source segments missing — wiping index for rebuild");
                FtsLog.Write("SegmentStore.Recover",
                    "Case D: target incomplete AND sources missing — wiping directory and throwing CorruptIndexException");
                WipeIndexDirectory();
                throw new CorruptIndexException("Merge source segments missing and target incomplete — index wiped for rebuild.", null);
            }

            foreach (int sid in op.Sources)
                Live.AddToLive(op.Level, sid);

            FtsLog.Write("SegmentStore.Recover",
                $"Case A/C: re-running merge from sources — targetExists={targetExists} allSourcesExist={allSourcesExist}");

            Wal.Open();
            try
            {
                FtsLog.Write("SegmentStore.Recover", "re-running merge for recovery...");
                _merger.MergeLevel(op.Level, targetSegId: op.Target);
                FtsLog.Write("SegmentStore.Recover", "recovery merge complete");
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is FileNotFoundException || ex is IOException)
            {
                // Torn doc_source in a source segment arrives here as
                // InvalidDataException (wrapped at the read site in SegmentMerger),
                // so it heals like any other corruption. Environmental SQLite
                // failures writing the NEW target are deliberately NOT caught.
                Console.WriteLine("[Recovery] Corrupt or missing segment during merge — wiping index for rebuild: " + ex.Message);
                FtsLog.Write("SegmentStore.Recover",
                    "corrupt/missing segment during recovery merge — wiping: " + ex.GetType().Name + ": " + ex.Message);
                WipeIndexDirectory();
                throw new CorruptIndexException("Corrupt or missing segment detected during merge — index wiped for rebuild.", ex);
            }
            finally
            {
                // MergeLevel writes END_MERGE into the WAL. Now clear the whole file
                // so the next startup finds no pending merge and skips recovery.
                Wal.Clear();
                FtsLog.Write("SegmentStore.Recover", "Case A/C recovery merge complete — WAL cleared");
            }

            // If this was an interrupted force merge, resume merging the remaining levels.
            if (walRecovery.PendingForceMerge)
            {
                FtsLog.Write("SegmentStore.Recover",
                    "Case A/C + PendingForceMerge — resuming remaining levels");
                _forceMerger.ResumeForceMerge();
            }
        }

        /// <summary>
        /// Rebuilds the live segment state from the files on disk WITHOUT mutating
        /// anything — no .tmp cleanup, no WAL replay, no deletions, no merge re-run.
        /// Used when another process holds the index write lock: this process can
        /// still search whatever is currently on disk, and full recovery runs later
        /// under the lock (next BuildIndex, or the next SeforimIndex construction
        /// after the other process finishes).
        /// </summary>
        public void RecoverReadOnly()
        {
            FtsLog.Write("SegmentStore.RecoverReadOnly",
                "read-only live-state rebuild (write lock held elsewhere) in " + _dir);
            Live.RebuildFromDisk(ScanMaxSegId());
        }

        /// <summary>
        /// Shallow, non-mutating readability probe of every live segment: confirms
        /// each .dat/.db pair exists and the .dat's first record parses. Reads only a
        /// small buffer per segment (not the 4 MB streaming buffer), so it is cheap
        /// even cold. Returns false on the first unreadable segment.
        ///
        /// Used by the finalized-clean fast path in <see cref="SeforimIndex"/>: if the
        /// probe fails, the caller falls through to full <see cref="Recover"/>, which
        /// diagnoses and heals (wipe + rebuild) exactly as before. This preserves the
        /// startup self-heal for a finalized index whose files went bad after a clean
        /// close, without paying the full recovery cost when nothing is wrong.
        /// Must be called before any background tasks start — not thread-safe.
        /// </summary>
        internal bool TryProbeLiveSegments()
        {
            const int ProbeBufferSize = 64 * 1024;
            foreach (var (dat, db) in Live.GetLiveSegmentPaths())
            {
                if (!File.Exists(dat) || !File.Exists(db))
                {
                    FtsLog.Write("SegmentStore.TryProbeLiveSegments",
                        $"missing file for {Path.GetFileName(dat)} — probe failed");
                    return false;
                }
                try
                {
                    using (var reader = new SegmentReader(dat, ProbeBufferSize))
                        reader.MoveNext();
                }
                catch (Exception ex)
                {
                    FtsLog.Write("SegmentStore.TryProbeLiveSegments",
                        $"{Path.GetFileName(dat)} unreadable: {ex.GetType().Name}: {ex.Message}");
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Scans all segment files (including .tmp) for the highest segment ID ever
        /// allocated, so _nextSegId never reuses an ID even after .tmp cleanup.
        /// </summary>
        private int ScanMaxSegId()
        {
            int maxSegId = -1;
            foreach (var file in Directory.GetFiles(_dir, "seg_*.*"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                // Handle both "seg_0_5.dat" and "seg_0_5.dat.tmp"
                if (name.EndsWith(".dat")) name = name.Substring(0, name.Length - 4);
                var parts = name.Split('_');
                if (parts.Length == 3 && int.TryParse(parts[2], out int segId))
                {
                    if (segId > maxSegId) maxSegId = segId;
                }
            }
            return maxSegId;
        }

        /// <summary>
        /// Full structural validation of a segment pair. Scans every record of the
        /// .dat file to its end and cross-checks the .db term index (row count must
        /// match the term count, and the maximum posting extent must fit inside the
        /// .dat file). Used by merge recovery before trusting a target segment that
        /// has no END_MERGE record — a rename can land before the data is durable
        /// (e.g. power loss), so existence at the final path alone is not proof.
        /// </summary>
        private static bool ValidateSegmentFully(string datPath, string dbPath)
        {
            try
            {
                long datLen = new FileInfo(datPath).Length;
                if (datLen == 0) return false;

                // Note: a truncated final record does not throw — BinaryReader
                // returns short reads. The .db row-count cross-check below catches
                // that case because the truncated record is not counted.
                long termCount = 0;
                using (var reader = new SegmentReader(datPath))
                    while (reader.MoveNext()) termCount++;
                if (termCount == 0) return false;

                // doc_source must be readable too (or absent — that's the legacy
                // identity fallback). Searches and merges gate on this table with
                // read-failures PROPAGATING, so committing a target whose
                // doc_source is torn (while term_index survived) would brick the
                // index with no self-heal — unlike every other corruption mode,
                // which recovery wipes and rebuilds. A throw here lands in the
                // catch below → validation fails → normal recovery handling.
                ReadDocSourceRows(dbPath);

                // Pooling=false: a pooled read handle would survive this using-block and
                // keep the segment .db open, blocking a later merge's File.Delete of it.
                using (var conn = new SqliteConnection(
                    new SqliteConnectionStringBuilder
                    {
                        DataSource = dbPath,
                        Mode = SqliteOpenMode.ReadOnly,
                        Pooling = false,
                    }.ConnectionString))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT COUNT(*), COALESCE(MAX(offset + length), 0) FROM term_index";
                        using (var r = cmd.ExecuteReader())
                        {
                            if (!r.Read()) return false;
                            long rows   = r.GetInt64(0);
                            long maxEnd = r.GetInt64(1);
                            if (rows != termCount) return false;
                            if (maxEnd > datLen) return false;
                        }
                    }
                }
                FtsLog.Write("SegmentStore.ValidateSegmentFully",
                    $"{Path.GetFileName(datPath)} OK — {termCount:N0} terms, {datLen:N0}B");
                return true;
            }
            catch (Exception ex)
            {
                FtsLog.Write("SegmentStore.ValidateSegmentFully",
                    $"{Path.GetFileName(datPath)} FAILED: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Validates all live segment files by attempting to read their headers.
        /// Skips the pending merge target (if any) — it may be partial and will
        /// be handled by merge recovery in the next step.
        /// Throws InvalidDataException if any other segment is corrupt.
        /// </summary>
        private void ValidateAllSegments(int skipSegId = -1, int skipLevel = -1)
        {
            var paths = Live.GetLiveSegmentPaths();
            foreach (var (dat, db) in paths)
            {
                // Skip the pending merge target — may be partially written;
                // merge recovery (Step 5) will delete and re-run it.
                var nameParts = Path.GetFileNameWithoutExtension(dat).Split('_');
                if (nameParts.Length == 3 &&
                    int.TryParse(nameParts[1], out int fileLevel) &&
                    int.TryParse(nameParts[2], out int fileSegId) &&
                    fileSegId == skipSegId && fileLevel == skipLevel)
                {
                    FtsLog.Write("SegmentStore.ValidateAllSegments",
                        $"skipping pending merge target seg_{fileLevel}_{fileSegId} — recovery handles it");
                    Live.RemoveFromLive(fileLevel, fileSegId);
                    continue;
                }

                if (!File.Exists(dat))
                    throw new InvalidDataException($"Segment .dat file missing: {dat}");
                if (!File.Exists(db))
                    throw new InvalidDataException($"Segment .db file missing: {db}");

                try
                {
                    using (var reader = new SegmentReader(dat))
                        reader.MoveNext();
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException($"Corrupt segment file: {dat}", ex);
                }
            }
        }

        private void WipeIndexDirectory()
        {
            FtsLog.Write("SegmentStore.WipeIndexDirectory", "wiping all files in " + _dir);
            IsWiped = true;
            foreach (var file in Directory.GetFiles(_dir))
            {
                try { File.Delete(file); }
                catch (Exception ex)
                {
                    FtsLog.Write("SegmentStore.WipeIndexDirectory",
                        $"could not delete {Path.GetFileName(file)}: {ex.Message}");
                }
            }
            FtsLog.Write("SegmentStore.WipeIndexDirectory", "wipe complete");
        }

        // ── Resume support ────────────────────────────────────────────

        /// <summary>
        /// Highest doc (line) ID recorded in any live segment's <c>segment_meta</c>
        /// table, or -1 when no live segment carries one (segments written before
        /// this metadata existed).
        ///
        /// Used at build-resume time: the build.progress file lags the background
        /// flush and (in older builds) was not written atomically, so after a hard
        /// kill it can point BELOW lines already durably flushed — resuming there
        /// would re-index those lines into a second segment, and overlapping doc
        /// ranges silently corrupt AND-search results (non-ascending concatenated
        /// posting streams). The segments themselves are the source of truth.
        /// </summary>
        internal int MaxSegmentLastDoc()
        {
            int max = -1;
            foreach (var (dat, db) in Live.GetLiveSegmentPaths())
            {
                try
                {
                    // Pooling=false: a pooled handle would outlive this using-block
                    // and block a later merge's File.Delete of the segment.
                    using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder
                           {
                               DataSource = db,
                               Mode       = SqliteOpenMode.ReadOnly,
                               Pooling    = false,
                           }.ConnectionString))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT value FROM segment_meta WHERE key='last_doc'";
                            object r = cmd.ExecuteScalar();
                            if (r != null && r != DBNull.Value)
                            {
                                int v = (int)Convert.ToInt64(r);
                                if (v > max) max = v;
                            }
                        }
                    }
                }
                catch
                {
                    // Segment predates segment_meta, or a transient open failure —
                    // skip it; the caller falls back to the progress file.
                }
            }
            return max;
        }

        /// <summary>
        /// Reads a segment .db's doc_source rows (the persisted docId→corpus map).
        /// Returns an empty list for segments that predate the table — callers
        /// treat that as library-identity, the meaning such segments were built
        /// under. A MISSING table is detected via sqlite_master (never an error);
        /// an actual read failure on a present table propagates — the merger must
        /// abort (recoverable: sources untouched) rather than silently write a
        /// merged segment whose docs lost their corpus mapping.
        /// Pooling=false so the handle never outlives the call and blocks a later
        /// merge's File.Delete (same rationale as <see cref="MaxSegmentLastDoc"/>).
        /// </summary>
        internal static List<DocSourceRange> ReadDocSourceRows(string dbPath)
        {
            var rows = new List<DocSourceRange>();
            using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder
                   {
                       DataSource = dbPath,
                       Mode       = SqliteOpenMode.ReadOnly,
                       Pooling    = false,
                   }.ConnectionString))
            {
                conn.Open();
                using (var probe = conn.CreateCommand())
                {
                    probe.CommandText =
                        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='doc_source'";
                    if (Convert.ToInt64(probe.ExecuteScalar()) == 0)
                        return rows; // pre-doc_source segment — identity fallback
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        "SELECT doc_lo, doc_hi, source, src_lo FROM doc_source ORDER BY doc_lo";
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                            rows.Add(new DocSourceRange(
                                r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3)));
                    }
                }
            }
            return rows;
        }

        /// <summary>
        /// TEST HOOK — drops a segment's doc_source table to simulate a segment
        /// written before the table existed (legacy-fallback and mixed-merge
        /// tests). Never called by production code.
        /// </summary>
        internal static void DropDocSourceTableForTest(string dbPath)
        {
            using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder
                   { DataSource = dbPath, Pooling = false }.ConnectionString))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DROP TABLE IF EXISTS doc_source";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // ── Flush ─────────────────────────────────────────────────────

        /// <summary>
        /// Schedules <paramref name="ramIndex"/> to be written to a new level-0
        /// segment on a background task and returns immediately.
        ///
        /// Ownership of <paramref name="ramIndex"/> transfers to the background task —
        /// the caller must not touch it after this call.
        ///
        /// If the previous flush write is still in flight, this call blocks on the
        /// calling thread until it finishes (depth-1 back-pressure), then returns.
        ///
        /// <paramref name="lineId"/> is the highest line ID in the batch; it is
        /// written to <see cref="LastFlushedLineId"/> once the segment is on disk.
        ///
        /// <paramref name="docSourceRows"/> — the corpus layout clipped to this
        /// batch's doc range (computed by the caller on the indexing thread);
        /// persisted as the segment's doc_source table.
        /// </summary>
        public void Flush(RamIndex ramIndex, int lineId, List<DocSourceRange> docSourceRows = null)
        {
            // Back-pressure: block until the previous flush+merge cycle is free.
            _flushSlot.Wait();

            // Fail fast: if a previous background segment write failed, those lines
            // never reached disk. Continuing would let later flushes advance
            // LastFlushedLineId past the gap, so the progress file would claim
            // lines that were never indexed. Abort the build instead — the progress
            // file still points at the last segment that truly completed.
            Exception fault = _pipelineFault;
            if (fault != null)
            {
                _flushSlot.Release();
                throw new IOException(
                    "A previous background segment write failed — aborting build. " +
                    "Resume will continue from the last fully flushed segment.", fault);
            }

            int    segId   = Live.NextSegId();
            string datPath = Live.SegDatPath(0, segId);
            string dbPath  = Live.SegDbPath(0, segId);

            // Captured HERE, on the indexing thread, rather than read inside the task: the
            // flag belongs to the build that SCHEDULED this flush, and BuildIndex's finally
            // can clear it while a continuation is still queued (a build that throws, or the
            // last flush of a cancelled one). Reading it late let a background build's final
            // — usually largest — merge run at full priority, and let an unrelated caller's
            // flush (IndexWriter.Purge takes no write lock) get throttled without asking.
            bool flushAtBackgroundPriority = FlushInBackgroundPriority;

            // Sort terms on the calling thread — cheap, and keeps the background
            // task focused purely on I/O.
            var terms = new List<string>(ramIndex.Count);
            foreach (var kvp in ramIndex) terms.Add(kvp.Key);
            terms.Sort(StringComparer.Ordinal);

            FtsLog.Write("SegmentStore.Flush",
                $"scheduling seg_0_{segId} for lineId={lineId} ({ramIndex.Count:N0} terms)");

            lock (_pipelineLock)
            {
                _pipelineTask = _pipelineTask.ContinueWith(_ =>
                {
                    // The slot is released only after both the write AND any triggered
                    // merge complete, so the next flush never starts while a merge is
                    // still running on this thread.
                    // Background-priority builds lower this pool thread for exactly this
                    // task — the scope's Dispose in the outer finally restores the thread
                    // before the pool reuses it for unrelated work.
                    var priorityScope = BackgroundPriorityScope.EnterIf(flushAtBackgroundPriority);
                    try
                    {
                        FtsLog.Write("SegmentStore.Flush.BgTask",
                            $"writing seg_0_{segId} to disk...");
                        SegmentWriter.WriteSegment(ramIndex, terms, datPath, dbPath, docSourceRows);
                        Live.AddToLive(0, segId);
                        LastFlushedLineId = lineId;
                        FtsLog.Write("SegmentStore.Flush.BgTask",
                            $"seg_0_{segId} written — LastFlushedLineId={lineId}");

                        Wal.Open();
                        try
                        {
                            // No write lock here: the long merge write runs
                            // concurrently with searches. MergeLevel takes the
                            // write lock itself, only around its commit step.
                            FtsLog.Write("SegmentStore.Flush.BgTask",
                                $"running MergeIfNeeded(0) after seg_0_{segId} — L0 has {Live.LiveSegCount(0)} seg(s)");
                            try
                            {
                                _merger.MergeIfNeeded(0);
                            }
                            catch (OperationCanceledException)
                            {
                                // Shutdown requested mid-merge. The merge already
                                // cleaned up its .tmp files and recorded ABORT_MERGE;
                                // no source segment was touched. The flushed segment
                                // itself is complete, so this is NOT a pipeline fault.
                                FtsLog.Write("SegmentStore.Flush.BgTask",
                                    "merge aborted by shutdown request — flushed segment is intact");
                            }
                        }
                        finally
                        {
                            Wal.Close();
                        }
                        FtsLog.Write("SegmentStore.Flush.BgTask",
                            $"flush+merge cycle done for seg_0_{segId} — totalLiveSegs={Live.TotalLiveSegs()}");
                    }
                    catch (Exception ex)
                    {
                        // Segment write (or merge) failed. Record the fault so the
                        // next Flush() aborts the build on the indexing thread and
                        // WaitForMerge() knows not to clear the WAL.
                        _pipelineFault = ex;
                        FtsLog.Write("SegmentStore.Flush.BgTask",
                            $"PIPELINE FAULT for seg_0_{segId}: {ex.GetType().Name}: {ex.Message}");
                        throw;
                    }
                    finally
                    {
                        priorityScope?.Dispose();
                        _flushSlot.Release();
                    }

                }, TaskContinuationOptions.None);
            }
        }

        // ── Pipeline drain ────────────────────────────────────────────

        /// <summary>
        /// Waits for all pending flush writes and any triggered LSM merges to finish.
        /// Must be called before force merge or before disposing the store.
        /// </summary>
        /// <param name="clearWal">
        /// When true (default), clears the WAL file after draining so the next startup
        /// does not treat a finished index as needing recovery.
        /// Pass false when called internally before a force merge — the force merge
        /// will manage the WAL itself.
        /// </param>
        public void WaitForMerge(bool clearWal = true)
        {
            Task task;
            lock (_pipelineLock) { task = _pipelineTask; }
            try { task.Wait(); }
            catch (AggregateException ae)
            {
                Console.WriteLine("[SegmentStore] Pipeline exception: " + ae.InnerException?.Message);
                // Do not clear the WAL if the pipeline faulted — we may need it for recovery.
                return;
            }

            // A fault in an EARLIER task of the chain does not fault the tail task,
            // so task.Wait() above succeeds even when a mid-build flush failed.
            // Check the recorded fault explicitly before declaring the drain clean.
            if (_pipelineFault != null)
            {
                Console.WriteLine("[SegmentStore] Pipeline previously faulted: " + _pipelineFault.Message);
                FtsLog.Write("SegmentStore.WaitForMerge",
                    "pipeline drained but a flush previously FAULTED — WAL preserved: " + _pipelineFault.Message);
                return;
            }

            if (clearWal)
            {
                // All flushes and merges completed cleanly. Delete the WAL so the next
                // startup does not treat a finished index as needing recovery.
                Wal.Clear();
                FtsLog.Write("SegmentStore.WaitForMerge", "pipeline drained cleanly — WAL cleared");
            }
            else
            {
                FtsLog.Write("SegmentStore.WaitForMerge", "pipeline drained cleanly — WAL preserved (force merge will manage it)");
            }
        }

        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Incrementally merges all levels of the LSM tree bottom-up until every
        /// level has at most one segment. Searches run concurrently — each level
        /// merge takes the write lock only for its own commit step.
        ///
        /// Drains the background flush/merge pipeline first so no in-flight
        /// segment write or automatic LSM merge can race with the force merge.
        /// See <see cref="ForceMerger"/> for the full protocol.
        /// </summary>
        internal void MergeAll()
        {
            // Drain any in-flight background flush+merge tasks before opening
            // the WAL. This guarantees that every segment produced by the build
            // pipeline is on disk and the automatic LSM merges have all completed
            // before the force merge starts.
            // clearWal:false — ForceMerger manages the WAL itself.
            WaitForMerge(clearWal: false);

            Wal.Open();
            try   { _forceMerger.Run(); }
            finally { /* Wal.Clear() is done inside ForceMerger.Run */ }
        }

        /// <summary>Exposed for ForceMerger.ResumeForceMerge to call during recovery.</summary>
        internal void WipeIndexDirectoryInternal() => WipeIndexDirectory();

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
