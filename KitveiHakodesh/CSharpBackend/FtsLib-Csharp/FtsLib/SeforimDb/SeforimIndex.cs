using FtsLib.Indexing;
using FtsLib.Tokenization;
using System;
using System.Collections.Generic;
using System.Threading;

namespace FtsLib.SeforimDb
{
    /// <summary>
    /// Public API for full-text search over the seforim database.
    ///
    /// Owns a long-lived <see cref="SegmentStore"/> so that live segment state is
    /// always consistent between build sessions and concurrent searches. The store
    /// is initialised once (with crash recovery) in the constructor and reused for
    /// every subsequent <see cref="BuildIndex"/> and <see cref="Search"/> call.
    /// This eliminates the race where a search opens segments by scanning the
    /// directory while a concurrent merge is deleting source files.
    ///
    /// Query syntax:
    ///   word        — literal AND term
    ///   word*       — wildcard (prefix / infix / suffix)
    ///   wor?d       — optional char: the char before '?' is optional (matches "word" and "wrd")
    ///   word~       — fuzzy match, edit distance 1
    ///   word~2      — fuzzy match, edit distance 2
    ///   word~3      — fuzzy match, edit distance 3 (maximum)
    ///   a | b       — OR: lines matching a OR b satisfy this AND slot
    ///
    /// Multiple tokens are AND-ed together.
    /// '|'-separated tokens are OR-ed within one AND slot.
    /// Wildcard and fuzzy tokens are OR-expanded internally before the intersection;
    /// OR groups merge all their expansions.
    /// </summary>
    public sealed class SeforimIndex
    {
        private readonly string       _indexPath;
        private readonly string       _dbPath;
        private          SegmentStore _store;

        /// <summary>Default visible-character budget for the snippet window.</summary>
        public const int DefaultSnippetLength = SnippetPipeline.DefaultContextWords; // kept for binary compat

        /// <summary>Default number of words of context shown on each side of the match.</summary>
        public const int DefaultContextWords = SnippetPipeline.DefaultContextWords;

        public SeforimIndex(string indexPath, string dbPath)
        {
            if (string.IsNullOrWhiteSpace(indexPath))
                throw new ArgumentException("indexPath must not be empty.", nameof(indexPath));
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("dbPath must not be empty.", nameof(dbPath));

            _indexPath = indexPath;
            _dbPath    = dbPath;

            // Initialise the store eagerly so crash recovery runs once at startup
            // and the live segment state is ready before the first search.
            EnsureStore();

            // Self-heal trigram sidecars. The .tgm file's presence (and being newer
            // than its .db) IS the durable "built" marker — the WAL can't carry that
            // signal (it is cleared after every op, so it only ever means "an op was
            // INTERRUPTED", never "never built"). This covers: an index built before
            // trigrams existed; a build that finished but the process died before/while
            // sidecars were generated; and a per-segment gap if a multi-segment build
            // was interrupted. Build() writes .tmp then atomically renames, so a
            // half-written sidecar never appears as a complete seg.tgm — a partial
            // crash leaves only a .tmp, which this treats as "not built" and rebuilds.
            EnsureSidecars();
        }

        /// <summary>
        /// Builds any missing or stale trigram sidecar for the live segments. Runs under
        /// the index write lock (skips silently if another process holds it — that process's
        /// force-merge, or the next open, will build them). Best-effort per segment: a
        /// failure just leaves that segment on the SQLite LIKE fallback. A sidecar is
        /// considered stale (and rebuilt) if it is older than its source .db.
        /// </summary>
        private void EnsureSidecars()
        {
            System.Collections.Generic.List<(string dat, string db)> live;
            try { live = _store.GetLiveSegmentPaths(); }
            catch { return; } // store not in a queryable state — nothing to do

            // Cheap pre-check with no lock: is anything actually missing/stale?
            bool anyNeeded = false;
            foreach (var (dat, db) in live)
            {
                string tgm = FtsLib.Search.TrigramIndex.SidecarPath(dat);
                if (NeedsSidecar(tgm, db)) { anyNeeded = true; break; }
            }
            if (!anyNeeded) return;

            try
            {
                using (new IndexWriteLock(_indexPath))
                {
                    // Re-read live paths under the lock — recovery/merge may have changed them.
                    foreach (var (dat, db) in _store.GetLiveSegmentPaths())
                    {
                        string tgm = FtsLib.Search.TrigramIndex.SidecarPath(dat);
                        if (!NeedsSidecar(tgm, db)) continue;
                        try
                        {
                            FtsLib.Search.TrigramIndex.BuildFromDb(db, tgm);
                            FtsLib.Indexing.FtsLog.Write("SeforimIndex.EnsureSidecars", "built " + tgm);
                        }
                        catch (System.Exception ex)
                        {
                            FtsLib.Indexing.FtsLog.Write("SeforimIndex.EnsureSidecars", "skip " + db + ": " + ex.Message);
                        }
                    }
                }
            }
            catch (IndexWriteLockException)
            {
                FtsLib.Indexing.FtsLog.Write("SeforimIndex.EnsureSidecars",
                    "write lock busy — another process is building; deferring sidecar build");
            }
        }

