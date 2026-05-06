using FtsLib.Indexing;
using FtsLib.Search;
using FtsLib.Tokenization;
using System;
using System.IO;
using System.Threading;

namespace FtsLib.SeforimDb
{
    /// <summary>
    /// Builds the full-text index from the seforim SQLite database.
    /// Streams every line through the tokenizer and writes term/docId pairs
    /// to the index in strictly ascending ID order (required by the codec).
    ///
    /// Supports resuming an interrupted build: a <c>build.progress</c> file in the
    /// index directory records the last line ID that was fully flushed to a segment.
    /// On the next run, indexing resumes from that ID rather than starting over.
    /// </summary>
    internal static class IndexingPipeline
    {
        // Written after every flush — stores the last line ID confirmed in a segment.
        // Plain text: one integer on a single line.
        private const string ProgressFileName = "build.progress";

        // ── Progress file helpers ─────────────────────────────────────

        /// <summary>
        /// Returns the last flushed line ID from a previous interrupted build,
        /// or 0 if no progress file exists or it cannot be read.
        /// </summary>
        internal static int ReadResumeLineId(string indexPath)
        {
            string path = Path.Combine(indexPath, ProgressFileName);
            try
            {
                if (!File.Exists(path)) return 0;
                string text = File.ReadAllText(path).Trim();
                return int.TryParse(text, out int id) ? id : 0;
            }
            catch { return 0; }
        }

        private static void WriteProgressFile(string indexPath, int lineId)
        {
            try
            {
                File.WriteAllText(Path.Combine(indexPath, ProgressFileName), lineId.ToString());
            }
            catch { /* best-effort — a missed write means slightly more re-work on resume */ }
        }

