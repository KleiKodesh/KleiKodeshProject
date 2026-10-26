using System.Globalization;
using KitveiHakodesh.Core.Common;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// Which library release a seforim.db is: schema_meta's db_version and db_schema_version.
    /// Reading these to STAMP or ROUTE an update is fine; detecting the database's SHAPE still
    /// goes by table structure (see SeforimDbSchema's remarks).
    /// </summary>
    public readonly record struct SeforimLibraryLocalVersion(int DbVersion, int? SchemaVersion)
    {
        /// <summary>The version of the database at <paramref name="dbPath"/>, or null when it has
        /// none — an update then has nothing to start from and must download in full.</summary>
        public static SeforimLibraryLocalVersion? Read(string dbPath)
        {
            try
            {
                using var connection = SqliteConnectionFactory.OpenCorpusProbe(dbPath);
                int? version = ReadInt(connection, "db_version");
                return version == null ? null : new SeforimLibraryLocalVersion(version.Value, ReadInt(connection, "db_schema_version"));
            }
            catch (SqliteException)
            {
                return null;
            }
        }

        internal static int? ReadInt(SqliteConnection connection, string key, string schema = "main")
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT value FROM {schema}.schema_meta WHERE key = $key LIMIT 1";
            command.Parameters.AddWithValue("$key", key);
            object? value = command.ExecuteScalar();
            return int.TryParse(System.Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : null;
        }
    }
}
