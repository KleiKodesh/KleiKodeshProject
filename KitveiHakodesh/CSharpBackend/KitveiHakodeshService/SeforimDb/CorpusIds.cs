namespace KitveiHakodeshService.SeforimDb;

/// <summary>Which database a row came from.</summary>
public enum Corpus
{
    /// <summary>The seforim library (`seforim.db`) — the default for every existing id.</summary>
    Library = 0,

    /// <summary>Otzaria's personal books (`user_books.db`).</summary>
    UserBooks = 1,
}

/// <summary>
/// Translates between the ids the app sees and the ids each database actually stores.
///
/// The two databases number their rows independently — both `book.id` and `line.id`
/// start at 1 in each — so an id alone cannot say which database it belongs to. Rather
/// than merge the corpora (a UNION makes `WHERE id = 5` match a library sefer AND a
/// personal book, silently), personal-book ids are shifted into their own numeric range
/// on the way out and shifted back on the way in. The id then carries its own routing
/// information and every query still runs against exactly one database, unchanged.
///
/// <see cref="UserBooksBase"/> is chosen to sit far above any real library id while
/// leaving room below int32's ceiling: the library's largest `line.id` is ~6.5 million,
/// and ids must stay non-negative because the search layer's bitmap filter drops
/// negative values.
/// </summary>
public static class CorpusIds
{
    /// <summary>First app-visible id belonging to the personal-books database.</summary>
    public const int UserBooksBase = 1_000_000_000;

    /// <summary>Which database an app-visible id refers to.</summary>
    public static Corpus CorpusOf(int appId) =>
        appId >= UserBooksBase ? Corpus.UserBooks : Corpus.Library;

    /// <summary>True when the id belongs to the personal-books database.</summary>
    public static bool IsUserBooks(int appId) => appId >= UserBooksBase;

    /// <summary>
    /// The id as stored in its own database. Library ids pass through untouched, so the
    /// library path is byte-identical to what it was before personal books existed.
    /// </summary>
    public static int ToLocalId(int appId) =>
        appId >= UserBooksBase ? appId - UserBooksBase : appId;

    /// <summary>The app-visible id for a row read out of <paramref name="corpus"/>.</summary>
    public static int ToAppId(int localId, Corpus corpus) =>
        corpus == Corpus.UserBooks ? localId + UserBooksBase : localId;

    /// <summary>Nullable-id variant for optional columns (parentId, lineId): null stays null.</summary>
    public static int? ToAppId(int? localId, Corpus corpus) =>
        localId is int id ? ToAppId(id, corpus) : null;

    /// <summary>
    /// Splits a mixed list of app-visible ids into one group per corpus, each already
    /// translated to local ids. A search can return hits from both databases at once, so
    /// callers that take an id LIST must fetch per corpus and recombine — a single query
    /// cannot serve both.
    ///
    /// Returns a single Library group unchanged when no personal-book id is present,
    /// which is both the common case and the one that must stay allocation-cheap.
    /// </summary>
    public static List<(Corpus Corpus, List<int> LocalIds)> GroupByCorpus(List<int> appIds)
    {
        List<int>? userBooks = null;
        for (int i = 0; i < appIds.Count; i++)
        {
            if (!IsUserBooks(appIds[i])) continue;
            userBooks ??= new List<int>();
            userBooks.Add(appIds[i] - UserBooksBase);
        }

        if (userBooks is null)
            return new List<(Corpus, List<int>)> { (Corpus.Library, appIds) };

        var library = new List<int>(appIds.Count - userBooks.Count);
        for (int i = 0; i < appIds.Count; i++)
        {
            if (!IsUserBooks(appIds[i])) library.Add(appIds[i]);
        }

        var groups = new List<(Corpus, List<int>)>(2);
        if (library.Count > 0) groups.Add((Corpus.Library, library));
        groups.Add((Corpus.UserBooks, userBooks));
        return groups;
    }
}
