using System;
using System.Collections.Generic;
using System.IO;

namespace MinimalIndexer
{
    internal sealed class BloomFilterCollectionReader : IDisposable
    {
        private const int ChunkTargetBytes = 10 * 1024 * 1024; // ~10 MB
        private readonly FileStream stream;
        private readonly BinaryReader reader;
        private readonly int filterCount;
        private readonly short chunkSize;

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
            chunkSize = reader.ReadInt16();
        }

        /// <summary>
        /// Search Bloom filters and yield results as an enumerable.
        /// If fewer than 100 max-scoring chunks are found, yields up to 100 best chunks.
        /// If 100+ max-scoring chunks are found, yields only those.
        /// </summary>
        internal IEnumerable<SearchResult> Search(string[] searchTerms)
        {
            var allResults = new List<SearchResult>();
            int bestScore = 0;
            int filtersRead = 0;
            int maxScoreCount = 0;

            while (filtersRead < filterCount)
            {
                long chunkStart = stream.Position;

                while (filtersRead < filterCount &&
                       stream.Position - chunkStart < ChunkTargetBytes)
                {
                    int bitCount = reader.ReadInt32();
                    int hashFunctions = reader.ReadInt32();
                    int byteLen = (bitCount + 7) / 8;
                    byte[] bytes = reader.ReadBytes(byteLen);

                    var filter = new BloomFilter(bytes, bitCount, hashFunctions);
                    int score = 0;

                    for (int i = 0; i < searchTerms.Length; i++)
                        if (filter.Contains(searchTerms[i]))
                            score++;

                    if (score > 0)
                    {
                        allResults.Add(new SearchResult
                        {
                            Id = filtersRead,
                            Score = score
                        });

                        // Track best score and count
                        if (score > bestScore)
                        {
                            bestScore = score;
                            maxScoreCount = 1;
                        }
                        else if (score == bestScore)
                        {
                            maxScoreCount++;
                        }
                    }

                    filtersRead++;
                }
            }

            if (allResults.Count == 0)
                yield break;

            // Sort by score descending
            allResults.Sort((a, b) => b.Score.CompareTo(a.Score));

            // If we found fewer than 100 max-scoring chunks, return up to 100 best overall
            // If we found 100+ max-scoring chunks, return only those
            int take = maxScoreCount >= 100
                ? maxScoreCount
                : Math.Min(100, allResults.Count);

            for (int i = 0; i < take; i++)
            {
                yield return allResults[i];
            }
        }

        public void Dispose()
        {
            reader?.Dispose();
            stream?.Dispose();
        }
    }

    internal struct SearchResult
    {
        internal int Id;
        internal int Score;
    }
}