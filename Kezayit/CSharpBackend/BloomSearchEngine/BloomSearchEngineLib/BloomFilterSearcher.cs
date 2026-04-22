using MinimalIndexer;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BloomSearchEngineLib
{
    public sealed class BloomFilterSearcher
    {
        private readonly string _id;

        public BloomFilterSearcher(string id = "lines") { _id = id; }

        public IEnumerable<SearchResultItem> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) yield break;

            // Normalize each query term the same way TermExtractor normalizes during indexing:
            // strip nikud, keep only Hebrew letters (א–ת) and ASCII letters.
            // This ensures the Bloom filter lookup and IndexOf match use the same form
            // as the indexed terms, regardless of what punctuation or diacritics the
            // user typed.
            var rawTerms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var termList = new List<string>(rawTerms.Length);
            foreach (var raw in rawTerms)
            {
                var normalized = TextNormalizer.NormalizeQueryTerm(raw);
                if (normalized != null) termList.Add(normalized);
            }
            var terms = termList.ToArray();
            if (terms.Length == 0) yield break;

            using (var reader = new BloomFilterCollectionReader(_id))
            {
                var hits = reader.Search(terms);
                if (hits.Length == 0) yield break;

                // Process hits one chunk at a time and stream results to the caller.
                // Each chunk's perfect matches are sorted by proximity before yielding,
                // so the frontend receives well-ordered results as they arrive rather
                // than waiting for all chunks to finish.
                var chunkBatch    = new List<SearchResultItem>();
                var partialMatches = new TopNPartialMatches(100);
                int perfectCount  = 0;

                using (var db = new ZayitDbManager())
                {
                    foreach (var hit in hits)
                    {
                        chunkBatch.Clear();
                        ProcessChunk(db, hit, terms, terms.Length, chunkBatch, partialMatches);

                        if (chunkBatch.Count == 0) continue;

                        foreach (var item in chunkBatch) { perfectCount++; yield return item; }
                    }
                }

                Console.WriteLine("[Search] {0} perfect + {1} partial", perfectCount, partialMatches.Count);

                foreach (var item in HydratePartialMatches(partialMatches, perfectCount))
                    yield return item;

                Console.WriteLine("[Search completed] RAM: {0:F2} MB", GC.GetTotalMemory(false) / (1024.0 * 1024.0));
            }
        }

        private static IEnumerable<SearchResultItem> HydratePartialMatches(TopNPartialMatches partialMatches, int perfectCount)
        {
            if (perfectCount >= 100) yield break;

            int remaining = Math.Min(100 - perfectCount, partialMatches.Count);
            var partials = partialMatches.GetTop(remaining);
            if (partials.Length == 0) yield break;

            using (var db = new ZayitDbManager())
            {
                var lineIds = new List<int>(partials.Length);
                foreach (var p in partials) lineIds.Add(p.LineId);

                var metadata = db.GetLineMetadataBatch(lineIds);

                foreach (var p in partials)
                {
                    var content = db.GetLineContent(p.LineId).NormalizeText();
                    if (!metadata.TryGetValue(p.LineId, out var meta)) continue;
                    yield return new SearchResultItem
                    {
                        LineId = p.LineId,
                        BookId = meta.bookId,
                        BookTitle = meta.bookTitle,
                        TocText = meta.tocText,
                        Score = p.Score,
                        ProximityScore = p.ProximityScore,
                        Snippet = SearchEngineMatcher.ExtractSnippetFromCluster(content, p.ClusterStart, p.ClusterEnd)
                    };
                }
            }
        }

        private static void ProcessChunk(
            ZayitDbManager db,
            SearchResult hit,
            string[] terms,
            int maxScore,
            List<SearchResultItem> perfectMatches,
            TopNPartialMatches partialMatches)
        {
            var perfectLineIds   = new List<int>();
            var perfectMatchInfo = new List<(int lineId, MatchInfo match, string norm)>();

            foreach (var (lineId, content) in db.GetLineContentsChunk(hit.FirstLineId, hit.LastLineId))
            {
                // Cheap raw pre-filter: if any term's first character is absent from the
                // raw content, normalization cannot produce a match — skip it entirely.
                if (!TextNormalizer.RawContentMightMatch(content, terms)) continue;

                string norm = content.NormalizeText();
                var match = SearchEngineMatcher.Match(norm, terms, hit.Score);
                if (match == null) continue;

                if (match.Words.Length == maxScore)
                {
                    perfectLineIds.Add(lineId);
                    perfectMatchInfo.Add((lineId, match, norm));
                }
                else
                {
                    partialMatches.TryAdd(new PartialMatchData
                    {
                        LineId = lineId,
                        Score = match.Words.Length,
                        ProximityScore = match.ProximityScore,
                        ClusterStart = match.ClusterStart,
                        ClusterEnd = match.ClusterEnd
                    });
                }
            }

            if (perfectLineIds.Count == 0) return;

            var metadata = db.GetLineMetadataBatch(perfectLineIds);

            foreach (var (lineId, match, norm) in perfectMatchInfo)
            {
                if (!metadata.TryGetValue(lineId, out var meta)) continue;
                perfectMatches.Add(new SearchResultItem
                {
                    LineId = lineId,
                    BookId = meta.bookId,
                    BookTitle = meta.bookTitle,
                    TocText = meta.tocText,
                    Score = match.Words.Length,
                    ProximityScore = match.ProximityScore,
                    Snippet = match.Snippet(norm)
                });
            }
        }

        private struct PartialMatchData
        {
            public int LineId, Score, ClusterStart, ClusterEnd;
            public double ProximityScore;
        }

        private class TopNPartialMatches
        {
            private readonly object _lock = new object();
            private readonly SortedSet<PartialMatchData> _set;
            private readonly int _max;

            public TopNPartialMatches(int max)
            {
                _max = max;
                _set = new SortedSet<PartialMatchData>(Comparer<PartialMatchData>.Create((a, b) =>
                {
                    int c = b.Score.CompareTo(a.Score);
                    if (c != 0) return c;
                    c = b.ProximityScore.CompareTo(a.ProximityScore);
                    return c != 0 ? c : a.LineId.CompareTo(b.LineId);
                }));
            }

            public void TryAdd(PartialMatchData m)
            {
                lock (_lock)
                {
                    if (_set.Count < _max) { _set.Add(m); return; }
                    var worst = _set.Max;
                    if (m.Score > worst.Score || (m.Score == worst.Score && m.ProximityScore > worst.ProximityScore))
                    {
                        _set.Remove(worst);
                        _set.Add(m);
                    }
                }
            }

            public PartialMatchData[] GetTop(int count) { lock (_lock) return _set.Take(count).ToArray(); }
            public int Count { get { lock (_lock) return _set.Count; } }
        }
    }
}
