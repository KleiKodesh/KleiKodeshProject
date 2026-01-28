//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.IO;

//namespace MinimalIndexer
//{
//    internal class BloomFilterCollectionReader : IDisposable
//    {
//        string FiltersPath;
//        string MetaDataPath;
//        FileStream FiltersStream;
//        FileStream MetaDataStream;
//        internal MetaDataModel[] MetaData { get; private set; }
//        internal short ChunkSize { get; private set; }
//        internal int MetaDataCount { get; private set; }

//        private const int METADATA_RECORD_SIZE = 24;
//        private const int METADATA_HEADER_SIZE = 6;

//        private bool isInitialized = false;

//        internal BloomFilterCollectionReader(string id)
//        {
//            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", id);
//            FiltersPath = Path.Combine(dir, "bloom_filters.idx");
//            MetaDataPath = Path.Combine(dir, "metadata.idx");

//            if (!Directory.Exists(dir) || !File.Exists(MetaDataPath) || !File.Exists(FiltersPath))
//                throw new FileNotFoundException("Index files not found. Please create index first.");

//            FiltersStream = new FileStream(FiltersPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1048576);
//            MetaDataStream = new FileStream(MetaDataPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 262144);

//            using (var reader = new BinaryReader(MetaDataStream, System.Text.Encoding.UTF8, leaveOpen: true))
//            {
//                MetaDataCount = reader.ReadInt32();
//                ChunkSize = reader.ReadInt16() is short cs && cs != 0 ? cs : (short)25;
//            }
//        }

//        internal void LoadAllMetaData()
//        {
//            if (isInitialized)
//            {
//                Console.WriteLine("[LoadAllMetaData] Metadata already loaded, skipping...");
//                return;
//            }

//            int totalBytes = MetaDataCount * METADATA_RECORD_SIZE;
//            byte[] buffer = new byte[totalBytes];

//            MetaDataStream.Seek(METADATA_HEADER_SIZE, SeekOrigin.Begin);
//            int bytesRead = 0;
//            while (bytesRead < totalBytes)
//            {
//                int read = MetaDataStream.Read(buffer, bytesRead, totalBytes - bytesRead);
//                if (read == 0) break;
//                bytesRead += read;
//            }

//            var parseStopwatch = Stopwatch.StartNew();

//            MetaData = new MetaDataModel[MetaDataCount];

//            int pos = 0;
//            for (int i = 0; i < MetaDataCount; i++)
//            {
//                MetaData[i] = new MetaDataModel
//                {
//                    Offset = BitConverter.ToInt64(buffer, pos),
//                    Size = BitConverter.ToInt32(buffer, pos + 8),
//                    HashFunctions = BitConverter.ToInt32(buffer, pos + 12),
//                    Id = BitConverter.ToInt32(buffer, pos + 16),
//                    Grouping = BitConverter.ToInt32(buffer, pos + 20)
//                };
//                pos += METADATA_RECORD_SIZE;
//            }

//            parseStopwatch.Stop();
//            isInitialized = true;
//        }

//        internal MetaDataModel GetMetaDataByIndex(int index)
//        {
//            if (index < 0 || index >= MetaDataCount)
//                throw new ArgumentOutOfRangeException(nameof(index), $"Index must be between 0 and {MetaDataCount - 1}");

//            if (isInitialized)
//                return MetaData[index];

//            byte[] buffer = new byte[METADATA_RECORD_SIZE];
//            long position = METADATA_HEADER_SIZE + (index * METADATA_RECORD_SIZE);
//            MetaDataStream.Seek(position, SeekOrigin.Begin);
//            MetaDataStream.Read(buffer, 0, METADATA_RECORD_SIZE);

//            return new MetaDataModel
//            {
//                Offset = BitConverter.ToInt64(buffer, 0),
//                Size = BitConverter.ToInt32(buffer, 8),
//                HashFunctions = BitConverter.ToInt32(buffer, 12),
//                Id = BitConverter.ToInt32(buffer, 16),
//                Grouping = BitConverter.ToInt32(buffer, 20)
//            };
//        }

//        internal List<MetaDataModel> GetMetaDataByGrouping(int grouping)
//        {
//            var results = new List<MetaDataModel>();

//            if (isInitialized)
//            {
//                for (int i = 0; i < MetaData.Length; i++)
//                {
//                    if (MetaData[i].Grouping == grouping)
//                        results.Add(MetaData[i]);
//                }
//                return results;
//            }

