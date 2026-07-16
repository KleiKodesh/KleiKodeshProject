using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Diagnostic: reproduces the FRONTEND relevancy sort on real index hits so we can
    /// see exactly how "sort by relevancy" reorders a query's results.
    ///
    /// For a query it collects every hit (no cap), records each hit's WordDistance and
    /// Score (char span), then prints:
    ///   • the DEFAULT order (as the pipeline streams them — ascending LineId), and
    ///   • the RELEVANCY order the frontend applies:
    ///        wordDistance ASC, then score ASC, then lineId ASC
    ///
    /// This is the ground-truth check for "adjacent-word hits must come first".
    ///
    /// Usage:
    ///   FtsLibTest.exe sortdiag [tier] "query"
    /// Example:
    ///   FtsLibTest.exe sortdiag 500k "כי ביצחק"
    /// </summary>
    internal static class SortDiag
    {
        private readonly struct Hit
        {
            public readonly int LineId;
            public readonly int WordDistance;
            public readonly int Score;
            public readonly string BookTitle;
            public Hit(int lineId, int wordDistance, int score, string bookTitle)
            {
                LineId = lineId; WordDistance = wordDistance; Score = score; BookTitle = bookTitle;
            }
        }

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length < 3)
            {
                Console.WriteLine("Usage: FtsLibTest.exe sortdiag [tier] \"query\"");
                return;
            }

            string tierLabel = args[1];
            string query     = string.Join(" ", args, 2, args.Length - 2);

            string label;
            try   { label = TestHelpers.ResolveTier(tierLabel).Label; }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string dbPath   = BuildTest.ResolveDbPath();
            string indexDir = TestHelpers.IndexDir(label);

            if (!Directory.Exists(indexDir) ||
                Directory.GetFiles(indexDir, "seg_*.dat").Length == 0)
            {
                Console.WriteLine($"No index found at: {indexDir}. Run 'build {label}' first.");
                return;
            }

            var index = new SeforimIndex(indexDir, dbPath);

            // Collect every hit with its snippet-derived WordDistance/Score — exactly the two
            // fields the C# hit ships to the frontend (see FtsHit.WordDistance / .Score).
            var hits = new List<Hit>();
            foreach (var r in index.Search(query))
            {
                var s = index.GenerateSnippet(r);
                if (!s.IsMatch) continue;                 // false positives are filtered before shipping
                hits.Add(new Hit(r.LineId, s.WordDistance, s.Score, r.BookTitle));
            }

            Console.WriteLine();
            Console.WriteLine($"╔══ SORT DIAG: \"{query}\"  [{label.ToUpper()}]  {hits.Count} matched hit(s) ══");

            // DEFAULT order = ascending LineId (how the pipeline streams them).
            var byLineId = hits.OrderBy(h => h.LineId).ToList();

            // RELEVANCY order = the frontend comparator: wordDistance, then lineId.
            // (No score tiebreak — ties in distance keep original document order.)
            var byRelevance = hits
                .OrderBy(h => h.WordDistance)
                .ThenBy(h => h.LineId)
                .ToList();

            PrintTop("DEFAULT (line-id / original order)", byLineId, 15);
            PrintTop("RELEVANCY (wordDist, then lineId)", byRelevance, 15);

            // Sanity: the relevancy head must be all wordDistance==0 if any adjacent hits exist.
            int adjacent = hits.Count(h => h.WordDistance == 0);
            int leadAdjacent = 0;
            foreach (var h in byRelevance) { if (h.WordDistance == 0) leadAdjacent++; else break; }
            Console.WriteLine("║");
            Console.WriteLine($"║  adjacent (wordDist=0) hits : {adjacent}");
            Console.WriteLine($"║  leading run of wordDist=0  : {leadAdjacent}");
            Console.WriteLine($"║  CHECK: all adjacent hits lead the relevancy order → {(leadAdjacent == adjacent ? "PASS" : "FAIL")}");
            Console.WriteLine("╚══ SORT DIAG DONE ══");
            Console.WriteLine();
        }

        private static void PrintTop(string title, List<Hit> hits, int n)
        {
            Console.WriteLine("║");
            Console.WriteLine($"║  ── {title} — first {Math.Min(n, hits.Count)} of {hits.Count} ──");
            Console.WriteLine($"║     {"#",3}  {"LineId",8}  {"wDist",5}  {"score",5}  Book");
            for (int i = 0; i < hits.Count && i < n; i++)
            {
                var h = hits[i];
                Console.WriteLine($"║     {i + 1,3}  {h.LineId,8}  {h.WordDistance,5}  {h.Score,5}  {TestHelpers.Truncate(h.BookTitle, 30)}");
            }
        }
    }
}
