using System;
using System.IO;

namespace MinimalIndexer
{
    public sealed class BloomFilterCollectionWriter : IDisposable
    {
        private const int FlushThreshold = 10 * 1024 * 1024;
        private const int CacheLineSize = 64;

        private readonly FileStream _fs;
        private readonly MemoryStream _buf;
        private readonly BinaryWriter _w;

        public int Count { get; private set; }

        public BloomFilterCollectionWriter(string id, short chunkSize)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", $"{id}.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536,
                FileOptions.SequentialScan | FileOptions.WriteThrough);

            using (var bw = new BinaryWriter(_fs, System.Text.Encoding.Default, true))
            { bw.Write(0); bw.Write(chunkSize); }

            _buf = new MemoryStream();
            _w = new BinaryWriter(_buf);
        }

        public void Commit(BloomFilter filter)
        {
            var bytes = filter.GetBytes();
            int byteSize = filter.GetByteSize();
            _w.Write(filter.Size);
            _w.Write(filter.HashFunctions);
            _w.Write(bytes, 0, byteSize);

            long entrySize = 8 + byteSize;
            int pad = (int)((CacheLineSize - entrySize % CacheLineSize) % CacheLineSize);
            for (int i = 0; i < pad; i++) _w.Write((byte)0);

            Count++;
            if (_buf.Length >= FlushThreshold) Flush();
        }

        private void Flush() { if (_buf.Length == 0) return; _buf.Position = 0; _buf.CopyTo(_fs); _buf.SetLength(0); }

        public void Dispose()
        {
            Flush();
            _fs.Seek(0, SeekOrigin.Begin);
            using (var bw = new BinaryWriter(_fs, System.Text.Encoding.Default, true)) bw.Write(Count);
            _w.Dispose(); _buf.Dispose(); _fs.Dispose();
        }
    }
}
