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

        /// <summary>The database has a <c>line_note</c> table (Otzaria schema 7): each book with
        /// footnotes gets a virtual notes book right after it (<see cref="SeforimDbNoteIds"/>,
        /// issue #13). False = every read is exactly as before.</summary>
        private readonly bool _hasNotes;

        public SeforimDbFtsCorpus(string databasePath)
        {
            _connection = SqliteConnectionFactory.OpenCorpusRead(databasePath);
            _schema = SeforimDbSchema.ForDatabase(_connection, databasePath);
            _codec = SeforimDbLineContentCodec.Create(_connection);
            _hasNotes = SeforimDbSchema.TableExists(_connection, "line_note");

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
            long lines = (long)command.ExecuteScalar()!;
            if (!_hasNotes) return lines;
            command.CommandText = SeforimDbSqlStrings.FtsCountNotes;
            return lines + (long)command.ExecuteScalar()!;
        }

        public long CountDocumentsUpTo(long upToId)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsCountLinesUpTo;
            command.Parameters.AddWithValue("@id", upToId);
            return (long)command.ExecuteScalar()!;
        }

        public string? GetDocumentText(long id)
        {
            if (_hasNotes && SeforimDbNoteIds.IsVirtual(id))
            {
                foreach (var note in FetchNotes(new[] { id })) return DecodeNote(note.Body, id);
                return null;
            }
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsGetLineContent(_schema);
            command.Parameters.AddWithValue("@id", id);
            return _codec.Decode(command.ExecuteScalar(), id);
        }

        public IEnumerable<(long Id, string Text)> ReadDocuments(int limit, CancellationToken ct = default)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsReadLines(_schema, limit > 0);
            if (limit > 0) command.Parameters.AddWithValue("@lim", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                yield return (reader.GetInt64(0), _codec.DecodeColumnOrEmpty(reader, 1, reader.GetInt64(0)));
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
        public IEnumerable<(long Id, string Text)> ReadDocumentsInResultOrder(long skip, CancellationToken ct = default)
        {
            // The guard, once per build: an id the notes encoding cannot hold fails the build
            // here, naming it, instead of colliding silently.
            if (_hasNotes) SeforimDbNoteIds.EnsureRepresentable(_connection);
            var (bookOrder, lineCounts) = BooksInResultOrder();

            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsReadBookLines(_schema);
            var pBook = command.Parameters.Add("@b", SqliteType.Integer);
            var pSkip = command.Parameters.Add("@skip", SqliteType.Integer);
            using var notes = _connection.CreateCommand();
            notes.CommandText = SeforimDbSqlStrings.FtsReadBookNotes;
            var pNotesBook = notes.Parameters.Add("@b", SqliteType.Integer);
            var pNotesSkip = notes.Parameters.Add("@skip", SqliteType.Integer);
            foreach (long bookId in bookOrder)
            {
                long lines = lineCounts[bookId];
                if (skip >= lines) { skip -= lines; continue; }
                if (SeforimDbNoteIds.TryDecodeBook(bookId, out long sourceBookId))
                {
                    // A notes book: the source book's notes in source order, the body alone.
                    pNotesBook.Value = sourceBookId;
                    pNotesSkip.Value = skip;
                    skip = 0;
                    using var noteReader = notes.ExecuteReader();
                    while (noteReader.Read())
                    {
                        ct.ThrowIfCancellationRequested();
                        long id = SeforimDbNoteIds.EncodeLine(noteReader.GetInt64(0), noteReader.GetInt64(1));
                        yield return (id, DecodeNote(noteReader.IsDBNull(2) ? null : noteReader.GetValue(2), id));
                    }
                    continue;
                }
                pBook.Value = bookId;
                pSkip.Value = skip;
                skip = 0;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    ct.ThrowIfCancellationRequested();
                    yield return (reader.GetInt64(0), _codec.DecodeColumnOrEmpty(reader, 1, reader.GetInt64(0)));
                }
            }
        }

        /// <summary>
        /// The books in result order with their line counts — THE definition of the index's
        /// order, read by the build (<see cref="ReadDocumentsInResultOrder"/>) and by
        /// <see cref="BookPositionRanges"/>, so the two can never disagree.
        /// </summary>
        private (List<long> BookOrder, Dictionary<long, long> LineCounts) BooksInResultOrder()
        {
            var treeOrder = SeforimDbBookTreeOrder.Load(_connection);

            var lineCounts = new Dictionary<long, long>();
            using (var count = _connection.CreateCommand())
            {
                count.CommandText = SeforimDbSqlStrings.FtsLineCountsByBook;
                using var r = count.ExecuteReader();
                while (r.Read()) lineCounts[r.GetInt64(0)] = r.GetInt64(1);
            }

            var bookOrder = new List<long>(lineCounts.Keys);
            bookOrder.Sort((a, b) =>
            {
                bool hasA = treeOrder.TryGetValue(checked((int)a), out int rankA), hasB = treeOrder.TryGetValue(checked((int)b), out int rankB);
                if (hasA != hasB) return hasA ? -1 : 1;
                return hasA ? rankA.CompareTo(rankB) : a.CompareTo(b);
            });
            if (!_hasNotes) return (bookOrder, lineCounts);

            // Each book with notes is followed by its notes book (issue #13).
            var noteCounts = new Dictionary<long, long>();
            using (var count = _connection.CreateCommand())
            {
                count.CommandText = SeforimDbSqlStrings.FtsNoteCountsByBook;
                using var r = count.ExecuteReader();
                while (r.Read()) noteCounts[r.GetInt64(0)] = r.GetInt64(1);
            }
            if (noteCounts.Count == 0) return (bookOrder, lineCounts);
            var withNotes = new List<long>(bookOrder.Count + noteCounts.Count);
            foreach (long bookId in bookOrder)
            {
                withNotes.Add(bookId);
                if (!noteCounts.TryGetValue(bookId, out long notes)) continue;
                long notesBook = SeforimDbNoteIds.EncodeBook(bookId);
                withNotes.Add(notesBook);
                lineCounts[notesBook] = notes;
            }
            return (withNotes, lineCounts);
        }

        /// <summary>
        /// Each book's block of index positions: the build numbers lines 1, 2, 3… in result
        /// order, a book's lines in reading order, so a book is the contiguous run
        /// [First, First + Count - 1] and its line at lineIndex i is position First + i.
        /// </summary>
        public Dictionary<long, (long First, long Count)> BookPositionRanges()
        {
            var (bookOrder, lineCounts) = BooksInResultOrder();
            var ranges = new Dictionary<long, (long First, long Count)>(bookOrder.Count);
            long next = 1;
            foreach (long bookId in bookOrder)
            {
                long count = lineCounts[bookId];
                ranges[bookId] = (next, count);
                next += count;
            }
            return ranges;
        }

        public IEnumerable<(long Id, string Text)> ReadDocumentsAfter(long afterId, int limit = 0, CancellationToken ct = default)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsReadLinesAfter(_schema, limit > 0);
            command.Parameters.AddWithValue("@after", afterId);
            if (limit > 0) command.Parameters.AddWithValue("@lim", limit);

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                yield return (reader.GetInt64(0), _codec.DecodeColumnOrEmpty(reader, 1, reader.GetInt64(0)));
            }
        }

        /// <summary>Search-result content without placement - the plain corpus seam, and the
        /// searcher's verification fetch: id and text only, by primary key from the table that
        /// holds the text (<see cref="SeforimDbSqlStrings.FtsFetchLines"/>). The Title slot is
        /// left empty on purpose: Core enriches hits with their book ID instead and never reads
        /// the engine's title. The engine's own diagnostics run over the test corpus, which
        /// does fill it.</summary>
        public IEnumerable<(long Id, string Text, string Title)> FetchDocuments(IEnumerable<long> ids)
        {
            var chunk = new List<long>(SqliteInListChunkSize);
            List<long>? noteIds = null;
            using var command = _connection.CreateCommand();
            string[] parameterNames = AddChunkParameters(command, SqliteInListChunkSize);

            foreach (long id in ids)
            {
                if (_hasNotes && SeforimDbNoteIds.IsVirtual(id)) { (noteIds ??= new List<long>()).Add(id); continue; }
                chunk.Add(id);
                if (chunk.Count == SqliteInListChunkSize)
                {
                    foreach (var row in FetchTextChunk(command, parameterNames, chunk)) yield return row;
                    chunk.Clear();
                }
            }

            if (chunk.Count > 0)
                foreach (var row in FetchTextChunk(command, parameterNames, chunk)) yield return row;

            if (noteIds != null)
                foreach (var note in FetchNotes(noteIds))
                    yield return (note.Id, DecodeNoteForSearch(note.Body, note.Id), "");
        }

        private IEnumerable<(long Id, string Text, string Title)> FetchTextChunk(
            SqliteCommand command, string[] parameterNames, List<long> ids)
        {
            command.CommandText = SeforimDbSqlStrings.FtsFetchLines(_schema, ids.Count);
            for (int i = 0; i < ids.Count; i++)
                command.Parameters[parameterNames[i]].Value = ids[i];

            using var reader = command.ExecuteReader();
            while (reader.Read())
                yield return (reader.GetInt64(0), DecodeForSearch(reader, 1, reader.GetInt64(0)), "");
        }

        /// <summary>
        /// The placement (book id, lineIndex) of each line, no text - what the
        /// searcher reads AFTER verification, for the hits that passed
        /// (<see cref="SeforimDbSqlStrings.FtsFetchPlacements"/>). An id with no line is absent.
        /// </summary>
        public Dictionary<long, FtsDocumentPlacement> FetchPlacements(IReadOnlyList<long> ids)
        {
            var placements = new Dictionary<long, FtsDocumentPlacement>(ids?.Count ?? 0);
            if (ids == null || ids.Count == 0) return placements;

            // A note is placed in its notes book; lineIndex = its source line's.
            if (_hasNotes)
            {
                List<long>? noteIds = null, realIds = null;
                foreach (long id in ids)
                    if (SeforimDbNoteIds.IsVirtual(id)) (noteIds ??= new List<long>()).Add(id);
                    else (realIds ??= new List<long>()).Add(id);
                if (noteIds != null)
                {
                    foreach (var note in FetchNotes(noteIds))
                        placements[note.Id] = new FtsDocumentPlacement(SeforimDbNoteIds.EncodeBook(note.BookId), note.LineIndex);
                    ids = realIds ?? (IReadOnlyList<long>)Array.Empty<long>();
                    if (ids.Count == 0) return placements;
                }
            }

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
                    placements[reader.GetInt64(0)] = new FtsDocumentPlacement(
                        bookId: reader.IsDBNull(1) ? 0 : reader.GetInt64(1),
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
        public long? PositionOfLine(long lineId, Dictionary<long, (long First, long Count)> books)
        {
            if (SeforimDbNoteIds.TryDecodeLine(lineId, out long sourceLineId, out int noteIndex))
            {
                foreach (var note in FetchNotes(new[] { lineId }))
                {
                    if (!books.TryGetValue(SeforimDbNoteIds.EncodeBook(note.BookId), out var notesBook)) return null;
                    using var rankCommand = _connection.CreateCommand();
                    rankCommand.CommandText = SeforimDbSqlStrings.FtsNoteRankInBook;
                    rankCommand.Parameters.AddWithValue("@b", note.BookId);
                    rankCommand.Parameters.AddWithValue("@li", note.LineIndex);
                    rankCommand.Parameters.AddWithValue("@id", sourceLineId);
                    rankCommand.Parameters.AddWithValue("@ni", noteIndex);
                    long noteRank = (long)rankCommand.ExecuteScalar()!;
                    return noteRank < notesBook.Count ? notesBook.First + noteRank : null;
                }
                return null;
            }
            var placement = FetchPlacements(new[] { lineId });
            if (!placement.TryGetValue(lineId, out var at) || !books.TryGetValue(at.BookId, out var book)) return null;
            using var command = _connection.CreateCommand();
            command.CommandText = SeforimDbSqlStrings.FtsLineRankInBook;
            command.Parameters.AddWithValue("@b", at.BookId);
            command.Parameters.AddWithValue("@li", at.LineIndex);
            command.Parameters.AddWithValue("@id", lineId);
            long rank = (long)command.ExecuteScalar()!;
            return rank < book.Count ? book.First + rank : null;
        }

        /// <summary>Search-result content plus each line's placement (book id, lineIndex),
        /// from the one `line` row the content fetch reads - what lets the
        /// searcher finish a hit without a second query. See
        /// <see cref="SeforimDbSqlStrings.FtsFetchPlacedLines"/>.</summary>
        public IEnumerable<(long Id, string Text, FtsDocumentPlacement Placement)> FetchPlacedDocuments(IEnumerable<long> ids)
        {
            var chunk = new List<long>(SqliteInListChunkSize);
            List<long>? noteIds = null;

            using var command = _connection.CreateCommand();
            string[] parameterNames = AddChunkParameters(command, SqliteInListChunkSize);

            foreach (long id in ids)
            {
                if (_hasNotes && SeforimDbNoteIds.IsVirtual(id)) { (noteIds ??= new List<long>()).Add(id); continue; }
                chunk.Add(id);
                if (chunk.Count == SqliteInListChunkSize)
                {
                    foreach (var row in FetchPlacedChunk(command, parameterNames, chunk)) yield return row;
                    chunk.Clear();
                }
            }

            if (chunk.Count > 0)
                foreach (var row in FetchPlacedChunk(command, parameterNames, chunk)) yield return row;

            if (noteIds != null)
                foreach (var note in FetchNotes(noteIds))
                    yield return (note.Id, DecodeNoteForSearch(note.Body, note.Id),
                                  new FtsDocumentPlacement(SeforimDbNoteIds.EncodeBook(note.BookId), note.LineIndex));
        }

        private IEnumerable<(long Id, string Text, FtsDocumentPlacement Placement)> FetchPlacedChunk(
            SqliteCommand command, string[] parameterNames, List<long> ids)
        {
            command.CommandText = SeforimDbSqlStrings.FtsFetchPlacedLines(_schema, ids.Count);
            for (int i = 0; i < ids.Count; i++)
                command.Parameters[parameterNames[i]].Value = ids[i];

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var placement = new FtsDocumentPlacement(
                    bookId: reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                    lineIndex: reader.IsDBNull(3) ? 0 : reader.GetInt32(3));
                yield return (reader.GetInt64(0), DecodeForSearch(reader, 1, reader.GetInt64(0)), placement);
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
        public List<FtsSnippetText> FetchSnippetTexts(IReadOnlyList<long> lineIds, IReadOnlyCollection<long> shortLineIds)
        {
            var shortSet = new HashSet<long>(shortLineIds);
            var plain = new List<long>(lineIds.Count);
            foreach (long id in lineIds) if (!shortSet.Contains(id)) plain.Add(id);
            var shortIds = new List<long>(shortSet);

            var texts = new Dictionary<long, string>(plain.Count + shortIds.Count);
            var unreadable = new HashSet<long>();

            // A note's snippet is its text alone: no neighbour context (issue #13).
            List<long>? noteIds = null;
            if (_hasNotes)
            {
                foreach (long id in plain) if (SeforimDbNoteIds.IsVirtual(id)) (noteIds ??= new List<long>()).Add(id);
                foreach (long id in shortIds) if (SeforimDbNoteIds.IsVirtual(id)) (noteIds ??= new List<long>()).Add(id);
                if (noteIds != null)
                {
                    plain.RemoveAll(SeforimDbNoteIds.IsVirtual);
                    shortIds.RemoveAll(SeforimDbNoteIds.IsVirtual);
                    foreach (var note in FetchNotes(noteIds))
                        texts[note.Id] = DecodeNoteForSearch(note.Body, note.Id, unreadable);
                }
            }
            var before = new Dictionary<long, SortedList<int, string>>();
            var after = new Dictionary<long, SortedList<int, string>>();

            // One chunk stays under SQLite's 999 variables (@radius is one more).
            const int perStatement = SqliteInListChunkSize - 1;
            int p = 0, q = 0;
            while (p < plain.Count || q < shortIds.Count)
            {
                int plainCount = Math.Min(perStatement, plain.Count - p);
                int shortCount = Math.Min(perStatement - plainCount, shortIds.Count - q);
                using var command = _connection.CreateCommand();
                command.CommandText = SeforimDbSqlStrings.FtsFetchSnippetLines(_schema, plainCount, shortCount);
                for (int i = 0; i < plainCount; i++) command.Parameters.AddWithValue("@p" + i, plain[p + i]);
                for (int i = 0; i < shortCount; i++) command.Parameters.AddWithValue("@s" + i, shortIds[q + i]);
                if (shortCount > 0) command.Parameters.AddWithValue("@radius", SnippetNeighbourLineRadius);
                p += plainCount;
                q += shortCount;

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long id = reader.GetInt64(0);
                    int delta = reader.GetInt32(1);
                    // A neighbour that cannot be read is context only: it is left out, and
                    // only the matched line itself is reported unreadable.
                    string text = delta == 0
                        ? DecodeForSearch(reader, 2, id, unreadable, alwaysLenient: true)
                        : DecodeForSearch(reader, 2, -1, null, alwaysLenient: true);
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
                    Unreadable = unreadable.Contains(entry.Key),
                });
            }
            return result;
        }

        public IDictionary<long, (string Previous, string Next)> FetchNeighbourText(
            IReadOnlyList<long> ids, int radius)
        {
            var neighbours = new Dictionary<long, (string Previous, string Next)>(ids?.Count ?? 0);
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
        private IDictionary<long, SeforimDbFtsNeighbourLines> FetchNeighbourLines(
            IReadOnlyList<long>? ids, int radius)
        {
            var neighbours = new Dictionary<long, SeforimDbFtsNeighbourLines>(ids?.Count ?? 0);
            if (ids == null || ids.Count == 0 || radius <= 0) return neighbours;

            // Neighbour lines per matched id, keyed by their lineIndex delta, so each side joins
            // back in document order: previous = negative deltas ascending, next = positive.
            var previousParts = new Dictionary<long, SortedList<int, (long Id, string Text)>>();
            var nextParts = new Dictionary<long, SortedList<int, (long Id, string Text)>>();

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
                        long matchedId = reader.GetInt64(0);
                        int delta = reader.GetInt32(1);
                        string text = _codec.DecodeColumnOrEmpty(reader, 2, reader.GetInt64(3));
                        if (text.Length == 0) continue;
                        long neighbourId = reader.GetInt64(3);

                        var side = delta < 0 ? previousParts : nextParts;
                        if (!side.TryGetValue(matchedId, out var parts))
                            side[matchedId] = parts = new SortedList<int, (long, string)>();
                        parts[delta] = (neighbourId, text);
                    }
                }
            }

            foreach (long id in ids)
            {
                var entry = new SeforimDbFtsNeighbourLines();
                if (previousParts.TryGetValue(id, out var p)) Collect(p, entry.PreviousIds, out entry.Previous);
                if (nextParts.TryGetValue(id, out var n)) Collect(n, entry.NextIds, out entry.Next);
                if (entry.Previous.Length > 0 || entry.Next.Length > 0) neighbours[id] = entry;
            }

            return neighbours;

            static void Collect(SortedList<int, (long Id, string Text)> parts, List<long> idsOut, out string joined)
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

        /// <summary>
        /// The notes behind the given virtual line ids, with their source placement (book id,
        /// lineIndex) and RAW body; the caller decodes. A note with no row (or no line row) is
        /// absent. One statement per chunk of distinct source lines.
        /// </summary>
        private IEnumerable<(long Id, object? Body, long BookId, int LineIndex)> FetchNotes(IEnumerable<long> virtualIds)
        {
            if (!_hasNotes) yield break;
            var wanted = new HashSet<long>();
            var sourceLines = new List<long>();
            var seenLines = new HashSet<long>();
            foreach (long id in virtualIds)
            {
                if (!SeforimDbNoteIds.TryDecodeLine(id, out long lineId, out _) || !wanted.Add(id)) continue;
                if (seenLines.Add(lineId)) sourceLines.Add(lineId);
            }
            for (int start = 0; start < sourceLines.Count; start += SqliteInListChunkSize)
            {
                int count = Math.Min(SqliteInListChunkSize, sourceLines.Count - start);
                using var command = _connection.CreateCommand();
                command.CommandText = SeforimDbSqlStrings.FtsFetchNotes(count);
                for (int i = 0; i < count; i++) command.Parameters.AddWithValue("@p" + i, sourceLines[start + i]);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    long lineId = reader.GetInt64(0), noteIndex = reader.GetInt64(1);
                    if (lineId < 0 || lineId > SeforimDbNoteIds.MaxSourceLineId || noteIndex < 0 || noteIndex > SeforimDbNoteIds.MaxNoteIndex) continue;
                    long id = SeforimDbNoteIds.EncodeLine(lineId, noteIndex);
                    if (!wanted.Contains(id)) continue;
                    yield return (id, reader.IsDBNull(2) ? null : reader.GetValue(2),
                                  reader.IsDBNull(3) ? 0 : reader.GetInt64(3), reader.IsDBNull(4) ? 0 : reader.GetInt32(4));
                }
            }
        }

        /// <summary>A note body, strict (the index build): an undecodable body throws an error
        /// naming the note and its line.</summary>
        private string DecodeNote(object? body, long virtualId)
        {
            try { return _codec.Decode(body) ?? string.Empty; }
            catch (SeforimDbContentCodecException exception)
            {
                SeforimDbNoteIds.TryDecodeLine(virtualId, out long lineId, out int noteIndex);
                throw new SeforimDbContentCodecException("Note " + noteIndex + " of line " + lineId + ": " + exception.Message);
            }
        }

        /// <summary>
        /// A line's text on the SEARCH path. Inside a search's
        /// <see cref="SeforimDbFtsUnreadableRows"/> scope (or with <paramref name="alwaysLenient"/>)
        /// an undecodable row reads as empty and is recorded with its id (in the scope and in
        /// <paramref name="unreadable"/>); otherwise it throws, naming the line (issue #8).
        /// </summary>
        private string DecodeForSearch(SqliteDataReader reader, int ordinal, long lineId,
                                       HashSet<long>? unreadable = null, bool alwaysLenient = false)
        {
            try { return _codec.DecodeColumnOrEmpty(reader, ordinal, lineId); }
            catch (SeforimDbContentCodecException exception)
                when (alwaysLenient || SeforimDbFtsUnreadableRows.Current != null)
            {
                if (lineId >= 0)
                {
                    SeforimDbFtsUnreadableRows.Current?.Add(lineId, exception.Message);
                    unreadable?.Add(lineId);
                }
                return string.Empty;
            }
        }

        /// <summary>DecodeForSearch for a note body. Snippet texts (<paramref name="unreadable"/>
        /// given) are always lenient.</summary>
        private string DecodeNoteForSearch(object? body, long virtualId, HashSet<long>? unreadable = null)
        {
            try { return DecodeNote(body, virtualId); }
            catch (SeforimDbContentCodecException exception)
                when (unreadable != null || SeforimDbFtsUnreadableRows.Current != null)
            {
                SeforimDbFtsUnreadableRows.Current?.Add(virtualId, exception.Message);
                unreadable?.Add(virtualId);
                return string.Empty;
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
            // The connection goes back to the SAME pool book reads draw from, and pragmas stay
            // on the pooled handle: put back the standard read policy, or a later book read
            // inherits this scan's 64 MB page cache.
            try { SqliteConnectionFactory.RestoreCorpusReadPragmas(_connection); }
            finally { _connection.Dispose(); }
        }
    }

    /// <summary>One matched line's same-book neighbours: each side's joined text and the ids
    /// it was joined from, in document order. See <see cref="SeforimDbFtsCorpus.FetchNeighbourText"/>.</summary>
    internal sealed class SeforimDbFtsNeighbourLines
    {
        public string Previous = "";
        public string Next = "";
        public readonly List<long> PreviousIds = new List<long>();
        public readonly List<long> NextIds = new List<long>();
    }
}
