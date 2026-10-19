using System;
using System.IO;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Holds open resources for one term dictionary pair (.dat + .db) — the FTS5 engine's
    /// terms.db/terms.dat, read by the expanders through <see cref="Conn"/>.
    ///
    /// The .dat posting file is read via a plain <see cref="FileStream"/> with a
    /// lock-guarded Seek+Read so that concurrent searches sharing the same handle
    /// do not race on the stream position.  A memory-mapped approach was used
    /// previously but exhausts the 32-bit virtual address space on large files,
    /// producing "Not enough memory resources" errors.
    ///
    /// The FileStream is opened with <c>FileShare.Read | FileShare.Delete</c>:
    ///   - FileShare.Delete allows a rebuild (or another process) to call
    ///     File.Delete on the .dat file while this handle is still open.  On Windows,
    ///     FILE_SHARE_DELETE unlinks the directory entry but the file content remains
    ///     accessible through the existing handle until all handles are closed.
    ///   - FileShare.Read allows multiple concurrent SegmentHandles to open the same
    ///     file simultaneously.
    /// </summary>
    internal sealed class SegmentHandle : IDisposable
    {
        public readonly string DatPath;
        public readonly Microsoft.Data.Sqlite.SqliteConnection Conn;
        public readonly Microsoft.Data.Sqlite.SqliteCommand    Lookup;

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
                    // carry 0) → fall back to LIKE until the next index build
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
                            _trigram?.Dispose();
                            _trigram = null;
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
                // Pooling=false: disposing this SegmentHandle must release the OS file
                // handle to the .db *immediately* so a rebuild can delete or replace it.
                // Microsoft.Data.Sqlite pools by default — a pooled connection keeps the
                // handle open after Dispose, which makes the merge's File.Delete fail with
                // "used by another process".
                Conn = new Microsoft.Data.Sqlite.SqliteConnection(
                    new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                    {
                        DataSource = dbPath,
                        Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                        Pooling = false,
                    }.ConnectionString);
                Conn.Open();
                Lookup = Conn.CreateCommand();
                Lookup.CommandText =
                    "SELECT skip_offset, skip_count, offset, length, count FROM term_index WHERE term = @t";
                Lookup.Parameters.Add("@t", Microsoft.Data.Sqlite.SqliteType.Text);
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
