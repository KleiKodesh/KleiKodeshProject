using FtsLib.SeforimDb;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Minimal repro/diagnosis for the filtered-OR-query id loss seen in
    /// filtertest (e.g. "משה | אהרן" subset-small: expected 51, got 43).
    ///
    /// Prints the exact missing ids, classifies each (which term(s) contain it),
    /// and probes minimal filters (single missing id; only-missing-ids) so the
    /// failure can be pinned to a specific iterator path.
    ///
    /// Usage: FtsLibTest.exe filterrepro [tier] [query terms...]
    /// </summary>
    internal static class FilterRepro
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string tierLabel = args.Length > 1 ? args[1] : "full";
            string label;
            try   { label = TestHelpers.ResolveTier(tierLabel).Label; }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string query = args.Length > 2
                ? string.Join(" ", args, 2, args.Length - 2)
                : "משה | אהרן";

            string indexDir = TestHelpers.IndexDir(label);
            if (!Directory.Exists(indexDir))
            { Console.WriteLine($"No index at: {indexDir}"); return; }

            string dbPath = BuildTest.ResolveDbPath();
            var index = new SeforimIndex(indexDir, dbPath);

            Console.WriteLine($"Query: \"{query}\"");

            var baseline = index.SearchIds(query).ToList();
            Console.WriteLine($"Baseline: {baseline.Count:N0} ids");

            // Same sampling as FilterTest subset-small.
            var subset = new List<int>();
            int n = Math.Max(1, baseline.Count / 50);
            for (int i = 0; i < baseline.Count; i += n) subset.Add(baseline[i]);

            var expected = new List<int>(subset); // all sampled from baseline → all expected
            var actual   = index.SearchIds(query, filterIds: subset).ToList();

            var actualSet = new HashSet<int>(actual);
            var missing   = expected.Where(id => !actualSet.Contains(id)).ToList();

            Console.WriteLine($"Filter: {subset.Count} ids → got {actual.Count}, missing {missing.Count}");
            if (missing.Count == 0) { Console.WriteLine("No repro — all ids returned."); return; }

            Console.WriteLine($"Missing ids: {string.Join(", ", missing)}");
            Console.WriteLine($"Subset ids : {string.Join(", ", subset)}");

            // Which alternative term(s) contain each missing id? (Assumes the
            // query is a single OR group of literal alternatives.)
            var terms = query.Split('|').Select(t => t.Trim())
                             .Where(t => t.Length > 0).ToList();
            var termSets = new Dictionary<string, HashSet<int>>();
            foreach (var t in terms)
                termSets[t] = new HashSet<int>(index.SearchIds(t));

            Console.WriteLine();
            Console.WriteLine("Classification of missing ids:");
            foreach (var id in missing)
            {
                var owners = terms.Where(t => termSets[t].Contains(id)).ToList();
                // Single-id filter probe: minimal repro candidate.
                bool singleOk = index.SearchIds(query, filterIds: new[] { id }).Contains(id);
                Console.WriteLine($"  {id,10}  in [{string.Join(", ", owners)}]  single-id-filter: {(singleOk ? "FOUND" : "LOST")}");
            }

            // Filter containing ONLY the missing ids.
            var onlyMissing = index.SearchIds(query, filterIds: missing).ToList();
            Console.WriteLine();
            Console.WriteLine($"Filter=only-missing ({missing.Count} ids) → got {onlyMissing.Count}");

            // Classify the ids that WERE returned, for contrast (first few).
            Console.WriteLine();
            Console.WriteLine("Classification of first 8 returned ids:");
            foreach (var id in actual.Take(8))
            {
                var owners = terms.Where(t => termSets[t].Contains(id)).ToList();
                Console.WriteLine($"  {id,10}  in [{string.Join(", ", owners)}]");
            }

            // Segment-order forensics: a single term's id stream walks segments in
            // reader order (ConcatIterator), so its maximal ascending runs expose
            // each segment's id range for that term. Overlapping runs = overlapping
            // segment doc ranges; descending run starts = wrong segment order.
            Console.WriteLine();
            Console.WriteLine("Ascending-run analysis per term (runs ≈ segments in reader order):");
            foreach (var t in terms)
            {
                Console.WriteLine($"  term \"{t}\":");
                int  runStart = -1, prev = int.MinValue, runCount = 0, runIdx = 0;
                foreach (var id in index.SearchIds(t))
                {
                    if (id < prev)
                    {
                        Console.WriteLine($"    run {runIdx++}: {runStart:N0} .. {prev:N0}  ({runCount:N0} ids)");
                        runStart = id; runCount = 0;
                    }
                    if (runStart < 0) runStart = id;
                    prev = id; runCount++;
                }
                if (runCount > 0)
                    Console.WriteLine($"    run {runIdx}: {runStart:N0} .. {prev:N0}  ({runCount:N0} ids)");

                // Duplicates = same doc appears in more than one live segment
                // (overlapping segment CONTENT — corruption). No duplicates with
                // overlapping run RANGES = interleaved-but-disjoint ranges — a
                // pure segment-ordering bug.
                var all = index.SearchIds(t).ToList();
                Console.WriteLine($"    total {all.Count:N0} vs distinct {all.Distinct().Count():N0}"
                    + (all.Count == all.Distinct().Count() ? "  (no duplicates)" : "  ⚠ DUPLICATED POSTINGS"));
            }
        }
    }
}