//            byte[] buffer = new byte[METADATA_RECORD_SIZE * 10000];
//            MetaDataStream.Seek(METADATA_HEADER_SIZE, SeekOrigin.Begin);

//            int recordsRead = 0;
//            while (recordsRead < MetaDataCount)
//            {
//                int recordsToRead = Math.Min(10000, MetaDataCount - recordsRead);
//                int bytesToRead = recordsToRead * METADATA_RECORD_SIZE;
//                int bytesRead = MetaDataStream.Read(buffer, 0, bytesToRead);

//                int pos = 0;
//                for (int i = 0; i < recordsToRead && pos < bytesRead; i++)
//                {
//                    int grp = BitConverter.ToInt32(buffer, pos + 20);
//                    if (grp == grouping)
//                    {
//                        results.Add(new MetaDataModel
//                        {
//                            Offset = BitConverter.ToInt64(buffer, pos),
//                            Size = BitConverter.ToInt32(buffer, pos + 8),
//                            HashFunctions = BitConverter.ToInt32(buffer, pos + 12),
//                            Id = BitConverter.ToInt32(buffer, pos + 16),
//                            Grouping = grp
//                        });
//                    }
//                    pos += METADATA_RECORD_SIZE;
//                }

//                recordsRead += recordsToRead;
//            }

//            return results;
//        }

//        internal IEnumerable<(int Id, int Grouping, BloomFilter Filter)> GetAllFilters()
//        {
//            if (!isInitialized)
//                LoadAllMetaData();

//            // Read ENTIRE filter file sequentially in one pass
//            FiltersStream.Seek(0, SeekOrigin.Begin);
//            byte[] allFilterData = new byte[FiltersStream.Length];
//            FiltersStream.Read(allFilterData, 0, allFilterData.Length);

//            // Now parse filters from the in-memory buffer
//            for (int i = 0; i < MetaData.Length; i++)
//            {
//                var meta = MetaData[i];
//                int byteSize = (meta.Size + 7) / 8;

//                // Extract from the buffer (no I/O!)
//                byte[] filterData = new byte[byteSize];
//                Array.Copy(allFilterData, meta.Offset, filterData, 0, byteSize);

//                var filter = new BloomFilter(filterData, meta.Size, meta.HashFunctions);
//                yield return (meta.Id, meta.Grouping, filter);
//            }
//        }

//        internal IEnumerable<(int Id, int Grouping, BloomFilter Filter)> GetFiltersByGrouping(int grouping)
//        {
//            var filteredMeta = GetMetaDataByGrouping(grouping);

//            foreach (var meta in filteredMeta)
//            {
//                yield return (meta.Id, meta.Grouping, GetFilter(meta));
//            }
//        }

//        internal BloomFilter GetFilter(MetaDataModel meta)
//        {
//            // CRITICAL FIX: Always seek to offset
//            FiltersStream.Seek(meta.Offset, SeekOrigin.Begin);

//            // CRITICAL: meta.Size is in BITS, convert to BYTES
//            int byteSize = (meta.Size + 7) / 8;
//            byte[] serializedData = new byte[byteSize];

//            int bytesRead = 0;
//            while (bytesRead < byteSize)
//            {
//                int read = FiltersStream.Read(serializedData, bytesRead, byteSize - bytesRead);
//                if (read == 0)
//                    throw new EndOfStreamException($"Could not read filter at offset {meta.Offset}. Read {bytesRead} of {byteSize} bytes.");
//                bytesRead += read;
//            }

//            // Pass bitCount (meta.Size) to constructor, not byteSize
//            return new BloomFilter(serializedData, meta.Size, meta.HashFunctions);
//        }

//        internal BloomFilter GetFilterByIndex(int index)
//        {
//            var meta = GetMetaDataByIndex(index);
//            return GetFilter(meta);
//        }

//        public void Dispose()
//        {
//            FiltersStream?.Dispose();
//            MetaDataStream?.Dispose();
//        }

//        internal struct MetaDataModel
//        {
//            internal long Offset { get; set; }
//            internal int Size { get; set; }
//            internal int HashFunctions { get; set; }
//            internal int Id { get; set; }
//            internal int Grouping { get; set; }
//        }
//    }
//}