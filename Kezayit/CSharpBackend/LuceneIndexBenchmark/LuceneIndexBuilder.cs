using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Store;
using Lucene.Net.Util;

namespace LuceneIndexBenchmark
{
    /// <summary>
    /// Builds a Lucene index from the SQLite `line` table using the high-RAM,
    /// single-flush strategy: set RAMBufferSizeMB large enough that Lucene never
    /// flushes a segment mid-build, then ForceMerge(1) at the end for a single
    /// optimized segment.
    ///
    /// Field layout (all Store.NO except lineId):
    ///   lineId   — StringField, Store.YES  — the only stored value; used to fetch text from SQLite
    ///   bookId   — StringField, Store.NO   — enables per-book filtering at query time
    ///   content  — TextField,   Store.NO   — analyzed Hebrew text; tokens only, no stored text
    /// </summary>
    public sealed class LuceneIndexBuilder
    {
        private readonly string _indexDirectoryPath;
        private readonly double _ramBufferMegabytes;
        private readonly int _reportIntervalMs;

        public event EventHandler<IndexProgressEventArgs> ProgressChanged;

        public LuceneIndexBuilder(string indexDirectoryPath, double ramBufferMegabytes = 512.0)
        {
            _indexDirectoryPath = indexDirectoryPath;
            _ramBufferMegabytes = ramBufferMegabytes;
            _reportIntervalMs = 1000;
        }

        /// <summary>
        /// Builds the index. Returns elapsed time.
        /// Uses a producer/consumer pipeline identical in structure to BloomFilterIndexer:
        ///   - Reader thread: streams SQLite rows, batches them into chunks
        ///   - Worker threads: normalize + tokenize each chunk, produce Document lists
        ///   - Writer thread (main): adds Document lists to IndexWriter in order
        ///
        /// The IndexWriter is configured with a very large RAM buffer so it never
        /// flushes a segment until we explicitly call ForceMerge(1) at the end.
        /// </summary>
        public TimeSpan Build(string databasePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            Console.WriteLine("[LuceneIndexBuilder] Starting build");
            Console.WriteLine("[LuceneIndexBuilder] Index path : " + _indexDirectoryPath);
            Console.WriteLine("[LuceneIndexBuilder] RAM buffer : " + _ramBufferMegabytes + " MB");

            var overallStopwatch = Stopwatch.StartNew();

            // ── Open index directory ──────────────────────────────────────────
            var fsDirectory = FSDirectory.Open(new DirectoryInfo(_indexDirectoryPath));

            // ── Analyzer: WhitespaceAnalyzer ──────────────────────────────────
            // We do our own normalization before indexing, so we want Lucene to
            // split only on whitespace and apply no further transforms.
            var analyzer = new WhitespaceAnalyzer(LuceneVersion.LUCENE_48);

            // ── IndexWriter config ────────────────────────────────────────────
            var indexWriterConfig = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer)
            {
                OpenMode        = OpenMode.CREATE,
                RAMBufferSizeMB = _ramBufferMegabytes,
                // Disable doc-count-based flushing — use RAM limit only
                MaxBufferedDocs = IndexWriterConfig.DISABLE_AUTO_FLUSH,
            };

            // Disable compound file format during build: faster segment writes.
            // We re-enable it implicitly after ForceMerge produces the final segment.
            indexWriterConfig.MergePolicy = new LogByteSizeMergePolicy
            {
                NoCFSRatio = 0.0,
            };

            int totalLines;
            using (var lineReader = new SqliteLineReader(databasePath))
            {
                totalLines = lineReader.GetTotalLineCount();
            }
            Console.WriteLine("[LuceneIndexBuilder] Total lines: " + totalLines);

            using (var indexWriter = new IndexWriter(fsDirectory, indexWriterConfig))
            {
                const int chunkSize = 500;
                int workerCount = Math.Max(1, Environment.ProcessorCount - 1);
                Console.WriteLine("[LuceneIndexBuilder] Worker threads: " + workerCount);

                // Bounded queues — same pattern as BloomFilterIndexer
                var workQueue   = new System.Collections.Concurrent.BlockingCollection<LineChunk>(workerCount * 4);
                var resultQueue = new System.Collections.Concurrent.BlockingCollection<DocumentChunk>(workerCount * 4);

                // ── Reader task ───────────────────────────────────────────────
                var readerTask = Task.Run(() =>
                {
                    try
                    {
                        using (var lineReader = new SqliteLineReader(databasePath))
                        {
                            int sequenceNumber = 0;
                            var currentChunk = new List<(int LineId, int BookId, string Content)>(chunkSize);

                            foreach (var (lineId, content) in lineReader.ReadAllLines())
                            {
                                if (cancellationToken.IsCancellationRequested)
                                    return;

                                // bookId is not in the line table directly — we pass 0 here.
                                // In a real build you would JOIN to get bookId, or pass it
                                // via a separate lookup. See Program.cs for the full-schema variant.
                                currentChunk.Add((lineId, 0, content));

                                if (currentChunk.Count == chunkSize)
                                {
                                    workQueue.Add(new LineChunk { SequenceNumber = sequenceNumber++, Lines = currentChunk });
                                    currentChunk = new List<(int LineId, int BookId, string Content)>(chunkSize);
                                }
                            }

                            if (currentChunk.Count > 0 && !cancellationToken.IsCancellationRequested)
                                workQueue.Add(new LineChunk { SequenceNumber = sequenceNumber, Lines = currentChunk });
                        }
                    }
                    finally { workQueue.CompleteAdding(); }
                });

                // ── Worker tasks ──────────────────────────────────────────────
                var workerTasks = new Task[workerCount];
                for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
                {
                    workerTasks[workerIndex] = Task.Run(() =>
                    {
                        foreach (var chunk in workQueue.GetConsumingEnumerable())
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            var documents = new List<Document>(chunk.Lines.Count);
                            foreach (var (lineId, bookId, content) in chunk.Lines)
                            {
                                string normalizedContent = HebrewTextNormalizer.Normalize(content);
                                if (string.IsNullOrWhiteSpace(normalizedContent))
                                    continue;

                                var document = new Document();

                                // lineId: stored so we can retrieve it from search hits
                                document.Add(new StringField("lineId", lineId.ToString(), Field.Store.YES));

                                // bookId: indexed but not stored — for per-book filtering
                                if (bookId > 0)
                                    document.Add(new StringField("bookId", bookId.ToString(), Field.Store.NO));

                                // content: analyzed, not stored — tokens only
                                // We pre-normalized, so WhitespaceAnalyzer just splits on spaces
                                document.Add(new TextField("content", normalizedContent, Field.Store.NO));

                                documents.Add(document);
                            }

                            resultQueue.Add(new DocumentChunk
                            {
                                SequenceNumber = chunk.SequenceNumber,
                                Documents      = documents,
                            });
                        }
                    });
                }

