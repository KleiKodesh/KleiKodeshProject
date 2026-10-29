using System;
using System.Collections.Generic;
using KitveiHakodesh.Core.SeforimDbFullTextSearch;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// The in-book and commentary-panel search, whole: every line inside the given (book,
    /// first lineIndex, last lineIndex) ranges that holds the query as a SUBSTRING, with its
    /// occurrence count — numbers only, nothing kept.
    ///
    /// The full-text index narrows when it can (the merged text only; a fragment that is not
    /// too common): its candidate lines are a superset, confirmed here by the native matcher
    /// on their text alone. When it cannot — an alternate version, a book not indexed yet, a
    /// one- or two-letter fragment — the ranges' lines are scanned natively instead. Either
    /// way the caller gets the same answer, in the same shape, from one call.
    /// </summary>
    public static class SeforimDbLineSubstringSearch
    {
        /// <summary>Shorter queries go straight to the native scan (see Search).</summary>
        private const int MinIndexQueryLength = 2;

        public static List<SeforimDbLineTextScan.RangeHit> Search(
            SeforimDbFtsIndexCoordinator index, SeforimDbQueries queries,
            string query, IReadOnlyList<FtsLineRange> ranges, int versionId)
        {
            var asTuples = new List<(int BookId, int FirstLineIndex, int LastLineIndex)>(ranges.Count);
            foreach (var r in ranges) asTuples.Add((r.BookId, r.FirstLineIndex, r.LastLineIndex));
            if (string.IsNullOrWhiteSpace(query) || asTuples.Count == 0) return new List<SeforimDbLineTextScan.RangeHit>();

            // A single character expands to most of the dictionary: the index takes seconds just
            // to decline it, where the scan answers in under one. Ask the index from two on.
            if (versionId == 0 && SeforimDbLineTextScan.NormalizeQuery(query).Length >= MinIndexQueryLength)
            {
                var candidates = index.LinesSearch(query, ranges);
                if (candidates.Answered)
                {
                    // Confirm the candidates on their text, per book, reading only those lines.
                    var byBook = new Dictionary<int, List<int>>();
                    var order = new List<int>();
                    for (int i = 0; i < candidates.BookIds.Length; i++)
                    {
                        int bookId = candidates.BookIds[i];
                        if (!byBook.TryGetValue(bookId, out var list)) { list = new List<int>(); byBook[bookId] = list; order.Add(bookId); }
                        list.Add(candidates.LineIndexes[i]);
                    }
                    var result = new List<SeforimDbLineTextScan.RangeHit>();
                    foreach (int bookId in order)
                    {
                        var indexes = byBook[bookId];
                        indexes.Sort();
                        foreach (var hit in SeforimDbLineTextScan.ScanIndexes(queries, bookId, indexes, 0, query))
                            result.Add(new SeforimDbLineTextScan.RangeHit(bookId, hit.LineIndex, hit.LineId, hit.Count));
                    }
                    return result;
                }
            }
            return SeforimDbLineTextScan.ScanRanges(queries, asTuples, versionId, query);
        }
    }
}
