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
        private readonly double _falsePositiveRate;

        // Timer-based progress reporting interval
        private const int ProgressReportIntervalMs = 1000; // Report every ---ms

        public event EventHandler<IndexProgressChangedEventArgs> IndexProgressChanged;
        public event EventHandler<DatabaseInitProgressEventArgs> DatabaseInitProgressChanged;

        public BloomFilterIndexer(
            string id = "lines",
            short chunkSize = 25,
            double falsePositiveRate = 0.01)
        {
            _id = id;
            _chunkSize = chunkSize;
            _falsePositiveRate = falsePositiveRate;
        }

        public void CreateBloomFilters()
        {
            InitializeDatabase();

            using (var db = new ZayitDbManager())
            using (var writer = new BloomFilterCollectionWriter(_id, _chunkSize))
            {
                int totalLines = db.GetLineCount();
                int totalChunks = (totalLines + _chunkSize - 1) / _chunkSize;

                var sw = Stopwatch.StartNew();
                var lastReportTime = sw.Elapsed;
                var chunk = new List<string>(_chunkSize);
                int processedChunks = 0;

                // Reusable term extractor to avoid allocations
                var termExtractor = new TermExtractor();

                void Commit()
                {
                    var terms = termExtractor.ExtractTermsFromLines(chunk);
                    if (terms.Count > 0)
                    {
                        var filter = new BloomFilter(terms.Count, _falsePositiveRate);
                        foreach (var t in terms)
                            filter.Add(t);
                        writer.Commit(filter);
                    }

                    chunk.Clear();
                    processedChunks++;

                    // TIMER-BASED REPORTING: Only fire event if enough time has passed
                    var currentTime = sw.Elapsed;
                    if ((currentTime - lastReportTime).TotalMilliseconds >= ProgressReportIntervalMs)
                    {
                        // FIX: Guard against division by zero
                        var eta = (processedChunks > 0 && processedChunks < totalChunks)
                            ? TimeSpan.FromMilliseconds(
                                sw.Elapsed.TotalMilliseconds / processedChunks *
                                (totalChunks - processedChunks))
                            : TimeSpan.Zero;

                        IndexProgressChanged?.Invoke(
                            this,
                            new IndexProgressChangedEventArgs(
                                processedChunks, totalChunks, sw.Elapsed, eta));

                        lastReportTime = currentTime;
                    }
                }

                foreach (var line in db.GetAllLineContents())
                {
                    chunk.Add(line);
                    if (chunk.Count == _chunkSize)
                        Commit();
                }

                if (chunk.Count > 0)
                    Commit();

                // FINAL REPORT: Always fire at 100% completion
                var finalEta = TimeSpan.Zero;
                IndexProgressChanged?.Invoke(
                    this,
                    new IndexProgressChangedEventArgs(
                        processedChunks, totalChunks, sw.Elapsed, finalEta));
            }
        }

        private void InitializeDatabase()
        {
            using (var db = new ZayitDbManager())
            {
                if (!db.NeedsChunkIndexUpdate(_chunkSize))
                {
                    DatabaseInitProgressChanged?.Invoke(
                        this,
                        new DatabaseInitProgressEventArgs(1, 1, TimeSpan.Zero, TimeSpan.Zero));
                    return;
                }

                var sw = Stopwatch.StartNew();
                var lastReportTime = sw.Elapsed;

                db.InitializeChunkIndex(_chunkSize, (processed, total) =>
                {
                    // TIMER-BASED REPORTING for database init too
                    var currentTime = sw.Elapsed;
                    if ((currentTime - lastReportTime).TotalMilliseconds >= ProgressReportIntervalMs ||
                        processed == total) // Always report completion
                    {
                        // FIX: Guard against division by zero
                        var eta = (processed > 0 && processed < total)
                            ? TimeSpan.FromMilliseconds(
                                sw.Elapsed.TotalMilliseconds / processed * (total - processed))
                            : TimeSpan.Zero;

                        DatabaseInitProgressChanged?.Invoke(
                            this,
                            new DatabaseInitProgressEventArgs(
                                processed, total, sw.Elapsed, eta));

                        lastReportTime = currentTime;
                    }
                });
            }
        }
    }
}