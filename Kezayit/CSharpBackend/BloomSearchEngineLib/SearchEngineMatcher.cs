using System;
using System.Collections.Generic;

namespace BloomSearchEngineLib
{
    public static class SearchEngineMatcher
    {
        private const double MinProximity = 0.2;

        public static MatchInfo Match(string text, string[] words, int minRequired)
        {
            var foundWords = new List<string>();
            var foundPositions = new List<List<int>>();

            for (int i = 0; i < words.Length; i++)
            {
                int pos = text.IndexOf(words[i], StringComparison.Ordinal);
                if (pos != -1) { foundWords.Add(words[i]); foundPositions.Add(new List<int> { pos }); }
            }

            if (foundWords.Count < minRequired) return null;

            for (int i = 0; i < foundWords.Count; i++)
            {
                int idx = foundPositions[i][0] + 1;
                while ((idx = text.IndexOf(foundWords[i], idx, StringComparison.Ordinal)) != -1)
                { foundPositions[i].Add(idx); idx++; }
            }

            var info = new MatchInfo { Words = foundWords.ToArray(), AllPositions = foundPositions.ToArray() };
            CalcProximity(info);
            return info.ProximityScore < MinProximity ? null : info;
        }

        private static void CalcProximity(MatchInfo m)
        {
            if (m.Words.Length == 1)
            {
                m.ProximityScore = 1.0;
                m.ClusterStart = m.ClusterEnd = m.AllPositions[0][0];
                return;
            }
            var c = FindMinSpan(m.AllPositions, m.Words);
            m.ProximityScore = 1.0 / (1.0 + c.Span / 100.0);
            m.ClusterStart = c.Min; m.ClusterEnd = c.Max;
        }

        private struct PosEntry { public int Position, WordIndex; }

        private static (int Span, int Min, int Max) FindMinSpan(List<int>[] positions, string[] words)
        {
            int total = 0;
            for (int i = 0; i < words.Length; i++) total += positions[i].Count;

            var merged = new PosEntry[total];
            int mi = 0;
            for (int w = 0; w < words.Length; w++)
                for (int j = 0; j < positions[w].Count; j++)
                    merged[mi++] = new PosEntry { Position = positions[w][j], WordIndex = w };

            Array.Sort(merged, (a, b) => a.Position.CompareTo(b.Position));

            var count = new int[words.Length];
            int unique = 0, left = 0, minSpan = int.MaxValue, bestMin = -1, bestMax = -1;

            for (int right = 0; right < merged.Length; right++)
            {
                int wi = merged[right].WordIndex;
                if (count[wi]++ == 0) unique++;

                while (unique == words.Length)
                {
                    int span = merged[right].Position - merged[left].Position;
                    if (span < minSpan) { minSpan = span; bestMin = merged[left].Position; bestMax = merged[right].Position; }
                    int lwi = merged[left].WordIndex;
                    if (--count[lwi] == 0) unique--;
                    left++;
                }
            }
            return (minSpan, bestMin, bestMax);
        }

        public static string ExtractSnippetFromCluster(string text, int start, int end, int maxLen = 500)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (start == -1 || end == -1) return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";

            start = Math.Max(0, Math.Min(start, text.Length - 1));
            end = Math.Max(0, Math.Min(end, text.Length - 1));

            int sStart = Math.Max(0, start - 50);
            int sEnd = Math.Min(text.Length, end + 50);

            if (sStart > 0 && sStart < text.Length) { int wb = text.LastIndexOf(' ', sStart); if (wb > sStart - 20 && wb != -1) sStart = wb + 1; }
            if (sEnd < text.Length) { int wb = text.IndexOf(' ', sEnd); if (wb != -1 && wb < sEnd + 20) sEnd = wb; }

            if (sStart >= text.Length) sStart = Math.Max(0, text.Length - 1);
            if (sEnd <= sStart) sEnd = Math.Min(sStart + 1, text.Length);

            string snippet = text.Substring(sStart, sEnd - sStart);
            if (sStart > 0) snippet = "..." + snippet;
            if (sEnd < text.Length) snippet += "...";
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
        public string Snippet(string text) => SearchEngineMatcher.ExtractSnippetFromCluster(text, ClusterStart, ClusterEnd);
    }
}
