using System;
using System.IO;

namespace MinimalIndexer
{
    internal sealed class BloomFilterCollectionWriter : IDisposable
    {
        private const int FlushThreshold = 10 * 1024 * 1024; // 10 MB

        private readonly FileStream fileStream;
        private readonly MemoryStream buffer;
        private readonly BinaryWriter writer;

        internal int Count { get; private set; }

        internal BloomFilterCollectionWriter(string id, short chunkSize)
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters");
            Directory.CreateDirectory(dir);

            fileStream = new FileStream(
                Path.Combine(dir, $"{id}.dat"),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                65536,
                FileOptions.SequentialScan);

            buffer = new MemoryStream();
            writer = new BinaryWriter(buffer);

            // Header (count patched later)
            writer.Write(0);
            writer.Write(chunkSize);
        }

        internal void Commit(BloomFilter filter)
        {
            writer.Write(filter.Size);
            writer.Write(filter.HashFunctions);

            byte[] bytes = filter.GetBytes();
            writer.Write(bytes, 0, filter.GetByteSize());

            Count++;

            if (buffer.Length >= FlushThreshold)
                FlushBuffer();
        }

        private void FlushBuffer()
        {
            buffer.Seek(0, SeekOrigin.Begin);
            buffer.CopyTo(fileStream);
            buffer.SetLength(0);
        }

        public void Dispose()
        {
            FlushBuffer();

            // Patch count in header
            fileStream.Seek(0, SeekOrigin.Begin);
            using (var bw = new BinaryWriter(fileStream, System.Text.Encoding.Default, leaveOpen: true))
                bw.Write(Count);

            fileStream.Dispose();
            writer.Dispose();
            buffer.Dispose();
        }
    }
}