        /// <summary>A sidecar must be (re)built when it is absent, or older than the source
        /// .db (a newer .db means the segment was rebuilt/merged since the sidecar).</summary>
        private static bool NeedsSidecar(string tgm, string db)
        {
            if (!System.IO.File.Exists(tgm)) return true;
            if (!System.IO.File.Exists(db)) return false; // no source — leave whatever exists
            try { return System.IO.File.GetLastWriteTimeUtc(tgm) < System.IO.File.GetLastWriteTimeUtc(db); }
            catch { return false; }
        }

        // ── Store lifecycle ───────────────────────────────────────────

        private void EnsureStore()
        {
            if (!System.IO.Directory.Exists(_indexPath))
                System.IO.Directory.CreateDirectory(_indexPath);

            _store = new SegmentStore(_indexPath);

            bool hasSegments = System.IO.Directory.GetFiles(_indexPath, "seg_*.dat").Length > 0;
            bool hasWal      = System.IO.File.Exists(System.IO.Path.Combine(_indexPath, "wal.log"));

            if (!hasSegments && !hasWal)
            {
                FtsLib.Indexing.FtsLog.Write("SeforimIndex.EnsureStore", "no segments and no WAL — skipping recovery");
                return;
            }

            // Fast path — a fully finalized, cleanly-closed index needs no crash
            // recovery. "Finalized + clean" means segments exist AND there is no
            // interrupted-work artifact of any kind:
            //   • no wal.log        → no interrupted level / force merge
            //   • no *.tmp          → no interrupted segment write
            //   • no build.progress → the build ran to completion (not paused mid-way)
            //   • no *.del          → no legacy tombstones awaiting cleanup
            // In that state the mutating Recover() below would only re-scan, re-validate
            // and re-clear an already-consistent directory — pure startup tax, and it
            // cold-reads a 4 MB buffer per segment just to validate headers. Instead we
            // rebuild the live segment state read-only (directory enumeration only) and
            // do a shallow readability probe. If the probe fails (a finalized index
            // whose files went bad after close), we fall through to full recovery, which
            // diagnoses and heals exactly as before — so no self-heal is lost.
            if (hasSegments && !hasWal && IsFinalizedClean())
            {
                _store.RecoverReadOnly();
                if (_store.TryProbeLiveSegments())
                {
                    FtsLib.Indexing.FtsLog.Write("SeforimIndex.EnsureStore",
                        "finalized-clean index — skipped full recovery (read-only open)");
                    return;
                }
                FtsLib.Indexing.FtsLog.Write("SeforimIndex.EnsureStore",
                    "finalized-clean probe FAILED — falling through to full recovery");
                // Reset the store so full recovery starts from a fresh live state.
                _store = new SegmentStore(_indexPath);
            }

            // Crash recovery MUTATES the index directory: it deletes .tmp files and
            // may re-run or finalize an interrupted merge. Running it while another
            // process is actively building/merging the same directory would destroy
            // that process's in-flight work — so recovery only runs under the
            // exclusive write lock. If the lock is busy, skip recovery: rebuild the
            // live segment state read-only so searches still work, and let full
            // recovery run later under the lock (the next BuildIndex, or the next
            // SeforimIndex construction after the other process finishes).
            try
            {
                using (new IndexWriteLock(_indexPath))
                {
                    Console.WriteLine("[SeforimIndex] Segments found — running crash recovery...");
                    FtsLib.Indexing.FtsLog.Write("SeforimIndex.EnsureStore", "segments or WAL found — running recovery under write lock");
                    _store.Recover();
                    Console.WriteLine("[SeforimIndex] Recovery complete.");
                    FtsLib.Indexing.FtsLog.Write("SeforimIndex.EnsureStore", "recovery complete");
                }
            }
            catch (IndexWriteLockException)
            {
                Console.WriteLine("[SeforimIndex] Another process is writing to the index — skipping recovery (read-only open).");
                FtsLib.Indexing.FtsLog.Write("SeforimIndex.EnsureStore",
                    "write lock busy — another process is building; skipping recovery, rebuilding live state read-only");
                _store.RecoverReadOnly();
            }
            catch (CorruptIndexException ex)
            {
                // Recovery wiped the directory — start with a clean store.
                FtsLib.Indexing.FtsLog.Write("SeforimIndex.EnsureStore",
                    "CorruptIndexException during recovery — directory wiped, starting clean: " + ex.Message);
                _store = new SegmentStore(_indexPath);
            }
        }

