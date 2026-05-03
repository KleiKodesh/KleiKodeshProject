using System.Data.SQLite;

namespace FtsEngine.Core
{
    /// <summary>
    /// Writes term metadata (term → offset, length, count) into index.db during the merge phase.
    /// Uses the same optimised SQLite settings as DiskIndexWriter:
    ///   - WAL + synchronous=NORMAL
    ///   - Plain rowid table during bulk insert
    ///   - Unique index built after all rows are inserted
    /// </summary>
    internal sealed class TermDictionary : System.IDisposable
    {
        private readonly SQLiteConnection _conn;
        private readonly SQLiteTransaction _tx;
        private readonly SQLiteCommand _ins;

        public TermDictionary(string indexDbPath)
        {
            var connStr =
                $"Data Source={indexDbPath};Version=3;" +
                $"Page Size=65536;Cache Size=8000;";

            _conn = new SQLiteConnection(connStr);
            _conn.Open();

            Exec("PRAGMA journal_mode=WAL;" +
                 "PRAGMA synchronous=NORMAL;" +
                 "PRAGMA temp_store=MEMORY;" +
                 "PRAGMA mmap_size=1073741824;");

            Exec("CREATE TABLE term_index (" +
                 "  term    TEXT    NOT NULL," +
                 "  offset  INTEGER NOT NULL," +
                 "  length  INTEGER NOT NULL," +
                 "  count   INTEGER NOT NULL" +
                 ");");

            _tx  = _conn.BeginTransaction();
            _ins = _conn.CreateCommand();
            _ins.CommandText =
                "INSERT INTO term_index (term, offset, length, count) " +
                "VALUES (@t, @o, @l, @c)";
            _ins.Parameters.Add("@t", System.Data.DbType.String);
            _ins.Parameters.Add("@o", System.Data.DbType.Int64);
            _ins.Parameters.Add("@l", System.Data.DbType.Int32);
            _ins.Parameters.Add("@c", System.Data.DbType.Int32);
        }

        public void Add(string term, long offset, int length, int count)
        {
            _ins.Parameters["@t"].Value = term;
            _ins.Parameters["@o"].Value = offset;
            _ins.Parameters["@l"].Value = length;
            _ins.Parameters["@c"].Value = count;
            _ins.ExecuteNonQuery();
        }

        public void Commit()
        {
            _tx.Commit();
            Exec("CREATE UNIQUE INDEX idx_term ON term_index (term); ANALYZE;");
        }

        private void Exec(string sql)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            _ins?.Dispose();
            _tx?.Dispose();
            _conn?.Dispose();
        }
    }
}
