using System;
using System.IO;

namespace MinimalIndexer
{
    public sealed class BloomFilterCollectionWriter : IDisposable
    {
        private const int FlushThreshold = 10 * 1024 * 1024;
        // Header layout: int32 count (4) + int16 chunkSize (2) + int32 lastLineId (4) = 10 bytes
        private const int HeaderSize = 10;

        private readonly FileStream _fs;
        private readonly MemoryStream _buf;
        private readonly BinaryWriter _w;

        public int Count { get; private set; }
        public int LastLineId { get; private set; }

        /// <summary>
        /// Opens the writer. If the .dat file already exists and resumeFromCount > 0,
        /// appends to it starting at that chunk count; otherwise creates fresh.
        /// Always opens ReadWrite so Flush() can seek back to patch the header
        /// incrementally — header is always in sync with what's on disk.
        /// </summary>
        public BloomFilterCollectionWriter(string id, short chunkSize, int resumeFromCount = 0, int resumeLastLineId = 0)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BloomFilters", $"{id}.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            bool appending = resumeFromCount > 0 && File.Exists(path);

            if (appending)
            {
                _fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 65536,
                    FileOptions.SequentialScan | FileOptions.WriteThrough);
                Count = resumeFromCount;
                LastLineId = resumeLastLineId;
                _fs.Seek(0, SeekOrigin.End);
            }
            else
            {
                _fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 65536,
                    FileOptions.SequentialScan | FileOptions.WriteThrough);
                Count = 0;
                LastLineId = 0;
                using (var bw = new BinaryWriter(_fs, System.Text.Encoding.Default, leaveOpen: true))
                { bw.Write(0); bw.Write(chunkSize); bw.Write(0); } // count=0, chunkSize, lastLineId=0
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

            Count++;
            LastLineId = lastLineId;
            if (_buf.Length >= FlushThreshold) Flush();
        }

        private void Flush()
        {
            if (_buf.Length == 0) return;

            _buf.Position = 0;
            _buf.CopyTo(_fs);
            _buf.SetLength(0);

            // Patch header: count at offset 0, lastLineId at offset 6.
            // chunkSize at offset 4 is written once on creation and never changes.
            // After this the file is fully self-consistent — a hard kill loses at most
            // the unflushed buffer, but the header always matches what's on disk.
            _fs.Seek(0, SeekOrigin.Begin);
            using (var bw = new BinaryWriter(_fs, System.Text.Encoding.Default, leaveOpen: true))
                bw.Write(Count);       // offset 0: int32 count
            _fs.Seek(6, SeekOrigin.Begin);
            using (var bw = new BinaryWriter(_fs, System.Text.Encoding.Default, leaveOpen: true))
                bw.Write(LastLineId);  // offset 6: int32 lastLineId

            _fs.Seek(0, SeekOrigin.End);
        }

        public void Dispose()
        {
            Flush(); // final flush patches the header
            _w.Dispose(); _buf.Dispose(); _fs.Dispose();
        }
    }
}