        /// <summary>
        /// True when the index directory contains no interrupted-work artifacts —
        /// no incomplete segment writes (*.tmp), no legacy tombstones (*.del), and no
        /// in-progress build (build.progress). Combined with "segments exist and no
        /// wal.log", this identifies a build that ran to completion and shut down
        /// cleanly, so full crash recovery can be safely skipped. See EnsureStore.
        /// </summary>
        private bool IsFinalizedClean()
        {
            try
            {
                if (System.IO.Directory.GetFiles(_indexPath, "*.tmp").Length > 0) return false;
                if (System.IO.Directory.GetFiles(_indexPath, "*.del").Length > 0) return false;
                if (System.IO.File.Exists(System.IO.Path.Combine(_indexPath, "build.progress"))) return false;
                return true;
            }
            catch
            {
                // If we can't inspect the directory, don't take the fast path — let
                // full recovery run and surface any real problem.
                return false;
            }
        }

        private void ResetStore()
        {
            _store = new SegmentStore(_indexPath);
        }

        /// <summary>
        /// Returns a consistent snapshot of all live segment paths under the store lock,
        /// together with a <see cref="SearchLease"/> that keeps the read lock held.
        ///
        /// The caller MUST dispose the lease when the corresponding
        /// <see cref="IndexReader"/> is disposed, so that any pending merge is
        /// unblocked as soon as the reader's file handles are closed.
        /// </summary>
        internal SearchLease AcquireSearchLease(out List<(string dat, string db)> livePaths)
        {
            if (_store != null)
                return _store.AcquireSearchLease(out livePaths);

            livePaths = new List<(string, string)>();
            return null;
        }

        // ── Build ─────────────────────────────────────────────────────

        public int GetResumeLineId() => IndexingPipeline.ReadResumeLineId(_indexPath);

        public void GetResumeState(out int lineId, out long totalLines, out long resumeOffset)
            => IndexingPipeline.ReadProgressFile(_indexPath, out lineId, out totalLines, out resumeOffset);

        public void DeleteBuildProgressFile() => IndexingPipeline.DeleteProgressFile(_indexPath);

        public long CountLines()
        {
            using (var db = new ZayitDb(_dbPath))
                return db.CountLines();
        }

        public long CountLinesUpTo(int upToId)
        {
            using (var db = new ZayitDb(_dbPath))
                return db.CountLinesUpTo(upToId);
        }

