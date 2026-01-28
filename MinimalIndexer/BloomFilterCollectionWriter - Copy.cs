//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.IO;

//namespace MinimalIndexer
//{
//    internal class BloomFilterCollectionWriter : IDisposable
//    {
//        BinaryWriter IndexWriter;
//        BinaryWriter MetaWriter;
//        short _chunkSize;
//        string Id;
//        internal int Count = 0;

//        private const int METADATA_BATCH_SIZE = 5000;
//        private List<MetadataRecord> metadataBuffer = new List<MetadataRecord>(METADATA_BATCH_SIZE);
//        private long totalFilterBytes = 0;
//        private int commitCount = 0;

//        // Validation tracking
//        private long expectedFilterFileSize = 0;
//        private int expectedMetadataRecords = 0;

//        private struct MetadataRecord
//        {
//            internal long Offset;
//            internal int Size;
//            internal int HashFunctions;
//            internal int Id;
//            internal int Grouping;
//        }

//        internal BloomFilterCollectionWriter(string id, short chunkSize)
//        {
//            Init(id, chunkSize);
//        }

//        void Init(string id, short chunkSize)
//        {
//            _chunkSize = chunkSize;
//            Id = id;
//            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", id);
//            string filtersPath = Path.Combine(dir, "bloom_filters.idx");
//            string metaDataPath = Path.Combine(dir, "metadata.idx");
//            Directory.CreateDirectory(dir);

//            var indexFileStream = new FileStream(
//                filtersPath,
//                FileMode.Create,
//                FileAccess.Write,
//                FileShare.None,
//                bufferSize: 4194304,
//                FileOptions.SequentialScan);
//            IndexWriter = new BinaryWriter(indexFileStream);

//            var metaFileStream = new FileStream(
//                metaDataPath,
//                FileMode.Create,
//                FileAccess.Write,
//                FileShare.None,
//                bufferSize: 1048576,
//                FileOptions.SequentialScan);
//            MetaWriter = new BinaryWriter(metaFileStream);

//            // Write placeholder for count and chunksize
//            MetaWriter.Write(0);
//            MetaWriter.Write(chunkSize);
//            Count = 0;
//        }

//        internal void Commit(BloomFilter filter, int id, int grouping)
//        {
//            long offset = IndexWriter.BaseStream.Position;

//            byte[] filterBytes = filter.GetBytes();
//            IndexWriter.Write(filterBytes);

//            totalFilterBytes += filterBytes.Length;
//            commitCount++;

//            // Track expected file size
//            expectedFilterFileSize += filterBytes.Length;

//            metadataBuffer.Add(new MetadataRecord
//            {
//                Offset = offset,
//                Size = filter.Size,  // Store bit count (not byte count)
//                HashFunctions = filter.HashFunctions,
//                Id = id,
//                Grouping = grouping
//            });

//            Count++;
//            expectedMetadataRecords++;

//            if (metadataBuffer.Count >= METADATA_BATCH_SIZE)
//            {
//                FlushMetadataBuffer();
//            }
//        }

//        private void FlushMetadataBuffer()
//        {
//            if (metadataBuffer.Count == 0) return;

//            foreach (var record in metadataBuffer)
//            {
//                MetaWriter.Write(record.Offset);
//                MetaWriter.Write(record.Size);
//                MetaWriter.Write(record.HashFunctions);
//                MetaWriter.Write(record.Id);
//                MetaWriter.Write(record.Grouping);
//            }

//            metadataBuffer.Clear();
//        }

//        private bool ValidateWrittenFiles()
//        {
//            Console.WriteLine("\n[Validation] Validating written index files...");
//            var validationStopwatch = Stopwatch.StartNew();
//            bool isValid = true;

//            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", Id);
//            string filtersPath = Path.Combine(dir, "bloom_filters.idx");
//            string metaDataPath = Path.Combine(dir, "metadata.idx");

