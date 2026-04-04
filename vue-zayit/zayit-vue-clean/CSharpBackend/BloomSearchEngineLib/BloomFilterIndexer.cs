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

        public void CreateBloomFilters()
        {
            Console.WriteLine("[BloomFilterIndexer] CreateBloomFilters called, dbPath=" + _dbPath);
            CancellationToken ct;
            if (!BloomIndexingCoordinator.TryAcquireIndexingLock(0, out ct))
            {
                Console.WriteLine("[BloomFilterIndexer] Could not acquire lock, aborting");
                return;
            }
            try
            {
                using (var db = new ZayitDbManager(_dbPath))
                using (var writer = new BloomFilterCollectionWriter(_id, _chunkSize))
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
                    int processed = 0;
                    var extractor = new TermExtractor();

                    void Commit()
                    {
                        var terms = extractor.ExtractTermsFromLines(chunk);
                        var filter = new BloomFilter(Math.Max(1, terms.Count), _fpRate);
                        foreach (var t in terms) filter.Add(t);
                        writer.Commit(filter);
                        chunk.Clear();
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

                    foreach (var line in db.GetAllLineContents())
                    {
                        if (ct.IsCancellationRequested) { Console.WriteLine("[BloomFilterIndexer] Cancelled during indexing"); return; }
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
