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
            short chunkSize = 100,
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
                        var eta = TimeSpan.FromMilliseconds(
                            sw.Elapsed.TotalMilliseconds / processedChunks *
                            (totalChunks - processedChunks));

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
                        var eta = processed < total
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

        // Add the TermExtractor class here (from Fix #1)
        private sealed class TermExtractor
        {
            private readonly HashSet<string> _terms = new HashSet<string>();
            private readonly System.Text.StringBuilder _wordBuilder = new System.Text.StringBuilder(64);

            public HashSet<string> ExtractTermsFromLines(List<string> lines)
            {
                _terms.Clear();

                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    ProcessLine(line);
                }

                return _terms;
            }

            private void ProcessLine(string text)
            {
                bool inTag = false;
                bool isLineBreakTag = false;
                int tagNamePos = 0;

                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];

                    // === TAG HANDLING ===
                    if (c == '<')
                    {
                        FlushWord();
                        inTag = true;
                        isLineBreakTag = false;
                        tagNamePos = 0;
                        continue;
                    }

                    if (inTag)
                    {
                        if (c == '>')
                        {
                            inTag = false;
                        }
                        else if (tagNamePos < 4)
                        {
                            char lc = (c >= 'A' && c <= 'Z') ? (char)(c | 32) : c;

                            if (c == '/' && tagNamePos == 0)
                                continue;

                            if (tagNamePos == 0)
                                isLineBreakTag = (lc == 'b' || lc == 'p' || lc == 'd');
                            else if (tagNamePos == 1 && isLineBreakTag)
                                isLineBreakTag = (lc == 'r' || lc == 'i');
                            else if (tagNamePos == 2 && isLineBreakTag)
                                isLineBreakTag = (lc == 'v');

                            if (lc >= 'a' && lc <= 'z')
                                tagNamePos++;
                        }
                        continue;
                    }

                    // === CHARACTER PROCESSING ===

                    // Whitespace/separators → flush current word
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' ||
                        c == '\u05BE' || c == '_')
                    {
                        FlushWord();
                    }
                    // Hebrew alphabet → add to current word
                    else if (c >= '\u05D0' && c <= '\u05EA')
                    {
                        _wordBuilder.Append(c);
                    }
                    // Latin uppercase → lowercase and add
                    else if (c >= 'A' && c <= 'Z')
                    {
                        _wordBuilder.Append((char)(c | 32));
                    }
                    // Latin lowercase → add directly
                    else if (c >= 'a' && c <= 'z')
                    {
                        _wordBuilder.Append(c);
                    }
                    // Everything else is ignored (stripped)
                }

                FlushWord();
            }

            private void FlushWord()
            {
                if (_wordBuilder.Length > 0)
                {
                    _terms.Add(_wordBuilder.ToString());
                    _wordBuilder.Clear();
                }
            }
        }
    }
}
