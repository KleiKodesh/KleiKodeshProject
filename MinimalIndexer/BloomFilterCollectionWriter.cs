using System;
using System.Collections.Generic;
using System.IO;

namespace MinimalIndexer
{
    internal class BloomFilterCollectionWriter : IDisposable
    {
        BinaryWriter IndexWriter;
        BinaryWriter MetaWriter;
        internal int Count = 0;

        private const int BATCH_SIZE = 5000;
        private const int FILTER_BUFFER_SIZE = 4194304; // 4MB

        private List<MetaDataModel> metadataBuffer = new List<MetaDataModel>(BATCH_SIZE);
        private MemoryStream filterBuffer = new MemoryStream(FILTER_BUFFER_SIZE);

        internal BloomFilterCollectionWriter(string id, short chunkSize)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", id);
            string filtersPath = Path.Combine(dir, "bloom_filters.idx");
            string metaDataPath = Path.Combine(dir, "metadata.idx");
            Directory.CreateDirectory(dir);

            var indexFileStream = new FileStream(
                filtersPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 8192, // Smaller since we're batching manually
                FileOptions.SequentialScan);
            IndexWriter = new BinaryWriter(indexFileStream);

            var metaFileStream = new FileStream(
                metaDataPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 8192,
                FileOptions.SequentialScan);
            MetaWriter = new BinaryWriter(metaFileStream);

            // Write placeholder for count and chunksize
            MetaWriter.Write(0);
            MetaWriter.Write(chunkSize);
            Count = 0;
        }

        internal void Commit(BloomFilter filter, int id, int grouping)
        {
            byte[] filterBytes = filter.GetBytes();

            // Buffer the filter
            if (filterBuffer.Position + filterBytes.Length > FILTER_BUFFER_SIZE)
                FlushFilterBuffer();

            filterBuffer.Write(filterBytes, 0, filterBytes.Length);

            // Buffer the metadata
            metadataBuffer.Add(new MetaDataModel
            {
                Length = filterBytes.Length,
                BitCount = filter.Size,
                HashFunctions = filter.HashFunctions,
                Id = id,
                Grouping = grouping
            });

            Count++;

            if (metadataBuffer.Count >= BATCH_SIZE)
            {
                FlushFilterBuffer();
                FlushMetadataBuffer();
            }
        }

        private void FlushFilterBuffer()
        {
            if (filterBuffer.Position == 0) return;

            byte[] buffer = filterBuffer.GetBuffer();
            IndexWriter.Write(buffer, 0, (int)filterBuffer.Position);
            filterBuffer.Position = 0;
            filterBuffer.SetLength(0);
        }

        private void FlushMetadataBuffer()
        {
            if (metadataBuffer.Count == 0) return;

            foreach (var record in metadataBuffer)
            {
                MetaWriter.Write(record.Length);
                MetaWriter.Write(record.BitCount);
                MetaWriter.Write(record.HashFunctions);
                MetaWriter.Write(record.Id);
                MetaWriter.Write(record.Grouping);
            }
            metadataBuffer.Clear();
        }

        public void Dispose()
        {
            // Flush any remaining data
            FlushFilterBuffer();
            FlushMetadataBuffer();

            if (MetaWriter != null)
            {
                // Update the count at the start of the file
                MetaWriter.Flush();
                MetaWriter.BaseStream.Seek(0, SeekOrigin.Begin);
                MetaWriter.Write(Count);
                MetaWriter.Flush();
                MetaWriter.Dispose();
                MetaWriter = null;
            }

            if (IndexWriter != null)
            {
                IndexWriter.Flush();
                IndexWriter.Dispose();
                IndexWriter = null;
            }

            filterBuffer?.Dispose();
        }

        private struct MetaDataModel
        {
            internal int Length { get; set; }
            internal int BitCount { get; set; }
            internal int HashFunctions { get; set; }
            internal int Id { get; set; }
            internal int Grouping { get; set; }
        }
    }
}