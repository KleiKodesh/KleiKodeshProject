using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using FtsLib;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Common;
using KitveiHakodesh.Core.SeforimDb;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// The seforim database, served to the FTS engine through its corpus seam
    /// (<see cref="IFtsCorpus"/>) — Core reads the lines, the engine indexes and searches them,
    /// and the engine's own built-in reader is never used.
    ///
    /// This is step 2 of the gradual FtsLib boundary (MIGRATION-PLAN slice 4b): construct
    /// <c>SeforimIndex(indexPath, () => new SeforimDbFtsCorpus(dbPath))</c> and the engine opens
    /// no content database of its own. Callers still on <c>SeforimIndex(indexPath, dbPath)</c>
    /// keep working; the two routes build byte-identical indexes because these queries are
    /// ports of the built-in reader's, kept semantically identical on purpose.
    ///
    /// ONE CONNECTION PER INSTANCE, ONE INSTANCE PER OPERATION — that is the seam's contract
    /// (see IFtsCorpus: lazy result sequences need the corpus to outlive the call, and
    /// concurrent searches must never share a connection). Construction is cheap: the corpus
    /// is read-only so pooling stays on, and "open" is a pooled-handle checkout.
    ///
    /// NOT part of <see cref="SeforimDbQueries"/>, although it reads the same file: that class
    /// opens a connection per method and returns lists, which is right for request/response
    /// queries and wrong for an indexing scan that streams millions of rows over one handle.
    /// Different lifetime, different class.
    /// </summary>
    public sealed class SeforimDbFtsCorpus : IFtsCorpus
    {
        /// <summary>SQLite's default variable limit is 999; staying under it caps the IN-lists.</summary>
        private const int NeighborChunkSize = 999;

        /// <summary>Result fetches use smaller chunks so the first hits reach the caller after
        /// 200 rows, not after the whole ID list — this feeds a UI that streams results in.</summary>
        private const int FetchChunkSize = 200;

        private readonly SqliteConnection _connection;

        public SeforimDbFtsCorpus(string databasePath)
        {
            _connection = SqliteConnectionFactory.OpenCorpusRead(databasePath);

            // Scan tuning on top of the standard corpus-read policy, per connection: an index
            // build reads every line of a multi-gigabyte file in one pass, which earns a bigger
            // page cache than the request/response reads get, and temp_store=MEMORY keeps the
            // neighbour self-join's transient structures off disk. Matches the built-in reader.
            using var command = _connection.CreateCommand();
            command.CommandText =
                "PRAGMA cache_size=-65536;" +   // up to 64 MB page cache
                "PRAGMA temp_store=MEMORY;";
            command.ExecuteNonQuery();
        }

        public long CountDocuments()
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsCountLines;
            return (long)command.ExecuteScalar()!;
        }

        public long CountDocumentsUpTo(int upToId)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsCountLinesUpTo;
            command.Parameters.AddWithValue("@id", upToId);
            return (long)command.ExecuteScalar()!;
        }

        public string? GetDocumentText(int id)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsGetLineContent;
            command.Parameters.AddWithValue("@id", id);
            object? result = command.ExecuteScalar();
            return result == null || result is DBNull ? null : (string)result;
        }

        public IEnumerable<(int Id, string Text)> ReadDocuments(int limit, CancellationToken ct = default)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsReadLines(limit > 0);
            if (limit > 0) command.Parameters.AddWithValue("@lim", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                yield return (reader.GetInt32(0), reader.IsDBNull(1) ? "" : reader.GetString(1));
            }
        }

        public IEnumerable<(int Id, string Text)> ReadDocumentsAfter(int afterId, int limit = 0, CancellationToken ct = default)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsReadLinesAfter(limit > 0);
            command.Parameters.AddWithValue("@after", afterId);
            if (limit > 0) command.Parameters.AddWithValue("@lim", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                yield return (reader.GetInt32(0), reader.IsDBNull(1) ? "" : reader.GetString(1));
            }
        }

        public IEnumerable<(int Id, string Text, string Title)> FetchDocuments(IEnumerable<int> ids)
        {
            var chunk = new List<int>(FetchChunkSize);

            using var command = _connection.CreateCommand();
            string[] parameterNames = AddChunkParameters(command, FetchChunkSize);

            foreach (int id in ids)
            {
                chunk.Add(id);
                if (chunk.Count == FetchChunkSize)
                {
                    foreach (var row in FetchChunk(command, parameterNames, chunk)) yield return row;
                    chunk.Clear();
                }
            }

            if (chunk.Count > 0)
                foreach (var row in FetchChunk(command, parameterNames, chunk)) yield return row;
        }

        private static IEnumerable<(int Id, string Text, string Title)> FetchChunk(
            SqliteCommand command, string[] parameterNames, List<int> ids)
        {
            command.CommandText = SeforimDbSqlStrings.FtsFetchLinesWithTitles(ids.Count);
            for (int i = 0; i < ids.Count; i++)
                command.Parameters[parameterNames[i]].Value = ids[i];

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                yield return (
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2));
            }
        }

        public IDictionary<int, (string Previous, string Next)> FetchNeighbourText(
            IReadOnlyList<int> ids, int radius)
        {
            var neighbours = new Dictionary<int, (string Previous, string Next)>(ids?.Count ?? 0);
            if (ids == null || ids.Count == 0 || radius <= 0) return neighbours;

            // Neighbour lines per matched id, keyed by their lineIndex delta, so each side joins
            // back in document order: previous = negative deltas ascending, next = positive.
            var previousParts = new Dictionary<int, SortedList<int, string>>();
            var nextParts = new Dictionary<int, SortedList<int, string>>();

            using (var command = _connection.CreateCommand())
            {
                string[] parameterNames = AddChunkParameters(command, NeighborChunkSize);
                command.Parameters.AddWithValue("@radius", radius);

                for (int start = 0; start < ids.Count; start += NeighborChunkSize)
                {
                    int count = Math.Min(NeighborChunkSize, ids.Count - start);
                    command.CommandText = SeforimDbSqlStrings.FtsFetchNeighborLines(count);
                    for (int i = 0; i < count; i++)
                        command.Parameters[parameterNames[i]].Value = ids[start + i];

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        int matchedId = reader.GetInt32(0);
                        int delta = reader.GetInt32(1);
                        string text = reader.IsDBNull(2) ? "" : reader.GetString(2);
                        if (text.Length == 0) continue;

                        var side = delta < 0 ? previousParts : nextParts;
                        if (!side.TryGetValue(matchedId, out var parts))
                            side[matchedId] = parts = new SortedList<int, string>();
                        parts[delta] = text;
                    }
                }
            }

            foreach (int id in ids)
            {
                string previous = previousParts.TryGetValue(id, out var p) ? string.Join(" ", p.Values) : "";
                string next = nextParts.TryGetValue(id, out var n) ? string.Join(" ", n.Values) : "";
                if (previous.Length > 0 || next.Length > 0) neighbours[id] = (previous, next);
            }

            return neighbours;
        }

        /// <summary>Pre-registers @p0..@pN once per command, so chunk loops only assign values
        /// instead of rebuilding the parameter collection per chunk.</summary>
        private static string[] AddChunkParameters(SqliteCommand command, int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = "@p" + i;
                command.Parameters.Add(names[i], SqliteType.Integer);
            }
            return names;
        }

        public void Dispose() => _connection.Dispose();
    }
}
