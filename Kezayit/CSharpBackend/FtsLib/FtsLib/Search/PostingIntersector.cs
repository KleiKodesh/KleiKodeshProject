using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace FtsLib.Search
{
    /// <summary>
    /// Search orchestration shared by RamIndex and IndexReader.
    ///
    /// All three search modes (AND, OR, mixed AND/OR) are implemented here.
    /// Callers supply two delegates:
    ///   resolve  — term → PostingIterator (return PostingIterator.Empty if missing)
    ///   getCount — term → doc count       (used to sort rarest-first for AND)
    ///
    /// The "missing term" contract for AND:
    ///   If resolve returns PostingIterator.Empty (IsDone = true), AndSearch
    ///   treats the term as absent and returns an empty result immediately.
    /// </summary>
    internal static class PostingIntersector
    {
        // ── AND ──────────────────────────────────────────────────────

        public static IEnumerable<int> AndSearch(
            IEnumerable<string>           terms,
            Func<string, PostingIterator> resolve,
            Func<string, int>             getCount,
            CancellationToken             ct = default)
        {
            var termList = new List<string>(terms);
            if (termList.Count == 0) return Enumerable.Empty<int>();

            termList.Sort((a, b) => getCount(a).CompareTo(getCount(b)));
            return AndMerge(termList, resolve, ct);
        }

        // ── OR ───────────────────────────────────────────────────────

        public static IEnumerable<int> OrSearch(
            IEnumerable<string>           terms,
            Func<string, PostingIterator> resolve,
            CancellationToken             ct = default)
        {
            var started = StartedIterators(terms, resolve, skipMissing: true);
            if (started.Count == 0) return Enumerable.Empty<int>();
            if (started.Count == 1) return DrainStarted(started[0], ct);
            return PostingMatcher.Union(started.ToArray(), ct);
        }

        // ── Mixed AND/OR ─────────────────────────────────────────────

        public static IEnumerable<int> MixedSearch(
            IEnumerable<IEnumerable<string>> groups,
            Func<string, PostingIterator>    resolve,
            CancellationToken                ct = default)
        {
            var groupIters = new List<PostingIterator>();
            foreach (var group in groups)
            {
                var started = StartedIterators(group, resolve, skipMissing: true);
                if (started.Count == 0) return Enumerable.Empty<int>();
                groupIters.Add(started.Count == 1
                    ? started[0]
                    : new UnionIterator(started.ToArray()));
            }

            if (groupIters.Count == 0) return Enumerable.Empty<int>();
            if (groupIters.Count == 1) return DrainStarted(groupIters[0], ct);
            return PostingMatcher.Intersect(groupIters.ToArray(), ct);
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static IEnumerable<int> AndMerge(
            List<string>                  terms,
            Func<string, PostingIterator> resolve,
            CancellationToken             ct)
        {
            var iters = new PostingIterator[terms.Count];
            for (int i = 0; i < terms.Count; i++)
            {
                iters[i] = resolve(terms[i]);
                if (iters[i].IsDone) yield break; // term not in index
            }

            for (int i = 0; i < iters.Length; i++)
                if (!iters[i].MoveNext()) yield break;

            foreach (var id in PostingMatcher.Intersect(iters, ct))
                yield return id;
        }

        private static List<PostingIterator> StartedIterators(
            IEnumerable<string>           terms,
            Func<string, PostingIterator> resolve,
            bool                          skipMissing)
        {
            var result = new List<PostingIterator>();
            foreach (var term in terms)
            {
                var it = resolve(term);
                if (it.IsDone) { if (!skipMissing) return null; continue; }
                if (it.MoveNext()) result.Add(it);
            }
            return result;
        }

        /// <summary>
        /// Yields all values from a pre-advanced iterator (Current is already valid).
        /// Unlike <see cref="PostingIterator.AsEnumerable"/>, this does NOT call
        /// MoveNext before yielding the first value.
        /// </summary>
        private static IEnumerable<int> DrainStarted(PostingIterator it, CancellationToken ct)
        {
            do
            {
                ct.ThrowIfCancellationRequested();
                yield return it.Current;
            }
            while (it.MoveNext());
        }
    }
}
