using FtsLib.SeforimDb;
using System;
using System.IO;
using System.Linq;

namespace FtsLibTest
{
    /// <summary>
    /// Reproduces / verifies the force-merge correctness bug: result counts must be
    /// IDENTICAL before and after a force merge (a merge only reorganizes storage, it
    /// must never change which lines match). Builds a fresh index WITHOUT force merge,
    /// counts a battery of queries, force-merges, then re-counts. Any query whose count
    /// changes is a merge data-loss/corruption bug.
    ///
    ///   FtsLibTest.exe forcemergebug [tier]
    /// </summary>
    internal static class ForceMergeBugTest
    {
        private static readonly string[] Queries =
        {
            "כי ביצחק", "כי", "ביצחק", "משה רבינו", "ארץ ישראל", "רבי", "תורה",
            "אלהים", "בית המקדש", "מלך", "עולם", "ויאמר",
        };

        public static void Run(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            string tier = args.Length > 1 ? args[1] : "3m";
            (string Label, int Limit) t;
            try { t = TestHelpers.ResolveTier(tier); }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string dbPath = BuildTest.ResolveDbPath();
            string indexDir = TestHelpers.IndexDir(t.Label + "_fmbug");
            if (!File.Exists(dbPath)) { Console.WriteLine("DB not found: " + dbPath); return; }

            Console.WriteLine($"[forcemergebug] tier={t.Label}  index={indexDir}");
            if (Directory.Exists(indexDir)) { try { Directory.Delete(indexDir, true); } catch { } }

            // 1) Fresh build, NO force merge → many live segments across levels.
            var index = new SeforimIndex(indexDir, dbPath);
            Console.WriteLine("[forcemergebug] building (no force merge)...");
            index.BuildIndex(limit: t.Limit, forceMergeOnComplete: false);
            index = new SeforimIndex(indexDir, dbPath); // reopen clean

            int segsBefore = Directory.GetFiles(indexDir, "seg_*.dat").Length;
            var before = Queries.ToDictionary(q => q, q => index.SearchIds(q).Count());

            // 2) Force merge.
            Console.WriteLine($"[forcemergebug] {segsBefore} segments before — force merging...");
            index.ForceMerge();
            int segsAfter = Directory.GetFiles(indexDir, "seg_*.dat").Length;
            var after = Queries.ToDictionary(q => q, q => index.SearchIds(q).Count());

            // 3) Compare.
            Console.WriteLine();
            Console.WriteLine($"  segments: {segsBefore} → {segsAfter}");
            Console.WriteLine("  query             before      after   status");
            Console.WriteLine("  --------------------------------------------------");
            bool ok = true;
            foreach (var q in Queries)
            {
                int b = before[q], a = after[q];
                bool same = a == b;
                if (!same) ok = false;
                Console.WriteLine($"  {q,-15} {b,9:N0} {a,9:N0}   {(same ? "ok" : "*** LOST " + (b - a).ToString("N0") + " ***")}");
            }
            Console.WriteLine("  --------------------------------------------------");
            Console.WriteLine($"[forcemergebug] {(ok ? "PASS — force merge is lossless" : "FAIL — force merge CHANGED result counts (corruption)")}");
        }
    }
}
