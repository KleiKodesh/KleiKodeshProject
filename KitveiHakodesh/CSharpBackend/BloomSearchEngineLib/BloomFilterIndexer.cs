using MinimalIndexer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BloomSearchEngineLib
{
    public sealed class BloomFilterIndexer
    {
        private readonly string _id;
        private readonly short _chunkSize;
        private readonly double _fpRate;
        private readonly string _dbPath;
        private const int ReportIntervalMs = 1000;

        public event EventHandler<IndexProgressChangedEventArgs> IndexProgressChanged;

        public BloomFilterIndexer(string id = "lines", short chunkSize = 100, double falsePositiveRate = 0.01, string dbPath = null)
        {
            _id = id; _chunkSize = chunkSize; _fpRate = falsePositiveRate; _dbPath = dbPath;
        }

        // Holds one chunk ready for processing.
        private struct ChunkWork
        {
            public int SequenceNumber;
            public List<string> Lines;
            public int FirstLineId;
            public int LastLineId;
        }

        // Holds one processed chunk ready for writing.
        private struct ChunkResult
        {
            public int SequenceNumber;
            public BloomFilter Filter;
            public int FirstLineId;
            public int LastLineId;
        }

        /// <summary>
        /// Start or resume indexing.
        /// Uses a parallel pipeline:
        ///   - Reader thread: streams rows from SQLite in bulk batches, splits into chunks,
        ///     enqueues ChunkWork items.
        ///   - Worker threads (ProcessorCount-1): each dequeues a ChunkWork, normalizes lines,
        ///     extracts terms, builds a BloomFilter, enqueues a ChunkResult.
        ///   - Writer (main thread): drains ChunkResults in sequence order, writes to .dat file.
        /// Order is preserved via sequence numbers and a reorder buffer on the writer side.
        /// </summary>
        public void CreateBloomFilters(int resumeAfterLineId = 0, int resumeChunkCount = 0)
        {
            Console.WriteLine("[BloomFilterIndexer] CreateBloomFilters called, resumeAfterLineId=" + resumeAfterLineId + " resumeChunkCount=" + resumeChunkCount);
            CancellationToken ct;
            if (!BloomIndexingCoordinator.TryAcquireIndexingLock(0, out ct))
            {
                Console.WriteLine("[BloomFilterIndexer] Could not acquire lock, aborting");
                return;
            }
            try
            {
                using (var db = new ZayitDbManager(_dbPath))
                using (var writer = new BloomFilterCollectionWriter(_id, _chunkSize, resumeChunkCount, resumeAfterLineId))
                {
                    Console.WriteLine("[BloomFilterIndexer] ZayitDbManager created, connection=" + (db._connection == null ? "NULL" : "OK"));
                    if (db._connection == null || db._connection.IsCanceled())
                    {
                        Console.WriteLine("[BloomFilterIndexer] DB connection null or cancelled, aborting");
                        return;
                    }

                    int totalLines = db.GetLineCount();
                    int totalChunks = (totalLines + _chunkSize - 1) / _chunkSize;
                    int workerCount = Math.Max(1, Environment.ProcessorCount - 1);
                    Console.WriteLine("[BloomFilterIndexer] totalLines=" + totalLines + " totalChunks=" + totalChunks + " workers=" + workerCount);

                    var sw = Stopwatch.StartNew();
                    var lastReport = sw.Elapsed;
                    int processed = resumeChunkCount;
                    double fpRate = _fpRate;
                    short chunkSize = _chunkSize;

                    // Queue from reader → workers. Bounded to limit memory: at most 4× workers pending.
                    var workQueue = new BlockingCollection<ChunkWork>(workerCount * 4);
                    // Queue from workers → writer. Same bound.
                    var resultQueue = new BlockingCollection<ChunkResult>(workerCount * 4);

                    // ── Reader task ───────────────────────────────────────────
                    var readerTask = Task.Run(() =>
                    {
                        try
                        {
                            int seq = resumeChunkCount;
                            const int NotSet = int.MinValue;
                            int chunkFirstId = NotSet, chunkLastId = NotSet;
                            var chunk = new List<string>(chunkSize);

                            foreach (var batch in db.GetAllLineContentsBulk(resumeAfterLineId))
                            {
                                foreach (var (lineId, line) in batch)
                                {
                                    if (ct.IsCancellationRequested) return;
                                    if (chunkFirstId == NotSet) chunkFirstId = lineId;
                                    chunkLastId = lineId;
                                    chunk.Add(line);
                                    if (chunk.Count == chunkSize)
                                    {
                                        workQueue.Add(new ChunkWork
                                        {
                                            SequenceNumber = seq++,
                                            Lines = chunk,
                                            FirstLineId = chunkFirstId,
                                            LastLineId = chunkLastId,
                                        });
                                        chunk = new List<string>(chunkSize);
                                        chunkFirstId = NotSet; chunkLastId = NotSet;
                                    }
                                }
                                if (ct.IsCancellationRequested) return;
                            }
                            // Final partial chunk
                            if (chunk.Count > 0 && !ct.IsCancellationRequested)
                                workQueue.Add(new ChunkWork
                                {
                                    SequenceNumber = seq,
                                    Lines = chunk,
                                    FirstLineId = chunkFirstId,
                                    LastLineId = chunkLastId,
                                });
                        }
                        finally { workQueue.CompleteAdding(); }
                    });

                    // ── Worker tasks ──────────────────────────────────────────
                    var workerTasks = new Task[workerCount];
                    for (int w = 0; w < workerCount; w++)
                    {
                        workerTasks[w] = Task.Run(() =>
                        {
                            var extractor = new TermExtractor();
                            foreach (var work in workQueue.GetConsumingEnumerable())
                            {
                                if (ct.IsCancellationRequested) break;
                                var terms = extractor.ExtractTermsFromLines(work.Lines);
                                var filter = new BloomFilter(Math.Max(1, terms.Count), fpRate);
                                foreach (var t in terms) filter.Add(t);
                                resultQueue.Add(new ChunkResult
                                {
                                    SequenceNumber = work.SequenceNumber,
                                    Filter = filter,
                                    FirstLineId = work.FirstLineId,
                                    LastLineId = work.LastLineId,
                                });
                            }
                        });
                    }

                    // Close resultQueue when all workers finish.
                    // The task is stored so its exceptions are observed by the
                    // readerTask.Wait() / writer-loop path below.
                    Task workerDrainTask = Task.Run(() =>
                    {
                        try
                        {
                            Task.WaitAll(workerTasks);
                        }
                        catch (AggregateException ae)
                        {
                            Console.WriteLine("[BloomFilterIndexer] Worker exception: " + ae);
                        }
                        finally
                        {
                            // Always signal the writer — even on worker failure —
                            // so the writer loop can drain and exit rather than hang.
                            resultQueue.CompleteAdding();
                        }
                    });

                    // ── Writer (this thread) ──────────────────────────────────
                    // Reorder buffer: holds out-of-order results until the next expected
                    // sequence number is available.
                    var reorderBuffer = new SortedDictionary<int, ChunkResult>();
                    int nextExpected = resumeChunkCount;
                    long msWrite = 0;

                    foreach (var result in resultQueue.GetConsumingEnumerable())
                    {
                        if (ct.IsCancellationRequested) break;
                        reorderBuffer[result.SequenceNumber] = result;

                        // Drain all consecutive results from the buffer
                        while (reorderBuffer.ContainsKey(nextExpected))
                        {
                            var r = reorderBuffer[nextExpected];
                            reorderBuffer.Remove(nextExpected);

                            var swWrite = Stopwatch.StartNew();
                            writer.Commit(r.Filter, r.FirstLineId, r.LastLineId);
                            swWrite.Stop(); msWrite += swWrite.ElapsedMilliseconds;

                            processed++;
                            nextExpected++;

                            var now = sw.Elapsed;
                            if ((now - lastReport).TotalMilliseconds >= ReportIntervalMs)
                            {
                                var eta = processed > 0 && processed < totalChunks
                                    ? TimeSpan.FromMilliseconds(sw.Elapsed.TotalMilliseconds / processed * (totalChunks - processed))
                                    : TimeSpan.Zero;
                                var args = new IndexProgressChangedEventArgs(processed, totalChunks, sw.Elapsed, eta);
                                IndexProgressChanged?.Invoke(this, args);
                                BloomIndexingCoordinator.NotifyProgress(args);
                                lastReport = now;
                            }
                        }
                    }

                    readerTask.Wait();
                    workerDrainTask.Wait();

                    sw.Stop();
                    Console.WriteLine("[BloomFilterIndexer] Done — total={0}s  chunks={1}  write={2}ms",
                        sw.Elapsed.TotalSeconds.ToString("F1"), processed, msWrite);

                    if (!ct.IsCancellationRequested)
                    {
                        var final = new IndexProgressChangedEventArgs(processed, totalChunks, sw.Elapsed, TimeSpan.Zero);
                        IndexProgressChanged?.Invoke(this, final);
                        BloomIndexingCoordinator.NotifyProgress(final);
                    }
                }
            }
            finally { BloomIndexingCoordinator.ReleaseIndexingLock(); }
        }
    }
}
