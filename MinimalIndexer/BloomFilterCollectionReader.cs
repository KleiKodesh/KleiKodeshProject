using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace MinimalIndexer
{
    internal class BloomFilterCollectionReader
    {
        string FiltersPath;
        string MetaDataPath;
        internal List<MetaDataModel> MetaData { get; private set; }
        internal List<KeyValuePair<int, List<(int Id, BloomFilter Filter)>>> Filters { get; private set; }
        internal short ChunkSize { get; private set; }
        internal int MetaDataCount { get; private set; }

        internal BloomFilterCollectionReader(string id)
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", id);
            FiltersPath = Path.Combine(dir, "bloom_filters.idx");
            MetaDataPath = Path.Combine(dir, "metadata.idx");

            if (!Directory.Exists(dir) || !File.Exists(MetaDataPath) || !File.Exists(FiltersPath))
                throw new FileNotFoundException("Index files not found. Please create index first.");

            LoadAllMetaData();
            LoadAllFilters();

            stopWatch.Stop();
            Console.WriteLine($"Loaded {id}: {MetaDataCount} filters in {stopWatch.Elapsed.TotalSeconds:F3} seconds");
        }

        void LoadAllMetaData()
        {
            MetaData = new List<MetaDataModel>(1024);
            using (var fs = new FileStream(MetaDataPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4 * 1024 * 1024, FileOptions.SequentialScan))
            using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8))
            {
                MetaDataCount = reader.ReadInt32();
                ChunkSize = reader.ReadInt16();

                long currentOffset = 0;
                for (int i = 0; i < MetaDataCount; i++)
                {
                    var meta = new MetaDataModel
                    {
                        Length = reader.ReadInt32(),
                        BitCount = reader.ReadInt32(),
                        HashFunctions = reader.ReadInt32(),
                        Id = reader.ReadInt32(),
                        Grouping = reader.ReadInt32(),
                        Offset = currentOffset
                    };
                    MetaData.Add(meta);
                    currentOffset += meta.Length;
                }
            }
        }

        internal void LoadAllFilters()
        {
            Filters = new List<KeyValuePair<int, List<(int, BloomFilter)>>>(1024);

            using (var fs = new FileStream(FiltersPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4 * 1024 * 1024, FileOptions.SequentialScan))
            using (var reader = new BinaryReader(fs))
            {
                foreach (var meta in MetaData)
                {
                    var bytes = reader.ReadBytes(meta.Length);
                    var filter = new BloomFilter(bytes, meta.BitCount, meta.HashFunctions);
                    int g = meta.Grouping;
                    var lastIndex = Filters.Count - 1;

                    // Fast path: same group as previous
                    if (lastIndex >= 0 && Filters[lastIndex].Key == g)
                    {
                        Filters[lastIndex].Value.Add((meta.Id, filter));
                    }
                    else
                    {
                        var list = new List<(int, BloomFilter)>(64);
                        list.Add((meta.Id, filter));
                        Filters.Add(new KeyValuePair<int, List<(int, BloomFilter)>>(g, list));
                    }
                }
            }
        }

        /// <summary>
        /// Batch read specific filters by their indices
        /// </summary>
        internal List<(int Id, BloomFilter Filter, int Grouping)> BatchReadFilters(int[] indices)
        {
            if (indices == null || indices.Length == 0)
                return new List<(int, BloomFilter, int)>();

            var results = new List<(int, BloomFilter, int)>(indices.Length);

            // Sort indices to read in sequential order for better disk performance
            Array.Sort(indices);

            using (var fs = new FileStream(FiltersPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 256 * 1024, FileOptions.RandomAccess))
            using (var reader = new BinaryReader(fs))
            {
                foreach (int index in indices)
                {
                    if (index < 0 || index >= MetaDataCount)
                        continue;

                    var meta = MetaData[index];

                    // Seek to the filter's position
                    fs.Seek(meta.Offset, SeekOrigin.Begin);

                    // Read the filter
                    var bytes = reader.ReadBytes(meta.Length);
                    var filter = new BloomFilter(bytes, meta.BitCount, meta.HashFunctions);

                    results.Add((meta.Id, filter, meta.Grouping));
                }
            }

            return results;
        }

        /// <summary>
        /// Batch read filters for specific groupings
        /// </summary>
        internal Dictionary<int, List<(int Id, BloomFilter Filter)>> BatchReadFiltersByGrouping(HashSet<int> groupings)
        {
            if (groupings == null || groupings.Count == 0)
                return new Dictionary<int, List<(int, BloomFilter)>>();

            var results = new Dictionary<int, List<(int, BloomFilter)>>(groupings.Count);

            // Pre-initialize lists for each grouping
            foreach (var grouping in groupings)
            {
                results[grouping] = new List<(int, BloomFilter)>(16);
            }

            using (var fs = new FileStream(FiltersPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4 * 1024 * 1024, FileOptions.SequentialScan))
            using (var reader = new BinaryReader(fs))
            {
                // Read sequentially through metadata
                for (int i = 0; i < MetaDataCount; i++)
                {
                    var meta = MetaData[i];

                    if (groupings.Contains(meta.Grouping))
                    {
                        // Read this filter
                        var bytes = reader.ReadBytes(meta.Length);
                        var filter = new BloomFilter(bytes, meta.BitCount, meta.HashFunctions);
                        results[meta.Grouping].Add((meta.Id, filter));
                    }
                    else
                    {
                        // Skip this filter
                        fs.Seek(meta.Length, SeekOrigin.Current);
                    }
                }
            }

            return results;
        }

        internal struct MetaDataModel
        {
            internal int Length { get; set; }          // Byte length for reading
            internal int BitCount { get; set; }        // Bit count for filter
            internal int HashFunctions { get; set; }   // Number of hash functions
            internal int Id { get; set; }              // Filter ID within grouping
            internal int Grouping { get; set; }        // Grouping ID (bookId for tier1, tier1ChunkId for tier2)
            internal long Offset { get; set; }         // Byte offset in the filters file
        }
    }
}