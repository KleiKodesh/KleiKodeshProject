using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;

namespace LuceneIndexBenchmark
{
    /// <summary>
    /// Streams rows from the SQLite `line` table in large batches to minimize
    /// round-trip overhead. Uses a single forward-only reader — no Dapper overhead.
    /// </summary>
    public sealed class SqliteLineReader : IDisposable
    {
        private readonly SQLiteConnection _connection;

        public SqliteLineReader(string databasePath)
        {
            _connection = new SQLiteConnection("Data Source=" + databasePath + ";Version=3;Read Only=True;");
            _connection.Open();
        }

        public int GetTotalLineCount()
        {
            using (var command = new SQLiteCommand("SELECT COUNT(*) FROM line", _connection))
            {
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        /// <summary>
        /// Streams all lines as (lineId, content) pairs.
        /// Uses a large page size to keep SQLite busy and minimize C# overhead.
        /// </summary>
        public IEnumerable<(int LineId, string Content)> ReadAllLines(int afterLineId = 0)
        {
            string sql = afterLineId > 0
                ? "SELECT id, content FROM line WHERE id > @afterId ORDER BY id"
                : "SELECT id, content FROM line ORDER BY id";

            using (var command = new SQLiteCommand(sql, _connection))
            {
                command.CommandTimeout = 0; // no timeout for large reads

                if (afterLineId > 0)
                    command.Parameters.AddWithValue("@afterId", afterLineId);

                // Large buffer: SQLite will read ahead
                command.Prepare();

                using (var reader = command.ExecuteReader(CommandBehavior.SequentialAccess))
                {
                    while (reader.Read())
                    {
                        int lineId = reader.GetInt32(0);
                        string content = reader.GetString(1);
                        yield return (lineId, content);
                    }
                }
            }
        }

        public void Dispose()
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}
