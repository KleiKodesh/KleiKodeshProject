using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;

public sealed class ZayitDbManager : IDisposable
{
    readonly SQLiteConnection _connection;
    public IDbConnection Connection => _connection;

    public ZayitDbManager()
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
    public int? GetCurrentChunkSize()
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
    public bool NeedsChunkIndexUpdate(int desiredChunkSize)
    {
        var currentChunkSize = GetCurrentChunkSize();
        return currentChunkSize == null || currentChunkSize.Value != desiredChunkSize;
    }

    /// <summary>
    /// One-time/update setup: adds/updates chunk_id column and index for fast chunk retrieval.
    /// Recreates the index if chunk size has changed.
    /// Uses temp table with ROW_NUMBER to assign chunk IDs based on (bookId, id) ordering.
    /// </summary>
    public void InitializeChunkIndex(int chunkSize, Action<int, int> progressCallback = null)
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

                    // Report initial progress
                    progressCallback?.Invoke(0, totalRows);

                    // Create temp table with ordered row numbers and chunk assignments
                    cmd.CommandText = $@"
                        CREATE TEMP TABLE temp_chunks AS
                        SELECT id, 
                               (ROW_NUMBER() OVER (ORDER BY bookId, id) - 1) / {chunkSize} as new_chunk_id
                        FROM line";
                    cmd.ExecuteNonQuery();

                    // Report progress after temp table creation (about 50% done)
                    progressCallback?.Invoke(totalRows / 2, totalRows);

                    // Create index on temp table for faster updates
                    cmd.CommandText = "CREATE INDEX idx_temp_chunks_id ON temp_chunks(id)";
                    cmd.ExecuteNonQuery();

                    // Update chunk_id based on temp table
                    cmd.CommandText = @"
                        UPDATE line 
                        SET chunk_id = (SELECT new_chunk_id FROM temp_chunks WHERE temp_chunks.id = line.id)";
                    cmd.ExecuteNonQuery();

                    // Report progress after update (about 75% done)
                    progressCallback?.Invoke(totalRows * 3 / 4, totalRows);

                    // Clean up temp table
                    cmd.CommandText = "DROP TABLE temp_chunks";
                    cmd.ExecuteNonQuery();

                    // Drop old index if it exists
                    cmd.CommandText = "DROP INDEX IF EXISTS idx_line_chunk_id";
                    cmd.ExecuteNonQuery();

                    // Create new index on chunk_id
                    cmd.CommandText = "CREATE INDEX idx_line_chunk_id ON line(chunk_id)";
                    cmd.ExecuteNonQuery();

                    // Update metadata
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO bloom_metadata (id, chunk_size) 
                        VALUES (1, @chunkSize)";
                    cmd.Parameters.AddWithValue("@chunkSize", chunkSize);
                    cmd.ExecuteNonQuery();

                    // Report completion
                    progressCallback?.Invoke(totalRows, totalRows);
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

    public int GetLineCount()
    {
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM line";
            return (int)(long)cmd.ExecuteScalar();
        }
    }

    public IEnumerable<string> GetAllLineContents()
    {
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT content FROM line ORDER BY bookId, id";
            using (var reader = cmd.ExecuteReader())
                while (reader.Read())
                    yield return reader.GetString(0);
        }
    }

    /// <summary>
    /// Gets lines for a specific chunk using indexed chunk_id (fast!).
    /// chunkNumber is zero-based: chunk 0 → first 'chunkSize' rows (ordered by bookId, id),
    /// chunk 1 → next 'chunkSize' rows, etc.
    /// Returns lines in the same order as GetAllLineContents (bookId, id).
    /// </summary>
    public IEnumerable<string> GetLineContentsChunk(int chunkNumber, int chunkSize)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText =
            "SELECT content FROM line WHERE chunk_id = @chunkId ORDER BY bookId, id";
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