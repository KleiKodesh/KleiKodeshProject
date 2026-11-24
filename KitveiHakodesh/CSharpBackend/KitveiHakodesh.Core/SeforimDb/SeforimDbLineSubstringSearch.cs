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
    /// ONE matcher decides what matches and how often (<see cref="SeforimDbLineTextScan"/>:
    /// the frontend's normalization, a KMP pass over each line's text). The only question is
    /// WHICH lines it reads:
    ///   - the full-text index narrows when it can (the merged text only; a fragment that is
    ///     not too common): its candidates are read in ONE batched query by line id, whatever
    ///     books they are in;
    ///   - otherwise - a few thousand lines or fewer (a commentary panel), an alternate version,
    ///     a book not indexed yet, a one-letter query, a fragment too common for the index, or
    ///     candidates that are most of the lines anyway - the ranges' lines are read and matched
    ///     directly.
    /// Either way the caller gets the same answer, in the same shape, from one call.
    /// </summary>
    public static class SeforimDbLineSubstringSearch
    {
        /// <summary>Shorter queries go straight to the direct read (see Search).</summary>
        private const int MinIndexQueryLength = 2;

        /// <summary>
        /// Candidates beyond this share of the wanted lines are read faster as the whole range:
        /// one sequential read beats fetching most of the lines one by one by id (measured: a
        /// common three-letter fragment, 3,503 candidates of 12,539 lines, confirmed 125-600 ms
        /// against a 120-300 ms scan).
        /// </summary>
        private const double MaxCandidateShare = 0.2;

        /// <summary>
        /// A search over at most this many lines reads them directly, without the index: the
        /// index query costs 150-300 ms however few lines are wanted (it expands the fragment to
        /// hundreds of terms), while reading a commentary panel's lines in one batched query and
        /// matching them takes 55-270 ms for 243-799 lines (measured 2026-11-10, busy machine).
        /// </summary>
        private const long DirectReadMaxLines = 3000;

        public static List<SeforimDbLineTextScan.RangeHit> Search(
            SeforimDbFtsIndexCoordinator index, SeforimDbQueries queries,
            string query, IReadOnlyList<FtsLineRange> ranges, int versionId)
        {
            var asTuples = new List<(int BookId, int FirstLineIndex, int LastLineIndex)>(ranges.Count);
            long wantedLines = 0;
            foreach (var r in ranges)
            {
                asTuples.Add((r.BookId, r.FirstLineIndex, r.LastLineIndex));
                if (r.LastLineIndex >= r.FirstLineIndex) wantedLines += r.LastLineIndex - r.FirstLineIndex + 1;
            }
            if (string.IsNullOrWhiteSpace(query) || asTuples.Count == 0) return new List<SeforimDbLineTextScan.RangeHit>();

            // A single character expands to most of the dictionary: the index takes seconds just
            // to decline it, where the direct read answers in under one. Ask the index from two on.
            if (versionId == 0 && wantedLines > DirectReadMaxLines
                && SeforimDbLineTextScan.NormalizeQuery(query).Length >= MinIndexQueryLength)
            {
                var candidates = index.LinesSearch(query, ranges);
                if (candidates.Answered && candidates.LineIds.Length <= wantedLines * MaxCandidateShare)
                    return Confirm(queries, query, candidates);
            }
            return SeforimDbLineTextScan.ScanRanges(queries, asTuples, versionId, query);
        }

        /// <summary>
        /// The candidates' text, read in ONE batched query by line id, through the matcher - in
        /// the candidates' own order (range order, then reading order). It used to be one read per
        /// book: 112 reads for a commentary panel's candidates.
        /// </summary>
        private static List<SeforimDbLineTextScan.RangeHit> Confirm(
            SeforimDbQueries queries, string query, FtsLinesSearchResult candidates)
        {
            var result = new List<SeforimDbLineTextScan.RangeHit>();
            var matcher = new SeforimDbLineTextScan.Matcher(SeforimDbLineTextScan.NormalizeQuery(query));
            if (matcher.IsEmpty || candidates.LineIds.Length == 0) return result;
            var countById = new Dictionary<long, int>(candidates.LineIds.Length);
            queries.ForEachLineByIds(candidates.LineIds, (id, _, _, content) =>
            {
                int count = SeforimDbLineTextScan.CountIn(content, matcher);
                if (count > 0) countById[id] = count;
            });
            for (int i = 0; i < candidates.LineIds.Length; i++)
            {
                long id = candidates.LineIds[i];
                if (countById.TryGetValue(id, out int count))
                    result.Add(new SeforimDbLineTextScan.RangeHit(candidates.BookIds[i], candidates.LineIndexes[i], checked((int)id), count));
            }
            return result;
        }
    }
}
