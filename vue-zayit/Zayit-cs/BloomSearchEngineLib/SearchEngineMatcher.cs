using System;
using System.Collections.Generic;

namespace BloomSearchEngineLib
{
    public static class SearchEngineMatcher
    {
        private const double MIN_PROXIMITY_SCORE = 0.2;

        /// <summary>
        /// Matches text against search words, requiring at least minRequiredWords to match.
        /// </summary>
        /// <param name="text">The text to search in</param>
        /// <param name="words">The words to search for</param>
        /// <param name="minRequiredWords">Minimum number of words that must be found (from Bloom filter score)</param>
        /// <returns>MatchInfo if at least minRequiredWords are found and proximity is acceptable, null otherwise</returns>
        public static MatchInfo Match(string text, string[] words, int minRequiredWords)
        {
            var matchInfo = new MatchInfo { Words = words };
            var foundWords = new List<string>();
            var foundPositions = new List<List<int>>();

            // Phase 1: Find which words exist and their first occurrence
            for (int i = 0; i < words.Length; i++)
            {
                int pos = text.IndexOf(words[i], StringComparison.Ordinal);
                if (pos != -1)
                {
                    foundWords.Add(words[i]);
                    foundPositions.Add(new List<int> { pos });
                }
            }

            // Check if we have enough matches
            if (foundWords.Count < minRequiredWords)
                return null; // Not enough words found

            // Phase 2: Collect all positions for each found word
            for (int i = 0; i < foundWords.Count; i++)
            {
                int index = foundPositions[i][0] + 1;
                while ((index = text.IndexOf(foundWords[i], index, StringComparison.Ordinal)) != -1)
                {
                    foundPositions[i].Add(index);
                    index++;
                }
            }

            // Store the found words and their positions
            matchInfo.Words = foundWords.ToArray();
            matchInfo.AllPositions = foundPositions.ToArray();

            // Phase 3: Calculate proximity score
            CalculateProximityScore(matchInfo);

            // Filter out matches with low proximity scores
            if (matchInfo.ProximityScore < MIN_PROXIMITY_SCORE)
                return null;

            return matchInfo;
        }

        private static void CalculateProximityScore(MatchInfo matchInfo)
        {
            if (matchInfo.Words.Length == 1)
            {
                matchInfo.ProximityScore = 1.0;
                matchInfo.ClusterStart = matchInfo.AllPositions[0][0];
                matchInfo.ClusterEnd = matchInfo.ClusterStart;
                return;
            }

            var clusterInfo = FindMinimumSpanWindow(matchInfo.AllPositions, matchInfo.Words);

            // Score inversely proportional to span
            matchInfo.ProximityScore = 1.0 / (1.0 + clusterInfo.Span / 100.0);
            matchInfo.ClusterStart = clusterInfo.MinPos;
            matchInfo.ClusterEnd = clusterInfo.MaxPos;
        }

        private struct PositionEntry
        {
            public int Position;
            public int WordIndex;
        }

        private static (int Span, int MinPos, int MaxPos) FindMinimumSpanWindow(
            List<int>[] allPositions, string[] words)
        {
            int numWords = words.Length;

            int totalPositions = 0;
            for (int i = 0; i < numWords; i++)
                totalPositions += allPositions[i].Count;

            var merged = new PositionEntry[totalPositions];
            int mergedIndex = 0;

            for (int wordIdx = 0; wordIdx < numWords; wordIdx++)
            {
                for (int j = 0; j < allPositions[wordIdx].Count; j++)
                {
                    merged[mergedIndex++] = new PositionEntry
                    {
                        Position = allPositions[wordIdx][j],
                        WordIndex = wordIdx
                    };
                }
            }

            Array.Sort(merged, (a, b) => a.Position.CompareTo(b.Position));

            var wordCount = new int[numWords];
            int uniqueWords = 0;
            int left = 0;
            int minSpan = int.MaxValue;
            int bestMinPos = -1;
            int bestMaxPos = -1;

            for (int right = 0; right < merged.Length; right++)
            {
                int wordIdx = merged[right].WordIndex;

                if (wordCount[wordIdx] == 0)
                    uniqueWords++;
                wordCount[wordIdx]++;

                while (uniqueWords == numWords)
                {
                    int span = merged[right].Position - merged[left].Position;

                    if (span < minSpan)
                    {
                        minSpan = span;
                        bestMinPos = merged[left].Position;
                        bestMaxPos = merged[right].Position;
                    }

                    int leftWordIdx = merged[left].WordIndex;
                    wordCount[leftWordIdx]--;

                    if (wordCount[leftWordIdx] == 0)
                        uniqueWords--;

                    left++;
                }
            }

            return (minSpan, bestMinPos, bestMaxPos);
        }

        public static string ExtractSnippetFromCluster(
            string text, int clusterStart, int clusterEnd, int maxSnippetLength = 200)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            if (clusterStart == -1 || clusterEnd == -1)
                return text.Length <= maxSnippetLength
                    ? text
                    : text.Substring(0, maxSnippetLength) + "...";

            // Ensure cluster positions are within text bounds
            clusterStart = Math.Max(0, Math.Min(clusterStart, text.Length - 1));
            clusterEnd = Math.Max(0, Math.Min(clusterEnd, text.Length - 1));

            int snippetStart = Math.Max(0, clusterStart - 50);
            int snippetEnd = Math.Min(text.Length, clusterEnd + 50);

            // Only search for word boundary if snippetStart > 0 AND < text.Length
            if (snippetStart > 0 && snippetStart < text.Length)
            {
                int wordBoundary = text.LastIndexOf(' ', snippetStart);
                if (wordBoundary > snippetStart - 20 && wordBoundary != -1)
                    snippetStart = wordBoundary + 1;
            }

            if (snippetEnd < text.Length)
            {
                int wordBoundary = text.IndexOf(' ', snippetEnd);
                if (wordBoundary != -1 && wordBoundary < snippetEnd + 20)
                    snippetEnd = wordBoundary;
            }

            // Ensure valid substring range
            if (snippetStart >= text.Length)
                snippetStart = Math.Max(0, text.Length - 1);
            if (snippetEnd <= snippetStart)
                snippetEnd = Math.Min(snippetStart + 1, text.Length);

            string snippet = text.Substring(snippetStart, snippetEnd - snippetStart);

            if (snippetStart > 0)
                snippet = "..." + snippet;
            if (snippetEnd < text.Length)
                snippet += "...";

            return snippet;
        }
    }

    public class MatchInfo
    {
        public string[] Words { get; set; }
        public List<int>[] AllPositions { get; set; }
        public double ProximityScore { get; set; }
        public int ClusterStart { get; set; }
        public int ClusterEnd { get; set; }

        public string Snippet(string text) =>
            SearchEngineMatcher.ExtractSnippetFromCluster(text, ClusterStart, ClusterEnd);
    }
}