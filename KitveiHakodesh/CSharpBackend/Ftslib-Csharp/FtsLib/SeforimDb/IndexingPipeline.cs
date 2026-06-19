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
        // Written after every flush — stores the last line ID confirmed in a segment,
        // the total line count of the database, and the count of lines up to the
        // resume point. All three are needed to compute the resume percentage without
        // any DB queries on restart.
        // Format: three integers separated by newlines.
        //   Line 1: last flushed line ID
        //   Line 2: total line count
        //   Line 3: count of lines up to (and including) the last flushed line ID
        // Lines 2 and 3 are optional — absent in files written by older builds.
        private const string ProgressFileName = "build.progress";

        // ── Progress file helpers ─────────────────────────────────────

        /// <summary>
        /// Returns the last flushed line ID from a previous interrupted build,
        /// or 0 if no progress file exists or it cannot be read.
        /// </summary>
        internal static int ReadResumeLineId(string indexPath)
        {
            ReadProgressFile(indexPath, out int lineId, out _, out _);
            return lineId;
        }

        /// <summary>
        /// Returns the last flushed line ID, the cached total line count, and the
        /// cached count of lines up to the resume point.
        /// Any value is 0 if the file is absent or was written by an older build.
        /// </summary>
        internal static void ReadProgressFile(string indexPath, out int lineId,
                                              out long totalLines, out long resumeOffset)
        {
            lineId       = 0;
            totalLines   = 0;
            resumeOffset = 0;
            string path = Path.Combine(indexPath, ProgressFileName);
            try
            {
                if (!File.Exists(path)) return;
                string[] lines = File.ReadAllText(path).Trim().Split('\n');
                if (lines.Length >= 1) int.TryParse(lines[0].Trim(), out lineId);
                if (lines.Length >= 2) long.TryParse(lines[1].Trim(), out totalLines);
                if (lines.Length >= 3) long.TryParse(lines[2].Trim(), out resumeOffset);
            }
            catch { }
        }

        private static void WriteProgressFile(string indexPath, int lineId,
                                              long totalLines, long resumeOffset)
        {
            try
            {
                string path = Path.Combine(indexPath, ProgressFileName);
                File.WriteAllText(path,
                    lineId.ToString() + "\n" + totalLines.ToString() + "\n" + resumeOffset.ToString());
                FtsLib.Indexing.FtsLog.Write("IndexingPipeline.WriteProgressFile",
                    $"wrote lineId={lineId} totalLines={totalLines} resumeOffset={resumeOffset} path={path}");
            }
            catch (Exception ex)
            {
                FtsLib.Indexing.FtsLog.Write("IndexingPipeline.WriteProgressFile",
                    "FAILED: " + ex.Message);
            }
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
                if (File.Exists(path))
                {
                    File.Delete(path);
                    FtsLib.Indexing.FtsLog.Write("IndexingPipeline.DeleteProgressFile", "deleted " + path);
                }
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
            long               totalLines = 0,
            long               resumeOffset = 0,
            Action<long>       onProgress = null,
            Action             onFlush    = null,
            CancellationToken  ct         = default)
        {
            FtsLib.Indexing.FtsLog.Separator("IndexingPipeline.Build START");
            FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build",
                $"indexPath={indexPath} dbPath={dbPath} limit={limit} totalLines={totalLines} resumeOffset={resumeOffset}");

            // Log current index directory state at entry
            try
            {
                if (System.IO.Directory.Exists(indexPath))
                {
                    var allFiles = System.IO.Directory.GetFiles(indexPath);
                    FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build",
                        $"index dir has {allFiles.Length} file(s): " +
                        string.Join(", ", System.Array.ConvertAll(allFiles, System.IO.Path.GetFileName)));
                }
                else
                {
                    FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build", "index directory does not exist yet");
                }
            }
            catch { }

            ReadProgressFile(indexPath, out int resumeLineId, out long cachedTotalLines, out _);
            FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build",
                $"progress file read: resumeLineId={resumeLineId} cachedTotalLines={cachedTotalLines}");

            if (resumeLineId != 0)
            {
                // Verify that at least one segment file exists on disk before trusting the
                // resume point. If segments are missing (e.g. wiped by a concurrent
                // ExecuteOnDbReady while the previous build task was still running and
                // writing to build.progress after the wipe), resuming from the stale line
                // ID would silently skip lines 1..resumeLineId and leave them unindexed.
                // Detect this and start fresh instead.
                int segCount = 0;
                try
                {
                    if (System.IO.Directory.Exists(indexPath))
                        segCount = System.IO.Directory.GetFiles(indexPath, "seg_*.dat").Length;
                }
                catch { }

                if (segCount == 0)
                {
                    FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build",
                        $"STALE PROGRESS FILE: resumeLineId={resumeLineId} but segCount=0 — resetting to fresh build");
                    Console.WriteLine($"[IndexingPipeline] build.progress says resume from {resumeLineId} but no segments on disk — starting fresh (stale progress file)");
                    try { File.Delete(Path.Combine(indexPath, ProgressFileName)); } catch { }
                    resumeLineId = 0;
                }
                else
                {
                    FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build",
                        $"RESUMING from lineId={resumeLineId}, segCount={segCount}");
                    Console.WriteLine($"[IndexingPipeline] Resuming from line id {resumeLineId} — {segCount} segment file(s) on disk");
                }
            }
            else
            {
                FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build", "no progress file — starting fresh build");
                Console.WriteLine("[IndexingPipeline] Starting fresh build");
            }

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
                    ? new IndexWriter(indexPath, store)
                    : new IndexWriter(indexPath);
            }
            catch (CorruptIndexException ex)
            {
                // A segment file is corrupt and recovery failed. Wipe the entire
                // index directory and start a fresh build from scratch.
                FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build",
                    "CorruptIndexException during writer init — wiping for clean rebuild: " + ex.Message);
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
                    FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build",
                        "FAILED to wipe index directory: " + wipeEx.Message);
                    Console.WriteLine("[IndexingPipeline] Failed to wipe index directory: " + wipeEx.Message);
                    throw;
                }
                Console.WriteLine("[IndexingPipeline] Starting fresh build from scratch...");
                FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build",
                    "index directory wiped — starting fresh build");
                resumeLineId = 0;
                // Store is now inconsistent (index wiped) — don't reuse it.
                writer = new IndexWriter(indexPath);
            }

            // Initialize progress tracking after the catch block so they reflect
            // the correct resumeLineId (0 if we wiped the index, or the original
            // value if recovery succeeded).
            int lastWrittenLineId  = resumeLineId;
            int lastProgressLineId = resumeLineId;

            // Use the caller-supplied totals if available; fall back to values
            // cached in the progress file from the previous session.
            long effectiveTotalLines   = totalLines   > 0 ? totalLines   : cachedTotalLines;
            long effectiveResumeOffset = resumeOffset > 0 ? resumeOffset : 0;

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
                        WriteProgressFile(indexPath, flushed, effectiveTotalLines, effectiveResumeOffset + n);
                        Console.WriteLine($"[IndexingPipeline] Progress file updated: lineId={flushed} (written={lastWrittenLineId}, n={n})");
                        FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build",
                            $"flush detected: flushed={flushed} written={lastWrittenLineId} n={n}");
                        lastProgressLineId = flushed;
                        onFlush?.Invoke();
                    }                }

                // Dispose() (called by the using block) flushes the remaining RAM index
                // and waits for all background flush tasks to complete. By the time we
                // exit the using block, every line up to lastWrittenLineId is on disk.
            }

            // All flushes are complete. Write the final progress file with the true
            // last line ID on disk. This is safe because Dispose() has already drained
            // the entire flush pipeline via WaitForMerge().
            if (anyLinesProcessed)
            {
                WriteProgressFile(indexPath, lastWrittenLineId, effectiveTotalLines, effectiveResumeOffset + n);
                Console.WriteLine($"[IndexingPipeline] Build complete — final progress lineId={lastWrittenLineId}");
                FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build",
                    $"BUILD COMPLETE — final progress lineId={lastWrittenLineId} n={n}");
            }
            else
            {
                Console.WriteLine($"[IndexingPipeline] No lines processed (WAL recovery only or empty DB) — progress file unchanged at {resumeLineId}");
                FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build",
                    $"no lines processed (WAL recovery only or empty DB) — progress unchanged at resumeLineId={resumeLineId}");
            }

            // Log final index directory state
            try
            {
                if (System.IO.Directory.Exists(indexPath))
                {
                    var allFiles = System.IO.Directory.GetFiles(indexPath);
                    FtsLib.Indexing.FtsLog.Write("IndexingPipeline.Build",
                        $"index dir at BUILD END ({allFiles.Length} files): " +
                        string.Join(", ", System.Array.ConvertAll(allFiles, System.IO.Path.GetFileName)));
                }
            }
            catch { }

            FtsLib.Indexing.FtsLog.Separator("IndexingPipeline.Build END");

            // Return true only when lines were actually processed — this distinguishes
            // a real completed build from a no-op (WAL recovery only, or empty DB).
            return anyLinesProcessed;
        }
    }
}
