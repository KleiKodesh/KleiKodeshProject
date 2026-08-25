using System;
using System.Collections.Generic;
using System.Threading;

namespace FtsLib
{
    /// <summary>
    /// The documents this engine indexes and fetches back — supplied by the caller, so the
    /// engine never opens a content database of its own.
    ///
    /// WHY THIS EXISTS. FtsLib shipped with a seforim-specific reader built in
    /// (<c>SeforimDb/ZayitDb.cs</c>), which meant a library named after full-text search knew
    /// the schema of one particular corpus, and that corpus had three independent readers across
    /// the solution. This interface is the seam that ends that — WITHOUT a rewrite:
    ///
    ///   • The built-in reader implements it, so every existing caller keeps working unchanged.
    ///     <c>SeforimIndex(indexPath, dbPath)</c> still opens the database itself.
    ///   • A caller with its own data access implements it instead and passes it in, and the
    ///     engine reads nothing.
    ///
    /// Both routes are live at once, which is what makes the migration gradual: the hosted app
    /// and the service can move over one at a time, and neither has to move for the other to.
    ///
    /// TERMINOLOGY. "Document" here is whatever unit the caller indexes — a line of a book, a
    /// paragraph, a record. Ids are the caller's own and must be positive and ascending; the
    /// engine stores them and hands them back, and never invents one.
    ///
    /// LIFETIME — THE ENGINE TAKES A FACTORY, NOT AN INSTANCE. Every entry point receives a
    /// <c>Func&lt;IFtsCorpus&gt;</c>, opens one per operation and disposes it when that operation
    /// ends. Two reasons, both load-bearing:
    ///
    ///   • A search returns a LAZY sequence. The corpus has to stay open until enumeration
    ///     finishes, not until the call returns — so the engine's own `using` has to sit inside
    ///     the iterator, which means the engine has to be the one that opened it.
    ///   • Searches run on arbitrary threads while a build is running on another. A shared
    ///     database connection is not thread-safe, so one instance per operation is not a
    ///     detail — it is what keeps concurrent searches from corrupting each other.
    ///
    /// So an implementation should be cheap to construct, and must be safe to construct several
    /// times over concurrently.
    /// </summary>
    public interface IFtsCorpus : IDisposable
    {
        /// <summary>How many documents there are. Used for the build's progress denominator, so
        /// an estimate is acceptable if an exact count would be expensive.</summary>
        long CountDocuments();

        /// <summary>How many documents have an id at or below <paramref name="upToId"/>. This is
        /// what turns a resume point — the highest id already indexed — into "documents done so
        /// far", so a resumed build reports honest progress instead of restarting at zero.</summary>
        long CountDocumentsUpTo(int upToId);

        /// <summary>One document's text, or null when there is no such id.</summary>
        string GetDocumentText(int id);

        /// <summary>
        /// Every document, ascending by id, for a build from scratch.
        ///
        /// STREAM IT. A real corpus does not fit in memory, and the caller reads this while
        /// writing index segments — materialising it would defeat the flush cadence the build
        /// depends on.
        /// </summary>
        /// <param name="limit">Stop after this many. 0 means no limit.</param>
        IEnumerable<(int Id, string Text)> ReadDocuments(int limit, CancellationToken ct = default);

        /// <summary>
        /// Documents after <paramref name="afterId"/>, ascending, for RESUMING a build. The
        /// bound is exclusive, so passing the highest id already committed to the index yields
        /// exactly what is left — the id itself is never handed out twice.
        /// </summary>
        /// <param name="limit">Stop after this many. 0 means no limit.</param>
        IEnumerable<(int Id, string Text)> ReadDocumentsAfter(int afterId, int limit = 0, CancellationToken ct = default);

        /// <summary>
        /// Text and display title for a set of ids, for turning search hits into results.
        ///
        /// The ids arrive ASCENDING, as the index produces them, and results must come back in
        /// that order — the search path relies on it and does no sorting of its own. Stream this
        /// too: a broad query can match tens of thousands of documents, and the caller yields
        /// each one onward as it arrives.
        ///
        /// An id with no document is skipped rather than yielded empty; a caller comparing counts
        /// should not assume one result per id.
        /// </summary>
        IEnumerable<(int Id, string Text, string Title)> FetchDocuments(IEnumerable<int> ids);

        /// <summary>
        /// The text immediately before and after each of <paramref name="ids"/>, up to
        /// <paramref name="radius"/> documents either side, joined per side.
        ///
        /// This is what lets a snippet run past the edges of the matched document, which is
        /// worth having when the unit is small — a single verse rarely carries enough context to
        /// read a match in. Ids with no neighbours are absent from the result rather than
        /// present with empty strings.
        /// </summary>
        IDictionary<int, (string Previous, string Next)> FetchNeighbourText(IReadOnlyList<int> ids, int radius);
    }
}