        public bool BuildIndex(int limit = 0, Action<long> onProgress = null,
                               Action onFlush = null,
                               long totalLines = 0,
                               long resumeOffset = 0,
                               bool forceMergeOnComplete = false,
                               CancellationToken ct = default)
        {
            FtsLib.Indexing.FtsLog.Write("SeforimIndex.BuildIndex",
                $"acquiring IndexWriteLock for {_indexPath}");
            using (new IndexWriteLock(_indexPath))
            {
                FtsLib.Indexing.FtsLog.Write("SeforimIndex.BuildIndex", "IndexWriteLock acquired");
                bool result = IndexingPipeline.Build(_indexPath, _dbPath, _store, limit, totalLines, resumeOffset, onProgress, onFlush, ct);
                if (_store.IsWiped)
                {
                    FtsLib.Indexing.FtsLog.Write("SeforimIndex.BuildIndex",
                        "store was wiped during build — resetting store");
                    ResetStore();
                }

                if (result && forceMergeOnComplete)
                {
                    FtsLib.Indexing.FtsLog.Write("SeforimIndex.BuildIndex",
                        "forceMergeOnComplete=true — starting force merge");
                    Console.WriteLine("[SeforimIndex] Build complete — starting force merge...");
                    _store.MergeAll();
                    if (_store.IsWiped)
                    {
                        FtsLib.Indexing.FtsLog.Write("SeforimIndex.BuildIndex",
                            "store wiped during force merge — resetting store");
                        ResetStore();
                    }
                    FtsLib.Indexing.FtsLog.Write("SeforimIndex.BuildIndex",
                        "force merge complete");
                    Console.WriteLine("[SeforimIndex] Force merge complete.");
                }

                FtsLib.Indexing.FtsLog.Write("SeforimIndex.BuildIndex",
                    $"IndexWriteLock releasing — result={result}");
                return result;
            }
        }

        // ── Merge / Optimize ──────────────────────────────────────────

        /// <summary>
        /// Forces a full merge of all segments at every level into a single segment
        /// per level, reducing the total number of segment files for faster search.
        ///
        /// This is an expensive operation — it rewrites every segment file. Call it
        /// after a full build is complete to produce a single-segment index.
        ///
        /// Searches run concurrently: each level merge blocks them only for its
        /// millisecond commit step, never for the merge write itself.
        /// </summary>
        public void ForceMerge()
        {
            if (_store == null)
                throw new InvalidOperationException("No index is open.");

            FtsLib.Indexing.FtsLog.Write("SeforimIndex.ForceMerge",
                $"ForceMerge requested for {_indexPath}");

            using (new IndexWriteLock(_indexPath))
            {
                FtsLib.Indexing.FtsLog.Write("SeforimIndex.ForceMerge", "IndexWriteLock acquired");

                // MergeAll drains the flush pipeline internally before merging —
                // no flush can race. Searches proceed concurrently; each level
                // merge locks only its own commit step.
                _store.MergeAll();

                if (_store.IsWiped)
                {
                    FtsLib.Indexing.FtsLog.Write("SeforimIndex.ForceMerge",
                        "store was wiped during merge — resetting store");
                    ResetStore();
                }

                FtsLib.Indexing.FtsLog.Write("SeforimIndex.ForceMerge",
                    "ForceMerge complete — IndexWriteLock releasing");
            }
        }

        // ── Search ────────────────────────────────────────────────────

        /// <param name="filterIds">Optional line-ID keep-set: when non-null, only
        /// results whose line ID is in this set are returned. A small set also
        /// speeds up heavy queries — it drives the posting intersection instead
        /// of merely trimming its output. Null = search everything; an empty
        /// collection matches nothing.</param>
        public IEnumerable<SearchResult> Search(string query, int cap = 0, bool expandKetiv = false,
            IEnumerable<int> filterIds = null, CancellationToken ct = default)
        {
            var lease = AcquireSearchLease(out var livePaths);
            return SearchPipeline.Search(query, _indexPath, _dbPath, livePaths, lease, cap, expandKetiv, filterIds, ct);
        }

        /// <param name="filterIds">Optional line-ID keep-set — see <see cref="Search"/>.</param>
        public IEnumerable<int> SearchIds(string query, bool expandKetiv = false,
            IEnumerable<int> filterIds = null, CancellationToken ct = default)
        {
            var lease = AcquireSearchLease(out var livePaths);
            return SearchPipeline.SearchIds(query, _indexPath, livePaths, lease, expandKetiv, filterIds, ct);
        }

        // ── Doc→source resolution ─────────────────────────────────────

