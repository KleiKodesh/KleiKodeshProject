using BloomSearchEngineLib;
using MinimalIndexer;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public sealed class BloomFilterSearchEngine
{
    private const string id = "lines";
    private const short chunkSize = 10;
    private const double falsePositiveRate = 0.01;

    public event EventHandler<IndexProgressChangedEventArgs> IndexProgressChanged;
    public event EventHandler<DatabaseInitProgressEventArgs> DatabaseInitProgressChanged;

    /// <summary>
    /// One-time/update setup: prepares the database for fast chunk retrieval.
    /// Call this before CreateBloomFilters(). Takes ~3-7 minutes for 6M rows.
    /// Automatically detects if chunk size has changed and rebuilds index if needed.
    /// </summary>
    void InitializeDatabase()
    {
        using (var db = new ZayitDbManager())
        {
            // Check if we need to update
            if (!db.NeedsChunkIndexUpdate(chunkSize))
            {
                // Already initialized with correct chunk size
                DatabaseInitProgressChanged?.Invoke(
                    this,
                    new DatabaseInitProgressEventArgs(1, 1, TimeSpan.Zero, TimeSpan.Zero));
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            int lastProcessedRows = 0;

            db.InitializeChunkIndex(chunkSize, (processedRows, totalRows) =>
            {
                var elapsed = stopwatch.Elapsed;
                var avgMsPerRow = elapsed.TotalMilliseconds / processedRows;
                var eta = TimeSpan.FromMilliseconds(
                    avgMsPerRow * (totalRows - processedRows));

                DatabaseInitProgressChanged?.Invoke(
                    this,
                    new DatabaseInitProgressEventArgs(
                        processedRows,
                        totalRows,
                        elapsed,
                        eta));

                lastProcessedRows = processedRows;
            });

            stopwatch.Stop();

            // Fire final progress event
            using (var finalDb = new ZayitDbManager())
            {
                int totalRows = finalDb.GetLineCount();
                DatabaseInitProgressChanged?.Invoke(
                    this,
                    new DatabaseInitProgressEventArgs(
                        totalRows,
                        totalRows,
                        stopwatch.Elapsed,
                        TimeSpan.Zero));
            }
        }
    }

    public void CreateBloomFilters()
    {
        InitializeDatabase();
        using (var db = new ZayitDbManager())
        using (var writer = new BloomFilterCollectionWriter(id, chunkSize))
        {
            int totalLines = db.GetLineCount();
            int totalChunks = (totalLines + chunkSize - 1) / chunkSize;
            var allLines = db.GetAllLineContents();
            var stopwatch = Stopwatch.StartNew();
            var chunk = new List<string>(chunkSize);
            int processedChunks = 0;

            void handleChunk()
            {
                var terms = ExtractTerms(chunk);
                var filter = new BloomFilter(terms.Count, falsePositiveRate);
                foreach (var term in terms)
                    filter.Add(term);
                writer.Commit(filter);
                chunk.Clear();
                processedChunks++;

                var elapsed = stopwatch.Elapsed;
                var avgMsPerChunk = elapsed.TotalMilliseconds / processedChunks;
                var eta = TimeSpan.FromMilliseconds(
                    avgMsPerChunk * (totalChunks - processedChunks));

                IndexProgressChanged?.Invoke(
                    this,
                    new IndexProgressChangedEventArgs(
                        processedChunks,
                        totalChunks,
                        elapsed,
                        eta));
            }

            foreach (var line in allLines)
            {
                chunk.Add(line);
                if (chunk.Count == chunkSize)
                    handleChunk();
            }

            if (chunk.Count > 0)
                handleChunk();
        }
    }

    /// <summary>
    /// Search and enumerate results by Bloom filter chunk.
    /// If fewer than 100 full-scoring matches are found, yields up to 100 best matches
    /// (sorted by score, proximity, then line ID).
    /// If 100+ full-scoring matches are found, yields only those during iteration.
    /// </summary>
    public IEnumerable<SearchResultItem> SearchByChunk(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            yield break;

        // Extract search terms from query
        var searchTerms = query.Normalize()
            .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (searchTerms.Length == 0)
            yield break;

        int maxPossibleScore = searchTerms.Length;
        var allMatches = new List<SearchResultItem>();
        int fullScoringMatchCount = 0;

        // Search using Bloom filters
        using (var reader = new BloomFilterCollectionReader(id))
        using (var db = new ZayitDbManager())
        {
            // Process each chunk
            foreach (var bloomResult in reader.Search(searchTerms))
            {
                int chunkNumber = bloomResult.Id;
                var chunkLines = db.GetLineContentsChunk(chunkNumber, chunkSize);

                int lineIndexInChunk = 0;
                foreach (var lineContent in chunkLines)
                {
                    // Verify match and get match info with proximity scoring
                    var matchInfo = SearchEngineMatcher.Match(lineContent, searchTerms);

                    if (matchInfo != null)
                    {
                        int globalLineId = chunkNumber * chunkSize + lineIndexInChunk;
                        var result = new SearchResultItem
                        {
                            LineId = globalLineId,
                            Content = lineContent,
                            Score = matchInfo.Words.Length,
                            ProximityScore = matchInfo.ProximityScore,
                            Snippet = matchInfo.Snippet(lineContent)
                        };

                        // Always store the match
                        allMatches.Add(result);

                        // Track full-scoring matches
                        if (result.Score == maxPossibleScore)
                        {
                            fullScoringMatchCount++;

                            // If we have 100+ full-scoring matches, yield immediately
                            if (fullScoringMatchCount >= 100)
                            {
                                yield return result;
                            }
                        }
                    }

                    lineIndexInChunk++;
                }
            }
        }

        // If we found fewer than 100 full-scoring matches, return up to 100 best overall
        if (fullScoringMatchCount < 100 && allMatches.Count > 0)
        {
            // Sort by score descending, then by proximity score, then by LineId
            allMatches.Sort((a, b) =>
            {
                int scoreComparison = b.Score.CompareTo(a.Score);
                if (scoreComparison != 0)
                    return scoreComparison;

                int proximityComparison = b.ProximityScore.CompareTo(a.ProximityScore);
                if (proximityComparison != 0)
                    return proximityComparison;

                return a.LineId.CompareTo(b.LineId);
            });

            // Take up to 100 best matches
            int count = Math.Min(100, allMatches.Count);
            for (int i = 0; i < count; i++)
            {
                yield return allMatches[i];
            }
        }
    }

    private HashSet<string> ExtractTerms(List<string> lines)
    {
        var terms = new HashSet<string>();
        foreach (var line in lines)
        {
            var words = line.Normalize().Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
                terms.Add(word);
        }
        return terms;
    }
}

public sealed class SearchResultItem
{
    public int LineId { get; set; }
    public string Content { get; set; }
    public int Score { get; set; }
    public double ProximityScore { get; set; }
    public string Snippet { get; set; }
}

public sealed class IndexProgressChangedEventArgs : EventArgs
{
    public int ProcessedChunks { get; }
    public int TotalChunks { get; }
    public double Percentage { get; }
    public TimeSpan Elapsed { get; }
    public TimeSpan Eta { get; }

    public IndexProgressChangedEventArgs(
        int processedChunks,
        int totalChunks,
        TimeSpan elapsed,
        TimeSpan eta)
    {
        ProcessedChunks = processedChunks;
        TotalChunks = totalChunks;
        Percentage = processedChunks * 100.0 / totalChunks;
        Elapsed = elapsed;
        Eta = eta;
    }
}

public sealed class DatabaseInitProgressEventArgs : EventArgs
{
    public int ProcessedRows { get; }
    public int TotalRows { get; }
    public double Percentage { get; }
    public TimeSpan Elapsed { get; }
    public TimeSpan Eta { get; }

    public DatabaseInitProgressEventArgs(
        int processedRows,
        int totalRows,
        TimeSpan elapsed,
        TimeSpan eta)
    {
        ProcessedRows = processedRows;
        TotalRows = totalRows;
        Percentage = processedRows * 100.0 / totalRows;
        Elapsed = elapsed;
        Eta = eta;
    }
}