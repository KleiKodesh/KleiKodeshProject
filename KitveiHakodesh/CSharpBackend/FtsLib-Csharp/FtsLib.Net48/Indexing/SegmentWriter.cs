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
            string       dbPath,
            List<DocSourceRange> docSourceRows = null)
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

                // Highest doc (line) ID in the segment = the max over all terms of
                // each term's LAST posting. Encoded values order exactly like doc
                // IDs (uint bias), so tracking the max encoded value suffices.
                // Recorded in the .db's segment_meta table for build-resume.
                uint maxLastEncoded = 0;
                bool anyPosting     = false;

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

                        if (entry.Stream.Count > 0 &&
                            (!anyPosting || entry.Stream.LastEncoded > maxLastEncoded))
                        {
                            maxLastEncoded = entry.Stream.LastEncoded;
                            anyPosting     = true;
                        }

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

                    // Push file data to stable storage BEFORE the rename below: on
                    // power loss NTFS can persist the rename while the data is still
                    // in the OS cache, and recovery treats a torn segment at a final
                    // path as index-wide corruption (wipe + full rebuild).
                    bw.Flush();
                    fs.Flush(flushToDisk: true);
                }

                long lastDoc = anyPosting ? (long)maxLastEncoded + int.MinValue : long.MinValue;
                WriteMetaDb(tmpDb, meta, lastDoc, docSourceRows);

                // Both files are fully written — rename to their final paths, replacing
                // any stale leftover already there (e.g. an unregistered segment file
                // whose id was re-allocated after an interrupted run's .tmp cleanup).
                // A plain File.Move would throw "Cannot create a file when that file
                // already exists"; the leftover is not a live segment, so replacing
                // it is safe.
                MoveReplace(tmpDat, datPath);
                MoveReplace(tmpDb,  dbPath);
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
        /// Move <paramref name="src"/> to <paramref name="dst"/>, replacing a stale file
        /// already at <paramref name="dst"/> (and its SQLite -wal/-shm sidecars). Behaves
        /// exactly like File.Move when nothing is there — this only rescues the interrupted-
        /// build case where a leftover unregistered file occupies the target path.
        /// </summary>
        internal static void MoveReplace(string src, string dst)
        {
            if (File.Exists(dst))
            {
                File.Delete(dst);
                if (File.Exists(dst + "-wal")) File.Delete(dst + "-wal");
                if (File.Exists(dst + "-shm")) File.Delete(dst + "-shm");
            }
            File.Move(src, dst);
        }

        /// <summary>
        /// Writes a SQLite term-index (.db) file from a pre-built metadata list.
        /// Used by SegmentMerger after writing the merged .dat file.
        ///
        /// <paramref name="lastDocId"/> (when not <see cref="long.MinValue"/>) is
        /// recorded in a small <c>segment_meta</c> table as the segment's highest
        /// doc (line) ID. Build-resume reads it as the source of truth for where
        /// indexing actually got to: the build.progress file LAGS the background
        /// flush and is not crash-atomic, so after a hard kill it can point below
        /// lines already durably flushed — resuming there re-indexes those lines
        /// into a second segment, and overlapping doc ranges silently corrupt
        /// AND-search results. Older segments without the table simply fall back
        /// to the progress file (no worse than before).
        ///
        /// <paramref name="docSourceRows"/> (when non-empty) is persisted as the
        /// <c>doc_source</c> table: which corpus each docId range belongs to and
        /// the affine docId→sourceId rule (see <see cref="DocSourceRange"/>).
        /// Readers of segments without the table fall back to library-identity,
        /// mirroring the segment_meta backward-compat precedent above.
        /// </summary>
        internal static void WriteMetaDb(
            string path,
            List<(string term, long skipOffset, int skipCount, long offset, int length, int count)> rows,
            long lastDocId = long.MinValue,
            List<DocSourceRange> docSourceRows = null)
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
                    "term TEXT NOT NULL,skip_offset INTEGER NOT NULL,skip_count INTEGER NOT NULL," +
                    "offset INTEGER NOT NULL,length INTEGER NOT NULL,count INTEGER NOT NULL);");

                using (var tx  = conn.BeginTransaction())
                using (var ins = conn.CreateCommand())
                {
                    ins.CommandText =
                        "INSERT INTO term_index(term,skip_offset,skip_count,offset,length,count) " +
                        "VALUES(@t,@so,@sc,@o,@l,@c)";
                    var pT  = ins.Parameters.Add("@t",  System.Data.DbType.String);
                    var pSO = ins.Parameters.Add("@so", System.Data.DbType.Int64);
                    var pSC = ins.Parameters.Add("@sc", System.Data.DbType.Int32);
                    var pO  = ins.Parameters.Add("@o",  System.Data.DbType.Int64);
                    var pL  = ins.Parameters.Add("@l",  System.Data.DbType.Int32);
                    var pC  = ins.Parameters.Add("@c",  System.Data.DbType.Int32);
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
                if (lastDocId != long.MinValue)
                {
                    Exec(conn, "CREATE TABLE segment_meta(key TEXT PRIMARY KEY, value INTEGER NOT NULL);");
                    using (var ins = conn.CreateCommand())
                    {
                        ins.CommandText = "INSERT INTO segment_meta(key,value) VALUES('last_doc',@v)";
                        ins.Parameters.Add("@v", System.Data.DbType.Int64).Value = lastDocId;
                        ins.ExecuteNonQuery();
                    }
                }

                if (docSourceRows != null && docSourceRows.Count > 0)
                {
                    Exec(conn,
                        "CREATE TABLE doc_source(" +
                        "doc_lo INTEGER NOT NULL,doc_hi INTEGER NOT NULL," +
                        "source INTEGER NOT NULL,src_lo INTEGER NOT NULL);");
                    using (var tx  = conn.BeginTransaction())
                    using (var ins = conn.CreateCommand())
                    {
                        ins.CommandText =
                            "INSERT INTO doc_source(doc_lo,doc_hi,source,src_lo) VALUES(@dl,@dh,@s,@sl)";
                        var pDL = ins.Parameters.Add("@dl", System.Data.DbType.Int32);
                        var pDH = ins.Parameters.Add("@dh", System.Data.DbType.Int32);
                        var pS  = ins.Parameters.Add("@s",  System.Data.DbType.Int32);
                        var pSL = ins.Parameters.Add("@sl", System.Data.DbType.Int32);
                        foreach (var r in docSourceRows)
                        {
                            pDL.Value = r.DocLo;
                            pDH.Value = r.DocHi;
                            pS.Value  = r.Source;
                            pSL.Value = r.SrcLo;
                            ins.ExecuteNonQuery();
                        }
                        tx.Commit();
                    }
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

            // Same rationale as the .dat fsync in WriteSegment: force the finished
            // .db's data to stable storage before the caller renames it into place.
            using (var f = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                f.Flush(flushToDisk: true);
        }

        private static void Exec(SQLiteConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            { cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        }
    }
}
