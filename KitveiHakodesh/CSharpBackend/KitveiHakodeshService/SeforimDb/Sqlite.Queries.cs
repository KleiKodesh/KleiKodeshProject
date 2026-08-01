using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// Query logic for the seforim DB and the optional Otzaria personal-books DB
/// (user_books.db). SQL strings live in Sqlite.Strings.cs (SeforimSql); this file
/// maps parameters, routes each call to the right database, and reads rows.
///
/// ROUTING. The two databases number their rows independently, so personal-book ids
/// are shifted by <see cref="CorpusIds.UserBooksBase"/> at this API boundary: every
/// method takes and returns APP-VISIBLE ids. Library ids pass through unshifted, which
/// keeps the library path byte-identical to the pre-personal-books code. Three shapes,
/// chosen per method by where ids flow (mis-classifying fails silently — the
/// regression test covers every method against both real databases):
///   - ROUTE: an inbound id picks the database; outbound ids are shifted back.
///   - SPLIT-MERGE: an inbound id LIST may span both corpora (search results do);
///     fetch per corpus and concatenate.
///   - UNION: no inbound id (enumeration / title lookup); query both, shift user rows.
///
/// CONNECTION TYPES are the exception to shifting: both DBs assign connection_type
/// ids lazily in encounter order, so the same id means DIFFERENT types per DB (real
/// data: user 2=COMMENTARY vs library 2=SUPER_COMMENTARY). Type ids are translated by
/// NAME instead — see <see cref="ToAppConnTypeId"/> / <see cref="ToLocalConnTypeIds"/>.
///
/// SCHEMA PROBES are per-corpus: the library (Zayit-built) and user_books.db
/// (Otzaria-built) answer every probe differently (link.targetLineIndex, link_anchor,
/// tocEntry.lineIndex). A single cached probe would let whichever DB is queried first
/// decide for both — wrong SQL on one of them.
/// </summary>
public sealed partial class SeforimDbService
{
    // ── Per-corpus schema probes ────────────────────────────────────────────────
    // Indexed by (int)Corpus. Schema generation is fixed per file (even a recreated
    // user_books.db comes from the same Otzaria build), so no change-stamp needed.

    private readonly bool?[] _tocHasLineIndex = new bool?[2];
    private readonly bool?[] _linkHasTargetLineIndex = new bool?[2];
    private readonly bool?[] _hasLinkAnchorTable = new bool?[2];

    private bool TocHasLineIndex(SqliteConnection conn, Corpus corpus) =>
        _tocHasLineIndex[(int)corpus] ??= ColumnExists(conn, "tocEntry", "lineIndex");

    private bool LinkHasTargetLineIndex(SqliteConnection conn, Corpus corpus) =>
        _linkHasTargetLineIndex[(int)corpus] ??= ColumnExists(conn, "link", "targetLineIndex");

    private bool HasLinkAnchor(SqliteConnection conn, Corpus corpus) =>
        _hasLinkAnchorTable[(int)corpus] ??= TableExists(conn, "link_anchor");

    // ── Connection-type translation (by NAME, never by shift) ──────────────────

    private sealed class ConnTypeMaps
    {
        public readonly Dictionary<int, string> IdToName = new();
        public readonly Dictionary<string, int> NameToId = new(StringComparer.Ordinal);
    }

    private readonly ConnTypeMaps?[] _connTypes = new ConnTypeMaps?[2];
    private long _connTypeUserStamp = -1;
    private readonly object _connTypeLock = new();

    /// <summary>The (id↔name) maps of one corpus, or null when that DB is unavailable.
    /// The user map is invalidated when user_books.db changes on disk (Otzaria appends
    /// connection types lazily as links are created); the library map loads once.
    ///
    /// The user maps and their stamp are read and stored as an ATOMIC PAIR under
    /// <see cref="_connTypeLock"/>: a load that lost a race stores its (older) maps
    /// together with the stamp it loaded FOR, so the very next stamp compare sees the
    /// mismatch and reloads. Advancing the stamp separately from the maps would pin a
    /// stale generation under the new stamp — translations silently wrong until the DB
    /// changed again.</summary>
    private ConnTypeMaps? GetConnTypes(Corpus corpus)
    {
        if (corpus == Corpus.Library)
        {
            if (!HasDb) return null;
            if (_connTypes[(int)Corpus.Library] is { } cachedLib) return cachedLib;
            var lib = LoadConnTypeMaps(Corpus.Library);
            if (lib is not null) _connTypes[(int)Corpus.Library] = lib; // idempotent content — benign race
            return lib;
        }

        long stamp = UserBooksChangeStamp;
        if (stamp == 0) return null;
        lock (_connTypeLock)
            if (_connTypes[(int)Corpus.UserBooks] is { } cached && _connTypeUserStamp == stamp)
                return cached;

        var maps = LoadConnTypeMaps(Corpus.UserBooks);
        if (maps is null) return null;
        lock (_connTypeLock)
        {
            _connTypes[(int)Corpus.UserBooks] = maps;
            _connTypeUserStamp = stamp;
        }
        return maps;
    }

