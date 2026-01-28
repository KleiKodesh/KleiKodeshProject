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
            Console.WriteLine($"Loaded: {MetaDataCount} in {stopWatch.Elapsed.TotalSeconds} seconds");
        }

        void LoadAllMetaData()
        {
            MetaData = new List<MetaDataModel>(1024);
            using (var fs = new FileStream(MetaDataPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4 * 1024 * 1024, FileOptions.SequentialScan))
            using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8))
            {
                MetaDataCount = reader.ReadInt32();
                ChunkSize = reader.ReadInt16();
                for (int i = 0; i < MetaDataCount; i++)
                {
                    MetaData.Add(new MetaDataModel
                    {
                        Length = reader.ReadInt32(),
                        BitCount = reader.ReadInt32(),      // ADD THIS LINE
                        HashFunctions = reader.ReadInt32(),
                        Id = reader.ReadInt32(),
                        Grouping = reader.ReadInt32()
                    });
                }
            }
        }

        internal void LoadAllFilters()
        {
            Filters = new List<KeyValuePair<int, List<(int, BloomFilter)>>>(1024);

            using (var fs = new FileStream(FiltersPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4 * 1024 * 1024, FileOptions.SequentialScan))
            using (var reader = new BinaryReader(fs))
            {
                foreach (var meta in MetaData)   // disk order preserved
                {
                    var bytes = reader.ReadBytes(meta.Length);
                    var filter = new BloomFilter(bytes, meta.BitCount, meta.HashFunctions);

                    int g = meta.Grouping;
                    var lastIndex = Filters.Count - 1;

                    // Fast path: same group as previous (very common in sequential data)
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


        internal struct MetaDataModel
        {
            internal int Length { get; set; }      // Byte length for reading
            internal int BitCount { get; set; }    // Bit count for filter
            internal int HashFunctions { get; set; }
            internal int Id { get; set; }
            internal int Grouping { get; set; }
        }
    }
}