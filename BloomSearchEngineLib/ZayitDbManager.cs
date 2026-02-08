using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;

internal sealed class ZayitDbManager : IDisposable
{
    readonly SQLiteConnection _connection;
    internal IDbConnection Connection => _connection;

    internal ZayitDbManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dbPath = Path.Combine(
            appData,
            "io.github.kdroidfilter.seforimapp",
            "databases",
            "seforim.db"
        );
        var connectionString = $"Data Source={dbPath};Version=3;Cache Size=10000;Page Size=4096;";
        _connection = new SQLiteConnection(connectionString);
        _connection.Open();
    }

    /// <summary>
    /// Gets the current chunk size stored in the database, or null if not initialized.
    /// </summary>
    internal int? GetCurrentChunkSize()
    {
        using (var cmd = _connection.CreateCommand())
        {
            // Check if metadata table exists
            cmd.CommandText = @"
                SELECT name FROM sqlite_master 
                WHERE type='table' AND name='bloom_metadata'";

            if (cmd.ExecuteScalar() == null)
                return null;

            // Get chunk size from metadata
            cmd.CommandText = "SELECT chunk_size FROM bloom_metadata LIMIT 1";
            var result = cmd.ExecuteScalar();
            return result != null ? (int)(long)result : (int?)null;
        }
    }

    /// <summary>
    /// Checks if chunk_id column exists and matches the desired chunk size.
    /// </summary>
    internal bool NeedsChunkIndexUpdate(int desiredChunkSize)
    {
        var currentChunkSize = GetCurrentChunkSize();
        return currentChunkSize == null || currentChunkSize.Value != desiredChunkSize;
    }

    /// <summary>
    /// One-time/update setup: adds/updates chunk_id column and index for fast chunk retrieval.
    /// Recreates the index if chunk size has changed.
    /// </summary>
    internal void InitializeChunkIndex(int chunkSize, Action<int, int> progressCallback = null)
    {
        using (var transaction = _connection.BeginTransaction())
        {
            try
            {
                using (var cmd = _connection.CreateCommand())
                {
                    // Create metadata table if it doesn't exist
                    cmd.CommandText = @"
                        CREATE TABLE IF NOT EXISTS bloom_metadata (
                            id INTEGER PRIMARY KEY CHECK (id = 1),
                            chunk_size INTEGER NOT NULL
                        )";
                    cmd.ExecuteNonQuery();

                    // Check if chunk_id column exists
                    cmd.CommandText = "PRAGMA table_info(line)";
                    bool columnExists = false;
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            if (reader.GetString(1) == "chunk_id")
                            {
                                columnExists = true;
                                break;
                            }
                        }
                    }

                    // Add column if it doesn't exist
                    if (!columnExists)
                    {
                        cmd.CommandText = "ALTER TABLE line ADD COLUMN chunk_id INTEGER";
                        cmd.ExecuteNonQuery();
                    }

                    // Get total rows for progress tracking
                    cmd.CommandText = "SELECT COUNT(*) FROM line";
                    int totalRows = (int)(long)cmd.ExecuteScalar();

                    // Update chunk_id in batches with progress reporting
                    const int batchSize = 50000;
                    int processedRows = 0;

                    while (processedRows < totalRows)
                    {
                        cmd.CommandText = $@"
                            UPDATE line 
                            SET chunk_id = (id / {chunkSize})
                            WHERE id >= {processedRows} AND id < {processedRows + batchSize}";
                        cmd.ExecuteNonQuery();

                        processedRows += batchSize;
                        int actualProcessed = Math.Min(processedRows, totalRows);
                        progressCallback?.Invoke(actualProcessed, totalRows);
                    }

                    // Drop old index if it exists
                    cmd.CommandText = "DROP INDEX IF EXISTS idx_line_chunk_id";
                    cmd.ExecuteNonQuery();

                    // Create new index
                    cmd.CommandText = "CREATE INDEX idx_line_chunk_id ON line(chunk_id)";
                    cmd.ExecuteNonQuery();

                    // Update metadata
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO bloom_metadata (id, chunk_size) 
                        VALUES (1, @chunkSize)";
                    cmd.Parameters.AddWithValue("@chunkSize", chunkSize);
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    internal int GetLineCount()
    {
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM line";
            return (int)(long)cmd.ExecuteScalar();
        }
    }

    internal IEnumerable<string> GetAllLineContents()
    {
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT content FROM line ORDER BY id";
            using (var reader = cmd.ExecuteReader())
                while (reader.Read())
                    yield return reader.GetString(0);
        }
    }

    /// <summary>
    /// Gets lines for a specific chunk using indexed chunk_id (fast!).
    /// chunkNumber is zero-based: chunk 0 → rows 0–24, chunk 50 → rows 1250–1274
    /// </summary>
    internal IEnumerable<string> GetLineContentsChunk(int chunkNumber, int chunkSize)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT content FROM line WHERE chunk_id = @chunkId ORDER BY id";
        cmd.Parameters.AddWithValue("@chunkId", chunkNumber);

        SQLiteDataReader reader = null;
        try
        {
            reader = cmd.ExecuteReader();
            while (reader.Read())
                yield return reader.GetString(0);
        }
        finally
        {
            reader?.Dispose();
            cmd.Dispose();
        }
    }

    public void Dispose()
    {
        _connection?.Close();
    }
}