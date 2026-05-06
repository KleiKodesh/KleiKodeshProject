using System;
using System.Buffers;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;

namespace FtsLib.Indexing
{
    /// <summary>
    /// Static I/O helpers for writing segment files.
    ///
    /// Owns two operations:
    ///   WriteSegment  — serialises a RamIndex to a .dat posting file and its
    ///                   companion .db SQLite term-index file.
    ///   WriteMetaDb   — writes (or rewrites) only the .db term-index file from
    ///                   a pre-built metadata list; used by SegmentMerger when
    ///                   producing a merged segment.
    ///
    /// Both methods are stateless and safe to call from any thread.
    /// </summary>
    internal static class SegmentWriter
    {
        /// <summary>
        /// Writes a RamIndex to a new segment pair (.dat + .db).
        /// <paramref name="sortedTerms"/> must be the terms from
        /// <paramref name="ramIndex"/> sorted with <see cref="StringComparer.Ordinal"/>.
        ///
        /// Writes to .tmp files first, then renames atomically so a crash mid-write
        /// never leaves a corrupt file at the final path.
        /// </summary>
        internal static void WriteSegment(
            RamIndex     ramIndex,
            List<string> sortedTerms,
            string       datPath,
            string       dbPath)
        {
            string tmpDat = datPath + ".tmp";
            string tmpDb  = dbPath  + ".tmp";

            // Clean up any leftover .tmp files from a previous crash.
            if (File.Exists(tmpDat)) File.Delete(tmpDat);
            if (File.Exists(tmpDb))  File.Delete(tmpDb);

            try
            {
                var meta = new List<(string term, long offset, int length, int count)>(sortedTerms.Count);

                using (var fs = new FileStream(tmpDat, FileMode.Create,
                                               FileAccess.Write, FileShare.None,
                                               bufferSize: 4 * 1024 * 1024))
                using (var bw = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: false))
                {
                    foreach (var term in sortedTerms)
                    {
                        var    entry       = ramIndex[term];
                        int    termByteLen = Encoding.UTF8.GetByteCount(term);
                        byte[] termBytes   = ArrayPool<byte>.Shared.Rent(termByteLen);
                        Encoding.UTF8.GetBytes(term, 0, term.Length, termBytes, 0);

                        byte[] postBuf = entry.Stream.Buffer;
                        int    postLen = entry.Stream.ByteLength;

                        bw.Write(termByteLen);
                        bw.Write(termBytes, 0, termByteLen);
                        bw.Write(postLen);
                        bw.Write(entry.Stream.Count);
                        bw.Write(entry.Stream.LastEncoded);
                        bw.Flush();

                        long off = fs.Position; // offset of posting data, after the header
                        fs.Write(postBuf, 0, postLen);

                        meta.Add((term, off, postLen, entry.Stream.Count));

                        ArrayPool<byte>.Shared.Return(termBytes);
                    }
                }

                WriteMetaDb(tmpDb, meta);

                // Both files are fully written — rename atomically to final paths.
                File.Move(tmpDat, datPath);
                File.Move(tmpDb,  dbPath);
            }
            catch
            {
                // Clean up partial .tmp files so recovery does not see them.
                try { if (File.Exists(tmpDat)) File.Delete(tmpDat); } catch { }
                try { if (File.Exists(tmpDb))  File.Delete(tmpDb);  } catch { }
                throw;
            }
        }

        /// <summary>
        /// Writes a SQLite term-index (.db) file from a pre-built metadata list.
        /// Used by SegmentMerger after writing the merged .dat file.
        /// </summary>
        internal static void WriteMetaDb(
            string path,
            List<(string term, long offset, int length, int count)> rows)
        {
            string connStr = $"Data Source={path};Version=3;Page Size=65536;Cache Size=8000;";
            using (var conn = new SQLiteConnection(connStr))
            {
                conn.Open();
                Exec(conn,
                    "PRAGMA journal_mode=WAL;PRAGMA synchronous=NORMAL;" +
                    "PRAGMA temp_store=MEMORY;PRAGMA mmap_size=1073741824;");
                Exec(conn,
                    "CREATE TABLE term_index(" +
                    "term TEXT NOT NULL,offset INTEGER NOT NULL," +
                    "length INTEGER NOT NULL,count INTEGER NOT NULL);");

                using (var tx  = conn.BeginTransaction())
                using (var ins = conn.CreateCommand())
                {
                    ins.CommandText =
                        "INSERT INTO term_index(term,offset,length,count) VALUES(@t,@o,@l,@c)";
                    var pT = ins.Parameters.Add("@t", System.Data.DbType.String);
                    var pO = ins.Parameters.Add("@o", System.Data.DbType.Int64);
                    var pL = ins.Parameters.Add("@l", System.Data.DbType.Int32);
                    var pC = ins.Parameters.Add("@c", System.Data.DbType.Int32);
                    foreach (var (term, off, len, cnt) in rows)
                    {
                        pT.Value = term; pO.Value = off; pL.Value = len; pC.Value = cnt;
                        ins.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
                Exec(conn, "CREATE UNIQUE INDEX idx_term ON term_index(term);ANALYZE;");
            }
        }

        private static void Exec(SQLiteConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }
    }
}
