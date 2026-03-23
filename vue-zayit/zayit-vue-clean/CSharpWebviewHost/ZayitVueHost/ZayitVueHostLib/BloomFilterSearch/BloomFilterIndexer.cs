using MinimalIndexer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Zayit.Services;

namespace BloomSearchEngineLib
{
    public sealed class BloomFilterIndexer
    {
        private readonly string _id;
        private readonly short _chunkSize;
        private readonly double _falsePositiveRate;
        private const int ProgressReportIntervalMs = 1000;

        public event EventHandler<IndexProgressChangedEventArgs> IndexProgressChanged;

        public BloomFilterIndexer(string id = "lines", short chunkSize = 100, double falsePositiveRate = 0.01)
        {
            _id = id;
            _chunkSize = chunkSize;
            _falsePositiveRate = falsePositiveRate;
        }

        public void CreateBloomFilters()
        {
            if (!BloomIndexingCoordinator.TryAcquireIndexingLock(0))
            {
                Console.WriteLine("[BloomFilterIndexer] Another instance is already indexing");
                return;
            }

            try
            {
                using (var writer = new BloomFilterCollectionWriter(_id, _chunkSize))
                {
                    int totalLines = DbService.GetLineCount();
                    int totalChunks = (totalLines + _chunkSize - 1) / _chunkSize;

                    var sw = Stopwatch.StartNew();
                    var lastReportTime = sw.Elapsed;
                    var chunk = new List<string>(_chunkSize);
                    int processedChunks = 0;
                    var termExtractor = new TermExtractor();

                    void Commit()
                    {
                        var terms = termExtractor.ExtractTermsFromLines(chunk);
                        if (terms.Count > 0)
                        {
                            var filter = new BloomFilter(terms.Count, _falsePositiveRate);
                            foreach (var t in terms) filter.Add(t);
                            writer.Commit(filter);
                        }
                        chunk.Clear();
                        processedChunks++;

                        var now = sw.Elapsed;
                        if ((now - lastReportTime).TotalMilliseconds >= ProgressReportIntervalMs)
                        {
                            var eta = (processedChunks > 0 && processedChunks < totalChunks)
                                ? TimeSpan.FromMilliseconds(now.TotalMilliseconds / processedChunks * (totalChunks - processedChunks))
                                : TimeSpan.Zero;
                            var args = new IndexProgressChangedEventArgs(processedChunks, totalChunks, now, eta);
                            IndexProgressChanged?.Invoke(this, args);
                            BloomIndexingCoordinator.NotifyProgress(args);
                            lastReportTime = now;
                        }
                    }

                    foreach (var line in DbService.GetAllLineContents())
                    {
                        chunk.Add(line);
                        if (chunk.Count == _chunkSize) Commit();
                    }
                    if (chunk.Count > 0) Commit();

                    var final = new IndexProgressChangedEventArgs(processedChunks, totalChunks, sw.Elapsed, TimeSpan.Zero);
                    IndexProgressChanged?.Invoke(this, final);
                    BloomIndexingCoordinator.NotifyProgress(final);
                }
            }
            finally
            {
                BloomIndexingCoordinator.ReleaseIndexingLock();
            }
        }
    }
}