//            // 1. Check filter file size
//            var filterFileInfo = new FileInfo(filtersPath);
//            if (filterFileInfo.Length != expectedFilterFileSize)
//            {
//                Console.WriteLine($"  [Validation] ERROR: Filter file size mismatch!");
//                Console.WriteLine($"    Expected: {expectedFilterFileSize:N0} bytes ({expectedFilterFileSize / 1024.0 / 1024.0:F2} MB)");
//                Console.WriteLine($"    Actual: {filterFileInfo.Length:N0} bytes ({filterFileInfo.Length / 1024.0 / 1024.0:F2} MB)");
//                Console.WriteLine($"    Difference: {Math.Abs(expectedFilterFileSize - filterFileInfo.Length):N0} bytes");
//                isValid = false;
//            }
//            else
//            {
//                Console.WriteLine($"  [Validation] ✓ Filter file size correct: {filterFileInfo.Length / 1024.0 / 1024.0:F2} MB");
//            }

//            // 2. Check metadata file structure
//            var metaFileInfo = new FileInfo(metaDataPath);
//            const int METADATA_HEADER_SIZE = 6; // 4 bytes count + 2 bytes chunkSize
//            const int METADATA_RECORD_SIZE = 24; // 8+4+4+4+4
//            long expectedMetaSize = METADATA_HEADER_SIZE + (expectedMetadataRecords * METADATA_RECORD_SIZE);

//            if (metaFileInfo.Length != expectedMetaSize)
//            {
//                Console.WriteLine($"  [Validation] ERROR: Metadata file size mismatch!");
//                Console.WriteLine($"    Expected: {expectedMetaSize:N0} bytes ({expectedMetadataRecords:N0} records)");
//                Console.WriteLine($"    Actual: {metaFileInfo.Length:N0} bytes");
//                Console.WriteLine($"    Difference: {Math.Abs(expectedMetaSize - metaFileInfo.Length):N0} bytes");
//                isValid = false;
//            }
//            else
//            {
//                Console.WriteLine($"  [Validation] ✓ Metadata file size correct: {metaFileInfo.Length:N0} bytes");
//            }

//            // 3. Verify metadata header
//            try
//            {
//                using (var fs = new FileStream(metaDataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
//                using (var reader = new BinaryReader(fs))
//                {
//                    int storedCount = reader.ReadInt32();
//                    short storedChunkSize = reader.ReadInt16();

//                    if (storedCount != Count)
//                    {
//                        Console.WriteLine($"  [Validation] ERROR: Metadata count mismatch!");
//                        Console.WriteLine($"    Expected: {Count:N0}");
//                        Console.WriteLine($"    Stored: {storedCount:N0}");
//                        isValid = false;
//                    }
//                    else
//                    {
//                        Console.WriteLine($"  [Validation] ✓ Metadata count correct: {storedCount:N0}");
//                    }

//                    if (storedChunkSize != _chunkSize)
//                    {
//                        Console.WriteLine($"  [Validation] ERROR: Chunk size mismatch!");
//                        Console.WriteLine($"    Expected: {_chunkSize}");
//                        Console.WriteLine($"    Stored: {storedChunkSize}");
//                        isValid = false;
//                    }
//                    else
//                    {
//                        Console.WriteLine($"  [Validation] ✓ Chunk size correct: {storedChunkSize}");
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"  [Validation] ERROR: Failed to read metadata header: {ex.Message}");
//                isValid = false;
//            }

//            // 4. Sample validation - read first and last metadata records
//            try
//            {
//                using (var fs = new FileStream(metaDataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
//                using (var reader = new BinaryReader(fs))
//                {
//                    // Skip header
//                    fs.Seek(METADATA_HEADER_SIZE, SeekOrigin.Begin);

//                    // Read first record
//                    long firstOffset = reader.ReadInt64();
//                    int firstSize = reader.ReadInt32();
//                    int firstHashFunc = reader.ReadInt32();
//                    int firstId = reader.ReadInt32();
//                    int firstGrouping = reader.ReadInt32();

//                    Console.WriteLine($"  [Validation] First metadata record: Offset={firstOffset}, Size={firstSize}, Id={firstId}, Grouping={firstGrouping}");

//                    if (Count > 1)
//                    {
//                        // Seek to last record
//                        fs.Seek(METADATA_HEADER_SIZE + (Count - 1) * METADATA_RECORD_SIZE, SeekOrigin.Begin);
//                        long lastOffset = reader.ReadInt64();
//                        int lastSize = reader.ReadInt32();
//                        int lastHashFunc = reader.ReadInt32();
//                        int lastId = reader.ReadInt32();
//                        int lastGrouping = reader.ReadInt32();

