#if NET6_0_OR_GREATER
using FtsLib;
using FtsLib.Search;
using FtsLib.Tokenization;
using Microsoft.Data.Sqlite;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace FtsLib.Fts5
{
    /// <summary>
    /// PROTOTYPE: the FtsLib index storage replaced by SQLite FTS5. Everything else — tokenizer,
    /// query parser, expanders, snippets — is FtsLib's own code.
    ///
    /// Files in the index directory:
    ///   lines.fts5.db — contentless, detail=none FTS5 table: term → rowid (line id) lists and
    ///                   nothing else. Each row is the line's distinct normalized terms joined by
    ///                   spaces (FtsLib's Tokenizer.Extract), split again by FTS5's 'ascii'
    ///                   tokenizer, which only breaks on the spaces. Built IN PLACE in WAL mode
    ///                   and committed in batches, so searches see the index grow during a build.
    ///                   Also holds:
    ///                     build_state — the resume watermark (last line id committed, lines
    ///                                   done, corpus size, complete flag), written in the
    ///                                   same transaction as the lines it describes;
    ///   dict_*.db/.dat/.fmi + dictionaries.txt — per-batch term dictionaries the build writes
    ///                   after every commit and merges as it goes (<see cref="Fts5TermDictionaries"/>):
    ///                   what wildcard and fuzzy expand against while the build runs.
    ///   terms.db      — the finished term dictionary, in FtsLib's term_index shape so the
    ///                   PRODUCTION expanders run on it unchanged: term + count (document
    ///                   frequency); chunk-location columns 0; rows in ordinal term order.
    ///   terms.dat     — empty; SegmentHandle needs the file to exist.
    ///   terms.tgm     — the production trigram sidecar, built from terms.db.
    ///   terms.fmi     — FM-index over terms.db: exact infix / suffix / fuzzy candidates.
    /// terms.db, .tgm and .fmi are written once the lines are complete, into a "building"
    /// folder, and published with terms.db LAST — its presence means the dictionary is ready.
    /// </summary>
    internal static class Fts5Index
    {
        public const string LinesDbName = "lines.fts5.db", TermsDbName = "terms.db",
                            TermsDatName = "terms.dat";

        public static string LinesDb(string dir) => Path.Combine(dir, LinesDbName);
        public static string TermsDb(string dir) => Path.Combine(dir, TermsDbName);
        public static string TermsDat(string dir) => Path.Combine(dir, TermsDatName);
        public static string TermsFmi(string dir) => Path.Combine(dir, "terms.fmi");

        const string DictionaryBuildFolderName = "building";

        /// <summary>
        /// Lines per committed batch. Small at first, so a fresh build becomes searchable
        /// within seconds; larger later, because every commit costs FTS5 a segment merge.
        /// </summary>
        static int CommitInterval(long linesDone) => linesDone < 100_000 ? 20_000 : 100_000;

        // ── Resume state ──────────────────────────────────────────────

        internal readonly struct BuildState
        {
            public readonly int LastLineId;
            public readonly long LinesDone, TotalLines;
            public readonly bool Complete;
            public BuildState(int lastLineId, long linesDone, long totalLines, bool complete)
            { LastLineId = lastLineId; LinesDone = linesDone; TotalLines = totalLines; Complete = complete; }
        }

        /// <summary>The build watermark, or null when no line table exists yet.</summary>
        public static BuildState? ReadBuildState(string dir)
        {
            if (!File.Exists(LinesDb(dir))) return null;
            try
            {
                using var conn = Open(LinesDb(dir), SqliteOpenMode.ReadWrite);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT last_line_id, lines_done, total_lines, complete FROM build_state WHERE id = 1";
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                return new BuildState((int)r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3) != 0);
            }
            catch (SqliteException) { return null; }
        }

        // ── Build ─────────────────────────────────────────────────────

        /// <summary>Fresh build from scratch (benchmarks).</summary>
        public static void Build(Func<IFtsCorpus> openCorpus, string dir, int limit = 0,
                                 Action<long> onProgress = null, CancellationToken ct = default)
        {
            Directory.CreateDirectory(dir);
            DeleteIndexFiles(dir);
            BuildLines(openCorpus, dir, limit, 0, onProgress, null, ct);
            BuildDictionary(dir);
            MarkComplete(dir);
        }

        public static void DeleteIndexFiles(string dir)
        {
            foreach (string f in new[] { LinesDb(dir), LinesDb(dir) + "-wal", LinesDb(dir) + "-shm",
                                         TermsDb(dir), TermsDat(dir), TrigramIndex.SidecarPath(TermsDat(dir)), TermsFmi(dir) })
                if (File.Exists(f)) File.Delete(f);
            string building = Path.Combine(dir, DictionaryBuildFolderName);
            if (Directory.Exists(building)) Directory.Delete(building, recursive: true);
            if (Directory.Exists(dir)) Fts5TermDictionaries.Clear(dir);
        }

        /// <summary>
        /// Inserts the corpus into the line table in place, resuming after the last committed
        /// line when an earlier build was interrupted. Each batch is committed together with the
        /// watermark, so the table and the watermark never disagree.
        /// </summary>
        /// <param name="onProgress">Lines indexed in THIS session, after every line.</param>
        /// <param name="onCommitted">After every committed batch: from the first one on, the
        /// new lines are searchable.</param>
        public static void BuildLines(Func<IFtsCorpus> openCorpus, string dir, int limit, long totalLines,
                                      Action<long> onProgress, Action onCommitted, CancellationToken ct,
                                      bool backgroundPriority = false)
        {
            Directory.CreateDirectory(dir);
            var total = Stopwatch.StartNew();
            long session = 0;
            using (var conn = Open(LinesDb(dir), SqliteOpenMode.ReadWriteCreate))
            {
                Execute(conn, "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA cache_size=-262144;");
                Execute(conn, "CREATE VIRTUAL TABLE IF NOT EXISTS line_fts USING fts5(terms, content='', detail=none, columnsize=0, tokenize='ascii')");
                Execute(conn, "CREATE TABLE IF NOT EXISTS build_state(id INTEGER PRIMARY KEY CHECK (id = 1), " +
                              "last_line_id INTEGER NOT NULL, lines_done INTEGER NOT NULL, total_lines INTEGER NOT NULL, complete INTEGER NOT NULL)");
                Execute(conn, "INSERT OR IGNORE INTO build_state VALUES (1, 0, 0, 0, 0)");
                var state = ReadState(conn);
                if (state.Complete) return;
                int lastLineId = state.LastLineId;
                long linesDone = state.LinesDone;

                var tokenizer = new Tokenizer();
                CatchUpDictionaries(openCorpus, dir, lastLineId, tokenizer, ct);
                if (totalLines > 0) Execute(conn, $"UPDATE build_state SET total_lines = {totalLines}");

                // A limit is the size of the whole index, so a resumed build only adds the rest.
                int remaining = 0;
                if (limit > 0)
                {
                    remaining = (int)Math.Max(0, limit - linesDone);
                    if (remaining == 0) limit = -1;   // nothing left to add
                }

                var text = new StringBuilder(4096);
                var batchCounts = new System.Collections.Generic.Dictionary<string, int>(1 << 16, StringComparer.Ordinal);
                using var insert = conn.CreateCommand();
                insert.CommandText = "INSERT INTO line_fts(rowid, terms) VALUES ($id, $terms)";
                var pId = insert.Parameters.Add("$id", SqliteType.Integer);
                var pTerms = insert.Parameters.Add("$terms", SqliteType.Text);
                using var watermark = conn.CreateCommand();
                watermark.CommandText = "UPDATE build_state SET last_line_id = $id, lines_done = $done";
                var pLastId = watermark.Parameters.Add("$id", SqliteType.Integer);
                var pDone = watermark.Parameters.Add("$done", SqliteType.Integer);

                // Dictionaries are written and merged on a worker so inserting never waits for
                // them; at most two finished batches queue up before the inserter waits.
                // Batches are handled in commit order, so the manifest's covered id only moves
                // forward (a crash in between is repaired by CatchUpDictionaries).
                using var dictionaryQueue = new System.Collections.Concurrent.BlockingCollection<
                    (System.Collections.Generic.Dictionary<string, int> Counts, int CoveredLineId)>(boundedCapacity: 2);
                // If the worker fails, this is cancelled so an inserter blocked on a full queue
                // wakes up instead of waiting forever.
                using var workerFailed = new CancellationTokenSource();
                var dictionaryWorker = System.Threading.Tasks.Task.Factory.StartNew(() =>
                {
                    try
                    {
                        // Priority is per thread: the worker takes the build's policy itself.
                        using (FtsLib.Indexing.BackgroundPriorityScope.EnterIf(backgroundPriority))
                            foreach (var (counts, covered) in dictionaryQueue.GetConsumingEnumerable())
                                Fts5TermDictionaries.AddBatch(dir, counts, covered);
                    }
                    catch
                    {
                        workerFailed.Cancel();
                        throw;
                    }
                }, System.Threading.Tasks.TaskCreationOptions.LongRunning);

                var tx = conn.BeginTransaction();
                long inBatch = 0;
                bool insertFailed = false;
                void Commit()
                {
                    pLastId.Value = lastLineId; pDone.Value = linesDone;
                    watermark.Transaction = tx;
                    watermark.ExecuteNonQuery();
                    tx.Commit(); tx.Dispose();
                    inBatch = 0;
                    // The batch's term dictionary, so wildcard and fuzzy find its terms.
                    try { dictionaryQueue.Add((batchCounts, lastLineId), workerFailed.Token); }
                    catch (OperationCanceledException) when (workerFailed.IsCancellationRequested)
                    {
                        dictionaryWorker.GetAwaiter().GetResult();   // rethrows the worker's exception
                        throw;
                    }
                    batchCounts = new System.Collections.Generic.Dictionary<string, int>(1 << 16, StringComparer.Ordinal);
                    onCommitted?.Invoke();
                }
                try
                {
                    if (limit >= 0)
                    using (var corpus = openCorpus())
                    {
                        var source = lastLineId > 0 ? corpus.ReadDocumentsAfter(lastLineId, remaining, ct)
                                                    : corpus.ReadDocuments(remaining, ct);
                        foreach (var (id, content) in source)
                        {
                            text.Clear();
                            foreach (string term in tokenizer.Extract(content))
                            {
                                if (text.Length > 0) text.Append(' ');
                                text.Append(term);
                                batchCounts.TryGetValue(term, out int n);
                                batchCounts[term] = n + 1;
                            }
                            pId.Value = id; pTerms.Value = text.ToString();
                            insert.Transaction = tx;
                            insert.ExecuteNonQuery();
                            lastLineId = id; linesDone++; inBatch++; session++;
                            onProgress?.Invoke(session);
                            if (inBatch >= CommitInterval(linesDone))
                            {
                                Commit();
                                ct.ThrowIfCancellationRequested();
                                tx = conn.BeginTransaction();
                            }
                        }
                    }
                    if (inBatch > 0) Commit(); else { tx.Commit(); tx.Dispose(); }
                }
                catch
                {
                    tx.Dispose();   // rolls back the open batch; committed batches stay
                    insertFailed = true;
                    throw;
                }
                finally
                {
                    // Every committed batch gets its dictionary before the build moves on (or
                    // gives up): the final dictionary step clears them, and a later resume
                    // relies on the manifest.
                    dictionaryQueue.CompleteAdding();
                    // A worker failure surfaces here, unless the insert already failed: then
                    // that original exception is the one to report.
                    try { dictionaryWorker.GetAwaiter().GetResult(); }
                    catch (Exception) when (insertFailed) { }
                }
                Log($"inserted {session:N0} lines ({linesDone:N0} total) in {total.Elapsed.TotalSeconds:F0}s");

                // Merge FTS5's batch segments into one. Readers keep working meanwhile (WAL).
                var sw = Stopwatch.StartNew();
                ct.ThrowIfCancellationRequested();
                Execute(conn, "INSERT INTO line_fts(line_fts) VALUES('optimize')");
                ct.ThrowIfCancellationRequested();
                Execute(conn, "VACUUM");
                Execute(conn, "PRAGMA wal_checkpoint(TRUNCATE)");
                Log($"optimize + vacuum {sw.Elapsed.TotalSeconds:F0}s");
            }
            SqliteConnection.ClearAllPools();
        }

        /// <summary>
        /// Builds terms.db, the trigram and the FM sidecars from the finished line table into a
        /// "building" folder, then publishes them with terms.db last.
        /// </summary>
        public static void BuildDictionary(string dir)
        {
            var sw = Stopwatch.StartNew();
            string building = Path.Combine(dir, DictionaryBuildFolderName);
            if (Directory.Exists(building)) Directory.Delete(building, recursive: true);
            Directory.CreateDirectory(building);

            using (var terms = Open(TermsDb(building), SqliteOpenMode.ReadWriteCreate))
            {
                Execute(terms, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA page_size=65536;");
                Execute(terms, "CREATE TABLE term_index(term TEXT NOT NULL, skip_offset INTEGER NOT NULL DEFAULT 0, " +
                               "skip_count INTEGER NOT NULL DEFAULT 0, offset INTEGER NOT NULL DEFAULT 0, " +
                               "length INTEGER NOT NULL DEFAULT 0, count INTEGER NOT NULL)");
                Execute(terms, $"ATTACH DATABASE '{LinesDb(dir).Replace("'", "''")}' AS lines");
                Execute(terms, "CREATE VIRTUAL TABLE temp.dictionary_vocab USING fts5vocab(lines, line_fts, row)");
                // BINARY (UTF-8 memcmp) order = ordinal for BMP text, so rowid order is ordinal
                // term order, like a force-merged FtsLib segment.
                Execute(terms, "INSERT INTO term_index(term, count) SELECT term, doc FROM temp.dictionary_vocab ORDER BY term");
                Execute(terms, "CREATE UNIQUE INDEX idx_term ON term_index(term)");
                Execute(terms, "ANALYZE");
            }
            SqliteConnection.ClearAllPools();
            File.WriteAllBytes(TermsDat(building), Array.Empty<byte>());
            TrigramIndex.BuildFromDb(TermsDb(building), TrigramIndex.SidecarPath(TermsDat(building)));
            BuildFmSidecar(building);

            // terms.dat is empty and identical either way, and searches running on the build-time
            // view hold the existing one open, so it is never replaced.
            foreach (string name in new[] { Path.GetFileName(TrigramIndex.SidecarPath(TermsDat(building))),
                                            Path.GetFileName(TermsFmi(building)), TermsDbName })
                MoveWithRetry(Path.Combine(building, name), Path.Combine(dir, name));
            if (!File.Exists(TermsDat(dir))) MoveWithRetry(TermsDat(building), TermsDat(dir));
            Directory.Delete(building, recursive: true);
            // Searches use terms.db from here on; the build-time dictionaries are done.
            Fts5TermDictionaries.Clear(dir);
            Log($"term dictionary + trigram + FM sidecars {sw.Elapsed.TotalSeconds:F0}s");
        }

        /// <summary>A search opening its files at this instant can block a replace briefly.</summary>
        static void MoveWithRetry(string from, string to)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { File.Move(from, to, overwrite: true); return; }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && attempt < 20)
                {
                    Thread.Sleep(250);
                }
            }
        }

        /// <summary>
        /// A crash between a batch commit and its dictionary leaves lines no dictionary covers.
        /// Re-tokenizes exactly those lines (after the manifest's covered id, up to the last
        /// committed line) into one more dictionary, so the term set matches the line table.
        /// </summary>
        static void CatchUpDictionaries(Func<IFtsCorpus> openCorpus, string dir, int lastCommittedLineId,
                                        Tokenizer tokenizer, CancellationToken ct)
        {
            int covered = Fts5TermDictionaries.Read(dir).CoveredLineId;
            if (covered >= lastCommittedLineId) return;
            var counts = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            using (var corpus = openCorpus())
                foreach (var (id, content) in corpus.ReadDocumentsAfter(covered, 0, ct))
                {
                    if (id > lastCommittedLineId) break;
                    foreach (string term in tokenizer.Extract(content))
                    {
                        counts.TryGetValue(term, out int n);
                        counts[term] = n + 1;
                    }
                }
            Fts5TermDictionaries.AddBatch(dir, counts, lastCommittedLineId);
            Log($"dictionary catch-up: lines after {covered} up to {lastCommittedLineId}");
        }

        public static void MarkComplete(string dir)
        {
            using var conn = Open(LinesDb(dir), SqliteOpenMode.ReadWrite);
            Execute(conn, "UPDATE build_state SET complete = 1");
        }

        /// <summary>FM-index over the term dictionary, rowid = terms.db rowid.</summary>
        public static void BuildFmSidecar(string dir)
        {
            var terms = new System.Collections.Generic.List<string>();
            var rowIds = new System.Collections.Generic.List<int>();
            using (var conn = Open(TermsDb(dir), SqliteOpenMode.ReadOnly))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT rowid, term FROM term_index ORDER BY rowid";
                using var r = cmd.ExecuteReader();
                while (r.Read()) { rowIds.Add((int)r.GetInt64(0)); terms.Add(r.GetString(1)); }
            }
            FmIndex.Build(TermsFmi(dir), terms, rowIds);
        }

        static BuildState ReadState(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT last_line_id, lines_done, total_lines, complete FROM build_state WHERE id = 1";
            using var r = cmd.ExecuteReader();
            r.Read();
            return new BuildState((int)r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3) != 0);
        }

        static void Log(string message) => FtsLib.Indexing.FtsLog.Write("Fts5Index", message);

        internal static SqliteConnection Open(string path, SqliteOpenMode mode)
        {
            var c = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = mode, Pooling = false }.ConnectionString);
            c.Open();
            return c;
        }

        static void Execute(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            cmd.ExecuteNonQuery();
        }
    }
}
#endif
