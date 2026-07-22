// Simulation harness for the proposed כתיב (literal-vs-variant) tiebreaker.
// Runs real queries against the real seforim.db index and prints the result ordering
// under three candidate placements of a new "IsLiteralMatch" signal relative to the
// existing (Level, TreeOrder) sort and the word-order post-filter:
//   A — literalness FIRST  (Level, TreeOrder demoted beneath it)
//   B — literalness BETWEEN Level and TreeOrder
//   C — literalness LAST   (only breaks ties Level+TreeOrder leave standing)
// Does not modify CatalogTocIndex — computes the flag independently by re-deriving,
// per hit, whether every query word appears literally in the hit's own matchable text
// (FullTocPath + catalog path + authors), using the same normalization pipeline.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KitveiHakodeshService.Catalog;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Tests
{
    public static class KtivTiebreakSim
    {
        public static int Run(string dbPath, string indexPath, string[] queries)
        {
            var index = new CatalogTocIndex(indexPath, dbPath);
            if (!index.TryOpenActive())
            {
                Console.Error.WriteLine("no index found — run --rebuild first");
                return 1;
            }

            // Book catalog-path + authors lookup, needed to reproduce the full matchable
            // text a hit was found through (path alone is not the whole SearchText).
            var catalogPathByBook = new Dictionary<int, string>();
            var authorsByBook = new Dictionary<int, string>();
            LoadBookMeta(dbPath, catalogPathByBook, authorsByBook);

            foreach (var query in queries)
            {
                Console.WriteLine();
                Console.WriteLine($"=== query: \"{query}\" ===");
                var hits = index.Search(query);
                if (hits.Count == 0) { Console.WriteLine("  (no hits)"); continue; }

                // VERIFY the SHIPPED ordering: confirm Search() already returns hits with
                // all IsLiteral==true ahead of all IsLiteral==false (strategy A in prod).
                int firstShippedVariant = hits.FindIndex(h => !h.IsLiteral);
                int lastShippedLiteral = hits.FindLastIndex(h => h.IsLiteral);
                bool shippedOrderOk = firstShippedVariant < 0 || lastShippedLiteral < 0 || firstShippedVariant > lastShippedLiteral;
                Console.WriteLine($"  SHIPPED Search() order: firstVariant=#{firstShippedVariant + 1} lastLiteral=#{lastShippedLiteral + 1} " +
                                  $"=> {(shippedOrderOk ? "literal-block-then-variant-block OK" : "VIOLATION: a variant precedes a literal")}");

                var qTokens = CatalogTocTextRules.Tokenize(query);
                var flagged = hits.Select(h =>
                {
                    string catalogPath = catalogPathByBook.GetValueOrDefault(h.BookId, "");
                    string authors = authorsByBook.GetValueOrDefault(h.BookId, "");
                    var docTokens = CatalogTocTextRules.Tokenize(catalogPath + " " + h.FullTocPath + " " + authors).ToHashSet();
                    bool literal = qTokens.All(docTokens.Contains);
                    return (Hit: h, Literal: literal);
                }).ToList();

                int literalCount = flagged.Count(f => f.Literal);
                Console.WriteLine($"  {hits.Count} hits total, {literalCount} literal, {hits.Count - literalCount} variant-matched");
                if (literalCount == 0 || literalCount == hits.Count)
                {
                    Console.WriteLine("  (no mix of literal/variant in this result set — tiebreaker placement can't matter here)");
                    continue;
                }

                // The tiebreaker only CHANGES anything when a variant-matched hit currently
                // out-ranks a literal one under the baseline. Detect and quantify that.
                var baselineSorted = new List<(CatalogTocHit Hit, bool Literal)>(flagged);
                baselineSorted.Sort(CompareBaseline);
                int firstVariantRank = baselineSorted.FindIndex(f => !f.Literal);
                int lastLiteralRank = baselineSorted.FindLastIndex(f => f.Literal);
                bool inversionExists = firstVariantRank >= 0 && firstVariantRank < lastLiteralRank;
                Console.WriteLine($"  baseline: first variant hit at rank #{firstVariantRank + 1}, " +
                                  $"last literal hit at rank #{lastLiteralRank + 1} => " +
                                  (inversionExists
                                     ? "INVERSION present (a variant out-ranks a literal — tiebreaker WILL change ordering)"
                                     : "no inversion (literals already all ahead of variants — tiebreaker is a no-op here)"));

                // Rank position of each Level's first literal vs first variant, plus how far
                // the tiebreaker would promote the earliest-mis-ranked literal.
                CompareStrategies(flagged);

                // Windowed view: show the ranks AROUND the first inversion under each strategy,
                // not just the top-10 (the divergence is usually deep in the list).
                if (inversionExists)
                {
                    int center = firstVariantRank;
                    PrintWindow("baseline (Level,TreeOrder)", flagged, CompareBaseline, center);
                    PrintWindow("A: literalness FIRST", flagged, CompareLiteralFirst, center);
                    PrintWindow("B: literalness BETWEEN Level and TreeOrder", flagged, CompareLiteralBetween, center);
                    PrintWindow("C: literalness LAST (tiebreak only)", flagged, CompareLiteralLast, center);
                }
                else
                {
                    PrintTop("baseline (Level,TreeOrder) top-10", flagged, CompareBaseline, 10);
                }
            }
            return 0;
        }

        private static int CompareBaseline((CatalogTocHit Hit, bool Literal) a, (CatalogTocHit Hit, bool Literal) b)
        {
            int c = a.Hit.Level.CompareTo(b.Hit.Level);
            return c != 0 ? c : a.Hit.TreeOrder.CompareTo(b.Hit.TreeOrder);
        }

        private static int CompareLiteralFirst((CatalogTocHit Hit, bool Literal) a, (CatalogTocHit Hit, bool Literal) b)
        {
            int c = b.Literal.CompareTo(a.Literal); // literal(true) first
            if (c != 0) return c;
            c = a.Hit.Level.CompareTo(b.Hit.Level);
            return c != 0 ? c : a.Hit.TreeOrder.CompareTo(b.Hit.TreeOrder);
        }

        private static int CompareLiteralBetween((CatalogTocHit Hit, bool Literal) a, (CatalogTocHit Hit, bool Literal) b)
        {
            int c = a.Hit.Level.CompareTo(b.Hit.Level);
            if (c != 0) return c;
            c = b.Literal.CompareTo(a.Literal);
            return c != 0 ? c : a.Hit.TreeOrder.CompareTo(b.Hit.TreeOrder);
        }

        private static int CompareLiteralLast((CatalogTocHit Hit, bool Literal) a, (CatalogTocHit Hit, bool Literal) b)
        {
            int c = a.Hit.Level.CompareTo(b.Hit.Level);
            if (c != 0) return c;
            c = a.Hit.TreeOrder.CompareTo(b.Hit.TreeOrder);
            return c != 0 ? c : b.Literal.CompareTo(a.Literal);
        }

        private static void PrintTop(string label, List<(CatalogTocHit Hit, bool Literal)> flagged,
            Comparison<(CatalogTocHit Hit, bool Literal)> cmp, int n)
        {
            var sorted = new List<(CatalogTocHit Hit, bool Literal)>(flagged);
            sorted.Sort(cmp);
            Console.WriteLine($"  --- {label} ---");
            for (int i = 0; i < Math.Min(n, sorted.Count); i++)
            {
                var (h, lit) = sorted[i];
                Console.WriteLine($"    #{i + 1,-3} [{(lit ? "LIT" : "VAR")}] lvl={h.Level} {h.FullTocPath}");
            }
        }

        // Show a window of ranks centered on `center` so the literal/variant divergence is
        // visible (it's typically deep in the list, not the top-10).
        private static void PrintWindow(string label, List<(CatalogTocHit Hit, bool Literal)> flagged,
            Comparison<(CatalogTocHit Hit, bool Literal)> cmp, int center)
        {
            var sorted = new List<(CatalogTocHit Hit, bool Literal)>(flagged);
            sorted.Sort(cmp);
            int lo = Math.Max(0, center - 3);
            int hi = Math.Min(sorted.Count, center + 5);
            Console.WriteLine($"  --- {label} (ranks #{lo + 1}..#{hi}) ---");
            for (int i = lo; i < hi; i++)
            {
                var (h, lit) = sorted[i];
                Console.WriteLine($"    #{i + 1,-3} [{(lit ? "LIT" : "VAR")}] lvl={h.Level} to={h.TreeOrder} {h.FullTocPath}");
            }
        }

        // Per-strategy summary: at what rank does the first variant-matched hit sit, and how
        // many literal hits does it jump ahead of (0 = no change vs baseline)?
        private static void CompareStrategies(List<(CatalogTocHit Hit, bool Literal)> flagged)
        {
            foreach (var (label, cmp) in new (string, Comparison<(CatalogTocHit, bool)>)[]
            {
                ("baseline", CompareBaseline),
                ("A first ", CompareLiteralFirst),
                ("B between", CompareLiteralBetween),
                ("C last  ", CompareLiteralLast),
            })
            {
                var sorted = new List<(CatalogTocHit Hit, bool Literal)>(flagged);
                sorted.Sort(cmp);
                int firstVariant = sorted.FindIndex(f => !f.Literal);
                int lastLiteral = sorted.FindLastIndex(f => f.Literal);
                int literalsAfterFirstVariant = 0;
                for (int i = firstVariant + 1; i < sorted.Count; i++)
                    if (sorted[i].Literal) literalsAfterFirstVariant++;
                Console.WriteLine($"    [{label}] firstVariant=#{firstVariant + 1}  lastLiteral=#{lastLiteral + 1}  " +
                                  $"literalsStillBelowAVariant={literalsAfterFirstVariant}");
            }
        }

        private static void LoadBookMeta(string dbPath, Dictionary<int, string> catalogPathByBook, Dictionary<int, string> authorsByBook)
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ConnectionString);
            conn.Open();

            var categories = new List<(int Id, int? ParentId, string Title)>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, parentId, title FROM category ORDER BY level";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    categories.Add((r.GetInt32(0), r.IsDBNull(1) ? null : r.GetInt32(1), r.IsDBNull(2) ? "" : r.GetString(2)));
            }
            var catById = new Dictionary<int, (int? ParentId, string Title)>();
            foreach (var c in categories) catById[c.Id] = (c.ParentId, c.Title);
            var pathCache = new Dictionary<int, string>();
            string CatPath(int id)
            {
                if (pathCache.TryGetValue(id, out var cached)) return cached;
                if (!catById.TryGetValue(id, out var c)) return "";
                string result = c.ParentId is { } pid ? (CatPath(pid) + " " + c.Title).Trim() : c.Title;
                pathCache[id] = result;
                return result;
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT b.id, b.categoryId, group_concat(a.name, ', ') AS authors
                    FROM book b
                    LEFT JOIN book_author ba ON ba.bookId = b.id
                    LEFT JOIN author a ON a.id = ba.authorId
                    GROUP BY b.id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    int id = r.GetInt32(0);
                    int catId = r.IsDBNull(1) ? 0 : r.GetInt32(1);
                    catalogPathByBook[id] = CatPath(catId);
                    authorsByBook[id] = r.IsDBNull(2) ? "" : r.GetString(2);
                }
            }
        }
    }
}
