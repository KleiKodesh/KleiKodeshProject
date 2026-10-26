using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FtsLib;
using KitveiHakodesh.Core.SeforimDb;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// Wraps one <see cref="SeforimDbFtsCorpus"/> per source — the primary seforim.db (library 0)
    /// plus every additional library currently configured and attachable (see
    /// <see cref="KitveiHakodesh.Core.Settings.SeforimDbAdditionalLibraries"/> /
    /// <see cref="SeforimDbAttachedLibraries"/>) — and presents them to FtsLib as ONE corpus
    /// whose document ids are the GLOBAL ids every other RPC already returns
    /// (<see cref="SeforimDbLibraryId"/>), so a full-text hit's line id resolves back to exactly
    /// the book <c>getAllBooks</c>/<c>getBookById</c> serve for that library.
    ///
    /// WHY THIS WORKS WITHOUT A MERGE STEP. <see cref="IFtsCorpus"/> requires its ids to be the
    /// caller's own, positive, and ASCENDING; <see cref="IFtsOrderedCorpus"/> additionally
    /// requires documents in a stable result order. Library N's global ids
    /// (<see cref="SeforimDbLibraryId.ToGlobal"/>) sit ENTIRELY inside the band
    /// <c>[N * OffsetUnit, (N + 1) * OffsetUnit)</c>, strictly above every earlier library's band
    /// and strictly below every later one's — so reading each underlying corpus in full, in
    /// ascending library-index order, yields a sequence that is already globally ascending and
    /// already a stable result order (each library's own order, library by library). No per-row
    /// merge/sort is needed; this class only needs to CONCATENATE in index order and translate
    /// ids at the boundary.
    ///
    /// LIFETIME: constructed once per corpus "operation" exactly like <see cref="SeforimDbFtsCorpus"/>
    /// — see <see cref="IFtsCorpus"/>'s remarks. Opens one connection (one <see cref="SeforimDbFtsCorpus"/>)
    /// per source eagerly in the constructor; the realistic number of additional libraries is tiny
    /// (a handful at most — <see cref="SeforimDbLibraryId.MaxLibraryIndex"/> is a LIFETIME budget of
    /// slots, not a concurrency target), so eager construction keeps this type simple without a
    /// meaningful cost.
    ///
    /// NOT used when there are no additional libraries: the coordinator hands FtsLib a plain
    /// <see cref="SeforimDbFtsCorpus"/> directly in that (overwhelmingly common) case, so
    /// single-library behaviour and performance are completely unchanged by this type's existence.
    /// </summary>
    internal sealed class CompositeFtsCorpus : IFtsCorpus, IFtsOrderedCorpus, IFtsPlacedCorpus, ISeforimDbFtsPositionCorpus
    {
        private readonly struct Source
        {
            public Source(int index, SeforimDbFtsCorpus corpus)
            {
                Index = index;
                Corpus = corpus;
            }

            public int Index { get; }
            public SeforimDbFtsCorpus Corpus { get; }
        }

        /// <summary>Every source, ascending by library index (0 = primary first) — the order both
        /// the global-id scheme and the "concatenate, don't merge" design above depend on.</summary>
        private readonly List<Source> _sources;

        /// <param name="primaryDbPath">The primary seforim.db — always library 0.</param>
        /// <param name="additionalLibraries">Every additional library to fold in, already
        /// resolved/validated by <see cref="SeforimDbAttachedLibraries.Resolve"/>. A library whose
        /// file cannot be opened right now (moved, mid-rebuild) is skipped for this corpus
        /// instance exactly like an attach failure is skipped elsewhere in this feature — it
        /// simply contributes nothing to this build/search rather than failing the whole corpus.</param>
        public CompositeFtsCorpus(
            string primaryDbPath, IReadOnlyList<SeforimDbAttachedLibraries.Library> additionalLibraries,
            Action<string, Exception>? onLibrarySkipped = null)
        {
            _sources = new List<Source>(1 + (additionalLibraries?.Count ?? 0))
            {
                new Source(0, new SeforimDbFtsCorpus(primaryDbPath)),
            };

            if (additionalLibraries is null) return;

            foreach (var library in additionalLibraries.OrderBy(l => l.Index))
            {
                SeforimDbFtsCorpus? corpus = null;
                try
                {
                    corpus = new SeforimDbFtsCorpus(library.Path, library.LineIds, library.TocIds);
                    // Prove the line text is actually readable BEFORE this library joins the
                    // corpus, so a library that passed the catalog-level probe but cannot serve
                    // text (corrupt page, missing content column/table) is excluded up front and
                    // never fails mid-enumeration after the primary was already indexed. Cheap:
                    // one decoded row, no scan. The same file gives the same verdict every time,
                    // so counts/positions stay consistent between operations.
                    using (var first = corpus.ReadDocuments(1).GetEnumerator())
                        first.MoveNext();
                    _sources.Add(new Source(library.Index, corpus));
                }
                catch (Exception ex)
                {
                    corpus?.Dispose();
                    onLibrarySkipped?.Invoke(library.Path, ex);
                }
            }
        }

        private Source? Find(int libraryIndex)
        {
            foreach (var source in _sources)
                if (source.Index == libraryIndex) return source;
            return null;
        }

        public long CountDocuments()
        {
            long total = 0;
            foreach (var source in _sources) total += source.Corpus.CountDocuments();
            return total;
        }

        public long CountDocumentsUpTo(int upToId)
        {
            var (ownerIndex, localUpTo) = SeforimDbLibraryId.Resolve(upToId);
            long total = 0;
            foreach (var source in _sources)
            {
                if (source.Index < ownerIndex) total += source.Corpus.CountDocuments();
                else if (source.Index == ownerIndex) total += source.Corpus.CountDocumentsUpTo(localUpTo);
                // Sources after the owning library contribute nothing to "up to" this id.
            }
            return total;
        }

        public string GetDocumentText(int id)
        {
            var (libraryIndex, localId) = SeforimDbLibraryId.Resolve(id);
            // IFtsCorpus.GetDocumentText is documented as nullable ("or null when there is no
            // such id") despite its non-nullable signature -- matches SeforimDbFtsCorpus's own
            // behaviour for a missing row.
#pragma warning disable CS8603 // Possible null reference return.
            return Find(libraryIndex) is { } source ? source.Corpus.GetDocumentText(localId) : null;
#pragma warning restore CS8603
        }

        public IEnumerable<(int Id, string Text)> ReadDocuments(int limit, CancellationToken ct = default)
        {
            long remaining = limit > 0 ? limit : long.MaxValue;
            foreach (var source in _sources)
            {
                if (remaining <= 0) yield break;
                int sourceLimit = limit > 0 && remaining < int.MaxValue ? (int)remaining : 0;
                foreach (var (localId, text) in source.Corpus.ReadDocuments(sourceLimit, ct))
                {
                    yield return (SeforimDbLibraryId.ToGlobal(source.Index, localId), text);
                    remaining--;
                    if (remaining <= 0) yield break;
                }
            }
        }

        /// <summary>
        /// For completeness of the <see cref="IFtsCorpus"/> contract (FtsLib's own, non-FTS5
        /// pipeline). The ACTIVE engine (SQLite FTS5 — see Fts5SeforimIndex) never calls this for
        /// an <see cref="IFtsOrderedCorpus"/> like this one; it resumes via
        /// <see cref="ReadDocumentsInResultOrder"/> instead (position-based, not id-based — see
        /// that method). Implemented defensively: if <paramref name="afterId"/>'s library is no
        /// longer one of <see cref="_sources"/> (removed since an interrupted build), this falls
        /// back to indexing every later-indexed library from its own start rather than guessing —
        /// the worst case is a few re-indexed lines, never a skipped line.
        /// </summary>
        public IEnumerable<(int Id, string Text)> ReadDocumentsAfter(int afterId, int limit = 0, CancellationToken ct = default)
        {
            var (ownerIndex, localAfter) = SeforimDbLibraryId.Resolve(afterId);
            long remaining = limit > 0 ? limit : long.MaxValue;
            bool ownerFound = false;

            foreach (var source in _sources)
            {
                if (remaining <= 0) yield break;

                IEnumerable<(int Id, string Text)> local;
                if (source.Index < ownerIndex) continue; // fully covered by a previous session
                if (source.Index == ownerIndex)
                {
                    ownerFound = true;
                    int sourceLimit = limit > 0 && remaining < int.MaxValue ? (int)remaining : 0;
                    local = source.Corpus.ReadDocumentsAfter(localAfter, sourceLimit, ct);
                }
                else
                {
                    int sourceLimit = limit > 0 && remaining < int.MaxValue ? (int)remaining : 0;
                    local = source.Corpus.ReadDocuments(sourceLimit, ct);
                }

                foreach (var (localId, text) in local)
                {
                    yield return (SeforimDbLibraryId.ToGlobal(source.Index, localId), text);
                    remaining--;
                    if (remaining <= 0) yield break;
                }
            }

            // The owning library is no longer attached — see the remarks above.
            _ = ownerFound;
        }

        /// <summary>
        /// Every document across every source in result order, library by library (ascending
        /// index — see the class remarks for why that is ALSO already globally-ascending id
        /// order). <paramref name="skip"/> is a document COUNT, exactly like a single
        /// <see cref="SeforimDbFtsCorpus"/>: whole libraries are skipped by their own document
        /// count, and the library the skip point lands inside resumes mid-stream via its own
        /// <see cref="IFtsOrderedCorpus.ReadDocumentsInResultOrder"/>.
        /// </summary>
        public IEnumerable<(int Id, string Text)> ReadDocumentsInResultOrder(long skip, CancellationToken ct = default)
        {
            long remainingSkip = skip;
            foreach (var source in _sources)
            {
                ct.ThrowIfCancellationRequested();
                if (remainingSkip > 0)
                {
                    long count = source.Corpus.CountDocuments();
                    if (remainingSkip >= count)
                    {
                        remainingSkip -= count;
                        continue;
                    }
                }

                long thisSkip = remainingSkip;
                remainingSkip = 0; // only the first (partially-skipped) source keeps a nonzero skip
                foreach (var (localId, text) in source.Corpus.ReadDocumentsInResultOrder(thisSkip, ct))
                    yield return (SeforimDbLibraryId.ToGlobal(source.Index, localId), text);
            }
        }

        public IEnumerable<(int Id, string Text, string Title)> FetchDocuments(IEnumerable<int> ids)
        {
            // Order does not matter to callers (Fts5SearchEngine.Fetch keys a dictionary off the
            // returned ids and re-walks its OWN requested-id list afterward) — only that every id
            // maps back to the right content. Grouping by owning library lets each sub-corpus run
            // its normal chunked fetch against local ids.
            var byLibrary = new Dictionary<int, List<int>>();
            foreach (int id in ids)
            {
                var (libraryIndex, localId) = SeforimDbLibraryId.Resolve(id);
                if (!byLibrary.TryGetValue(libraryIndex, out var localIds))
                    byLibrary[libraryIndex] = localIds = new List<int>();
                localIds.Add(localId);
            }

            foreach (var (libraryIndex, localIds) in byLibrary)
            {
                if (Find(libraryIndex) is not { } source) continue; // no longer attached — drop silently, like elsewhere
                foreach (var (localId, text, title) in source.Corpus.FetchDocuments(localIds))
                    yield return (SeforimDbLibraryId.ToGlobal(libraryIndex, localId), text, title);
            }
        }

        /// <summary>Same as <see cref="FetchDocuments"/>, plus each line's placement - translated
        /// to the global id space like the ids themselves: the book id and (when the line has
        /// one) its TOC entry id are the owning library's LOCAL ids lifted by that library's
        /// offset, so a hit's book/entry resolves to exactly what getAllBooks and
        /// getTocPathsForLines serve for it. lineIndex is a position, not an id.</summary>
        public IEnumerable<(int Id, string Text, FtsDocumentPlacement Placement)> FetchPlacedDocuments(IEnumerable<int> ids)
        {
            foreach (var (libraryIndex, localIds) in GroupByLibrary(ids))
            {
                if (Find(libraryIndex) is not { } source) continue; // no longer attached
                foreach (var (localId, text, placement) in source.Corpus.FetchPlacedDocuments(localIds))
                {
                    yield return (
                        SeforimDbLibraryId.ToGlobal(libraryIndex, localId),
                        text,
                        new FtsDocumentPlacement(
                            placement.BookId == 0 ? 0 : SeforimDbLibraryId.ToGlobal(libraryIndex, placement.BookId),
                            placement.LineIndex,
                            placement.TocEntryId == 0 ? 0 : SeforimDbLibraryId.ToGlobal(libraryIndex, placement.TocEntryId)));
                }
            }
        }

        /// <summary>Lines at (GLOBAL book id, lineIndex) positions: each library answers for its
        /// own books, ids and book ids come back global.</summary>
        public List<SeforimDbFtsLineAtPosition> FetchLinesAtBookPositions(IReadOnlyList<(int BookId, int LineIndex)> positions)
        {
            var lines = new List<SeforimDbFtsLineAtPosition>(positions?.Count ?? 0);
            if (positions == null || positions.Count == 0) return lines;

            var byLibrary = new Dictionary<int, List<(int BookId, int LineIndex)>>();
            foreach (var (bookId, lineIndex) in positions)
            {
                var (libraryIndex, localBookId) = SeforimDbLibraryId.Resolve(bookId);
                if (!byLibrary.TryGetValue(libraryIndex, out var list))
                    byLibrary[libraryIndex] = list = new List<(int, int)>();
                list.Add((localBookId, lineIndex));
            }

            foreach (var (libraryIndex, local) in byLibrary)
            {
                if (Find(libraryIndex) is not { } source) continue;
                foreach (var line in source.Corpus.FetchLinesAtBookPositions(local))
                    lines.Add(new SeforimDbFtsLineAtPosition(
                        SeforimDbLibraryId.ToGlobal(libraryIndex, line.Id),
                        SeforimDbLibraryId.ToGlobal(libraryIndex, line.BookId),
                        line.LineIndex,
                        line.Text));
            }
            return lines;
        }

        private static Dictionary<int, List<int>> GroupByLibrary(IEnumerable<int> globalIds)
        {
            var byLibrary = new Dictionary<int, List<int>>();
            foreach (int id in globalIds)
            {
                var (libraryIndex, localId) = SeforimDbLibraryId.Resolve(id);
                if (!byLibrary.TryGetValue(libraryIndex, out var localIds))
                    byLibrary[libraryIndex] = localIds = new List<int>();
                localIds.Add(localId);
            }
            return byLibrary;
        }

        public IDictionary<int, (string Previous, string Next)> FetchNeighbourText(IReadOnlyList<int> ids, int radius)
        {
            var result = new Dictionary<int, (string Previous, string Next)>(ids?.Count ?? 0);
            if (ids == null || ids.Count == 0 || radius <= 0) return result;

            var byLibrary = new Dictionary<int, List<int>>();
            foreach (int id in ids)
            {
                var (libraryIndex, localId) = SeforimDbLibraryId.Resolve(id);
                if (!byLibrary.TryGetValue(libraryIndex, out var localIds))
                    byLibrary[libraryIndex] = localIds = new List<int>();
                localIds.Add(localId);
            }

            foreach (var (libraryIndex, localIds) in byLibrary)
            {
                if (Find(libraryIndex) is not { } source) continue;
                foreach (var entry in source.Corpus.FetchNeighbourText(localIds, radius))
                    result[SeforimDbLibraryId.ToGlobal(libraryIndex, entry.Key)] = entry.Value;
            }
            return result;
        }

        public void Dispose()
        {
            foreach (var source in _sources) source.Corpus.Dispose();
        }
    }
}
