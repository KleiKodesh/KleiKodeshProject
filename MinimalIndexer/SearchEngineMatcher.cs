using System;
using System.Collections.Generic;

namespace MinimalIndexer
{
    internal static class SearchEngineMatcher
    {
        private const double MIN_PROXIMITY_SCORE = 0.2;

        internal static MatchInfo Match(string text, string[] words)
        {
            var matchInfo = new MatchInfo { Words = words };
            var firstPositions = new int[words.Length];

            // Phase 1: Find first occurrence of each word
            for (int i = 0; i < words.Length; i++)
            {
                int pos = text.IndexOf(words[i], StringComparison.Ordinal);
                if (pos == -1)
                    return null; // Early exit

                firstPositions[i] = pos;
            }

            // Phase 2: Collect all positions for each word
            matchInfo.AllPositions = new List<int>[words.Length];
            for (int i = 0; i < words.Length; i++)
            {
                var positions = new List<int> { firstPositions[i] };
                int index = firstPositions[i] + 1;

                while ((index = text.IndexOf(words[i], index, StringComparison.Ordinal)) != -1)
                {
                    positions.Add(index);
                    index++;
                }

                matchInfo.AllPositions[i] = positions;
            }

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
            internal int Position;
            internal int WordIndex;
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

        internal static string ExtractSnippetFromCluster(
            string text, int clusterStart, int clusterEnd, int maxSnippetLength = 200)
        {
            if (clusterStart == -1 || clusterEnd == -1)
                return text.Length <= maxSnippetLength
                    ? text
                    : text.Substring(0, maxSnippetLength) + "...";

            int snippetStart = Math.Max(0, clusterStart - 50);
            int snippetEnd = Math.Min(text.Length, clusterEnd + 50);

            if (snippetStart > 0)
            {
                int wordBoundary = text.LastIndexOf(' ', snippetStart);
                if (wordBoundary > snippetStart - 20)
                    snippetStart = wordBoundary + 1;
            }

            if (snippetEnd < text.Length)
            {
                int wordBoundary = text.IndexOf(' ', snippetEnd);
                if (wordBoundary != -1 && wordBoundary < snippetEnd + 20)
                    snippetEnd = wordBoundary;
            }

            string snippet = text.Substring(snippetStart, snippetEnd - snippetStart);

            if (snippetStart > 0)
                snippet = "..." + snippet;
            if (snippetEnd < text.Length)
                snippet += "...";

            return snippet;
        }
    }

    internal class MatchInfo
    {
        internal string[] Words { get; set; }
        internal List<int>[] AllPositions { get; set; }
        internal double ProximityScore { get; set; }
        internal int ClusterStart { get; set; }
        internal int ClusterEnd { get; set; }

        internal string Snippet(string text) =>
            SearchEngineMatcher.ExtractSnippetFromCluster(text, ClusterStart, ClusterEnd);
    }
}
