using System;
using System.IO;

namespace MinimalIndexer
{
    public sealed class BloomFilterCollectionWriter : IDisposable
    {
        private const int FlushThreshold = 10 * 1024 * 1024;

        private readonly FileStream fileStream;
        private readonly MemoryStream buffer;
        private readonly BinaryWriter writer;

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
                FileOptions.SequentialScan);

            using (var bw = new BinaryWriter(fileStream, System.Text.Encoding.Default, true))
            {
                bw.Write(0);          // filter count (patched on dispose)
                bw.Write(chunkSize);  // authoritative chunk size
            }

            buffer = new MemoryStream();
            writer = new BinaryWriter(buffer);
        }

        public void Commit(BloomFilter filter)
        {
            writer.Write(filter.Size);
            writer.Write(filter.HashFunctions);

            var bytes = filter.GetBytes();
            writer.Write(bytes, 0, filter.GetByteSize());

            Count++;

            if (buffer.Length >= FlushThreshold)
                Flush();
        }

        private void Flush()
        {
            buffer.Position = 0;
            buffer.CopyTo(fileStream);
            buffer.SetLength(0);
        }

        public void Dispose()
        {
            Flush();

            fileStream.Seek(0, SeekOrigin.Begin);
            using (var bw = new BinaryWriter(fileStream, System.Text.Encoding.Default, true))
                bw.Write(Count);

            writer.Dispose();
            buffer.Dispose();
            fileStream.Dispose();
        }
    }
}
