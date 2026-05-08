using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Builds the full index, probes for line 548 BEFORE the force-merge,
    /// runs Optimize(), then probes again AFTER.
    ///
    /// This isolates whether ForceMergeAll() is the step that loses line 548.
    ///
    /// Usage:
    ///   FtsLibTest.exe beforeafter [tier]
    ///
    /// The index is always wiped at the start so results are never stale.
    /// </summary>
    internal static class BeforeMergeAfterMergeTest
    {
        private const string ProbeQuery  = "כי ביצחק";
        private const int    RequiredId  = 548;

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string tierLabel = args.Length > 1 ? args[1] : "full";
            string label;
            int    limit;
            try
            {
                var tier = TestHelpers.ResolveTier(tierLabel);
                label = tier.Label;
                limit = tier.Limit;
            }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string indexDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                $"index_{label}_beforeafter");
            string dbPath = BuildTest.ResolveDbPath();

            // ── Wipe old index ────────────────────────────────────────
            if (Directory.Exists(indexDir))
            {
                Console.WriteLine($"Wiping old index: {indexDir}");
                Directory.Delete(indexDir, recursive: true);
            }
            Directory.CreateDirectory(indexDir);

            Console.WriteLine();
            Console.WriteLine($"╔══ BEFORE/AFTER MERGE TEST — {label.ToUpper()} ══");
            Console.WriteLine($"║  Index : {indexDir}");
            Console.WriteLine($"║  DB    : {dbPath}");
            Console.WriteLine($"║  Probe : \"{ProbeQuery}\"  (must contain id={RequiredId})");
            Console.WriteLine("╠══ PHASE 1: BUILD (no merge) ══");

            var index = new SeforimIndex(indexDir, dbPath);

            // ── Phase 1: Build ────────────────────────────────────────
            var swBuild = Stopwatch.StartNew();
            long linesIndexed = 0;
            index.BuildIndex(
                limit: limit,
                onProgress: n => { linesIndexed = n; });
            swBuild.Stop();

            Console.WriteLine($"║  Build done: {linesIndexed:N0} lines in {TestHelpers.FormatElapsed(swBuild.Elapsed)}");
            PrintSegments(indexDir, "║  ");

            // ── Phase 2: Probe BEFORE merge ───────────────────────────
            Console.WriteLine("╠══ PHASE 2: PROBE BEFORE MERGE ══");
            var beforeResult = Probe(index, indexDir);
            PrintProbeResult(beforeResult, "BEFORE");

            // ── Phase 3: Force-merge ──────────────────────────────────
            Console.WriteLine("╠══ PHASE 3: OPTIMIZE (force-merge) ══");
            var swMerge = Stopwatch.StartNew();
            index.Optimize();
            swMerge.Stop();
            Console.WriteLine($"║  Optimize done in {TestHelpers.FormatElapsed(swMerge.Elapsed)}");
            PrintSegments(indexDir, "║  ");

            // ── Phase 4: Probe AFTER merge ────────────────────────────
            Console.WriteLine("╠══ PHASE 4: PROBE AFTER MERGE ══");
            var afterResult = Probe(index, indexDir);
            PrintProbeResult(afterResult, "AFTER ");

            // ── Summary ───────────────────────────────────────────────
            Console.WriteLine("╠══ SUMMARY ══");
            Console.WriteLine($"║  BEFORE merge:  {beforeResult.Count:N0} results  id={RequiredId} → {(beforeResult.Found ? "FOUND ✓" : "MISSING ✗")}");
            Console.WriteLine($"║  AFTER  merge:  {afterResult.Count:N0} results  id={RequiredId} → {(afterResult.Found ? "FOUND ✓" : "MISSING ✗")}");

            if (!beforeResult.Found && !afterResult.Found)
                Console.WriteLine("║  ⚠  Line 548 missing in BOTH — bug is in the build, not the merge.");
            else if (beforeResult.Found && !afterResult.Found)
                Console.WriteLine("║  ✗  Line 548 LOST during force-merge — merge is the culprit.");
            else if (!beforeResult.Found && afterResult.Found)
                Console.WriteLine("║  ?  Line 548 appeared after merge — unexpected.");
            else
                Console.WriteLine("║  ✓  Line 548 present in both — force-merge is NOT the culprit.");

            Console.WriteLine("╚══ DONE ══");
        }

        // ── Helpers ───────────────────────────────────────────────────

        private sealed class ProbeResult
        {
            public int  Count;
            public bool Found;
            public long ElapsedMs;
        }

        private static ProbeResult Probe(SeforimIndex index, string indexDir)
        {
            var result = new ProbeResult();
            var sw     = Stopwatch.StartNew();
            var ids    = new List<int>();
            foreach (var r in index.Search(ProbeQuery))
                ids.Add(r.LineId);
            sw.Stop();

            result.Count     = ids.Count;
            result.ElapsedMs = sw.ElapsedMilliseconds;
            result.Found     = ids.Contains(RequiredId);

            Console.WriteLine($"║  Query  : \"{ProbeQuery}\"");
            Console.WriteLine($"║  Results: {result.Count:N0}  ({result.ElapsedMs} ms)");
            Console.WriteLine($"║  id={RequiredId} : {(result.Found ? "FOUND ✓" : "MISSING ✗")}");

            // Print the 5 smallest IDs so we can see if early IDs are generally missing
            ids.Sort();
            int show = Math.Min(5, ids.Count);
            var first5 = new StringBuilder("║  First IDs: ");
            for (int i = 0; i < show; i++)
            {
                if (i > 0) first5.Append(", ");
                first5.Append(ids[i]);
            }
            Console.WriteLine(first5.ToString());

            return result;
        }

        private static void PrintProbeResult(ProbeResult r, string label)
        {
            // Already printed inline in Probe(); this just adds the verdict line.
            string verdict = r.Found ? "PASS" : "FAIL";
            Console.WriteLine($"║  [{label}] {verdict}");
        }

        private static void PrintSegments(string indexDir, string prefix)
        {
            var files = Directory.GetFiles(indexDir, "seg_*.dat");
            Array.Sort(files);
            long total = 0;
            foreach (var f in files)
            {
                long sz = new FileInfo(f).Length;
                total += sz;
                Console.WriteLine($"{prefix}{Path.GetFileName(f),22}  {sz / 1_048_576.0,6:F1} MB");
            }
            Console.WriteLine($"{prefix}Total: {files.Length} segment(s)  {total / 1_048_576.0:F1} MB");
        }
    }
}
