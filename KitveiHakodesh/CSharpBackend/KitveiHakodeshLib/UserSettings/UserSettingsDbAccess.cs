using Dapper;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace KitveiHakodeshLib.UserSettings
{
    /// <summary>
    /// Read/write access to the user settings database (user_settings.db).
    ///
    /// The database lives in a "Settings" sub-folder next to the seforim database:
    ///   {seforimDbFolder}/Settings/user_settings.db
    ///
    /// On first access the database and its schema are created automatically.
    /// Unlike DbAccess (read-only), this connection is read/write.
    /// </summary>
    public class UserSettingsDbAccess : IDisposable
    {
        private SQLiteConnection _conn;

        /// <summary>
        /// Derives the user settings database path from the seforim database path.
        /// Example: C:\data\seforim.db  →  C:\data\Settings\user_settings.db
        /// </summary>
        public static string DeriveUserSettingsDbPath(string seforimDbPath)
        {
            string folder = System.IO.Path.GetDirectoryName(seforimDbPath);
            return System.IO.Path.Combine(folder, "Settings", "user_settings.db");
        }

        public string Path { get; private set; }

        public UserSettingsDbAccess(string seforimDbPath)
        {
            Path = DeriveUserSettingsDbPath(seforimDbPath);
            _EnsureSchemaExists();
        }

        private void _EnsureSchemaExists()
        {
            string dir = System.IO.Path.GetDirectoryName(this.Path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string connectionString = "Data Source=" + this.Path + ";Version=3;";
            _conn = new SQLiteConnection(connectionString);
            _conn.Open();

            _conn.Execute(@"
                CREATE TABLE IF NOT EXISTS user_highlights (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    bookId      INTEGER NOT NULL,
                    lineId      INTEGER NOT NULL,
                    startOffset INTEGER NOT NULL,
                    endOffset   INTEGER NOT NULL,
                    colorArgb   INTEGER NOT NULL,
                    createdAt   INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_user_highlights_book_line
                    ON user_highlights (bookId, lineId);

                CREATE TABLE IF NOT EXISTS user_notes (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    bookId      INTEGER NOT NULL,
                    lineId      INTEGER NOT NULL,
                    startOffset INTEGER NOT NULL,
                    endOffset   INTEGER NOT NULL,
                    note        TEXT    NOT NULL,
                    quote       TEXT    NOT NULL,
                    createdAt   INTEGER NOT NULL,
                    updatedAt   INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_user_notes_book_line
                    ON user_notes (bookId, lineId);
            ");
        }

        /// <summary>
        /// Executes a SELECT query and returns rows as a list of dictionaries.
        /// Positional ? parameters are converted to named @p0, @p1, ... for Dapper.
        /// </summary>
        public IEnumerable<IDictionary<string, object>> Query(string sql, object[] parameters)
        {
            int index = 0;
            string namedSql = Regex.Replace(sql, @"\?", _ => "@p" + index++);

            var dp = new DynamicParameters();
            for (int i = 0; i < parameters.Length; i++)
                dp.Add("@p" + i, parameters[i]);

            return _conn.Query(namedSql, dp)
                        .Cast<IDictionary<string, object>>()
                        .ToList();
        }

        /// <summary>
        /// Executes an INSERT, UPDATE, or DELETE statement.
        /// Returns the last inserted row id for INSERT, or rows affected for others.
        /// </summary>
        public long Execute(string sql, object[] parameters)
        {
            int index = 0;
            string namedSql = Regex.Replace(sql, @"\?", _ => "@p" + index++);

            var dp = new DynamicParameters();
            for (int i = 0; i < parameters.Length; i++)
                dp.Add("@p" + i, parameters[i]);

            _conn.Execute(namedSql, dp);
            return _conn.LastInsertRowId;
        }

        public void Dispose()
        {
            _conn?.Close();
            _conn?.Dispose();
            _conn = null;
        }
    }
}
