using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.UserAnnotations
{
    /// <summary>
    /// The user's own annotations — highlights and notes, anchored to (bookId, lineId, offsets).
    /// This is the ONLY write path in Core; every other database here is opened read-only.
    ///
    /// NAME MISMATCH, deliberate: the file on disk is "user_settings.db" but this holds user
    /// CONTENT, not preferences — those live in the registry, see AppSettingsRegistry. The
    /// filename is frozen because existing installs hold real highlights under it and renaming
    /// it would orphan them. Do NOT "fix" the file name to match the class.
    ///
    /// It also runs FRONTEND-SUPPLIED SQL instead of owning queries, which is why this is a
    /// *Store and not a *Queries: typed methods for the read-only shipped corpus, raw SQL for
    /// user-owned mutable data. The frontend sends parameterised SQL with positional '?', the
    /// same contract on both transports.
    ///
    /// Cross-process safety: the database is WAL, so another process (Zayit, a second instance
    /// of this app) can read and write at the same time. No connection is held between calls,
    /// so no file lock is held either; SqliteConnectionFactory.OpenUserData sets the busy
    /// timeout that makes a competing writer wait rather than fail instantly.
    /// </summary>
    public sealed class UserAnnotationStore
    {
        private readonly string _databasePath;

        /// <summary>
        /// Where the annotations live, given the seforim library path:
        /// <c>{libraryFolder}\Settings\user_settings.db</c>. Sitting beside the library means
        /// annotations follow it when the user points the app at a different one.
        /// </summary>
        public static string DeriveDatabasePath(string seforimDbPath)
        {
            if (string.IsNullOrWhiteSpace(seforimDbPath))
                throw new ArgumentException("seforimDbPath is required", nameof(seforimDbPath));

            string folder = Path.GetDirectoryName(seforimDbPath)
                ?? throw new ArgumentException("seforimDbPath has no directory", nameof(seforimDbPath));

            return Path.Combine(folder, "Settings", "user_settings.db");
        }

        public UserAnnotationStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("databasePath is required", nameof(databasePath));

            _databasePath = databasePath;
        }

        public string DatabasePath => _databasePath;

        /// <summary>
        /// Creates the schema if it is not there yet. Safe to call repeatedly.
        /// WAL is a persistent property of the file, so it survives this connection closing
        /// and applies to every later one, from any process.
        /// </summary>
        public void EnsureCreated()
        {
            string folder = Path.GetDirectoryName(_databasePath)!;
            Directory.CreateDirectory(folder);

            using var connection = SqliteConnectionFactory.OpenUserData(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = SchemaSql;
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Runs a SELECT supplied by the frontend and returns the rows.
        /// Returns plain dictionaries, not a serialized string: the transport decides the wire
        /// format, and Core never encodes one (MIGRATION-PLAN rule 0e).
        /// </summary>
        public List<Dictionary<string, object?>> Query(string sql, IReadOnlyList<object?> parameters)
        {
            using var connection = SqliteConnectionFactory.OpenUserData(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = SqlPlaceholders.ToNamed(sql);
            BindParameters(command, parameters);

            var rows = new List<Dictionary<string, object?>>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>
        /// Runs an INSERT/UPDATE/DELETE supplied by the frontend and returns the row id of an
        /// INSERT. Read on the SAME connection as the statement, so it cannot pick up another
        /// connection's insert.
        /// </summary>
        public long Execute(string sql, IReadOnlyList<object?> parameters)
        {
            using var connection = SqliteConnectionFactory.OpenUserData(_databasePath);
            using var command = connection.CreateCommand();
            command.CommandText = SqlPlaceholders.ToNamed(sql);
            BindParameters(command, parameters);
            command.ExecuteNonQuery();

            using var lastId = connection.CreateCommand();
            lastId.CommandText = "SELECT last_insert_rowid()";
            object? value = lastId.ExecuteScalar();
            return value is long id ? id : 0L;
        }

        private static void BindParameters(SqliteCommand command, IReadOnlyList<object?> parameters)
        {
            if (parameters == null) return;
            for (int i = 0; i < parameters.Count; i++)
                command.Parameters.AddWithValue("@p" + i, parameters[i] ?? DBNull.Value);
        }

        /// <summary>
        /// Highlights and notes. Both anchor to a line and a character range within it, so a
        /// single line can carry several of either.
        /// </summary>
        private const string SchemaSql = @"
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
        ";
    }

    /// <summary>
    /// Rewrites the positional <c>?</c> placeholders the frontend sends into the named
    /// <c>@p0, @p1, …</c> form the provider binds.
    ///
    /// It lives here because this is its only caller: once the seforim and dictionary bridges
    /// move to typed methods, user annotations are the last place raw SQL crosses the wire.
    ///
    /// QUOTE-AWARE, and that is the whole point. The net4.8 version did a plain
    /// <c>Regex.Replace(sql, "\\?", …)</c>, which also rewrites a '?' inside a string literal
    /// or a comment — so a note whose text contains a question mark came back mangled, or the
    /// statement failed with a parameter count mismatch. Anything inside quotes, brackets,
    /// backticks or a comment is copied through untouched.
    /// </summary>
    internal static class SqlPlaceholders
    {
        public static string ToNamed(string sql)
        {
            if (string.IsNullOrEmpty(sql) || sql.IndexOf('?') < 0) return sql;

            var result = new StringBuilder(sql.Length + 16);
            int index = 0;

            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];

                switch (c)
                {
                    case '\'':                       // 'text' — '' is an escaped quote
                    case '"':                        // "identifier"
                    case '`':                        // `identifier`
                        i = CopyDelimited(sql, i, c, c, result);
                        continue;

                    case '[':                        // [identifier]
                        i = CopyDelimited(sql, i, '[', ']', result);
                        continue;

                    case '-' when i + 1 < sql.Length && sql[i + 1] == '-':
                        i = CopyLineComment(sql, i, result);
                        continue;

                    case '/' when i + 1 < sql.Length && sql[i + 1] == '*':
                        i = CopyBlockComment(sql, i, result);
                        continue;

                    case '?':
                        result.Append("@p").Append(index++);
                        continue;

                    default:
                        result.Append(c);
                        continue;
                }
            }

            return result.ToString();
        }

        /// <summary>Copies a quoted or bracketed run verbatim; returns the index of its
        /// closing delimiter. A doubled closing char is an escape, not the end.</summary>
        private static int CopyDelimited(string sql, int start, char open, char close, StringBuilder result)
        {
            result.Append(sql[start]);

            for (int i = start + 1; i < sql.Length; i++)
            {
                char c = sql[i];
                result.Append(c);

                if (c != close) continue;

                if (open == close && i + 1 < sql.Length && sql[i + 1] == close)
                {
                    result.Append(close);      // '' inside a string — keep going
                    i++;
                    continue;
                }
                return i;
            }

            return sql.Length - 1;             // unterminated; let the provider report it
        }

        private static int CopyLineComment(string sql, int start, StringBuilder result)
        {
            for (int i = start; i < sql.Length; i++)
            {
                result.Append(sql[i]);
                if (sql[i] == '\n') return i;
            }
            return sql.Length - 1;
        }

        private static int CopyBlockComment(string sql, int start, StringBuilder result)
        {
            result.Append(sql[start]);         // '/'
            for (int i = start + 1; i < sql.Length; i++)
            {
                result.Append(sql[i]);
                if (sql[i] == '/' && sql[i - 1] == '*' && i > start + 1) return i;
            }
            return sql.Length - 1;
        }
    }
}
