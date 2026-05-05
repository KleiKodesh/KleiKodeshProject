using FtsLib.Core;
using FtsLib.Misc;
using System;
using System.IO;

namespace FtsLib.Seforim
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
        internal static void Build(
            string       indexPath,
            string       dbPath,
            int          limit      = 0,
            Action<long> onProgress = null)
        {
            int resumeLineId = ReadResumeLineId(indexPath);
            if (resumeLineId > 0)
                Console.WriteLine($"[IndexingPipeline] Resuming from line id {resumeLineId}");
            else
                Console.WriteLine("[IndexingPipeline] Starting fresh build");

            var tokenizer = new Tokenizer();
            long n = 0;

            using (var db     = new ZayitDb(dbPath))
            using (var writer = new IndexWriter(indexPath) { AutoOptimize = false })
            {
                // IndexWriter's constructor already ran Recover() when existing segments
                // were found, so the on-disk state is consistent and ready to append to.

                var lineSource = resumeLineId > 0
                    ? db.ReadLinesFrom(resumeLineId, limit)
                    : db.ReadLines(limit);

                int lastWrittenLineId = resumeLineId;
                int lastProgressLineId = resumeLineId; // last id written to progress file

                foreach (var (id, content) in lineSource)
                {
                    foreach (var token in tokenizer.Extract(content))
                        writer.Add(id, token);

                    lastWrittenLineId = id;
                    n++;
                    onProgress?.Invoke(n);

                    // After each flush, IndexWriter.LastFlushedLineId advances to the
                    // last line ID that is now safely on disk. Write the progress file
                    // whenever it advances so a resume skips as much work as possible.
                    int flushed = writer.LastFlushedLineId;
                    if (flushed > lastProgressLineId)
                    {
                        WriteProgressFile(indexPath, flushed);
                        lastProgressLineId = flushed;
                    }
                }

                // Dispose flushes the final RAM batch — all lines are now in segments.
            }

            // Final flush is complete. Update the progress file to the last written id
            // so a resume (if the app is killed before the version stamp is written)
            // skips all lines. SearchHandler deletes this file after writing the stamp.
            WriteProgressFile(indexPath, lastWrittenLineId);
        }

        /// <summary>
        /// Force-merges all segments into one for fastest subsequent search.
        /// Call this after <see cref="Build"/> returns — the index is already
        /// searchable before this completes, so it can run in the background.
        /// </summary>
        internal static void Optimize(string indexPath)
        {
            using (var writer = new IndexWriter(indexPath) { AutoOptimize = false })
                writer.Optimize();
        }
    }
}