        /// <summary>
        /// Deletes the progress file. Called by SearchHandler after the build
        /// completes and the version stamp is written, so a subsequent startup
        /// does not mistake a finished index for an interrupted one.
        /// </summary>
        internal static void DeleteProgressFile(string indexPath)
        {
            try
            {
                string path = Path.Combine(indexPath, ProgressFileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        // ── Build ─────────────────────────────────────────────────────

        /// <summary>
        /// Builds (or resumes) the index at <paramref name="indexPath"/> from the
        /// database at <paramref name="dbPath"/>.
        ///
        /// If a <c>build.progress</c> file exists in the index directory, indexing
        /// resumes from the last flushed line ID — lines already in the index are
        /// skipped. Otherwise a fresh build is started from the beginning.
        ///
        /// The index is searchable as soon as this method returns. Call
        /// <see cref="Optimize"/> afterwards (e.g. on a background thread) to merge
        /// all segments into one for the fastest possible search performance.
        /// </summary>
        /// <param name="indexPath">Directory where segment files are written.</param>
        /// <param name="dbPath">Path to the seforim SQLite database.</param>
        /// <param name="limit">
        /// Maximum number of lines to index. 0 (default) = all lines.
        /// Useful for partial builds during testing.
        /// </param>
        /// <param name="onProgress">
        /// Optional callback invoked after each line is processed.
        /// Receives the running count of lines processed in this session.
        /// </param>
        internal static bool Build(
            string             indexPath,
            string             dbPath,
            SegmentStore       store      = null,
            int                limit      = 0,
            Action<long>       onProgress = null,
            Action             onFlush    = null,
            CancellationToken  ct         = default)
        {
            int resumeLineId = ReadResumeLineId(indexPath);
            if (resumeLineId != 0)
            {
                // Log what's on disk so we can verify the segments actually cover
                // the lines the progress file claims they do.
                int segCount = 0;
                try
                {
                    if (System.IO.Directory.Exists(indexPath))
                        segCount = System.IO.Directory.GetFiles(indexPath, "seg_*.dat").Length;
                }
                catch { }
                Console.WriteLine($"[IndexingPipeline] Resuming from line id {resumeLineId} — {segCount} segment file(s) on disk");
            }
            else
                Console.WriteLine("[IndexingPipeline] Starting fresh build");

            var  tokenizer          = new Tokenizer();
            long n                  = 0;
            bool anyLinesProcessed  = false;

            // Force a segment flush at least every this many lines, independent of
            // how many terms have accumulated. Keeps the progress file current and
            // limits re-indexing work after a crash. Writing the segment file blocks
            // briefly on the calling thread; an LSM merge is only triggered in the
            // background if level 0 has reached the fanout threshold (4 segments).
            const long ForceFlushLineInterval = 250_000;

            IndexWriter writer;
            try
            {
                writer = store != null
                    ? new IndexWriter(indexPath, store) { AutoOptimize = false }
                    : new IndexWriter(indexPath)        { AutoOptimize = false };
            }
            catch (CorruptIndexException ex)
            {
                // A segment file is corrupt and recovery failed. Wipe the entire
                // index directory and start a fresh build from scratch.
                Console.WriteLine("[IndexingPipeline] " + ex.Message);
                Console.WriteLine("[IndexingPipeline] Wiping index directory for clean rebuild...");
                try
                {
                    if (Directory.Exists(indexPath))
                    {
                        foreach (var file in Directory.GetFiles(indexPath))
                            File.Delete(file);
                    }
                }
                catch (Exception wipeEx)
                {
                    Console.WriteLine("[IndexingPipeline] Failed to wipe index directory: " + wipeEx.Message);
                    throw;
                }
                Console.WriteLine("[IndexingPipeline] Starting fresh build from scratch...");
                resumeLineId = 0;
                // Store is now inconsistent (index wiped) — don't reuse it.
                writer = new IndexWriter(indexPath) { AutoOptimize = false };
            }

            // Initialize progress tracking after the catch block so they reflect
            // the correct resumeLineId (0 if we wiped the index, or the original
            // value if recovery succeeded).
            int lastWrittenLineId  = resumeLineId;
            int lastProgressLineId = resumeLineId;

            if (resumeLineId != 0)
                Console.WriteLine($"[IndexingPipeline] Writer ready — LastFlushedLineId={writer.LastFlushedLineId}, resumeLineId={resumeLineId}");

            using (var db = new ZayitDb(dbPath))
            using (writer)
            {
                var lineSource = resumeLineId != 0
                    ? db.ReadLinesFrom(resumeLineId, limit, ct)
                    : db.ReadLines(limit, ct);

                foreach (var (id, content) in lineSource)
                {
                    ct.ThrowIfCancellationRequested();
                    anyLinesProcessed = true;

                    foreach (var term in tokenizer.Extract(content))
                        writer.Add(id, term);

                    lastWrittenLineId = id;
                    n++;
                    onProgress?.Invoke(n);

                    // Force a flush on the interval boundary so a segment is written
                    // even if the term-count threshold has not been reached yet.
                    if (n % ForceFlushLineInterval == 0)
                        writer.ForceFlush();

                    // Update the progress file only when a flush has actually completed.
                    // LastFlushedLineId is written by the background flush task after
                    // the segment is fully on disk — it lags behind lastWrittenLineId.
                    int flushed = writer.LastFlushedLineId;
                    if (flushed > lastProgressLineId)
                    {
                        WriteProgressFile(indexPath, flushed);
                        Console.WriteLine($"[IndexingPipeline] Progress file updated: lineId={flushed} (written={lastWrittenLineId}, n={n})");
                        lastProgressLineId = flushed;
                        onFlush?.Invoke();
                    }
                }

                // Dispose() (called by the using block) flushes the remaining RAM index
                // and waits for all background flush tasks to complete. By the time we
                // exit the using block, every line up to lastWrittenLineId is on disk.
            }

            // All flushes are complete. Write the final progress file with the true
            // last line ID on disk. This is safe because Dispose() has already drained
            // the entire flush pipeline via WaitForMerge().
            if (anyLinesProcessed)
            {
                WriteProgressFile(indexPath, lastWrittenLineId);
                Console.WriteLine($"[IndexingPipeline] Build complete — final progress lineId={lastWrittenLineId}");
            }
            else
            {
                Console.WriteLine($"[IndexingPipeline] No lines processed (WAL recovery only or empty DB) — progress file unchanged at {resumeLineId}");
            }

            // Return true only when lines were actually processed — this distinguishes
            // a real completed build from a no-op (WAL recovery only, or empty DB).
            return anyLinesProcessed;
        }

        /// <summary>
        /// Force-merges all segments into one for fastest subsequent search.
        /// Call this after <see cref="Build"/> returns — the index is already
        /// searchable before this completes, so it can run in the background.
        /// </summary>
        internal static void Optimize(string indexPath, SegmentStore store = null)
        {
            if (store != null)
            {
                using (var writer = new IndexWriter(indexPath, store) { AutoOptimize = false })
                    writer.Optimize();
            }
            else
            {
                using (var writer = new IndexWriter(indexPath) { AutoOptimize = false })
                    writer.Optimize();
            }
        }
    }
}
