using System.Data.SQLite;

namespace FtsEngine.Core
{
    /// <summary>
    /// Writes term metadata (term → offset, length, count) into index.db during the merge phase.
    ///
    /// Uses a single transaction for the entire bulk load:
    ///   - journal_mode=OFF + synchronous=OFF → no fsync, no WAL overhead during insert
    ///   - One BEGIN / one COMMIT for all 1.4M rows
    ///   - UNIQUE INDEX built after all rows are inserted (not maintained during insert)
    /// </summary>
    internal sealed class TermDictionary : System.IDisposable
    {
        private readonly SQLiteConnection  _conn;
        private readonly SQLiteTransaction _tx;
        private readonly SQLiteCommand     _ins;

        public TermDictionary(string indexDbPath)
        {
            var connStr = $"Data Source={indexDbPath};Version=3;Page Size=65536;Cache Size=8000;";
            _conn = new SQLiteConnection(connStr);
            _conn.Open();

            Exec("PRAGMA journal_mode=OFF;" +
                 "PRAGMA synchronous=OFF;" +
                 "PRAGMA temp_store=MEMORY;" +
                 "PRAGMA mmap_size=1073741824;");

            Exec("CREATE TABLE term_index (" +
                 "  term    TEXT    NOT NULL," +
                 "  offset  INTEGER NOT NULL," +
                 "  length  INTEGER NOT NULL," +
                 "  count   INTEGER NOT NULL" +
                 ");");

            // Single transaction for the entire bulk load
            _tx  = _conn.BeginTransaction();
            _ins = _conn.CreateCommand();
            _ins.CommandText =
                "INSERT INTO term_index (term, offset, length, count) VALUES (@t, @o, @l, @c)";
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

            // Build index after all rows are loaded — far faster than maintaining during insert
            Exec("CREATE UNIQUE INDEX idx_term ON term_index (term); ANALYZE;");

            // Restore safe settings for readers
            Exec("PRAGMA synchronous=NORMAL; PRAGMA journal_mode=WAL;");
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