//                        Console.WriteLine($"  [Validation] Last metadata record: Offset={lastOffset}, Size={lastSize}, Id={lastId}, Grouping={lastGrouping}");

//                        // Check if last offset + size matches expected file size
//                        long expectedEndPosition = lastOffset + lastSize;
//                        if (Math.Abs(expectedEndPosition - expectedFilterFileSize) > 100)
//                        {
//                            Console.WriteLine($"  [Validation] WARNING: Last filter position mismatch!");
//                            Console.WriteLine($"    Last offset + size: {expectedEndPosition:N0}");
//                            Console.WriteLine($"    Expected file size: {expectedFilterFileSize:N0}");
//                        }
//                        else
//                        {
//                            Console.WriteLine($"  [Validation] ✓ Last filter position correct");
//                        }
//                    }
//                }
//            }
//            catch (Exception ex)
//            {
//                Console.WriteLine($"  [Validation] ERROR: Failed to validate metadata records: {ex.Message}");
//                isValid = false;
//            }

//            validationStopwatch.Stop();
//            Console.WriteLine($"[Validation] Completed in {validationStopwatch.ElapsedMilliseconds}ms");

//            if (isValid)
//            {
//                Console.WriteLine("[Validation] ✓✓✓ All checks passed! Index files are valid. ✓✓✓\n");
//            }
//            else
//            {
//                Console.WriteLine("[Validation] ✗✗✗ VALIDATION FAILED! Index may be corrupted. ✗✗✗\n");
//            }

//            return isValid;
//        }

//        public void Dispose()
//        {
//            // Flush any remaining metadata
//            FlushMetadataBuffer();

//            // CRITICAL: Validate BEFORE closing streams while we still have access
//            Console.WriteLine("\n[PreValidation] Checking stream positions before closing...");
//            long filterStreamPos = IndexWriter.BaseStream.Position;
//            long metaStreamPos = MetaWriter.BaseStream.Position;

//            Console.WriteLine($"  - Filter stream position: {filterStreamPos:N0} bytes ({filterStreamPos / 1024.0 / 1024.0:F2} MB)");
//            Console.WriteLine($"  - Expected filter size: {expectedFilterFileSize:N0} bytes ({expectedFilterFileSize / 1024.0 / 1024.0:F2} MB)");
//            Console.WriteLine($"  - Metadata stream position: {metaStreamPos:N0} bytes");
//            Console.WriteLine($"  - Expected metadata size: {6 + (expectedMetadataRecords * 24):N0} bytes");
//            Console.WriteLine($"  - Filters committed: {Count:N0}");
//            Console.WriteLine($"  - Expected records: {expectedMetadataRecords:N0}");

//            if (filterStreamPos != expectedFilterFileSize)
//            {
//                Console.WriteLine($"  ✗ ERROR: Filter stream position mismatch! Difference: {Math.Abs(filterStreamPos - expectedFilterFileSize):N0} bytes");
//            }
//            else
//            {
//                Console.WriteLine($"  ✓ Filter stream position correct");
//            }

//            if (MetaWriter != null)
//            {
//                // Update the count at the start of the file
//                MetaWriter.Flush();
//                MetaWriter.BaseStream.Seek(0, SeekOrigin.Begin);
//                MetaWriter.Write(Count);
//                MetaWriter.Flush();
//                MetaWriter.Dispose();
//                MetaWriter = null;
//            }

//            if (IndexWriter != null)
//            {
//                IndexWriter.Flush();
//                IndexWriter.Dispose();
//                IndexWriter = null;
//            }

//            double mbWritten = totalFilterBytes / 1024.0 / 1024.0;
//            Console.WriteLine($"\n[BloomFilterCollectionWriter] Wrote {Count:N0} filters");
//            Console.WriteLine($"  - Total data: {mbWritten:F1} MB");

//            // Validate the written files after closing
//            bool isValid = ValidateWrittenFiles();

//            if (!isValid)
//            {
//                Console.WriteLine("\n!!! WARNING: Index validation failed. You may need to recreate the index. !!!");
//            }
//        }
//    }
//}