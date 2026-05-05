using FtsLib.Core;
using FtsLib.Misc;
using System;

namespace FtsLib.Seforim
{
    /// <summary>
    /// Builds the full-text index from the seforim SQLite database.
    /// Streams every line through the tokenizer and writes term/docId pairs
    /// to the index in strictly ascending ID order (required by the codec).
    /// </summary>
    internal static class IndexingPipeline
    {
        /// <summary>
        /// Builds the index at <paramref name="indexPath"/> from the database at
        /// <paramref name="dbPath"/>.
        /// </summary>
        /// <param name="indexPath">Directory where segment files are written.</param>
        /// <param name="dbPath">Path to the seforim SQLite database.</param>
        /// <param name="limit">
        /// Maximum number of lines to index. 0 (default) = all lines.
        /// Useful for partial builds during testing.
        /// </param>
        /// <param name="onProgress">
        /// Optional callback invoked after each line is processed.
        /// Receives the running count of lines indexed so far.
        /// </param>
        internal static void Build(
            string      indexPath,
            string      dbPath,
            int         limit      = 0,
            Action<long> onProgress = null)
        {
            var tokenizer = new Tokenizer();
            long n = 0;

            using (var db     = new ZayitDb(dbPath))
            using (var writer = new IndexWriter(indexPath) { AutoOptimize = true })
            {
                foreach (var (id, content) in db.ReadLines(limit))
                {
                    foreach (var token in tokenizer.Extract(content))
                        writer.Add(id, token);

                    n++;
                    onProgress?.Invoke(n);
                }
            }
        }
    }
}
