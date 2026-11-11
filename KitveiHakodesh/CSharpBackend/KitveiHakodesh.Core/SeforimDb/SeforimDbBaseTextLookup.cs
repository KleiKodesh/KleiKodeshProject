using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// The base texts of many books at once, best first - the same answer as
    /// <see cref="SeforimDbSqlStrings.GetReverseBooks"/> per book, in one statement for all.
    ///
    /// GetReverseBooks drops lateral citations row by row with three correlated subqueries
    /// (declared base, or same top-level corpus and the asked-about book not a base text).
    /// Only the declared flag varies per row; the corpus test depends on the (base, book)
    /// PAIR. So the links are aggregated per pair once, the pair test is made once per pair
    /// from a per-book lookup, and a pair failing it keeps only its declared rows' figures.
    /// </summary>
    internal sealed class BaseTextLookup
    {
        private readonly SqliteConnection _conn;
        private readonly SeforimDbSchema _schema;
        private readonly List<int> _typeIds;
        /// <summary>Per call only: books already looked up (isBaseBook, orderIndex, corpus).</summary>
        private readonly Dictionary<int, BookFacts> _facts = new();

        private struct BookFacts
        {
            public int IsBaseBook;
            public int OrderIndex;
            public long? Corpus;
        }

        public BaseTextLookup(SqliteConnection conn, SeforimDbSchema schema, List<int> dependantTypeIds)
        {
            _conn = conn;
            _schema = schema;
            _typeIds = dependantTypeIds;
        }

        /// <summary>Every id in <paramref name="bookIds"/> mapped to its base texts, best first
        /// (empty when it has none). <paramref name="primaryOnly"/>: only the first of each list
        /// is wanted (the rest come back unordered), which spares most coverage counts.</summary>
        public Dictionary<int, List<int>> BasesOf(IReadOnlyCollection<int> bookIds, bool primaryOnly = false)
        {
            var result = bookIds.Distinct().ToDictionary(id => id, _ => new List<int>());
            if (result.Count == 0 || _typeIds.Count == 0) return result;
            string bp = "l." + _schema.DeclaredBaseColumn;
            string typeList = string.Join(",", _typeIds);

            // Pass 1: per (book, base) pair, the declared flag only - no per-line counting.
            var pairs = new List<(int Target, int Source, long MaxDeclared)>();
            using (var cmd = _conn.CreateCommand())
            {
                var targets = result.Keys.ToList();
                cmd.CommandText = $@"
                    SELECT l.targetBookId, l.sourceBookId, MAX({bp})
                    FROM link l
                    WHERE l.targetBookId IN ({SeforimDbSqlStrings.InPlaceholders("t", targets.Count)})
                      AND l.connectionTypeId IN ({typeList})
                      AND l.sourceBookId != l.targetBookId
                    GROUP BY l.targetBookId, l.sourceBookId";
                for (int i = 0; i < targets.Count; i++) cmd.Parameters.AddWithValue("@t" + i, targets[i]);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    pairs.Add((r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? long.MinValue : r.GetInt64(2)));
            }
            if (pairs.Count == 0) return result;

            LoadFacts(pairs.Select(p => p.Target).Concat(pairs.Select(p => p.Source)));

            // A pair failing the corpus test keeps only its declared rows (and so their MAX).
            var kept = new List<Ranked>();
            foreach (var p in pairs)
            {
                if (!_facts.TryGetValue(p.Source, out var source)) continue; // JOIN book sb
                bool hasTarget = _facts.TryGetValue(p.Target, out var target);
                bool pairAllowed = hasTarget && source.Corpus != null && target.Corpus != null
                                   && source.Corpus == target.Corpus && target.IsBaseBook == 0;
                if (pairAllowed || p.MaxDeclared > 0)
                    kept.Add(new Ranked
                    {
                        Target = p.Target, Source = p.Source, MaxDeclared = p.MaxDeclared,
                        IsBase = source.IsBaseBook, Order = source.OrderIndex, DeclaredOnly = !pairAllowed,
                    });
            }

            // Pass 2: the coverage count (COUNT DISTINCT target lines) only breaks ties on the
            // keys above - counted in ONE statement, only for the books that have such a tie
            // where the order is needed.
            static (long, int, int) Key(Ranked x) => (x.MaxDeclared, x.IsBase, x.Order);
            var byTarget = kept.GroupBy(k => k.Target)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.MaxDeclared).ThenByDescending(x => x.IsBase).ThenBy(x => x.Order).ToList());
            var needCounts = new List<Ranked>();
            foreach (var ordered in byTarget.Values)
            {
                var ties = ordered.GroupBy(Key).Where(g => g.Count() > 1);
                if (primaryOnly) ties = ties.Where(g => g.Key == Key(ordered[0]));
                foreach (var tie in ties) needCounts.AddRange(tie);
            }
            if (needCounts.Count > 0) LoadCoverageCounts(needCounts, bp, typeList);
            foreach (var ordered in byTarget.Values)
                foreach (var x in ordered.OrderByDescending(x => x.MaxDeclared).ThenByDescending(x => x.IsBase)
                             .ThenBy(x => x.Order).ThenByDescending(x => x.Count).ThenBy(x => x.Source))
                    result[x.Target].Add(x.Source);
            return result;
        }

        private sealed class Ranked
        {
            public int Target, Source, IsBase, Order;
            public long MaxDeclared, Count;
            public bool DeclaredOnly;
        }

        private void LoadCoverageCounts(List<Ranked> pairs, string bp, string typeList)
        {
            var targets = pairs.Select(p => p.Target).Distinct().ToList();
            var sources = pairs.Select(p => p.Source).Distinct().ToList();
            var byPair = pairs.ToDictionary(p => (p.Target, p.Source));
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT l.targetBookId, l.sourceBookId, COUNT(DISTINCT l.targetLineId),
                       COUNT(DISTINCT CASE WHEN {bp} > 0 THEN l.targetLineId END)
                FROM link l
                WHERE l.targetBookId IN ({string.Join(",", targets)})
                  AND l.sourceBookId IN ({string.Join(",", sources)})
                  AND l.connectionTypeId IN ({typeList})
                GROUP BY l.targetBookId, l.sourceBookId";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (byPair.TryGetValue((r.GetInt32(0), r.GetInt32(1)), out var x))
                    x.Count = x.DeclaredOnly ? r.GetInt64(3) : r.GetInt64(2);
        }

        /// <summary>Whether links from <paramref name="sourceBookId"/> into
        /// <paramref name="targetBookId"/> count as base-text links WITHOUT a declared flag -
        /// GetReverseLineData's per-row test, which is constant for a book pair.</summary>
        public bool PairAllowedUndeclared(int sourceBookId, int targetBookId)
        {
            LoadFacts(new[] { sourceBookId, targetBookId });
            return _facts.TryGetValue(sourceBookId, out var s) && _facts.TryGetValue(targetBookId, out var t)
                   && s.Corpus != null && t.Corpus != null && s.Corpus == t.Corpus && t.IsBaseBook == 0;
        }

        private void LoadFacts(IEnumerable<int> ids)
        {
            var missing = ids.Where(id => !_facts.ContainsKey(id)).Distinct().ToList();
            if (missing.Count == 0) return;
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT b.id, b.isBaseBook, b.orderIndex,
                       (SELECT cc.ancestorId FROM category_closure cc
                          JOIN category c ON c.id = cc.ancestorId AND c.level = 0
                         WHERE cc.descendantId = b.categoryId)
                FROM book b
                WHERE b.id IN ({SeforimDbSqlStrings.InPlaceholders("b", missing.Count)})";
            for (int i = 0; i < missing.Count; i++) cmd.Parameters.AddWithValue("@b" + i, missing[i]);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                _facts[r.GetInt32(0)] = new BookFacts
                {
                    IsBaseBook = r.IsDBNull(1) ? 0 : r.GetInt32(1),
                    OrderIndex = r.IsDBNull(2) ? 0 : r.GetInt32(2),
                    Corpus = r.IsDBNull(3) ? null : r.GetInt64(3),
                };
        }
    }
}
