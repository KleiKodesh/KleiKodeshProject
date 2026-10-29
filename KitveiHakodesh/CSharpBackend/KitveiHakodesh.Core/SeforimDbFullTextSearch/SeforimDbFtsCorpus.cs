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
    public sealed class SeforimDbFtsCorpus : IFtsCorpus, IFtsOrderedCorpus, IFtsPlacedCorpus, ISeforimDbFtsPositionCorpus
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

        /// <summary>Non-null for a library whose stored line ids are translated to dense local ids
        /// (<see cref="SeforimDbIdMap"/>): every id handed out is the local one, every id bound into
        /// this library's own SQL is the stored one.</summary>
        private readonly SeforimDbIdMap? _lineIds;

        /// <summary>Same for TOC-entry ids (the placement a hit reports).</summary>
        private readonly SeforimDbIdMap? _tocIds;

        public SeforimDbFtsCorpus(string databasePath, SeforimDbIdMap? lineIds = null, SeforimDbIdMap? tocIds = null)
        {
            _lineIds = lineIds;
            _tocIds = tocIds;
            _connection = SqliteConnectionFactory.OpenCorpusRead(databasePath);
            try
            {
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
            catch
            {
                // A throw after the connection opened (unreadable schema, codec setup) must not
                // leak the handle: the caller never gets an instance to Dispose.
                _codec?.Dispose();
                _connection.Dispose();
                throw;
            }
        }

        /// <summary>The id to BIND into this database's SQL for an id the rest of the app uses.</summary>
        private long StoredId(int id) => _lineIds is null ? id : _lineIds.ToStored(id);

        /// <summary>The id the rest of the app uses for an id READ from this database's SQL.</summary>
        private int LocalId(long storedId)
        {
            if (_lineIds is null) return checked((int)storedId);
            int local = _lineIds.ToLocal(storedId);
            if (local == 0) throw new InvalidDataException($"line id {storedId} is not in the id map");
            return local;
        }

        private int LocalTocId(long storedId)
        {
            if (storedId == 0) return 0;   // "this line has no TOC entry"
            if (_tocIds is null) return checked((int)storedId);
            int local = _tocIds.ToLocal(storedId);
            if (local == 0) throw new InvalidDataException($"TOC entry id {storedId} is not in the id map");
            return local;
        }

        /// <summary>Stored-id bound for an "id &lt;= x" / "id &gt; x" comparison: stored ids ascend with the
        /// local ids, so the comparison keeps its meaning, and a bound beyond the library covers all of it.</summary>
        private long StoredBound(int id)
        {
            if (_lineIds is null) return id;
            if (id < 1) return 0;
            return id > _lineIds.Total ? long.MaxValue : _lineIds.ToStored(id);
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
            command.Parameters.AddWithValue("@id", StoredBound(upToId));
            return (long)command.ExecuteScalar()!;
        }

        public string? GetDocumentText(int id)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsGetLineContent(_schema);
            command.Parameters.AddWithValue("@id", StoredId(id));
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
                yield return (LocalId(reader.GetInt64(0)), _codec.DecodeColumnOrEmpty(reader, 1));
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
                    yield return (LocalId(reader.GetInt64(0)), _codec.DecodeColumnOrEmpty(reader, 1));
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
            command.Parameters.AddWithValue("@after", StoredBound(afterId));
            if (limit > 0) command.Parameters.AddWithValue("@lim", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                yield return (LocalId(reader.GetInt64(0)), _codec.DecodeColumnOrEmpty(reader, 1));
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
                command.Parameters[parameterNames[i]].Value = StoredId(ids[i]);

            using var reader = command.ExecuteReader();
            while (reader.Read())
                yield return (LocalId(reader.GetInt64(0)), _codec.DecodeColumnOrEmpty(reader, 1), "");
        }

        /// <summary>
        /// The placement (book id, lineIndex) of each line, no text - what the
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
                    command.Parameters[parameterNames[i]].Value = StoredId(ids[start + i]);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                    placements[LocalId(reader.GetInt64(0))] = new FtsDocumentPlacement(
                        bookId: reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
                        lineIndex: reader.IsDBNull(2) ? 0 : reader.GetInt32(2));
            }
            return placements;
        }

        /// <summary>
        /// The line's index position — where a search resumed after it continues
        /// (<see cref="BookPositionRanges"/>: the book's first position plus the line's rank in
        /// the book). Two primary-key / index reads. Null for a line that no longer exists or a
        /// book the ranges do not hold.
        /// </summary>
        public long? PositionOfLine(int lineId, Dictionary<int, (long First, long Count)> books)
        {
            var placement = FetchPlacements(new[] { lineId });
            if (!placement.TryGetValue(lineId, out var at) || !books.TryGetValue(at.BookId, out var book)) return null;
            long rank = LineRankInBook(at.BookId, at.LineIndex, lineId);
            return rank < book.Count ? book.First + rank : null;
        }

        /// <summary>A line's 0-based rank among its book's lines (by lineIndex, id as the tie-break). The ids
        /// are this corpus's own (local, for a translated library).</summary>
        public long LineRankInBook(int bookId, int lineIndex, int lineId)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsLineRankInBook;
            command.Parameters.AddWithValue("@b", bookId);
            command.Parameters.AddWithValue("@li", lineIndex);
            command.Parameters.AddWithValue("@id", StoredId(lineId));
            return (long)command.ExecuteScalar()!;
        }

        /// <summary>Search-result content plus each line's placement (book id, lineIndex),
        /// from the one `line` row the content fetch reads - what lets the
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
                command.Parameters[parameterNames[i]].Value = StoredId(ids[i]);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var placement = new FtsDocumentPlacement(
                    bookId: reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                    lineIndex: reader.IsDBNull(3) ? 0 : reader.GetInt32(3));
                yield return (LocalId(reader.GetInt64(0)), _codec.DecodeColumnOrEmpty(reader, 1), placement);
            }
        }

        /// <summary>How many lines per side a SHORT snippet's context reaches (same book only).
        /// Two fills the snippet's visual space (~4 clamped lines) and reaches about the
        /// requested per-side word context for prose.</summary>
        public const int SnippetNeighbourLineRadius = 2;

        /// <summary>
        /// The snippet texts of the given lines — ONE statement per chunk
        /// (<see cref="SeforimDbSqlStrings.FtsFetchSnippetLines"/>): every line's own text, and
        /// for the lines in <paramref name="shortLineIds"/> also their non-empty same-book
        /// neighbours within <see cref="SnippetNeighbourLineRadius"/>, each side joined by
        /// single spaces in document order. A line with no text row is absent. A short id
        /// need not also appear in <paramref name="lineIds"/>.
        /// </summary>
        public List<FtsSnippetText> FetchSnippetTexts(IReadOnlyList<int> lineIds, IReadOnlyCollection<int> shortLineIds)
        {
            var shortSet = new HashSet<int>(shortLineIds);
            var plain = new List<int>(lineIds.Count);
            foreach (int id in lineIds) if (!shortSet.Contains(id)) plain.Add(id);
            var shortIds = new List<int>(shortSet);

            var texts = new Dictionary<int, string>(plain.Count + shortIds.Count);
            var before = new Dictionary<int, SortedList<int, string>>();
            var after = new Dictionary<int, SortedList<int, string>>();

            // One chunk stays under SQLite's 999 variables (@radius is one more).
            const int perStatement = SqliteInListChunkSize - 1;
            int p = 0, q = 0;
            while (p < plain.Count || q < shortIds.Count)
            {
                int plainCount = Math.Min(perStatement, plain.Count - p);
                int shortCount = Math.Min(perStatement - plainCount, shortIds.Count - q);
                using var command = _connection.CreateCommand();
                command.CommandText = SeforimDbSqlStrings.FtsFetchSnippetLines(_schema, plainCount, shortCount);
                for (int i = 0; i < plainCount; i++) command.Parameters.AddWithValue("@p" + i, StoredId(plain[p + i]));
                for (int i = 0; i < shortCount; i++) command.Parameters.AddWithValue("@s" + i, StoredId(shortIds[q + i]));
                if (shortCount > 0) command.Parameters.AddWithValue("@radius", SnippetNeighbourLineRadius);
                p += plainCount;
                q += shortCount;

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = LocalId(reader.GetInt64(0));
                    int delta = reader.GetInt32(1);
                    string text = _codec.DecodeColumnOrEmpty(reader, 2);
                    if (delta == 0) { texts[id] = text; continue; }
                    if (text.Length == 0) continue;   // empty neighbours are skipped, not joined
                    var side = delta < 0 ? before : after;
                    if (!side.TryGetValue(id, out var parts)) side[id] = parts = new SortedList<int, string>();
                    parts[delta] = text;
                }
            }

            var result = new List<FtsSnippetText>(texts.Count);
            foreach (var entry in texts)
            {
                result.Add(new FtsSnippetText
                {
                    LineId = entry.Key,
                    Text = entry.Value,
                    Before = before.TryGetValue(entry.Key, out var b) ? string.Join(" ", b.Values) : "",
                    After = after.TryGetValue(entry.Key, out var a) ? string.Join(" ", a.Values) : "",
                });
            }
            return result;
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
        /// self-join per chunk of ids; the searcher does not use this (snippet context is
        /// <see cref="FetchSnippetTexts"/>) — it only backs the plain corpus seam.
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
                        command.Parameters[parameterNames[i]].Value = StoredId(ids[start + i]);

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        int matchedId = LocalId(reader.GetInt64(0));
                        int delta = reader.GetInt32(1);
                        string text = _codec.DecodeColumnOrEmpty(reader, 2);
                        if (text.Length == 0) continue;
                        int neighbourId = LocalId(reader.GetInt64(3));

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
