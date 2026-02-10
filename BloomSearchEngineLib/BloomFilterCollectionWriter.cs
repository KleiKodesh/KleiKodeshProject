using System;
using System.IO;

namespace MinimalIndexer
{
    public sealed class BloomFilterCollectionWriter : IDisposable
    {
        private const int FlushThreshold = 10 * 1024 * 1024;
        private const int CacheLineSize = 64; // CPU cache line size for alignment

        private readonly FileStream fileStream;
        private readonly MemoryStream buffer;
        private readonly BinaryWriter writer;
        private long currentPosition;

        public int Count { get; private set; }

        public BloomFilterCollectionWriter(string id, short chunkSize)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters");
            Directory.CreateDirectory(dir);

            fileStream = new FileStream(
                Path.Combine(dir, $"{id}.dat"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                65536,
                FileOptions.SequentialScan | FileOptions.WriteThrough);

            using (var bw = new BinaryWriter(fileStream, System.Text.Encoding.Default, true))
            {
                bw.Write(0);          // filter count (patched on dispose)
                bw.Write(chunkSize);  // chunk size
            }

            currentPosition = 6; // After header (int + short)
            buffer = new MemoryStream();
            writer = new BinaryWriter(buffer);
        }

        public void Commit(BloomFilter filter)
        {
            // Write filter data
            writer.Write(filter.Size);              // 4 bytes
            writer.Write(filter.HashFunctions);     // 4 bytes
            var bytes = filter.GetBytes();
            int byteSize = filter.GetByteSize();
            writer.Write(bytes, 0, byteSize);       // variable bytes

            // Calculate padding for cache line alignment
            long entrySize = 8 + byteSize; // metadata + data
            int padding = CalculatePadding(entrySize);

            // Write padding bytes for alignment (improves cache performance during parallel search)
            if (padding > 0)
            {
                // Write zeros for padding
                for (int i = 0; i < padding; i++)
                    writer.Write((byte)0);
            }

            currentPosition += entrySize + padding;
            Count++;

            if (buffer.Length >= FlushThreshold)
                Flush();
        }

        private int CalculatePadding(long entrySize)
        {
            // Align to cache line boundary to prevent false sharing
            // and improve cache hit rate during parallel search
            long remainder = (entrySize % CacheLineSize);
            return remainder == 0 ? 0 : (int)(CacheLineSize - remainder);
        }

        private void Flush()
        {
            if (buffer.Length == 0)
                return;

            buffer.Position = 0;
            buffer.CopyTo(fileStream);
            buffer.SetLength(0);
        }

        public void Dispose()
        {
            Flush();

            // Patch filter count
            fileStream.Seek(0, SeekOrigin.Begin);
            using (var bw = new BinaryWriter(fileStream, System.Text.Encoding.Default, true))
                bw.Write(Count);

            writer.Dispose();
            buffer.Dispose();
            fileStream.Dispose();
        }
    }
}