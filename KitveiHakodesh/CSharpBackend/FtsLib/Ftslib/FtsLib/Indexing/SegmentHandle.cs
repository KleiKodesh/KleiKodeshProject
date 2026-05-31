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

    /// <summary>Holds open resources for one segment pair (.dat + .db).</summary>
    internal sealed class SegmentHandle : System.IDisposable
    {
        public readonly string         DatPath;
        public readonly System.Data.SQLite.SQLiteConnection Conn;
        public readonly System.Data.SQLite.SQLiteCommand    Lookup;
        public readonly FileStream DataStream;

        /// <summary>
        /// Per-segment delete set. Null when this segment has no deletions.
        /// Set by IndexReader after opening the handle.
        /// </summary>
        public DeleteSet Deletes;

        public SegmentHandle(string datPath, string dbPath)
        {
            DatPath    = datPath;
            DataStream = new FileStream(datPath, FileMode.Open, FileAccess.Read,
                                        FileShare.Read, bufferSize: 64 * 1024);
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
                DataStream.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Lookup?.Dispose();
            Conn?.Dispose();
            DataStream?.Dispose();
        }
    }
}
