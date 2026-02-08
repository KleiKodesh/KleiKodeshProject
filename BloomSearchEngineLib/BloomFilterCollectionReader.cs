using System;
using System.Collections.Generic;
using System.IO;

namespace MinimalIndexer
{
    internal sealed class BloomFilterCollectionReader : IDisposable
    {
        private const int ChunkTargetBytes = 10 * 1024 * 1024;

        private readonly FileStream stream;
        private readonly BinaryReader reader;
        private readonly int filterCount;
        internal short ChunkSize { get; }

        internal BloomFilterCollectionReader(string id)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters");
            stream = new FileStream(
                Path.Combine(dir, $"{id}.dat"),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                65536,
                FileOptions.SequentialScan);
            reader = new BinaryReader(stream);
            filterCount = reader.ReadInt32();
            ChunkSize = reader.ReadInt16();
        }

        internal SearchResult[] Search(string[] searchTerms)
        {
            stream.Seek(6, SeekOrigin.Begin);

            int maxScore = searchTerms.Length;

            // Pre-allocate for worst case (avoid resizing)
            var perfectMatches = new List<SearchResult>(filterCount / 10);
            var partialMatches = new List<SearchResult>(100);

            int read = 0;
            int lowestPartialScore = 0; // Track minimum score we're keeping in partials

            while (read < filterCount)
            {
                long chunkStart = stream.Position;

                while (read < filterCount && stream.Position - chunkStart < ChunkTargetBytes)
                {
                    int bits = reader.ReadInt32();
                    int hashes = reader.ReadInt32();
                    int bytesLen = (bits + 7) / 8;
                    var bytes = reader.ReadBytes(bytesLen);
                    var filter = new BloomFilter(bytes, bits, hashes);

                    // Fast score calculation - inline and optimized
                    int score = 0;
                    for (int i = 0; i < searchTerms.Length; i++)
                    {
                        if (filter.Contains(searchTerms[i]))
                            score++;
                    }

                    if (score == maxScore)
                    {
                        // Perfect match - always keep
                        perfectMatches.Add(new SearchResult { Id = read, Score = score });
                    }
                    else if (score > 0)
                    {
                        // Partial match - only keep if good enough
                        if (partialMatches.Count < 100)
                        {
                            partialMatches.Add(new SearchResult { Id = read, Score = score });

                            // Update lowest score if we just filled to 100
                            if (partialMatches.Count == 100)
                            {
                                lowestPartialScore = int.MaxValue;
                                for (int i = 0; i < partialMatches.Count; i++)
                                {
                                    if (partialMatches[i].Score < lowestPartialScore)
                                        lowestPartialScore = partialMatches[i].Score;
                                }
                            }
                        }
                        else if (score > lowestPartialScore)
                        {
                            // Replace the worst partial match
                            int worstIdx = 0;
                            int worstScore = partialMatches[0].Score;

                            for (int i = 1; i < partialMatches.Count; i++)
                            {
                                if (partialMatches[i].Score < worstScore)
                                {
                                    worstScore = partialMatches[i].Score;
                                    worstIdx = i;
                                }
                            }

                            partialMatches[worstIdx] = new SearchResult { Id = read, Score = score };
                            lowestPartialScore = score; // Update lowest
                        }
                    }

                    read++;
                }
            }

            // Sort partial matches by score (descending)
            partialMatches.Sort((a, b) => b.Score.CompareTo(a.Score));

            // Build final result: all perfect + needed partials
            int neededPartials = perfectMatches.Count < 100
                ? Math.Min(100 - perfectMatches.Count, partialMatches.Count)
                : 0;

            var finalResults = new SearchResult[perfectMatches.Count + neededPartials];

            // Copy perfect matches
            for (int i = 0; i < perfectMatches.Count; i++)
                finalResults[i] = perfectMatches[i];

            // Copy needed partial matches
            for (int i = 0; i < neededPartials; i++)
                finalResults[perfectMatches.Count + i] = partialMatches[i];

            return finalResults;
        }

        public void Dispose()
        {
            reader.Dispose();
            stream.Dispose();
        }
    }

    internal struct SearchResult
    {
        internal int Id;
        internal int Score;
    }
}