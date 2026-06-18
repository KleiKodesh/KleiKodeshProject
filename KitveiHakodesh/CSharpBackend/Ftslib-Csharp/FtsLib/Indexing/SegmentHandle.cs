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
    /// The underlying FileStream is opened with <c>FileShare.Read | FileShare.Delete</c>
    /// so that another process (or the segment merger in this process) can call
    /// <see cref="File.Delete"/> on the .dat file while this mapping is still alive.
    /// On Windows, <c>FILE_SHARE_DELETE</c> unlinks the file from the directory entry
    /// but the memory-mapped content remains accessible through the existing handle
    /// until all handles are closed and the mapping is released.  Without this flag
    /// a concurrent delete attempt throws "The process cannot access the file because
    /// it is being used by another process".
    ///
    /// Disposal order still matters: <see cref="Search.IndexReader.Dispose"/> disposes
    /// all SegmentHandles (unmapping the .dat file) before it releases the
    /// <see cref="SearchLease"/>.  This ensures that the file handle is fully closed
    /// before the merger's write lock is released and any subsequent file cleanup runs.
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

            // Map the entire .dat file read-only.
            //
            // FileShare.ReadWrite | FileShare.Delete is required for two reasons:
            //
            //   1. FileShare.Delete — allows another process (or the merger in this
            //      process) to call File.Delete on the .dat file while this mapping
            //      is still open.  On Windows, File.Delete with FILE_SHARE_DELETE
            //      marks the file for deletion but keeps it accessible via the existing
            //      handle until all handles are closed.  Without FileShare.Delete, any
            //      File.Delete attempt on an open memory-mapped file throws
            //      "The process cannot access the file because it is being used by
            //      another process" — the exact error observed during concurrent
            //      searches and merges, especially when a second app instance has the
            //      same segment files open.
            //
            //   2. FileShare.Read — allows concurrent readers (other SegmentHandles
            //      opened by other searches) to map the same file simultaneously.
            //
            // The MemoryMappedFile.CreateFromFile overload that accepts a FileStream
            // is used here so we can specify the exact FileShare flags.  The
            // MemoryMappedFileAccess.Read access mode ensures the mapping itself is
            // read-only regardless of the underlying FileStream access flags.
            var fileStream = new FileStream(
                datPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);

            try
            {
                _mmapFile = MemoryMappedFile.CreateFromFile(
                    fileStream,
                    mapName: null,
                    capacity: 0,
                    access: MemoryMappedFileAccess.Read,
                    inheritability: System.IO.HandleInheritability.None,
                    leaveOpen: false);   // MemoryMappedFile owns and closes the stream
            }
            catch
            {
                fileStream.Dispose();
                throw;
            }

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