    private ConnTypeMaps? LoadConnTypeMaps(Corpus corpus)
    {
        var maps = new ConnTypeMaps();
        try
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetAllConnectionTypes;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int id = r.GetInt32(0);
                string name = r.IsDBNull(1) ? "" : r.GetString(1);
                maps.IdToName[id] = name;
                if (name.Length > 0 && !maps.NameToId.ContainsKey(name)) maps.NameToId[name] = id;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "connection-type load failed ({Corpus})", corpus);
            return null;
        }
        return maps;
    }

    /// <summary>
    /// Row-translation snapshot for one query: resolve the maps ONCE before iterating a
    /// reader, not per row — per-row resolution would re-stat user_books.db for every
    /// row and could even mix two map generations inside one result set if Otzaria
    /// writes mid-iteration. Library rows translate as identity without touching maps.
    /// </summary>
    private (ConnTypeMaps? User, ConnTypeMaps? Lib) SnapshotConnTypes(Corpus corpus) =>
        corpus == Corpus.Library ? (null, null) : (GetConnTypes(Corpus.UserBooks), GetConnTypes(Corpus.Library));

    /// <summary>
    /// A connection-type id read from <paramref name="corpus"/>, translated to the
    /// app-visible space: the library's ids are that space, so a user-DB type maps to
    /// the library id of the SAME NAME. A user-only name (no library counterpart)
    /// falls back to its shifted id — kept distinct rather than mislabelled.
    /// </summary>
    private static int ToAppConnTypeId(int localTypeId, Corpus corpus, (ConnTypeMaps? User, ConnTypeMaps? Lib) maps)
    {
        if (corpus == Corpus.Library) return localTypeId;
        if (maps.User is { } user
            && user.IdToName.TryGetValue(localTypeId, out var name)
            && maps.Lib is { } lib
            && lib.NameToId.TryGetValue(name, out int libId))
            return libId;
        return CorpusIds.ToAppId(localTypeId, corpus);
    }

    /// <summary>
    /// Translates app-visible connection-type ids into <paramref name="corpus"/>'s
    /// local ids, DROPPING types that don't exist there — a type id from one DB must
    /// never be sent verbatim to the other. Empty result ⇒ skip that corpus entirely
    /// (an id that names no type there can match no rows).
    /// </summary>
    private List<int> ToLocalConnTypeIds(List<int> appTypeIds, Corpus corpus)
    {
        if (corpus == Corpus.Library)
        {
            // Library ids ARE the app space; only shifted user-only ids need dropping,
            // and the common case (none present) keeps the original list untouched.
            bool anyShifted = false;
            for (int i = 0; i < appTypeIds.Count; i++)
                if (CorpusIds.IsUserBooks(appTypeIds[i])) { anyShifted = true; break; }
            if (!anyShifted) return appTypeIds;

            var kept = new List<int>(appTypeIds.Count);
            for (int i = 0; i < appTypeIds.Count; i++)
                if (!CorpusIds.IsUserBooks(appTypeIds[i])) kept.Add(appTypeIds[i]);
            return kept;
        }

        var user = GetConnTypes(Corpus.UserBooks);
        if (user is null) return [];
        var lib = GetConnTypes(Corpus.Library);

        var local = new List<int>(appTypeIds.Count);
        for (int i = 0; i < appTypeIds.Count; i++)
        {
            int appId = appTypeIds[i];
            if (CorpusIds.IsUserBooks(appId))
            {
                int id = CorpusIds.ToLocalId(appId);
                if (user.IdToName.ContainsKey(id)) local.Add(id);
            }
            else if (lib is not null
                && lib.IdToName.TryGetValue(appId, out var name)
                && user.NameToId.TryGetValue(name, out int userId))
            {
                local.Add(userId);
            }
        }
        return local;
    }

    // ── Catalog ─────────────────────────────────────────────────────────────────
    // The LIBRARY catalog is STATIC for the life of the process — seforim.db is
    // read-only and only ever replaced while the service is down — and it's the
    // heaviest catalog cost (getAllBooks is a 3-table GROUP BY + group_concat over
    // every book, ~70-90ms), so it's cached once. user_books.db, by contrast, changes
    // while the service runs (the user adds books in Otzaria), so the MERGED catalog
    // is keyed on UserBooksChangeStamp and only the user part is re-read on change.

    private List<CategoryRow>? _libCategoriesCache;
    private List<BookRow>? _libBooksCache;
    private List<CategoryRow>? _categoriesCache;
    private List<BookRow>? _booksCache;
    private long _categoriesStamp = -1;
    private long _booksStamp = -1;
    private readonly object _catalogCacheLock = new();

    public List<CategoryRow> GetAllCategories()
    {
        long stamp = UserBooksChangeStamp;
        lock (_catalogCacheLock)
            if (_categoriesCache is { } cached && _categoriesStamp == stamp) return cached;

        var lib = LibraryCategories();
        List<CategoryRow> merged;
        bool userOk = true; // failed user read ⇒ serve library-only but DON'T cache it,
                            // so the next call retries instead of pinning until the
                            // file's stamp happens to change.
        if (stamp == 0)
        {
            merged = lib;
        }
        else
        {
            userOk = false;
            var user = new List<CategoryRow>();
            Run(Corpus.UserBooks, () =>
            {
                using var conn = Open(Corpus.UserBooks);
                bool hasOrder = ColumnExists(conn, "category", "orderIndex");
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SeforimSql.GetAllCategories(hasOrder);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    user.Add(new CategoryRow
                    {
                        Id = CorpusIds.ToAppId(r.GetInt32(0), Corpus.UserBooks),
                        ParentId = CorpusIds.ToAppId(r.IsDBNull(1) ? null : r.GetInt32(1), Corpus.UserBooks),
                        Title = r.IsDBNull(2) ? "" : r.GetString(2),
                        Level = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                    });
                }
                userOk = true;
            }, "getAllCategories");

            merged = new List<CategoryRow>(lib.Count + user.Count);
            merged.AddRange(lib);
            merged.AddRange(user);
        }

        if (merged.Count > 0 && userOk)
        {
            lock (_catalogCacheLock)
            {
                _categoriesCache = merged;
                _categoriesStamp = stamp;
            }
        }
        return merged;
    }

    private List<CategoryRow> LibraryCategories()
    {
        lock (_catalogCacheLock)
            if (_libCategoriesCache is { } cached) return cached;

        var list = new List<CategoryRow>();
        Run(() =>
        {
            using var conn = Open();
            // Detect the optional orderIndex column at query time (mirrors the
            // frontend's ensureCategorySchema()).
            bool hasOrder = ColumnExists(conn, "category", "orderIndex");
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetAllCategories(hasOrder);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new CategoryRow
                {
                    Id = r.GetInt32(0),
                    ParentId = r.IsDBNull(1) ? null : r.GetInt32(1),
                    Title = r.IsDBNull(2) ? "" : r.GetString(2),
                    Level = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                });
            }
        }, "getAllCategories");

        if (list.Count > 0)
            lock (_catalogCacheLock) _libCategoriesCache ??= list;
        return list;
    }

    public List<BookRow> GetAllBooks()
    {
        long stamp = UserBooksChangeStamp;
        lock (_catalogCacheLock)
            if (_booksCache is { } cached && _booksStamp == stamp) return cached;

        var lib = LibraryBooks();
        List<BookRow> merged;
        bool userOk = true; // see GetAllCategories — a failed user read must not be cached
        if (stamp == 0)
        {
            merged = lib;
        }
        else
        {
            userOk = false;
            var user = new List<BookRow>();
            Run(Corpus.UserBooks, () =>
            {
                using var conn = Open(Corpus.UserBooks);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SeforimSql.GetAllBooks;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    user.Add(new BookRow
                    {
                        Id = CorpusIds.ToAppId(r.GetInt32(0), Corpus.UserBooks),
                        CategoryId = CorpusIds.ToAppId(r.IsDBNull(1) ? 0 : r.GetInt32(1), Corpus.UserBooks),
                        Title = r.IsDBNull(2) ? "" : r.GetString(2),
                        HasTeamim = r.IsDBNull(3) ? null : r.GetInt32(3),
                        Authors = r.IsDBNull(4) ? null : r.GetString(4),
                    });
                }
                userOk = true;
            }, "getAllBooks");

            merged = new List<BookRow>(lib.Count + user.Count);
            merged.AddRange(lib);
            merged.AddRange(user);
        }

        if (merged.Count > 0 && userOk)
        {
            lock (_catalogCacheLock)
            {
                _booksCache = merged;
                _booksStamp = stamp;
            }
        }
        return merged;
    }

    private List<BookRow> LibraryBooks()
    {
        lock (_catalogCacheLock)
            if (_libBooksCache is { } cached) return cached;

        var list = new List<BookRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetAllBooks;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new BookRow
                {
                    Id = r.GetInt32(0),
                    CategoryId = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    Title = r.IsDBNull(2) ? "" : r.GetString(2),
                    HasTeamim = r.IsDBNull(3) ? null : r.GetInt32(3),
                    Authors = r.IsDBNull(4) ? null : r.GetString(4),
                });
            }
        }, "getAllBooks");

        if (list.Count > 0)
            lock (_catalogCacheLock) _libBooksCache ??= list;
        return list;
    }

    // ── Book + lines ──────────────────────────────────────────────────────────

    public BookInfo? GetBookById(int id)
    {
        var corpus = CorpusIds.CorpusOf(id);
        BookInfo? book = null;
        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetBookById;
            cmd.Parameters.AddWithValue("@id", CorpusIds.ToLocalId(id));
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                book = new BookInfo
                {
                    TotalLines = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                    HasTeamim = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    HasTargumConnection = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                    HasReferenceConnection = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                    HasSourceConnection = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    HasCommentaryConnection = r.IsDBNull(5) ? 0 : r.GetInt32(5),
                    HasOtherConnection = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                };
            }
        }, "getBookById");
        return book;
    }

    public List<LineRow> GetLinesPaged(int bookId, int limit, int offset)
    {
        var corpus = CorpusIds.CorpusOf(bookId);
        var list = new List<LineRow>();
        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetLinesPaged;
            cmd.Parameters.AddWithValue("@bookId", CorpusIds.ToLocalId(bookId));
            cmd.Parameters.AddWithValue("@limit", limit);
            cmd.Parameters.AddWithValue("@offset", offset);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new LineRow
                {
                    Id = CorpusIds.ToAppId(r.GetInt32(0), corpus),
                    LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    Content = r.IsDBNull(2) ? "" : r.GetString(2),
                });
            }
        }, "getLinesPaged");
        return list;
    }

    // ── TOC ──────────────────────────────────────────────────────────────────

    public List<TocEntryRow> GetAllTocEntries(int bookId)
    {
        var corpus = CorpusIds.CorpusOf(bookId);
        return ReadTocEntries(
            corpus,
            hasIdx => SeforimSql.GetAllTocEntries(hasIdx),
            ("@bookId", CorpusIds.ToLocalId(bookId)));
    }

    public List<TocEntryRow> GetAllAltTocEntries(int structureId)
    {
        var corpus = CorpusIds.CorpusOf(structureId);
        return ReadTocEntries(
            corpus,
            _ => SeforimSql.GetAllAltTocEntries, // alt_toc_entry never has lineIndex
            ("@structureId", CorpusIds.ToLocalId(structureId)));
    }

    private List<TocEntryRow> ReadTocEntries(Corpus corpus, Func<bool, string> sql, params (string, object)[] ps)
    {
        var list = new List<TocEntryRow>();
        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql(TocHasLineIndex(conn, corpus));
            foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new TocEntryRow
                {
                    Id = CorpusIds.ToAppId(r.GetInt32(0), corpus),
                    ParentId = CorpusIds.ToAppId(r.IsDBNull(1) ? null : r.GetInt32(1), corpus),
                    Level = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                    LineId = CorpusIds.ToAppId(r.IsDBNull(3) ? null : r.GetInt32(3), corpus),
                    HasChildren = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    Text = r.IsDBNull(5) ? "" : r.GetString(5),
                    LineIndex = r.IsDBNull(6) ? null : r.GetInt32(6),
                });
            }
        }, "getTocEntries");
        return list;
    }

    public List<AltTocStructureRow> GetAltTocStructures(int bookId)
    {
        var corpus = CorpusIds.CorpusOf(bookId);
        var list = new List<AltTocStructureRow>();
        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetAltTocStructures;
            cmd.Parameters.AddWithValue("@bookId", CorpusIds.ToLocalId(bookId));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new AltTocStructureRow
                {
                    Id = CorpusIds.ToAppId(r.GetInt32(0), corpus),
                    Key = r.IsDBNull(1) ? "" : r.GetString(1),
                    Title = r.IsDBNull(2) ? null : r.GetString(2),
                    HeTitle = r.IsDBNull(3) ? null : r.GetString(3),
                });
            }
        }, "getAltTocStructures");
        return list;
    }

    public List<TocPrefixRow> GetTocEntryByTextPrefix(int bookId, string pattern)
    {
        var corpus = CorpusIds.CorpusOf(bookId);
        var list = new List<TocPrefixRow>();
        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetTocEntryByTextPrefix(TocHasLineIndex(conn, corpus));
            cmd.Parameters.AddWithValue("@bookId", CorpusIds.ToLocalId(bookId));
            cmd.Parameters.AddWithValue("@pattern", pattern);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new TocPrefixRow
                {
                    Id = CorpusIds.ToAppId(r.GetInt32(0), corpus),
                    LineIndex = r.IsDBNull(1) ? null : r.GetInt32(1),
                });
            }
        }, "getTocEntryByTextPrefix");
        return list;
    }

    public List<TocTitleRow> GetTocTitlesForBooks(List<int> bookIds, string? filterWord)
    {
        var list = new List<TocTitleRow>();
        if (bookIds is null || bookIds.Count == 0) return list;

        string word = (filterWord ?? "").ToLowerInvariant();
        bool usePrefilter = word.Length > 0 && IsLikeSafe(word);

        foreach (var (corpus, localIds) in CorpusIds.GroupByCorpus(bookIds))
        {
            Run(corpus, () =>
            {
                using var conn = Open(corpus);
                bool hasIdx = TocHasLineIndex(conn, corpus);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = usePrefilter
                    ? SeforimSql.GetTocTitlesMatchingForBooks(localIds.Count, hasIdx)
                    : SeforimSql.GetTocTitlesForBooks(localIds.Count, hasIdx);
                for (int i = 0; i < localIds.Count; i++) cmd.Parameters.AddWithValue("@b" + i, localIds[i]);
                if (usePrefilter) cmd.Parameters.AddWithValue("@word", EscapeLikeWord(word));
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new TocTitleRow
                    {
                        Id = CorpusIds.ToAppId(r.GetInt32(0), corpus),
                        ParentId = CorpusIds.ToAppId(r.IsDBNull(1) ? null : r.GetInt32(1), corpus),
                        BookId = r.IsDBNull(2) ? 0 : CorpusIds.ToAppId(r.GetInt32(2), corpus),
                        Text = r.IsDBNull(3) ? "" : r.GetString(3),
                        LineIndex = r.IsDBNull(4) ? null : r.GetInt32(4),
                    });
                }
            }, "getTocTitlesForBooks");
        }
        return list;
    }

    // Mirrors the frontend LIKE_SAFE_WORD_RE = /^[ -~֐-׿]+$/ and escapeLikeWord().
    private static bool IsLikeSafe(string word)
    {
        if (word.Length == 0) return false;
        foreach (char c in word)
        {
            bool ascii = c is >= ' ' and <= '~';
            bool hebrew = c is >= '֐' and <= '׿'; // Hebrew block (regex ֐-׿)
            if (!ascii && !hebrew) return false;
        }
        return true;
    }

    private static string EscapeLikeWord(string word)
    {
        var sb = new System.Text.StringBuilder(word.Length);
        foreach (char c in word)
        {
            if (c is '\\' or '%' or '_') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    // ── Commentary / links ──────────────────────────────────────────────────────

    public List<CommentaryLinkRow> GetCommentaryLinksForSourceLineRange(List<int> lineIds)
    {
        var list = new List<CommentaryLinkRow>();
        if (lineIds is null || lineIds.Count == 0) return list;

        foreach (var (corpus, localIds) in CorpusIds.GroupByCorpus(lineIds))
        {
            Run(corpus, () =>
            {
                using var conn = Open(corpus);
                var typeMaps = SnapshotConnTypes(corpus);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SeforimSql.GetCommentaryLinksForSourceLineRange(
                    localIds.Count, LinkHasTargetLineIndex(conn, corpus));
                BindList(cmd, "p", localIds);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new CommentaryLinkRow
                    {
                        TargetBookId = r.IsDBNull(0) ? 0 : CorpusIds.ToAppId(r.GetInt32(0), corpus),
                        TargetLineId = r.IsDBNull(1) ? 0 : CorpusIds.ToAppId(r.GetInt32(1), corpus),
                        ConnectionTypeId = r.IsDBNull(2) ? 0 : ToAppConnTypeId(r.GetInt32(2), corpus, typeMaps),
                        LineIndex = r.IsDBNull(3) ? 0 : r.GetInt32(3),
                    });
                }
            }, "getCommentaryLinksForSourceLineRange");
        }
        return list;
    }

    /// <summary>Word-level link anchors for a batch of source lines. Supported reflects
    /// the LIBRARY's schema (the deployment's primary corpus): false tells callers to
    /// stop asking, and that must not happen just because one batch was all personal-book
    /// lines — those simply contribute no rows when their DB lacks link_anchor.</summary>
    public WordLinkAnchorsResult GetWordLinkAnchorsForLines(List<int> lineIds)
    {
        var result = new WordLinkAnchorsResult();
        if (lineIds is null || lineIds.Count == 0)
        {
            result.Supported = _hasLinkAnchorTable[(int)Corpus.Library] ?? true; // unknown yet — don't tell callers to stop
            return result;
        }

        foreach (var (corpus, localIds) in CorpusIds.GroupByCorpus(lineIds))
        {
            Run(corpus, () =>
            {
                using var conn = Open(corpus);
                if (!HasLinkAnchor(conn, corpus)) return;
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SeforimSql.GetWordLinkAnchorsForLines(
                    localIds.Count, LinkHasTargetLineIndex(conn, corpus));
                BindList(cmd, "p", localIds);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    result.Rows.Add(new WordLinkAnchorRow
                    {
                        LineId = r.IsDBNull(0) ? 0 : CorpusIds.ToAppId(r.GetInt32(0), corpus),
                        CharStart = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                        CharEnd = r.IsDBNull(2) ? null : r.GetInt32(2),
                        Label = r.IsDBNull(3) ? null : r.GetString(3),
                        TargetBookId = r.IsDBNull(4) ? 0 : CorpusIds.ToAppId(r.GetInt32(4), corpus),
                        TargetLineId = r.IsDBNull(5) ? 0 : CorpusIds.ToAppId(r.GetInt32(5), corpus),
                        TargetLineIndex = r.IsDBNull(6) ? 0 : r.GetInt32(6),
                    });
                }
            }, "getWordLinkAnchorsForLines");
        }

        result.Supported = _hasLinkAnchorTable[(int)Corpus.Library] ?? true;
        return result;
    }

    public List<LineContentRow> GetLineContents(List<int> lineIds)
    {
        var list = new List<LineContentRow>();
        if (lineIds is null || lineIds.Count == 0) return list;

        foreach (var (corpus, localIds) in CorpusIds.GroupByCorpus(lineIds))
        {
            Run(corpus, () =>
            {
                using var conn = Open(corpus);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SeforimSql.GetLineContents(localIds.Count);
                BindList(cmd, "p", localIds);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new LineContentRow
                    {
                        Id = CorpusIds.ToAppId(r.GetInt32(0), corpus),
                        Content = r.IsDBNull(1) ? "" : r.GetString(1),
                    });
                }
            }, "getLineContents");
        }
        return list;
    }

    /// <summary>Library connection types verbatim (their ids ARE the app-visible type
    /// space), plus any user-DB types whose NAME the library lacks, with shifted ids —
    /// so the frontend can resolve names for those too. With today's data the user's
    /// names are a subset of the library's, so the output matches the pre-routing one.</summary>
    public List<ConnectionTypeRow> GetAllConnectionTypes()
    {
        var list = new List<ConnectionTypeRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetAllConnectionTypes;
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new ConnectionTypeRow { Id = r.GetInt32(0), Name = r.IsDBNull(1) ? "" : r.GetString(1) });
        }, "getAllConnectionTypes");

        if (GetConnTypes(Corpus.UserBooks) is { } user && GetConnTypes(Corpus.Library) is { } lib)
        {
            foreach (var (id, name) in user.IdToName)
            {
                if (name.Length == 0 || lib.NameToId.ContainsKey(name)) continue;
                list.Add(new ConnectionTypeRow
                {
                    Id = CorpusIds.ToAppId(id, Corpus.UserBooks),
                    Name = name,
                });
            }
        }
        return list;
    }

    public List<DefaultCommentatorRow> GetDefaultCommentators(int bookId)
    {
        var corpus = CorpusIds.CorpusOf(bookId);
        var list = new List<DefaultCommentatorRow>();
        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetDefaultCommentators;
            cmd.Parameters.AddWithValue("@bookId", CorpusIds.ToLocalId(bookId));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new DefaultCommentatorRow { CommentatorBookId = CorpusIds.ToAppId(r.GetInt32(0), corpus) });
        }, "getDefaultCommentators");
        return list;
    }

    // ── Reverse lookups ─────────────────────────────────────────────────────────

    public List<ReverseLineRow> GetReverseLineData(List<int> lineIds, List<int> typeIds)
    {
        var list = new List<ReverseLineRow>();
        if (lineIds is null || lineIds.Count == 0 || typeIds is null || typeIds.Count == 0) return list;

        foreach (var (corpus, localIds) in CorpusIds.GroupByCorpus(lineIds))
        {
            var localTypes = ToLocalConnTypeIds(typeIds, corpus);
            if (localTypes.Count == 0) continue;
            Run(corpus, () =>
            {
                using var conn = Open(corpus);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SeforimSql.GetReverseLineData(localIds.Count, localTypes.Count);
                BindList(cmd, "t", localIds);
                BindList(cmd, "c", localTypes);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new ReverseLineRow
                    {
                        SourceBookId = r.IsDBNull(0) ? 0 : CorpusIds.ToAppId(r.GetInt32(0), corpus),
                        SourceLineId = r.IsDBNull(1) ? 0 : CorpusIds.ToAppId(r.GetInt32(1), corpus),
                        LineIndex = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                        Content = r.IsDBNull(3) ? "" : r.GetString(3),
                    });
                }
            }, "getReverseLineData");
        }
        return list;
    }

    public List<ReverseBookRow> GetReverseBooks(int bookId, List<int> typeIds)
    {
        var list = new List<ReverseBookRow>();
        if (typeIds is null || typeIds.Count == 0) return list;

        var corpus = CorpusIds.CorpusOf(bookId);
        var localTypes = ToLocalConnTypeIds(typeIds, corpus);
        if (localTypes.Count == 0) return list;

        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetReverseBooks(localTypes.Count);
            cmd.Parameters.AddWithValue("@bookId", CorpusIds.ToLocalId(bookId));
            BindList(cmd, "c", localTypes);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new ReverseBookRow { SourceBookId = CorpusIds.ToAppId(r.GetInt32(0), corpus) });
        }, "getReverseBooks");
        return list;
    }

    public List<StaticFilterRow> GetStaticFilterBooks(int sourceBookId, List<int> typeIds)
    {
        var list = new List<StaticFilterRow>();
        if (typeIds is null || typeIds.Count == 0) return list;

        var corpus = CorpusIds.CorpusOf(sourceBookId);
        var localTypes = ToLocalConnTypeIds(typeIds, corpus);
        if (localTypes.Count == 0) return list;

        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            var typeMaps = SnapshotConnTypes(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetStaticFilterBooks(localTypes.Count);
            cmd.Parameters.AddWithValue("@bookId", CorpusIds.ToLocalId(sourceBookId));
            BindList(cmd, "c", localTypes);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new StaticFilterRow
                {
                    TargetBookId = r.IsDBNull(0) ? 0 : CorpusIds.ToAppId(r.GetInt32(0), corpus),
                    ConnectionTypeId = r.IsDBNull(1) ? 0 : ToAppConnTypeId(r.GetInt32(1), corpus, typeMaps),
                });
            }
        }, "getStaticFilterBooks");
        return list;
    }

    // ── Commentary navigation ────────────────────────────────────────────────────
    // Links never span database files, so a main book and a commentary book from
    // different corpora cannot be linked — those calls return empty by definition.

    public List<SectionNavRow> GetSectionWithCommentary(int mainBookId, int commentaryBookId, int lineIndex, bool next)
    {
        var list = new List<SectionNavRow>();
        var corpus = CorpusIds.CorpusOf(mainBookId);
        if (CorpusIds.CorpusOf(commentaryBookId) != corpus) return list;

        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetSectionWithCommentary(next);
            cmd.Parameters.AddWithValue("@mainBookId", CorpusIds.ToLocalId(mainBookId));
            cmd.Parameters.AddWithValue("@commentaryBookId", CorpusIds.ToLocalId(commentaryBookId));
            cmd.Parameters.AddWithValue("@lineIndex", lineIndex);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new SectionNavRow
                {
                    Id = CorpusIds.ToAppId(r.GetInt32(0), corpus),
                    LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                });
            }
        }, "getSectionWithCommentary");
        return list;
    }

    public List<TocSectionRow> GetTocSectionWithCommentary(int mainBookId, int commentaryBookId, List<int> rangePairs, bool next)
    {
        var list = new List<TocSectionRow>();
        if (rangePairs is null || rangePairs.Count < 2) return list;
        var corpus = CorpusIds.CorpusOf(mainBookId);
        if (CorpusIds.CorpusOf(commentaryBookId) != corpus) return list;

        int count = rangePairs.Count / 2;
        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetTocSectionWithCommentary(count, next);
            for (int i = 0; i < count; i++)
            {
                // Range bounds are lineIndex POSITIONS, not ids — no translation.
                cmd.Parameters.AddWithValue("@s" + i, rangePairs[i * 2]);
                cmd.Parameters.AddWithValue("@e" + i, rangePairs[i * 2 + 1]);
            }
            cmd.Parameters.AddWithValue("@mainBookId", CorpusIds.ToLocalId(mainBookId));
            cmd.Parameters.AddWithValue("@commentaryBookId", CorpusIds.ToLocalId(commentaryBookId));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new TocSectionRow { SectionStart = r.GetInt32(0) });
        }, "getTocSectionWithCommentary");
        return list;
    }

    public List<LinkTargetRow> GetLinkTargetForSourceLineAndBook(int sourceLineId, int targetBookId)
    {
        var list = new List<LinkTargetRow>();
        var corpus = CorpusIds.CorpusOf(sourceLineId);
        if (CorpusIds.CorpusOf(targetBookId) != corpus) return list;

        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetLinkTargetForSourceLineAndBook;
            cmd.Parameters.AddWithValue("@sourceLineId", CorpusIds.ToLocalId(sourceLineId));
            cmd.Parameters.AddWithValue("@targetBookId", CorpusIds.ToLocalId(targetBookId));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new LinkTargetRow
                {
                    TargetLineId = CorpusIds.ToAppId(r.GetInt32(0), corpus),
                    LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                });
            }
        }, "getLinkTargetForSourceLineAndBook");
        return list;
    }

    // ── TOC paths & line→book/index helpers ──────────────────────────────────────

    public List<TocPathRow> GetTocPathsForLines(List<int> lineIds)
    {
        var list = new List<TocPathRow>();
        if (lineIds is null || lineIds.Count == 0) return list;

        foreach (var (corpus, localIds) in CorpusIds.GroupByCorpus(lineIds))
        {
            Run(corpus, () =>
            {
                using var conn = Open(corpus);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SeforimSql.GetTocPathsForLines(localIds.Count);
                BindList(cmd, "p", localIds);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new TocPathRow
                    {
                        LineId = CorpusIds.ToAppId(r.GetInt32(0), corpus),
                        BookId = r.IsDBNull(1) ? 0 : CorpusIds.ToAppId(r.GetInt32(1), corpus),
                        TocPath = r.IsDBNull(2) ? "" : r.GetString(2),
                    });
                }
            }, "getTocPathsForLines");
        }
        return list;
    }

    /// <summary>triples = flat [groupKey, firstLineId, lastLineId, …]. groupKey is a
    /// caller token, NOT an id — it passes through untouched. Each range's endpoints
    /// must sit in one corpus (they bound one span of one book); a mixed range is a
    /// caller bug and is dropped with a log rather than answered from the wrong DB.</summary>
    public List<EnclosingTocPathRow> GetEnclosingTocPathForLineRanges(List<int> triples)
    {
        var list = new List<EnclosingTocPathRow>();
        if (triples is null || triples.Count < 3) return list;

        int groupCount = triples.Count / 3;
        List<int>? library = null, userBooks = null;
        for (int i = 0; i < groupCount; i++)
        {
            int g = triples[i * 3], f = triples[i * 3 + 1], l = triples[i * 3 + 2];
            var corpus = CorpusIds.CorpusOf(f);
            if (CorpusIds.CorpusOf(l) != corpus)
            {
                logger.LogWarning("enclosing-TOC range for group {Group} spans corpora — dropped", g);
                continue;
            }
            var target = corpus == Corpus.Library ? (library ??= []) : (userBooks ??= []);
            target.Add(g);
            target.Add(CorpusIds.ToLocalId(f));
            target.Add(CorpusIds.ToLocalId(l));
        }

        ReadEnclosingTocPaths(Corpus.Library, library, list);
        ReadEnclosingTocPaths(Corpus.UserBooks, userBooks, list);
        return list;
    }

    private void ReadEnclosingTocPaths(Corpus corpus, List<int>? triples, List<EnclosingTocPathRow> list)
    {
        if (triples is null || triples.Count == 0) return;
        int groupCount = triples.Count / 3;
        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetEnclosingTocPathForLineRanges(groupCount);
            for (int i = 0; i < groupCount; i++)
            {
                cmd.Parameters.AddWithValue("@g" + i, triples[i * 3]);
                cmd.Parameters.AddWithValue("@f" + i, triples[i * 3 + 1]);
                cmd.Parameters.AddWithValue("@l" + i, triples[i * 3 + 2]);
            }
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new EnclosingTocPathRow
                {
                    GroupKey = r.GetInt32(0),
                    BookId = r.IsDBNull(1) ? 0 : CorpusIds.ToAppId(r.GetInt32(1), corpus),
                    TocPath = r.IsDBNull(2) ? "" : r.GetString(2),
                });
            }
        }, "getEnclosingTocPathForLineRanges");
    }

    public List<LineBookRow> GetBookIdsForLines(List<int> lineIds)
    {
        var list = new List<LineBookRow>();
        if (lineIds is null || lineIds.Count == 0) return list;

        foreach (var (corpus, localIds) in CorpusIds.GroupByCorpus(lineIds))
        {
            Run(corpus, () =>
            {
                using var conn = Open(corpus);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SeforimSql.GetBookIdsForLines(localIds.Count);
                BindList(cmd, "p", localIds);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new LineBookRow
                    {
                        LineId = CorpusIds.ToAppId(r.GetInt32(0), corpus),
                        BookId = r.IsDBNull(1) ? 0 : CorpusIds.ToAppId(r.GetInt32(1), corpus),
                    });
                }
            }, "getBookIdsForLines");
        }
        return list;
    }

    public List<LineIndexRow> GetLineIndexFromLineId(int lineId)
    {
        var corpus = CorpusIds.CorpusOf(lineId);
        var list = new List<LineIndexRow>();
        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetLineIndexFromLineId;
            cmd.Parameters.AddWithValue("@id", CorpusIds.ToLocalId(lineId));
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new LineIndexRow
                {
                    LineIndex = r.IsDBNull(0) ? 0 : r.GetInt32(0),
                    BookId = r.IsDBNull(1) ? 0 : CorpusIds.ToAppId(r.GetInt32(1), corpus),
                });
            }
        }, "getLineIndexFromLineId");
        return list;
    }

    // ── Dictionary sources in the seforim DB ─────────────────────────────────────

    public List<BookIdRow> GetBookIdsByTitlePattern(string pattern) =>
        ReadBookIds(SeforimSql.GetBookIdsByTitlePattern, ("@pattern", pattern));

    public List<BookIdRow> GetBookIdByExactTitle(string title) =>
        ReadBookIds(SeforimSql.GetBookIdByExactTitle, ("@title", title));

    // Title lookups enumerate rather than route (ids flow OUT) — union both corpora,
    // library first so existing consumers that take the first match keep their answer.
    private List<BookIdRow> ReadBookIds(string sql, params (string, object)[] ps)
    {
        var list = new List<BookIdRow>();
        Run(() =>
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new BookIdRow { Id = r.GetInt32(0) });
        }, "readBookIds");

        if (HasUserBooksDb)
        {
            Run(Corpus.UserBooks, () =>
            {
                using var conn = Open(Corpus.UserBooks);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new BookIdRow { Id = CorpusIds.ToAppId(r.GetInt32(0), Corpus.UserBooks) });
            }, "readBookIds");
        }
        return list;
    }

    public List<BoldLineRow> GetLinesWithContentPatternForBooks(List<int> bookIds, string pattern)
    {
        var list = new List<BoldLineRow>();
        if (bookIds is null || bookIds.Count == 0) return list;

        // The SQL carries LIMIT 50 per query; with two corpora that could yield up to
        // 100 rows, so re-apply the cap on the merged list. GroupByCorpus emits the
        // library group first, which keeps today's top-50 exactly when it fills up.
        foreach (var (corpus, localIds) in CorpusIds.GroupByCorpus(bookIds))
        {
            if (list.Count >= 50) break;
            Run(corpus, () =>
            {
                using var conn = Open(corpus);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = SeforimSql.GetLinesWithContentPatternForBooks(localIds.Count);
                BindList(cmd, "b", localIds);
                cmd.Parameters.AddWithValue("@pattern", pattern);
                using var r = cmd.ExecuteReader();
                while (r.Read() && list.Count < 50)
                {
                    list.Add(new BoldLineRow
                    {
                        Content = r.IsDBNull(0) ? "" : r.GetString(0),
                        Title = r.IsDBNull(1) ? "" : r.GetString(1),
                        BookId = r.IsDBNull(2) ? 0 : CorpusIds.ToAppId(r.GetInt32(2), corpus),
                        LineId = r.IsDBNull(3) ? 0 : CorpusIds.ToAppId(r.GetInt32(3), corpus),
                        LineIndex = r.IsDBNull(4) ? 0 : r.GetInt32(4),
                    });
                }
            }, "getLinesWithContentPatternForBooks");
        }
        return list;
    }

    public List<RawLineRow> GetLinesWithEitherContentPattern(int bookId, string p1, string p2)
    {
        var corpus = CorpusIds.CorpusOf(bookId);
        var list = new List<RawLineRow>();
        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetLinesWithEitherContentPattern;
            cmd.Parameters.AddWithValue("@bookId", CorpusIds.ToLocalId(bookId));
            cmd.Parameters.AddWithValue("@p1", p1);
            cmd.Parameters.AddWithValue("@p2", p2);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new RawLineRow
                {
                    Id = CorpusIds.ToAppId(r.GetInt32(0), corpus),
                    LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    Content = r.IsDBNull(2) ? "" : r.GetString(2),
                });
            }
        }, "getLinesWithEitherContentPattern");
        return list;
    }

    public List<RawLineRow> GetLineByBookAndLineIndex(int bookId, int lineIndex)
    {
        var corpus = CorpusIds.CorpusOf(bookId);
        var list = new List<RawLineRow>();
        Run(corpus, () =>
        {
            using var conn = Open(corpus);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = SeforimSql.GetLineByBookAndLineIndex;
            cmd.Parameters.AddWithValue("@bookId", CorpusIds.ToLocalId(bookId));
            cmd.Parameters.AddWithValue("@lineIndex", lineIndex);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new RawLineRow
                {
                    Id = CorpusIds.ToAppId(r.GetInt32(0), corpus),
                    LineIndex = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    Content = r.IsDBNull(2) ? "" : r.GetString(2),
                });
            }
        }, "getLineByBookAndLineIndex");
        return list;
    }

    /// <summary>Binds a list of ints to @{prefix}0..@{prefix}N-1 (for dynamic IN clauses).</summary>
    private static void BindList(SqliteCommand cmd, string prefix, List<int> values)
    {
        for (int i = 0; i < values.Count; i++) cmd.Parameters.AddWithValue("@" + prefix + i, values[i]);
    }
}
