using MinimalIndexer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

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

        /// <summary>
        /// Start or resume indexing.
        /// resumeAfterLineId: skip all lines with id &lt;= this value. Only meaningful when resuming.
        /// resumeChunkCount: number of chunks already committed to the .dat file (0 = fresh start).
        /// </summary>
        public void CreateBloomFilters(int resumeAfterLineId = 0, int resumeChunkCount = 0)
        {
            Console.WriteLine("[BloomFilterIndexer] CreateBloomFilters called, dbPath=" + _dbPath
                + " resumeAfterLineId=" + resumeAfterLineId + " resumeChunkCount=" + resumeChunkCount);
            CancellationToken ct;
            if (!BloomIndexingCoordinator.TryAcquireIndexingLock(0, out ct))
            {
                Console.WriteLine("[BloomFilterIndexer] Could not acquire lock, aborting");
                return;
            }
            try
            {
                using (var db = new ZayitDbManager(_dbPath))
                using (var writer = new BloomFilterCollectionWriter(_id, _chunkSize, resumeChunkCount))
                {
                    Console.WriteLine("[BloomFilterIndexer] ZayitDbManager created, connection=" + (db._connection == null ? "NULL" : "OK"));
                    if (db._connection == null || db._connection.IsCanceled())
                    {
                        Console.WriteLine("[BloomFilterIndexer] DB connection null or cancelled, aborting");
                        return;
                    }

                    int totalLines = db.GetLineCount();
                    int totalChunks = (totalLines + _chunkSize - 1) / _chunkSize;
                    Console.WriteLine("[BloomFilterIndexer] totalLines=" + totalLines + " totalChunks=" + totalChunks);
                    var sw = Stopwatch.StartNew();
                    var lastReport = sw.Elapsed;
                    var chunk = new List<string>(_chunkSize);
                    // Start processed count from already-committed chunks so progress % is correct
                    int processed = resumeChunkCount;
                    bool isResuming = resumeChunkCount > 0;
                    var extractor = new TermExtractor();
                    // Use int.MinValue as "not set" sentinel — safe since no real line ID will be that low
                    const int NotSet = int.MinValue;
                    int chunkFirstId = NotSet, chunkLastId = NotSet;

                    void Commit()
                    {
                        var terms = extractor.ExtractTermsFromLines(chunk);
                        var filter = new BloomFilter(Math.Max(1, terms.Count), _fpRate);
                        foreach (var t in terms) filter.Add(t);
                        writer.Commit(filter, chunkFirstId, chunkLastId);
                        // Sentinel updated AFTER the chunk is fully written — mid-chunk kill redoes this chunk
                        KezayitLib.Search.SearchHandler.UpdateSentinel(chunkLastId, writer.Count);
                        chunk.Clear();
                        chunkFirstId = NotSet; chunkLastId = NotSet;
                        processed++;

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

                    foreach (var (lineId, line) in db.GetAllLineContents())
                    {
                        if (ct.IsCancellationRequested) { Console.WriteLine("[BloomFilterIndexer] Cancelled during indexing"); return; }
                        // Skip lines already committed in a previous run (only when resuming)
                        if (isResuming && lineId <= resumeAfterLineId) continue;
                        if (chunkFirstId == NotSet) chunkFirstId = lineId;
                        chunkLastId = lineId;
                        chunk.Add(line);
                        if (chunk.Count == _chunkSize) Commit();
                    }
                    if (chunk.Count > 0 && !ct.IsCancellationRequested) Commit();

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
