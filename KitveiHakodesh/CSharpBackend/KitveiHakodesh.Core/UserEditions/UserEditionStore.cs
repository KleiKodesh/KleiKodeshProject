using System;
using System.Collections.Generic;
using System.IO;
using KitveiHakodesh.Core.Common;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.UserEditions
{
    /// <summary>One scanned edition the user attached to a book: a PDF file of their own
    /// (<see cref="Kind"/> "file") or a HebrewBooks id ("hb").</summary>
    public sealed class UserEdition
    {
        public long Id { get; set; }
        public int BookId { get; set; }
        public string Kind { get; set; } = "";
        public string? FilePath { get; set; }
        public string? HbId { get; set; }
        public string Title { get; set; } = "";
        public long AddedAt { get; set; }
    }

    /// <summary>
    /// The user's own scanned editions per book, in the user_editions table of the same
    /// user_settings.db that <see cref="UserAnnotations.UserAnnotationStore"/> owns (beside the
    /// library; path from <see cref="UserAnnotations.UserAnnotationStore.DeriveDatabasePath"/>).
    /// Typed queries, unlike the annotation store's frontend-supplied SQL.
    /// </summary>
    public sealed class UserEditionStore
    {
        public const string KindFile = "file";
        public const string KindHebrewBooks = "hb";

        private const string Columns = "id, bookId, kind, filePath, hbId, title, addedAt";

        private readonly string _databasePath;
        private readonly object _initLock = new();
        private volatile bool _created;

        public UserEditionStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("databasePath is required", nameof(databasePath));
            _databasePath = databasePath;
        }

        /// <summary>The book's editions, newest first.</summary>
        public List<UserEdition> List(int bookId)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {Columns} FROM user_editions WHERE bookId = @b ORDER BY addedAt DESC, id DESC";
            command.Parameters.AddWithValue("@b", bookId);
            var rows = new List<UserEdition>();
            using var reader = command.ExecuteReader();
            while (reader.Read()) rows.Add(Read(reader));
            return rows;
        }

        /// <summary>Adds an edition, or returns the existing row for the same book + kind + path/id.</summary>
        public UserEdition Add(int bookId, string kind, string? filePath, string? hbId, string? title)
        {
            if (kind != KindFile && kind != KindHebrewBooks)
                throw new ArgumentException("kind must be 'file' or 'hb'", nameof(kind));
            string? key = kind == KindFile ? filePath : hbId;
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException(kind == KindFile ? "filePath is required" : "hbId is required");

            using var connection = Open();
            using (var find = connection.CreateCommand())
            {
                find.CommandText = kind == KindFile
                    ? $"SELECT {Columns} FROM user_editions WHERE bookId = @b AND kind = @k AND filePath = @v COLLATE NOCASE LIMIT 1"
                    : $"SELECT {Columns} FROM user_editions WHERE bookId = @b AND kind = @k AND hbId = @v LIMIT 1";
                find.Parameters.AddWithValue("@b", bookId);
                find.Parameters.AddWithValue("@k", kind);
                find.Parameters.AddWithValue("@v", key);
                using var reader = find.ExecuteReader();
                if (reader.Read()) return Read(reader);
            }

            var row = new UserEdition
            {
                BookId = bookId,
                Kind = kind,
                FilePath = kind == KindFile ? filePath : null,
                HbId = kind == KindHebrewBooks ? hbId : null,
                Title = title ?? "",
                AddedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO user_editions (bookId, kind, filePath, hbId, title, addedAt) VALUES (@b, @k, @f, @h, @t, @a); SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("@b", bookId);
            insert.Parameters.AddWithValue("@k", kind);
            insert.Parameters.AddWithValue("@f", (object?)row.FilePath ?? DBNull.Value);
            insert.Parameters.AddWithValue("@h", (object?)row.HbId ?? DBNull.Value);
            insert.Parameters.AddWithValue("@t", row.Title);
            insert.Parameters.AddWithValue("@a", row.AddedAt);
            row.Id = Convert.ToInt64(insert.ExecuteScalar());
            return row;
        }

        /// <summary>Removes one edition. Returns whether a row was there.</summary>
        public bool Remove(long id)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM user_editions WHERE id = @id";
            command.Parameters.AddWithValue("@id", id);
            return command.ExecuteNonQuery() > 0;
        }

        /// <summary>Opens the user database, creating the table on first use (idempotent).</summary>
        private SqliteConnection Open()
        {
            if (!_created)
            {
                lock (_initLock)
                {
                    if (!_created)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
                        using var setup = SqliteConnectionFactory.OpenUserData(_databasePath);
                        using var command = setup.CreateCommand();
                        command.CommandText = SchemaSql;
                        command.ExecuteNonQuery();
                        _created = true;
                    }
                }
            }
            return SqliteConnectionFactory.OpenUserData(_databasePath);
        }

        private static UserEdition Read(SqliteDataReader r) => new()
        {
            Id = r.GetInt64(0),
            BookId = r.GetInt32(1),
            Kind = r.GetString(2),
            FilePath = r.IsDBNull(3) ? null : r.GetString(3),
            HbId = r.IsDBNull(4) ? null : r.GetString(4),
            Title = r.IsDBNull(5) ? "" : r.GetString(5),
            AddedAt = r.GetInt64(6),
        };

        private const string SchemaSql = @"
            CREATE TABLE IF NOT EXISTS user_editions (
                id       INTEGER PRIMARY KEY AUTOINCREMENT,
                bookId   INTEGER NOT NULL,
                kind     TEXT    NOT NULL,
                filePath TEXT    NULL,
                hbId     TEXT    NULL,
                title    TEXT    NOT NULL,
                addedAt  INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_user_editions_book ON user_editions (bookId);
        ";
    }
}
