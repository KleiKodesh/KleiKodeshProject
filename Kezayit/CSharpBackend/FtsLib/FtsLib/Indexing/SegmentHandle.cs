using System.IO;

namespace FtsLib.Indexing
{
    /// <summary>Location of one term's posting data within a segment.</summary>
    internal sealed class SegmentChunk
    {
        public readonly SegmentHandle Seg;
        public readonly long          Offset;
        public readonly int           Length;
        public readonly int           Count;

        public SegmentChunk(SegmentHandle seg, long offset, int length, int count)
        { Seg = seg; Offset = offset; Length = length; Count = count; }
    }

    /// <summary>Holds open resources for one segment pair (.dat + .db).</summary>
    internal sealed class SegmentHandle : System.IDisposable
    {
        public readonly string         DatPath;
        public readonly System.Data.SQLite.SQLiteConnection Conn;
        public readonly System.Data.SQLite.SQLiteCommand    Lookup;
        public readonly FileStream DataStream;

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
                    "SELECT offset, length, count FROM term_index WHERE term = @t";
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
