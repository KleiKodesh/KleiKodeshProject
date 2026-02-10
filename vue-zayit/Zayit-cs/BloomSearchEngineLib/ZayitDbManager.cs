public sealed class ZayitDbManager : IDisposable
{
    readonly SQLiteConnection _connection;

    public ZayitDbManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string defaultPath = Path.Combine(
            appData,
            "io.github.kdroidfilter.seforimapp",
            "databases",
            "seforim.db"
        );
        string dbPath = Interaction.GetSetting("ZayitApp", "Database", "Path", defaultPath);
        var connectionString = $"Data Source={dbPath};Version=3;Cache Size=10000;Page Size=4096;";
        _connection = new SQLiteConnection(connectionString);
        _connection.Open();

        // Enable WAL mode - allows reads during writes!
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();
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
            cmd.CommandText = "SELECT content FROM line ORDER BY id";
            using (var reader = cmd.ExecuteReader())
                while (reader.Read())
                    yield return reader.GetString(0);
        }
    }

    /// <summary>
    /// Gets lines for a specific chunk using RANGE query on line id (fast with primary key!).
    /// chunkNumber is zero-based: chunk 0 → rows 0–24, chunk 50 → rows 1250–1274
    /// </summary>
    public IEnumerable<string> GetLineContentsChunk(int chunkNumber, int chunkSize)
    {
        int startId = chunkNumber * chunkSize;
        int endId = startId + chunkSize - 1;

        var cmd = _connection.CreateCommand();
        cmd.CommandText = @"
            SELECT content 
            FROM line 
            WHERE id BETWEEN @startId AND @endId 
            ORDER BY id";
        cmd.Parameters.AddWithValue("@startId", startId);
        cmd.Parameters.AddWithValue("@endId", endId);

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