                // Close resultQueue when all workers finish
                Task.Run(() =>
                {
                    Task.WaitAll(workerTasks);
                    resultQueue.CompleteAdding();
                });

                // ── Writer (this thread) ──────────────────────────────────────
                // Reorder buffer preserves sequence order so IndexWriter sees docs
                // in lineId order — better for segment locality.
                var reorderBuffer = new SortedDictionary<int, DocumentChunk>();
                int nextExpected  = 0;
                int totalIndexed  = 0;
                var lastReport    = overallStopwatch.Elapsed;

                foreach (var chunk in resultQueue.GetConsumingEnumerable())
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    reorderBuffer[chunk.SequenceNumber] = chunk;

                    while (reorderBuffer.ContainsKey(nextExpected))
                    {
                        var orderedChunk = reorderBuffer[nextExpected];
                        reorderBuffer.Remove(nextExpected);

                        foreach (var document in orderedChunk.Documents)
                            indexWriter.AddDocument(document);

                        totalIndexed += orderedChunk.Documents.Count;
                        nextExpected++;

                        var now = overallStopwatch.Elapsed;
                        if ((now - lastReport).TotalMilliseconds >= _reportIntervalMs)
                        {
                            double percent = totalLines > 0 ? totalIndexed * 100.0 / totalLines : 0;
                            var eta = totalIndexed > 0 && totalIndexed < totalLines
                                ? TimeSpan.FromMilliseconds(now.TotalMilliseconds / totalIndexed * (totalLines - totalIndexed))
                                : TimeSpan.Zero;

                            ProgressChanged?.Invoke(this, new IndexProgressEventArgs(
                                totalIndexed, totalLines, now, eta));

                            Console.WriteLine(string.Format(
                                "[LuceneIndexBuilder] {0:N0}/{1:N0} lines ({2:F1}%)  elapsed={3}  eta={4}  ram={5:F0}MB",
                                totalIndexed, totalLines, percent,
                                FormatTimeSpan(now), FormatTimeSpan(eta),
                                GC.GetTotalMemory(false) / 1024.0 / 1024.0));

                            lastReport = now;
                        }
                    }
                }

                readerTask.Wait();

                if (!cancellationToken.IsCancellationRequested)
                {
                    Console.WriteLine("[LuceneIndexBuilder] Indexing complete. Starting ForceMerge(1)...");
                    var mergeStopwatch = Stopwatch.StartNew();
                    indexWriter.ForceMerge(1);
                    mergeStopwatch.Stop();
                    Console.WriteLine("[LuceneIndexBuilder] ForceMerge done in " + FormatTimeSpan(mergeStopwatch.Elapsed));
                }

                Console.WriteLine("[LuceneIndexBuilder] Committing...");
                indexWriter.Commit();
            }

            overallStopwatch.Stop();
            Console.WriteLine("[LuceneIndexBuilder] Total build time: " + FormatTimeSpan(overallStopwatch.Elapsed));
            return overallStopwatch.Elapsed;
        }

        private static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalHours >= 1)
                return string.Format("{0:F0}h {1:D2}m {2:D2}s", Math.Floor(timeSpan.TotalHours), timeSpan.Minutes, timeSpan.Seconds);
            if (timeSpan.TotalMinutes >= 1)
                return string.Format("{0:D2}m {1:D2}s", (int)timeSpan.TotalMinutes, timeSpan.Seconds);
            return string.Format("{0:F1}s", timeSpan.TotalSeconds);
        }

        // ── Internal data structures ──────────────────────────────────────────

        private struct LineChunk
        {
            public int SequenceNumber;
            public List<(int LineId, int BookId, string Content)> Lines;
        }

        private struct DocumentChunk
        {
            public int SequenceNumber;
            public List<Document> Documents;
        }
    }

    public sealed class IndexProgressEventArgs : EventArgs
    {
        public int IndexedCount  { get; }
        public int TotalCount    { get; }
        public TimeSpan Elapsed  { get; }
        public TimeSpan Eta      { get; }

        public IndexProgressEventArgs(int indexedCount, int totalCount, TimeSpan elapsed, TimeSpan eta)
        {
            IndexedCount = indexedCount;
            TotalCount   = totalCount;
            Elapsed      = elapsed;
            Eta          = eta;
        }
    }
}