        /// <summary>
        /// Resolves an index docId (as returned by <see cref="SearchIds"/> /
        /// <see cref="Search"/> results) to its source corpus and source-local id,
        /// using the doc_source mapping persisted in the live segments.
        ///
        /// Source 0 is the library (seforim.db): sourceLineId is the line.id.
        /// docIds not covered by any persisted row — including every doc of an
        /// index built before the mapping existed — resolve as library-identity.
        /// Returns false only when the index has no live segments at all.
        /// </summary>
        public bool TryResolveDocId(int docId, out int source, out int sourceLineId)
        {
            source       = Indexing.DocSourceMap.LibrarySource;
            sourceLineId = docId;

            var lease = AcquireSearchLease(out var livePaths);
            if (lease == null && livePaths.Count == 0) return false;
            using (lease)
            {
                if (livePaths.Count == 0) return false;

                var rows = new List<Indexing.DocSourceRange>();
                foreach (var (dat, db) in livePaths)
                    rows.AddRange(Indexing.SegmentStore.ReadDocSourceRows(db));

                var map = rows.Count == 0 ? Indexing.DocSourceMap.Identity
                                          : Indexing.DocSourceMap.FromRows(rows);
                map.Resolve(docId, out source, out sourceLineId);
                return true;
            }
        }

        // ── Snippets ──────────────────────────────────────────────────

        public SnippetResult GenerateSnippet(int lineId, string query)
        {
            var terms = SearchPipeline.ExtractTerms(query);
            return SnippetPipeline.GenerateFromDb(lineId, terms, _dbPath);
        }

        /// <summary>
        /// Batch-fetches the surrounding-line context (up to <paramref name="radius"/>
        /// lines each side, same book) for the given matched line ids — one query per
        /// chunk. Returns id → (prevText, nextText); ids at a book edge or not found are
        /// simply absent. Used to embellish snippets that came out shorter than the
        /// requested context. Opening the read-only DB is a cheap pooled checkout.
        /// </summary>
        public System.Collections.Generic.Dictionary<int, (string Prev, string Next)>
            FetchNeighborContext(System.Collections.Generic.IReadOnlyList<int> lineIds, int radius)
        {
            if (lineIds == null || lineIds.Count == 0 || radius <= 0)
                return new System.Collections.Generic.Dictionary<int, (string, string)>();
            using (var db = new ZayitDb(_dbPath))
                return db.FetchNeighborContext(lineIds, radius);
        }

        public SnippetResult GenerateSnippet(SearchResult result, bool requireOrdered = false,
            int contextWords = DefaultContextWords)
        {
            if (result == null) return SnippetResult.NoMatch;
            if (result.MatchedGroups.Count == 0) return SnippetResult.NoMatch;

            // Results from Search carry the query's prepared
            // term→group map (built once, shared across lines and threads).
            // Externally constructed results fall back to preparing here.
            var prepared = result.Prepared
                ?? FtsLib.Snippets.PreparedQueryGroups.FromGroups(result.MatchedGroups);

            return SnippetPipeline.Generate(
                result.Content,
                prepared,
                requireOrdered,
                result.OriginalGroupCount,
                contextWords);
        }

        /// <summary>
        /// Like <see cref="GenerateSnippet(SearchResult,bool,int)"/> but renders the
        /// snippet over the matched line plus the supplied surrounding lines. Use this
        /// only for results whose plain snippet was shorter than the requested context
        /// (<see cref="SnippetResult.WindowWordCount"/> below <c>contextWords</c>) — the
        /// caller batch-fetches the neighbors, so no per-line DB round-trip happens here.
        /// </summary>
        public SnippetResult GenerateSnippetWithNeighbors(SearchResult result,
            string prevContent, string nextContent,
            bool requireOrdered = false, int contextWords = DefaultContextWords)
        {
            if (result == null) return SnippetResult.NoMatch;
            if (result.MatchedGroups.Count == 0) return SnippetResult.NoMatch;

            var prepared = result.Prepared
                ?? FtsLib.Snippets.PreparedQueryGroups.FromGroups(result.MatchedGroups);

            return SnippetPipeline.GenerateWithNeighbors(
                prevContent,
                result.Content,
                nextContent,
                prepared,
                requireOrdered,
                result.OriginalGroupCount,
                contextWords);
        }
    }
}
