using MinimalIndexer;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BloomSearchEngineLib
{
    public sealed class BloomFilterIndexer
    {
        private readonly string _id;
        private readonly short _chunkSize;
        private readonly double _fpRate;
        private const int ReportIntervalMs = 1000;

        public event EventHandler<IndexProgressChangedEventArgs> IndexProgressChanged;

        public BloomFilterIndexer(string id = "lines", short chunkSize = 100, double falsePositiveRate = 0.01)
        {
            _id = id; _chunkSize = chunkSize; _fpRate = falsePositiveRate;
        }

        public void CreateBloomFilters()
        {
            if (!BloomIndexingCoordinator.TryAcquireIndexingLock(0)) return;
            try
            {
                using (var db = new ZayitDbManager())
                using (var writer = new BloomFilterCollectionWriter(_id, _chunkSize))
                {
                    if (db._connection == null || db._connection.IsCanceled()) return;

                    int totalLines = db.GetLineCount();
                    int totalChunks = (totalLines + _chunkSize - 1) / _chunkSize;
                    var sw = Stopwatch.StartNew();
                    var lastReport = sw.Elapsed;
                    var chunk = new List<string>(_chunkSize);
                    int processed = 0;
                    var extractor = new TermExtractor();

                    void Commit()
                    {
                        var terms = extractor.ExtractTermsFromLines(chunk);
                        // Always write a filter — even an empty one — so filter index N always maps to chunk N.
                        // An empty filter will never match any term, which is correct for an all-whitespace chunk.
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
                        chunk.Add(line);
                        if (chunk.Count == _chunkSize) Commit();
                    }
                    if (chunk.Count > 0) Commit();

                    var final = new IndexProgressChangedEventArgs(processed, totalChunks, sw.Elapsed, TimeSpan.Zero);
                    IndexProgressChanged?.Invoke(this, final);
                    BloomIndexingCoordinator.NotifyProgress(final);
                }
            }
            finally { BloomIndexingCoordinator.ReleaseIndexingLock(); }
        }
    }
}
