using System;
using System.IO;

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
    /// The .dat posting file is read via a plain <see cref="FileStream"/> with a
    /// lock-guarded Seek+Read so that concurrent searches sharing the same handle
    /// do not race on the stream position.  A memory-mapped approach was used
    /// previously but exhausts the 32-bit virtual address space when the index
    /// contains many large segments, producing "Not enough memory resources" errors.
    ///
    /// The FileStream is opened with <c>FileShare.Read | FileShare.Delete</c>:
    ///   - FileShare.Delete allows the segment merger (or another process) to call
    ///     File.Delete on the .dat file while this handle is still open.  On Windows,
    ///     FILE_SHARE_DELETE unlinks the directory entry but the file content remains
    ///     accessible through the existing handle until all handles are closed.
    ///   - FileShare.Read allows multiple concurrent SegmentHandles to open the same
    ///     file simultaneously.
    ///
    /// Disposal order still matters: <see cref="Search.IndexReader.Dispose"/> disposes
    /// all SegmentHandles before it releases the <see cref="SearchLease"/>, ensuring
    /// file handles are fully closed before the merger's write lock is released.
    /// </summary>
    internal sealed class SegmentHandle : IDisposable
    {
        public readonly string DatPath;
        public readonly System.Data.SQLite.SQLiteConnection Conn;
        public readonly System.Data.SQLite.SQLiteCommand    Lookup;

        // Plain FileStream for positioned reads.
        // ReadBytes() locks _readLock before seeking so concurrent callers are serialised.
        private readonly FileStream _datStream;
        private readonly object     _readLock = new object();

        // Lazily-opened trigram sidecar (seg.tgm). null when absent → callers fall back to
        // SQLite LIKE. Opened once on first access; disposed with the handle.
        private FtsLib.Search.TrigramIndex.Reader _trigram;
        private bool _trigramProbed;
        public FtsLib.Search.TrigramIndex.Reader Trigram
        {
            get
            {
                if (!_trigramProbed)
                {
                    _trigramProbed = true;
                    string p = FtsLib.Search.TrigramIndex.SidecarPath(DatPath);
                    if (File.Exists(p))
                        try { _trigram = new FtsLib.Search.TrigramIndex.Reader(p); } catch { _trigram = null; }

                    // Content-binding check: the sidecar must have been built from
                    // THIS term_index. Timestamps alone don't prove that (a backup
                    // restore can preserve them), and a stale sidecar maps trigram
                    // hits to the wrong rowids — candidate loss that survives the
                    // LIKE confirm. Mismatch (incl. pre-binding sidecars, which
                    // carry 0) → fall back to LIKE until the next force merge
                    // rebuilds the sidecar.
                    if (_trigram != null)
                    {
                        try
                        {
                            using (var cmd = Conn.CreateCommand())
                            {
                                cmd.CommandText =
                                    "SELECT COUNT(*), COALESCE(MAX(rowid),0) FROM term_index";
                                using (var r = cmd.ExecuteReader())
                                {
                                    r.Read();
                                    uint expected = FtsLib.Search.TrigramIndex.ComputeBinding(
                                        r.GetInt64(0), r.GetInt64(1));
                                    if (_trigram.Binding != expected)
                                    {
                                        FtsLog.Write("SegmentHandle.Trigram",
                                            $"sidecar binding mismatch for {Path.GetFileName(p)} — falling back to LIKE");
                                        _trigram.Dispose();
                                        _trigram = null;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            if (_trigram != null) { _trigram.Dispose(); _trigram = null; }
                        }
                    }
                }
                return _trigram;
            }
        }

        public SegmentHandle(string datPath, string dbPath)
        {
            DatPath = datPath;

            _datStream = new FileStream(
                datPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 4096,
                useAsync: false);

            try
            {
                Conn = new System.Data.SQLite.SQLiteConnection(
                    string.Format("Data Source={0};Version=3;Read Only=True;", dbPath));
                Conn.Open();
                Lookup = Conn.CreateCommand();
                Lookup.CommandText =
                    "SELECT skip_offset, skip_count, offset, length, count FROM term_index WHERE term = @t";
                Lookup.Parameters.Add("@t", System.Data.DbType.String);
            }
            catch
            {
                _datStream.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Reads <paramref name="count"/> bytes starting at <paramref name="offset"/>
        /// in the .dat file into <paramref name="buffer"/> beginning at
        /// <paramref name="bufferOffset"/>.
        ///
        /// Thread-safe: concurrent callers are serialised on <c>_readLock</c> so they
        /// do not race on the stream's shared position.
        ///
        /// Returns the number of bytes actually read (always equals
        /// <paramref name="count"/> for a well-formed segment).
        /// </summary>
        public int ReadBytes(long offset, byte[] buffer, int bufferOffset, int count)
        {
            lock (_readLock)
            {
                _datStream.Seek(offset, SeekOrigin.Begin);
                int totalRead = 0;
                while (totalRead < count)
                {
                    int read = _datStream.Read(buffer, bufferOffset + totalRead, count - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }
                return totalRead;
            }
        }

        public void Dispose()
        {
            _trigram?.Dispose();
            Lookup?.Dispose();
            Conn?.Dispose();
            _datStream?.Dispose();
        }
    }
}
