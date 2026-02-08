using MinimalIndexer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BloomSearchEngineLib
{
    public sealed class BloomFilterSearcher
    {
        private readonly string _id;

        public BloomFilterSearcher(string id = "lines")
        {
            _id = id;
        }

        public IEnumerable<SearchResultItem> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                yield break;

            // Normalize query ONCE, same as text
            string normalizedQuery = query.NormalizeText();
            var terms = normalizedQuery
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (terms.Length == 0)
                yield break;

            int maxScore = terms.Length;
            var allMatches = new ConcurrentBag<SearchResultItem>();

            using (var reader = new BloomFilterCollectionReader(_id))
            {
                short chunkSize = reader.ChunkSize;
                var hits = reader.Search(terms);

                // ONLY parallelize if we have enough chunks to make it worthwhile
                if (hits.Length < 10)
                {
                    // Small result set - just process sequentially
                    using (var db = new ZayitDbManager())
                    {
                        foreach (var hit in hits)
                        {
                            ProcessChunk(hit, db, chunkSize, terms, allMatches);
                        }
                    }
                }
                else
                {
                    // Large result set - parallelize chunk processing
                    // Each thread gets its own database connection
                    Parallel.ForEach(hits,
                        new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                        () => new ZayitDbManager(), // Thread-local DB connection
                        (hit, loopState, db) =>
                        {
                            ProcessChunk(hit, db, chunkSize, terms, allMatches);
                            return db;
                        },
                        (db) => db.Dispose() // Cleanup thread-local DB
                    );
                }
            }

            // Separate perfect matches from partial matches
            var allMatchesList = allMatches.ToList();
            var perfectMatches = allMatchesList.Where(x => x.Score == maxScore).ToList();
            var partialMatches = allMatchesList.Where(x => x.Score < maxScore).ToList();

            // Sort perfect matches by proximity (best first), then by line ID
            var sortedPerfect = perfectMatches
                .OrderByDescending(x => x.ProximityScore)
                .ThenBy(x => x.LineId);

            // Return ALL perfect matches (even if more than 100)
            foreach (var item in sortedPerfect)
                yield return item;

            // If we have fewer than 100 perfect matches, fill up to 100 with partial matches
            if (perfectMatches.Count < 100)
            {
                int remaining = 100 - perfectMatches.Count;
                foreach (var item in partialMatches
                    .OrderByDescending(x => x.Score)           // Higher score first
                    .ThenByDescending(x => x.ProximityScore)   // Better proximity within same score
                    .ThenBy(x => x.LineId)                     // Consistent ordering
                    .Take(remaining))
                {
                    yield return item;
                }
            }
        }

        private void ProcessChunk(
            SearchResult hit,
            ZayitDbManager db,
            short chunkSize,
            string[] terms,
            ConcurrentBag<SearchResultItem> results)
        {
            int minRequiredWords = hit.Score;
            var lines = db.GetLineContentsChunk(hit.Id, chunkSize);
            int i = 0;

            foreach (var line in lines)
            {
                string normalizedLine = line.NormalizeText();
                var match = SearchEngineMatcher.Match(normalizedLine, terms, minRequiredWords);

                if (match != null)
                {
                    var item = new SearchResultItem
                    {
                        LineId = hit.Id * chunkSize + i,
                        Score = match.Words.Length,
                        ProximityScore = match.ProximityScore,
                        Snippet = match.Snippet(normalizedLine)
                    };
                    results.Add(item);
                }
                i++;
            }
        }
    }
}