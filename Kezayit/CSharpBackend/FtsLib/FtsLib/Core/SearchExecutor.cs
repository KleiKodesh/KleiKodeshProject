using System;
using System.Collections.Generic;
using System.Linq;

namespace FtsLib.Core
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
    internal static class SearchExecutor
    {
        // ── AND ──────────────────────────────────────────────────────

        public static IEnumerable<int> AndSearch(
            IEnumerable<string>          terms,
            Func<string, PostingIterator> resolve,
            Func<string, int>             getCount)
        {
            var termList = new List<string>(terms);
            if (termList.Count == 0) return Enumerable.Empty<int>();

            termList.Sort((a, b) => getCount(a).CompareTo(getCount(b)));
            return AndMerge(termList, resolve);
        }

        // ── OR ───────────────────────────────────────────────────────

        public static IEnumerable<int> OrSearch(
            IEnumerable<string>          terms,
            Func<string, PostingIterator> resolve)
        {
            var started = StartedIterators(terms, resolve, skipMissing: true);
            if (started.Count == 0) return Enumerable.Empty<int>();
            if (started.Count == 1) return started[0].AsEnumerable();
            return PostingMatcher.Union(started.ToArray());
        }

        // ── Mixed AND/OR ─────────────────────────────────────────────

        public static IEnumerable<int> MixedSearch(
            IEnumerable<IEnumerable<string>> groups,
            Func<string, PostingIterator>    resolve)
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
            if (groupIters.Count == 1) return groupIters[0].AsEnumerable();
            return PostingMatcher.Intersect(groupIters.ToArray());
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static IEnumerable<int> AndMerge(
            List<string>                 terms,
            Func<string, PostingIterator> resolve)
        {
            var iters = new PostingIterator[terms.Count];
            for (int i = 0; i < terms.Count; i++)
            {
                iters[i] = resolve(terms[i]);
                if (iters[i].IsDone) yield break; // term not in index
            }

            for (int i = 0; i < iters.Length; i++)
                if (!iters[i].MoveNext()) yield break;

            foreach (var id in PostingMatcher.Intersect(iters))
                yield return id;
        }

        private static List<PostingIterator> StartedIterators(
            IEnumerable<string>          terms,
            Func<string, PostingIterator> resolve,
            bool                         skipMissing)
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
    }
}
