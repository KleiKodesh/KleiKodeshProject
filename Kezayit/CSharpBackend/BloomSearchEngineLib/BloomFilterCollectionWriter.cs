using System;
using System.IO;

namespace MinimalIndexer
{
    public sealed class BloomFilterCollectionWriter : IDisposable
    {
        private const int FlushThreshold = 10 * 1024 * 1024;
        private const int CacheLineSize = 64;
        // Header layout: int32 count (4 bytes) + int16 chunkSize (2 bytes) = 6 bytes
        private const int HeaderSize = 6;

        private readonly FileStream _fs;
        private readonly MemoryStream _buf;
        private readonly BinaryWriter _w;

        public int Count { get; private set; }

        /// <summary>
        /// Opens the writer. If the .dat file already exists and resumeFromCount > 0,
        /// appends to it starting at that chunk count; otherwise creates fresh.
        /// </summary>
        public BloomFilterCollectionWriter(string id, short chunkSize, int resumeFromCount = 0)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", $"{id}.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            bool appending = resumeFromCount > 0 && File.Exists(path);

            if (appending)
            {
                // Open for read+write so we can patch the count header at the end
                _fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 65536,
                    FileOptions.SequentialScan | FileOptions.WriteThrough);
                Count = resumeFromCount;
                _fs.Seek(0, SeekOrigin.End); // append after existing chunks
            }
            else
            {
                _fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536,
                    FileOptions.SequentialScan | FileOptions.WriteThrough);
                Count = 0;
                using (var bw = new BinaryWriter(_fs, System.Text.Encoding.Default, true))
                { bw.Write(0); bw.Write(chunkSize); }
            }

            _buf = new MemoryStream();
            _w = new BinaryWriter(_buf);
        }

        public void Commit(BloomFilter filter, int firstLineId, int lastLineId)
        {
            var bytes = filter.GetBytes();
            int byteSize = filter.GetByteSize();
            _w.Write(filter.Size);
            _w.Write(filter.HashFunctions);
            _w.Write(firstLineId);
            _w.Write(lastLineId);
            _w.Write(bytes, 0, byteSize);

            long entrySize = 16 + byteSize;
            int pad = (int)((CacheLineSize - entrySize % CacheLineSize) % CacheLineSize);
            for (int i = 0; i < pad; i++) _w.Write((byte)0);

            Count++;
            if (_buf.Length >= FlushThreshold) Flush();
        }

        private void Flush() { if (_buf.Length == 0) return; _buf.Position = 0; _buf.CopyTo(_fs); _buf.SetLength(0); }

        public void Dispose()
        {
            Flush();
            // Patch the count in the header (works for both create and append modes)
            _fs.Seek(0, SeekOrigin.Begin);
            using (var bw = new BinaryWriter(_fs, System.Text.Encoding.Default, true)) bw.Write(Count);
            _w.Dispose(); _buf.Dispose(); _fs.Dispose();
        }
    }
}
