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
    /// The engine is constructed as
    /// <c>Fts5SeforimIndex(indexPath, () => new SeforimDbFtsCorpus(dbPath))</c> and opens no
    /// content database of its own.
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
    public sealed class SeforimDbFtsCorpus : IFtsCorpus, IFtsOrderedCorpus, IFtsPlacedCorpus
    {
        /// <summary>SQLite's default variable limit is 999; staying under it caps the IN-lists.
        /// How many ids a fetch is asked for at a time is the CALLER's decision (the searcher's
        /// batch plan); this only splits a request that would not fit one statement.</summary>
        private const int SqliteInListChunkSize = 999;

        private readonly SqliteConnection _connection;

        /// <summary>
        /// The database's shape, and the decoder for its line text. Held for the life of this
        /// corpus because it owns ONE connection used for the whole indexing scan: probing the
        /// schema once and loading the zstd dictionary once is the point, over millions of rows.
        /// Both are read-only after construction, matching this class's single-threaded use.
        /// </summary>
        private readonly SeforimDbSchema _schema;
        private readonly SeforimDbLineContentCodec _codec;

        public SeforimDbFtsCorpus(string databasePath)
        {
            _connection = SqliteConnectionFactory.OpenCorpusRead(databasePath);
            _schema = SeforimDbSchema.ForDatabase(_connection, databasePath);
            _codec = SeforimDbLineContentCodec.Create(_connection);

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
            command.CommandText = SeforimDbSqlStrings.FtsGetLineContent(_schema);
            command.Parameters.AddWithValue("@id", id);
            return _codec.Decode(command.ExecuteScalar());
        }

        public IEnumerable<(int Id, string Text)> ReadDocuments(int limit, CancellationToken ct = default)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsReadLines(_schema, limit > 0);
            if (limit > 0) command.Parameters.AddWithValue("@lim", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                yield return (reader.GetInt32(0), _codec.DecodeColumnOrEmpty(reader, 1));
            }
        }

        /// <summary>
        /// Every line in the order the app shows the library: books in catalog tree order
        /// (<see cref="SeforimDbBookTreeOrder"/>, the frontend's treeOrder), each book's lines
        /// in reading order — so the full-text index stores, and returns, lines in that order.
        /// Lines of a book the catalog does not list (none in the shipped databases) follow at
        /// the end, by book id, so every line is indexed exactly once. <paramref name="skip"/>
        /// counts lines in this order: whole books are skipped by their line counts.
        /// </summary>
        public IEnumerable<(int Id, string Text)> ReadDocumentsInResultOrder(long skip, CancellationToken ct = default)
        {
            var (bookOrder, lineCounts) = BooksInResultOrder();

            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsReadBookLines(_schema);
            var pBook = command.Parameters.Add("@b", SqliteType.Integer);
            var pSkip = command.Parameters.Add("@skip", SqliteType.Integer);
            foreach (int bookId in bookOrder)
            {
                long lines = lineCounts[bookId];
                if (skip >= lines) { skip -= lines; continue; }
                pBook.Value = bookId;
                pSkip.Value = skip;
                skip = 0;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    yield return (reader.GetInt32(0), _codec.DecodeColumnOrEmpty(reader, 1));
                }
            }
        }

        /// <summary>
        /// The books in result order with their line counts — THE definition of the index's
        /// order, read by the build (<see cref="ReadDocumentsInResultOrder"/>) and by
        /// <see cref="BookPositionRanges"/>, so the two can never disagree.
        /// </summary>
        private (List<int> BookOrder, Dictionary<int, long> LineCounts) BooksInResultOrder()
        {
            var treeOrder = SeforimDbBookTreeOrder.Load(_connection);

            var lineCounts = new Dictionary<int, long>();
            using (var count = _connection.CreateCommand())
            {
                count.CommandText = SeforimDbSqlStrings.FtsLineCountsByBook;
                using var r = count.ExecuteReader();
                while (r.Read()) lineCounts[r.GetInt32(0)] = r.GetInt64(1);
            }

            var bookOrder = new List<int>(lineCounts.Keys);
            bookOrder.Sort((a, b) =>
            {
                bool hasA = treeOrder.TryGetValue(a, out int rankA), hasB = treeOrder.TryGetValue(b, out int rankB);
                if (hasA != hasB) return hasA ? -1 : 1;
                return hasA ? rankA.CompareTo(rankB) : a.CompareTo(b);
            });
            return (bookOrder, lineCounts);
        }

        /// <summary>
        /// Each book's block of index positions: the build numbers lines 1, 2, 3… in result
        /// order, a book's lines in reading order, so a book is the contiguous run
        /// [First, First + Count - 1] and its line at lineIndex i is position First + i.
        /// </summary>
        public Dictionary<int, (long First, long Count)> BookPositionRanges()
        {
            var (bookOrder, lineCounts) = BooksInResultOrder();
            var ranges = new Dictionary<int, (long First, long Count)>(bookOrder.Count);
            long next = 1;
            foreach (int bookId in bookOrder)
            {
                long count = lineCounts[bookId];
                ranges[bookId] = (next, count);
                next += count;
            }
            return ranges;
        }

        public IEnumerable<(int Id, string Text)> ReadDocumentsAfter(int afterId, int limit = 0, CancellationToken ct = default)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsReadLinesAfter(_schema, limit > 0);
            command.Parameters.AddWithValue("@after", afterId);
            if (limit > 0) command.Parameters.AddWithValue("@lim", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                yield return (reader.GetInt32(0), _codec.DecodeColumnOrEmpty(reader, 1));
            }
        }

        /// <summary>Search-result content without placement - the plain corpus seam, and the
        /// searcher's verification fetch: id and text only, by primary key from the table that
        /// holds the text (<see cref="SeforimDbSqlStrings.FtsFetchLines"/>). The Title slot is
        /// left empty on purpose: Core enriches hits with their book ID instead and never reads
        /// the engine's title. The engine's own diagnostics run over the test corpus, which
        /// does fill it.</summary>
        public IEnumerable<(int Id, string Text, string Title)> FetchDocuments(IEnumerable<int> ids)
        {
            var chunk = new List<int>(SqliteInListChunkSize);
            using var command = _connection.CreateCommand();
            string[] parameterNames = AddChunkParameters(command, SqliteInListChunkSize);

            foreach (int id in ids)
            {
                chunk.Add(id);
                if (chunk.Count == SqliteInListChunkSize)
                {
                    foreach (var row in FetchTextChunk(command, parameterNames, chunk)) yield return row;
                    chunk.Clear();
                }
            }

            if (chunk.Count > 0)
                foreach (var row in FetchTextChunk(command, parameterNames, chunk)) yield return row;
        }

        private IEnumerable<(int Id, string Text, string Title)> FetchTextChunk(
            SqliteCommand command, string[] parameterNames, List<int> ids)
        {
            command.CommandText = SeforimDbSqlStrings.FtsFetchLines(_schema, ids.Count);
            for (int i = 0; i < ids.Count; i++)
                command.Parameters[parameterNames[i]].Value = ids[i];

            using var reader = command.ExecuteReader();
            while (reader.Read())
                yield return (reader.GetInt32(0), _codec.DecodeColumnOrEmpty(reader, 1), "");
        }

        /// <summary>
        /// The placement (book id, lineIndex, own TOC entry) of each line, no text - what the
        /// searcher reads AFTER verification, for the hits that passed
        /// (<see cref="SeforimDbSqlStrings.FtsFetchPlacements"/>). An id with no line is absent.
        /// </summary>
        public Dictionary<int, FtsDocumentPlacement> FetchPlacements(IReadOnlyList<int> ids)
        {
            var placements = new Dictionary<int, FtsDocumentPlacement>(ids?.Count ?? 0);
            if (ids == null || ids.Count == 0) return placements;

            using var command = _connection.CreateCommand();
            string[] parameterNames = AddChunkParameters(command, SqliteInListChunkSize);
            for (int start = 0; start < ids.Count; start += SqliteInListChunkSize)
            {
                int count = Math.Min(SqliteInListChunkSize, ids.Count - start);
                command.CommandText = SeforimDbSqlStrings.FtsFetchPlacements(_schema, count);
                for (int i = 0; i < count; i++)
                    command.Parameters[parameterNames[i]].Value = ids[start + i];

                using var reader = command.ExecuteReader();
                while (reader.Read())
                    placements[reader.GetInt32(0)] = new FtsDocumentPlacement(
                        bookId: reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                        lineIndex: reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                        tocEntryId: reader.GetInt32(3));
            }
            return placements;
        }

        /// <summary>Search-result content plus each line's placement (book id, lineIndex, own
        /// TOC entry), from the one `line` row the content fetch reads - what lets the
        /// searcher finish a hit without a second query. See
        /// <see cref="SeforimDbSqlStrings.FtsFetchPlacedLines"/>.</summary>
        public IEnumerable<(int Id, string Text, FtsDocumentPlacement Placement)> FetchPlacedDocuments(IEnumerable<int> ids)
        {
            var chunk = new List<int>(SqliteInListChunkSize);

            using var command = _connection.CreateCommand();
            string[] parameterNames = AddChunkParameters(command, SqliteInListChunkSize);

            foreach (int id in ids)
            {
                chunk.Add(id);
                if (chunk.Count == SqliteInListChunkSize)
                {
                    foreach (var row in FetchPlacedChunk(command, parameterNames, chunk)) yield return row;
                    chunk.Clear();
                }
            }

            if (chunk.Count > 0)
                foreach (var row in FetchPlacedChunk(command, parameterNames, chunk)) yield return row;
        }

        private IEnumerable<(int Id, string Text, FtsDocumentPlacement Placement)> FetchPlacedChunk(
            SqliteCommand command, string[] parameterNames, List<int> ids)
        {
            command.CommandText = SeforimDbSqlStrings.FtsFetchPlacedLines(_schema, ids.Count);
            for (int i = 0; i < ids.Count; i++)
                command.Parameters[parameterNames[i]].Value = ids[i];

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var placement = new FtsDocumentPlacement(
                    bookId: reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    lineIndex: reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    tocEntryId: reader.GetInt32(4));
                yield return (reader.GetInt32(0), _codec.DecodeColumnOrEmpty(reader, 1), placement);
            }
        }

        /// <summary>
        /// The lines at the given (bookId, lineIndex) positions: id, position and text. One
        /// statement per 499 positions (two parameters each, under SQLite's 999 limit). What
        /// the searcher asks for a batch's short hits' neighbours — the positions it could
        /// not already serve from the batch's own fetched content. A position with no line
        /// is absent; empty lines are returned (the caller skips them, as the self-join did).
        /// </summary>
        public List<SeforimDbFtsLineAtPosition> FetchLinesAtBookPositions(
            IReadOnlyList<(int BookId, int LineIndex)> positions)
        {
            var lines = new List<SeforimDbFtsLineAtPosition>(positions?.Count ?? 0);
            if (positions == null || positions.Count == 0) return lines;

            const int positionsPerStatement = SqliteInListChunkSize / 2;
            using var command = _connection.CreateCommand();
            string[] parameterNames = AddChunkParameters(command, positionsPerStatement * 2);

            for (int start = 0; start < positions.Count; start += positionsPerStatement)
            {
                int count = Math.Min(positionsPerStatement, positions.Count - start);
                command.CommandText = SeforimDbSqlStrings.FtsFetchLinesAtBookPositions(_schema, count);
                for (int i = 0; i < count; i++)
                {
                    command.Parameters[parameterNames[2 * i]].Value = positions[start + i].BookId;
                    command.Parameters[parameterNames[2 * i + 1]].Value = positions[start + i].LineIndex;
                }

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    lines.Add(new SeforimDbFtsLineAtPosition(
                        id: reader.GetInt32(0),
                        bookId: reader.GetInt32(1),
                        lineIndex: reader.GetInt32(2),
                        text: _codec.DecodeColumnOrEmpty(reader, 3)));
                }
            }
            return lines;
        }

        public IDictionary<int, (string Previous, string Next)> FetchNeighbourText(
            IReadOnlyList<int> ids, int radius)
        {
            var neighbours = new Dictionary<int, (string Previous, string Next)>(ids?.Count ?? 0);
            foreach (var entry in FetchNeighbourLines(ids, radius))
                neighbours[entry.Key] = (entry.Value.Previous, entry.Value.Next);
            return neighbours;
        }

        /// <summary>
        /// <see cref="FetchNeighbourText"/> plus WHICH lines each side was joined from, in the
        /// same document order. Empty neighbour lines are skipped on both counts, so joining
        /// the ids' texts with single spaces reproduces <c>Previous</c> / <c>Next</c>. One
        /// self-join per chunk of ids; the searcher no longer uses this — it serves its short
        /// hits' neighbours from the batch's own content plus
        /// <see cref="FetchLinesAtBookPositions"/> — so it only backs the plain corpus seam.
        /// </summary>
        private IDictionary<int, SeforimDbFtsNeighbourLines> FetchNeighbourLines(
            IReadOnlyList<int>? ids, int radius)
        {
            var neighbours = new Dictionary<int, SeforimDbFtsNeighbourLines>(ids?.Count ?? 0);
            if (ids == null || ids.Count == 0 || radius <= 0) return neighbours;

            // Neighbour lines per matched id, keyed by their lineIndex delta, so each side joins
            // back in document order: previous = negative deltas ascending, next = positive.
            var previousParts = new Dictionary<int, SortedList<int, (int Id, string Text)>>();
            var nextParts = new Dictionary<int, SortedList<int, (int Id, string Text)>>();

            using (var command = _connection.CreateCommand())
            {
                string[] parameterNames = AddChunkParameters(command, SqliteInListChunkSize);
                command.Parameters.AddWithValue("@radius", radius);

                for (int start = 0; start < ids.Count; start += SqliteInListChunkSize)
                {
                    int count = Math.Min(SqliteInListChunkSize, ids.Count - start);
                    command.CommandText = SeforimDbSqlStrings.FtsFetchNeighborLines(_schema, count);
                    for (int i = 0; i < count; i++)
                        command.Parameters[parameterNames[i]].Value = ids[start + i];

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        int matchedId = reader.GetInt32(0);
                        int delta = reader.GetInt32(1);
                        string text = _codec.DecodeColumnOrEmpty(reader, 2);
                        if (text.Length == 0) continue;
                        int neighbourId = reader.GetInt32(3);

                        var side = delta < 0 ? previousParts : nextParts;
                        if (!side.TryGetValue(matchedId, out var parts))
                            side[matchedId] = parts = new SortedList<int, (int, string)>();
                        parts[delta] = (neighbourId, text);
                    }
                }
            }

            foreach (int id in ids)
            {
                var entry = new SeforimDbFtsNeighbourLines();
                if (previousParts.TryGetValue(id, out var p)) Collect(p, entry.PreviousIds, out entry.Previous);
                if (nextParts.TryGetValue(id, out var n)) Collect(n, entry.NextIds, out entry.Next);
                if (entry.Previous.Length > 0 || entry.Next.Length > 0) neighbours[id] = entry;
            }

            return neighbours;

            static void Collect(SortedList<int, (int Id, string Text)> parts, List<int> idsOut, out string joined)
            {
                var texts = new List<string>(parts.Count);
                foreach (var part in parts.Values)
                {
                    idsOut.Add(part.Id);
                    texts.Add(part.Text);
                }
                joined = string.Join(" ", texts);
            }
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

        public void Dispose()
        {
            // The codec holds the database's zstd dictionary, which is megabytes - release it
            // with the connection it belongs to rather than waiting for a collection.
            _codec.Dispose();
            _connection.Dispose();
        }
    }

    /// <summary>One line of the library with its position: what
    /// <see cref="SeforimDbFtsCorpus.FetchLinesAtBookPositions"/> returns.</summary>
    public readonly struct SeforimDbFtsLineAtPosition
    {
        public SeforimDbFtsLineAtPosition(int id, int bookId, int lineIndex, string text)
        {
            Id = id;
            BookId = bookId;
            LineIndex = lineIndex;
            Text = text;
        }

        public int Id { get; }
        public int BookId { get; }
        public int LineIndex { get; }
        public string Text { get; }
    }

    /// <summary>One matched line's same-book neighbours: each side's joined text and the ids
    /// it was joined from, in document order. See <see cref="SeforimDbFtsCorpus.FetchNeighbourText"/>.</summary>
    internal sealed class SeforimDbFtsNeighbourLines
    {
        public string Previous = "";
        public string Next = "";
        public readonly List<int> PreviousIds = new List<int>();
        public readonly List<int> NextIds = new List<int>();
    }
}
