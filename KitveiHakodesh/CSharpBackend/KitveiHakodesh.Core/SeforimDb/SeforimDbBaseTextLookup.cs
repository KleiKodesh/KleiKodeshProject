using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>
    /// The base texts of many books at once, best first, for the related-books list.
    ///
    /// A book's bases are its rows in the library's <c>book_base_text</c> table; a book with no
    /// row there (and every book on a library without the table - Zayit) takes the books that
    /// link to it with a DECLARED base link (schema.DeclaredBaseColumn &gt; 0). Bases inferred
    /// from undeclared links are not used. Several bases rank as GetReverseBooks ranked them:
    /// declared flag (declared links only), a base-text book first, catalogue order, then how
    /// much of the book the base covers - counted only when the order still needs it.
    ///
    /// <see cref="PairAllowedUndeclared"/> serves the click resolver, whose per-line link rules
    /// are GetReverseLineData's and unchanged.
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
            if (result.Count == 0) return result;
            string bp = _schema.DeclaredBaseExpr("l");
            string typeList = string.Join(",", _typeIds);
            var kept = new List<Ranked>();

            // The table first: one indexed read for all of them.
            if (_schema.HasBookBaseText)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = $"SELECT bookId, baseBookId FROM book_base_text WHERE bookId IN ({string.Join(",", result.Keys)}) AND baseBookId != bookId";
                using var r = cmd.ExecuteReader();
                while (r.Read()) kept.Add(new Ranked { Target = r.GetInt32(0), Source = r.GetInt32(1), FromTable = true });
            }

            // Books with no row: their declared base links, one grouped statement.
            var withoutRow = result.Keys.Where(id => !kept.Any(k => k.Target == id)).ToList();
            if (withoutRow.Count > 0 && _typeIds.Count > 0 && _schema.HasDeclaredBaseColumn)
            {
                using var cmd = _conn.CreateCommand();
                cmd.CommandText = $@"
                    SELECT l.targetBookId, l.sourceBookId, MAX({bp})
                    FROM link l
                    WHERE l.targetBookId IN ({string.Join(",", withoutRow)})
                      AND l.connectionTypeId IN ({typeList})
                      AND {bp} > 0
                      AND l.sourceBookId != l.targetBookId
                    GROUP BY l.targetBookId, l.sourceBookId";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    kept.Add(new Ranked { Target = r.GetInt32(0), Source = r.GetInt32(1), MaxDeclared = r.GetInt64(2), DeclaredOnly = true });
            }
            if (kept.Count == 0) return result;

            LoadFacts(kept.Select(k => k.Source));
            kept.RemoveAll(k => !_facts.ContainsKey(k.Source)); // a base missing from the book table
            foreach (var k in kept)
            {
                var f = _facts[k.Source];
                k.IsBase = f.IsBaseBook;
                k.Order = f.OrderIndex;
            }

            // Coverage (COUNT DISTINCT target lines) only breaks ties on the keys above -
            // counted in ONE statement, only for the books that have such a tie.
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

        /// <summary>The books whose table row names <paramref name="baseBookIds"/> as a base
        /// (idx_book_base_text_base). Empty on a library without the table.</summary>
        public List<int> BooksBasedOn(IReadOnlyCollection<int> baseBookIds)
        {
            var list = new List<int>();
            if (!_schema.HasBookBaseText || baseBookIds.Count == 0) return list;
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"SELECT DISTINCT bookId FROM book_base_text WHERE baseBookId IN ({string.Join(",", baseBookIds)}) ORDER BY bookId";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(r.GetInt32(0));
            return list;
        }

        private sealed class Ranked
        {
            public int Target, Source, IsBase, Order;
            public long MaxDeclared, Count;
            public bool DeclaredOnly, FromTable;
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
