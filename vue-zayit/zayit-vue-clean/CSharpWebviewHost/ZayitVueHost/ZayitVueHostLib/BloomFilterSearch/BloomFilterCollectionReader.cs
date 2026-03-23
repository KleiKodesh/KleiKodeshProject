using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MinimalIndexer
{
    public sealed class BloomFilterCollectionReader : IDisposable
    {
        private const int CacheLineSize = 64;
        private readonly int filterCount;
        public short ChunkSize { get; }
        private readonly BloomFilterData[] filters;

        public BloomFilterCollectionReader(string id)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters");
            string filePath = Path.Combine(dir, $"{id}.dat");

            // Load all data into memory in one go
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
            using (var reader = new BinaryReader(stream))
            {
                filterCount = reader.ReadInt32();
                ChunkSize = reader.ReadInt16();

                // Pre-allocate array for all filters
                filters = new BloomFilterData[filterCount];

                // Load all filters into memory
                for (int i = 0; i < filterCount; i++)
                {
                    int bits = reader.ReadInt32();
                    int hashes = reader.ReadInt32();
                    int bytesLen = (bits + 7) / 8;
                    var bytes = reader.ReadBytes(bytesLen);

                    filters[i] = new BloomFilterData
                    {
                        Filter = new BloomFilter(bytes, bits, hashes),
                        Id = i
                    };

                    // Skip padding bytes for cache line alignment
                    long entrySize = 8 + bytesLen; // metadata (4+4) + data
                    int padding = CalculatePadding(entrySize);
                    if (padding > 0)
                        reader.ReadBytes(padding);
                }
            }
        }

        private int CalculatePadding(long entrySize)
        {
            long remainder = (entrySize % CacheLineSize);
            return remainder == 0 ? 0 : (int)(CacheLineSize - remainder);
        }

        public SearchResult[] Search(string[] searchTerms)
        {
            int maxScore = searchTerms.Length;
            int threadCount = Environment.ProcessorCount;

            // Calculate chunk size for each thread
            int chunkSize = (filterCount + threadCount - 1) / threadCount;

            // Storage for results from each thread
            var threadResults = new ThreadSearchResults[threadCount];

            // Process chunks in parallel
            Parallel.For(0, threadCount, threadIndex =>
            {
                int startIdx = threadIndex * chunkSize;
                int endIdx = Math.Min(startIdx + chunkSize, filterCount);

                if (startIdx >= filterCount)
                    return; // No work for this thread

                var perfectMatches = new List<SearchResult>(chunkSize / 10);
                var partialMatches = new List<SearchResult>(100);
                int lowestPartialScore = 0;

                // Process this thread's chunk
                for (int i = startIdx; i < endIdx; i++)
                {
                    var filterData = filters[i];

                    // Calculate score
                    int score = 0;
                    for (int t = 0; t < searchTerms.Length; t++)
                    {
                        if (filterData.Filter.Contains(searchTerms[t]))
                            score++;
                    }

                    if (score == maxScore)
                    {
                        // Perfect match
                        perfectMatches.Add(new SearchResult { Id = filterData.Id, Score = score });
                    }
                    else if (score > 0)
                    {
                        // Partial match - keep top 100 per thread
                        if (partialMatches.Count < 100)
                        {
                            partialMatches.Add(new SearchResult { Id = filterData.Id, Score = score });

                            if (partialMatches.Count == 100)
                            {
                                lowestPartialScore = int.MaxValue;
                                for (int j = 0; j < partialMatches.Count; j++)
                                {
                                    if (partialMatches[j].Score < lowestPartialScore)
                                        lowestPartialScore = partialMatches[j].Score;
                                }
                            }
                        }
                        else if (score > lowestPartialScore)
                        {
                            // Replace worst partial match
                            int worstIdx = 0;
                            int worstScore = partialMatches[0].Score;

                            for (int j = 1; j < partialMatches.Count; j++)
                            {
                                if (partialMatches[j].Score < worstScore)
                                {
                                    worstScore = partialMatches[j].Score;
                                    worstIdx = j;
                                }
                            }

                            partialMatches[worstIdx] = new SearchResult { Id = filterData.Id, Score = score };
                            lowestPartialScore = score;
                        }
                    }
                }

                // Store this thread's results
                threadResults[threadIndex] = new ThreadSearchResults
                {
                    PerfectMatches = perfectMatches,
                    PartialMatches = partialMatches
                };
            });

            // Merge results from all threads
            return MergeResults(threadResults, maxScore);
        }

        private SearchResult[] MergeResults(ThreadSearchResults[] threadResults, int maxScore)
        {
            // Collect all perfect matches
            var allPerfectMatches = new List<SearchResult>();
            var allPartialMatches = new List<SearchResult>();

            foreach (var result in threadResults)
            {
                if (result == null) continue;

                if (result.PerfectMatches != null)
                    allPerfectMatches.AddRange(result.PerfectMatches);

                if (result.PartialMatches != null)
                    allPartialMatches.AddRange(result.PartialMatches);
            }

            // Sort partial matches by score descending, then take top ones
            allPartialMatches.Sort((a, b) => b.Score.CompareTo(a.Score));

            // Determine how many partials we need
            int neededPartials = allPerfectMatches.Count < 100
                ? Math.Min(100 - allPerfectMatches.Count, allPartialMatches.Count)
                : 0;

            // Build final result array
            var finalResults = new SearchResult[allPerfectMatches.Count + neededPartials];

            // Copy perfect matches
            for (int i = 0; i < allPerfectMatches.Count; i++)
                finalResults[i] = allPerfectMatches[i];

            // Copy needed partial matches
            for (int i = 0; i < neededPartials; i++)
                finalResults[allPerfectMatches.Count + i] = allPartialMatches[i];

            return finalResults;
        }

        public void Dispose()
        {
            // Filters are in memory, nothing to dispose
        }
    }

    public struct BloomFilterData
    {
        public BloomFilter Filter;
        public int Id;
    }

    public class ThreadSearchResults
    {
        public List<SearchResult> PerfectMatches;
        public List<SearchResult> PartialMatches;
    }

    public struct SearchResult
    {
        public int Id;
        public int Score;
    }
}