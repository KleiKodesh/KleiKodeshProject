using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace FtsLib.Indexing
{
    /// <summary>Location of one term's posting data within a segment.</summary>
    internal sealed class SegmentChunk
    {
        public readonly SegmentHandle Seg;
        /// <summary>Byte offset of the skip table in the .dat file (0 when no skip table).</summary>
        public readonly long SkipOffset;
        /// <summary>Number of skip entries (triplets). 0 means no skip table.</summary>
        public readonly int  SkipCount;
        /// <summary>Byte offset of the posting data in the .dat file.</summary>
        public readonly long Offset;
        public readonly int  Length;
        public readonly int  Count;

        public SegmentChunk(SegmentHandle seg, long skipOffset, int skipCount,
                            long offset, int length, int count)
        {
            Seg        = seg;
            SkipOffset = skipOffset;
            SkipCount  = skipCount;
            Offset     = offset;
            Length     = length;
            Count      = count;
        }
    }

    /// <summary>
    /// Holds open resources for one segment pair (.dat + .db).
    ///
    /// The .dat posting file is mapped into the process's virtual address space via
    /// <see cref="MemoryMappedFile"/>. Random-access reads for individual posting
    /// chunks are served directly from the OS page cache rather than through a
    /// buffered <see cref="FileStream"/>, avoiding kernel transitions and copy overhead
    /// on every seek+read pair.
    ///
    /// Disposal order matters on Windows: the <see cref="MemoryMappedViewAccessor"/>
    /// and <see cref="MemoryMappedFile"/> must both be disposed before
    /// <see cref="File.Delete"/> is called on the .dat file.  The existing
    /// <see cref="SearchLease"/> / <see cref="System.Threading.ReaderWriterLockSlim"/>
    /// protocol already guarantees this: <see cref="Search.IndexReader.Dispose"/>
    /// disposes all SegmentHandles (unmapping the file) before it releases the lease,
    /// and the merger only calls <see cref="File.Delete"/> after it has acquired the
    /// exclusive write lock — which it cannot acquire until the last lease is released.
    /// </summary>
    internal sealed class SegmentHandle : IDisposable
    {
        public readonly string DatPath;
        public readonly System.Data.SQLite.SQLiteConnection Conn;
        public readonly System.Data.SQLite.SQLiteCommand    Lookup;

        // Memory-mapped view of the entire .dat file.
        // ReadBytes() reads from this instead of a FileStream.
        private readonly MemoryMappedFile         _mmapFile;
        private readonly MemoryMappedViewAccessor _mmapView;

        public SegmentHandle(string datPath, string dbPath)
        {
            DatPath = datPath;

            // Map the entire .dat file read-only.  MemoryMappedFileAccess.Read
            // opens the underlying file with FileAccess.Read + FileShare.Read,
            // matching the previous FileStream flags.
            _mmapFile = MemoryMappedFile.CreateFromFile(
                datPath,
                FileMode.Open,
                mapName: null,          // anonymous — no named mapping needed
                capacity: 0,            // 0 = use the file's current size
                access: MemoryMappedFileAccess.Read);

            _mmapView = _mmapFile.CreateViewAccessor(
                offset: 0,
                size: 0,                // 0 = map the entire file
                access: MemoryMappedFileAccess.Read);

            try
            {
                Conn = new System.Data.SQLite.SQLiteConnection(
                    $"Data Source={dbPath};Version=3;Read Only=True;");
                Conn.Open();
                Lookup = Conn.CreateCommand();
                Lookup.CommandText =
                    "SELECT skip_offset, skip_count, offset, length, count FROM term_index WHERE term = @t";
                Lookup.Parameters.Add("@t", System.Data.DbType.String);
            }
            catch
            {
                _mmapView.Dispose();
                _mmapFile.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Reads <paramref name="count"/> bytes starting at <paramref name="offset"/>
        /// in the .dat file into <paramref name="buffer"/> beginning at
        /// <paramref name="bufferOffset"/>.
        ///
        /// Returns the number of bytes actually read (always equals
        /// <paramref name="count"/> for a well-formed segment).
        /// </summary>
        public int ReadBytes(long offset, byte[] buffer, int bufferOffset, int count)
            => _mmapView.ReadArray(offset, buffer, bufferOffset, count);

        public void Dispose()
        {
            Lookup?.Dispose();
            Conn?.Dispose();
            // Unmap BEFORE the file handle is closed so Windows can later delete the file.
            _mmapView?.Dispose();
            _mmapFile?.Dispose();
        }
    }
}
