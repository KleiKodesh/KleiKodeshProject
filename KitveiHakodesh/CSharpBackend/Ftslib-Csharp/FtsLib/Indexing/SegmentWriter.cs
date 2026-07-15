using System;
using System.Buffers;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
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
        // Each skip entry is 3 × int32 = 12 bytes: docId, byteOffset, prevEncoded.
        private const int SkipEntryBytes = 12;

        /// <summary>
        /// Writes a RamIndex to a new segment pair (.dat + .db).
        /// <paramref name="sortedTerms"/> must be the terms from
        /// <paramref name="ramIndex"/> sorted with <see cref="StringComparer.Ordinal"/>.
        ///
        /// Writes to .tmp files first, then renames atomically so a crash mid-write
        /// never leaves a corrupt file at the final path.
        ///
        /// Per-term record layout in .dat:
        ///   4 bytes  int    termByteLen
        ///   N bytes         term (UTF-8)
        ///   4 bytes  int    chunkByteLen
        ///   4 bytes  int    docCount
        ///   4 bytes  uint   lastEncoded
        ///   4 bytes  int    skipCount
        ///   skipCount × 12 bytes  skip table (int32 docId, int32 byteOffset, int32 prevEncoded)
        ///   M bytes         varint posting data
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
            // Also clean up any SQLite WAL sidecar .tmp files.
            if (File.Exists(tmpDb + "-shm")) File.Delete(tmpDb + "-shm");
            if (File.Exists(tmpDb + "-wal")) File.Delete(tmpDb + "-wal");

            try
            {
                var meta = new List<(string term, long skipOffset, int skipCount, long offset, int length, int count)>(sortedTerms.Count);

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

                        byte[] postBuf   = entry.Stream.Buffer;
                        int    postLen   = entry.Stream.ByteLength;
                        int    skipCount = entry.SkipLen / 3;

                        bw.Write(termByteLen);
                        bw.Write(termBytes, 0, termByteLen);
                        bw.Write(postLen);
                        bw.Write(entry.Stream.Count);
                        bw.Write(entry.Stream.LastEncoded);
                        bw.Write(skipCount);
                        bw.Flush();

                        // Write skip table — each entry is 3 × int32.
                        long skipOff = fs.Position;
                        for (int i = 0; i < entry.SkipLen; i++)
                            bw.Write(entry.Skip[i]);
                        bw.Flush();

                        long postOff = fs.Position; // offset of posting data
                        fs.Write(postBuf, 0, postLen);

                        meta.Add((term, skipOff, skipCount, postOff, postLen, entry.Stream.Count));

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
            List<(string term, long skipOffset, int skipCount, long offset, int length, int count)> rows)
        {
            // Pooling=False: these segment .db files are written once then File.Move'd to their
            // final name. With pooling on, Microsoft.Data.Sqlite keeps the file handle open after
            // Dispose, so the subsequent File.Move fails with a sharing violation on .NET (Core/10).
            string connStr = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString;
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                // page_size + cache_size were connection-string options under System.Data.SQLite;
                // Microsoft.Data.Sqlite needs them as PRAGMAs, set BEFORE any table/WAL write so the
                // 64 KB page size is baked into the fresh segment .db (preserves the on-disk format).
                Exec(conn, "PRAGMA page_size=65536;PRAGMA cache_size=8000;");
                Exec(conn,
                    "PRAGMA journal_mode=WAL;PRAGMA synchronous=NORMAL;" +
                    "PRAGMA temp_store=MEMORY;PRAGMA mmap_size=1073741824;");
                Exec(conn,
                    "CREATE TABLE term_index(" +
                    "term TEXT NOT NULL,skip_offset INTEGER NOT NULL,skip_count INTEGER NOT NULL," +
                    "offset INTEGER NOT NULL,length INTEGER NOT NULL,count INTEGER NOT NULL);");

                using (var tx  = conn.BeginTransaction())
                using (var ins = conn.CreateCommand())
                {
                    ins.CommandText =
                        "INSERT INTO term_index(term,skip_offset,skip_count,offset,length,count) " +
                        "VALUES(@t,@so,@sc,@o,@l,@c)";
                    var pT  = ins.Parameters.Add("@t",  SqliteType.Text);
                    var pSO = ins.Parameters.Add("@so", SqliteType.Integer);
                    var pSC = ins.Parameters.Add("@sc", SqliteType.Integer);
                    var pO  = ins.Parameters.Add("@o",  SqliteType.Integer);
                    var pL  = ins.Parameters.Add("@l",  SqliteType.Integer);
                    var pC  = ins.Parameters.Add("@c",  SqliteType.Integer);
                    foreach (var (term, skipOff, skipCnt, off, len, cnt) in rows)
                    {
                        pT.Value  = term;
                        pSO.Value = skipOff;
                        pSC.Value = skipCnt;
                        pO.Value  = off;
                        pL.Value  = len;
                        pC.Value  = cnt;
                        ins.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
                Exec(conn, "CREATE UNIQUE INDEX idx_term ON term_index(term);ANALYZE;");

                // Checkpoint the WAL and switch back to DELETE journal mode so no
                // -shm / -wal sidecar files are left next to the .db file.
                // This is critical for crash-safety: stale .db-wal files left from a
                // previous session can corrupt reads if the WAL is partially applied.
                Exec(conn, "PRAGMA wal_checkpoint(TRUNCATE);PRAGMA journal_mode=DELETE;");
            }

            // Belt-and-suspenders: delete any SQLite WAL sidecar files that may have
            // been left even after the checkpoint above (e.g. on Windows when another
            // handle is transiently open).
            try { if (File.Exists(path + "-shm")) File.Delete(path + "-shm"); } catch { }
            try { if (File.Exists(path + "-wal")) File.Delete(path + "-wal"); } catch { }
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }
    }
}
