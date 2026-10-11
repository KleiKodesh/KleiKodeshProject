using FtsLib.Search;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// <c>fuzzybench [indexDir] [queriesPerBucket=40]</c>
    ///
    /// Fuzzy (Levenshtein) term lookup: the memory-mapped FM-index trie walk vs the production
    /// FuzzyExpander candidate path, per segment. Queries are random terms of length 3..8 with
    /// one random edit (deterministic seed), searched at max distance 1 and 2. Times:
    ///   current — production routing mirrored from FuzzyExpander.Expand: trigram-sidecar
    ///             union + rowid-IN OR-LIKE confirm (≤ 8000 candidates), else OR-LIKE scan
    ///             (bigrams / over cap), else the ±d length-window scan; then the Levenshtein
    ///             confirm. Expansion cap NOT applied (it is downstream and the same for both).
    ///   fmIds   — FmIndex.FindFuzzy: exact rowids with their distances
    ///   fmFetch — fmIds + fetching the production columns by rowid IN (...)
    /// Every result set is checked against a brute-force Levenshtein scan of all terms.
    /// Numbers only; queries are never printed.
    /// </summary>
    internal static class FuzzyBench
    {
        const int TrigramCandidateCap = 8000; // same as FuzzyExpander

        public static void Run(string[] args)
        {
            string indexDir = args.Length > 1 ? args[1] : TestHelpers.IndexDir("full");
            int perBucket = args.Length > 2 ? int.Parse(args[2]) : 40;
            string outDir = indexDir.TrimEnd('\\', '/') + "_fmbench";
            Directory.CreateDirectory(outDir);

            foreach (string db in Directory.GetFiles(indexDir, "seg_*.db"))
            {
                try { RunSegment(db, outDir, perBucket); }
                catch (NotSupportedException ex) { Console.WriteLine($"{Path.GetFileName(db)} skipped: {ex.Message}"); }
            }
        }

        static void RunSegment(string dbPath, string outDir, int perBucket)
        {
            string name = Path.GetFileNameWithoutExtension(dbPath);
            Console.WriteLine($"== segment {name}");

            var terms = new List<string>(); var rowIds = new List<int>();
            using (var conn = FmIndexBench.Open(dbPath))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT rowid, term FROM term_index ORDER BY rowid";
                using var r = cmd.ExecuteReader();
                while (r.Read()) { rowIds.Add((int)r.GetInt64(0)); terms.Add(r.GetString(1)); }
            }

            string fmPath = Path.Combine(outDir, name + ".fmi");
            var sw = Stopwatch.StartNew();
            FmIndex.Build(fmPath, terms, rowIds);
            Console.WriteLine($"fm build ms {sw.Elapsed.TotalMilliseconds:F0}   size MB {FmIndexBench.Mb(new FileInfo(fmPath).Length):F2}");

            // The production sidecar next to the segment, if present.
            string tgmPath = TrigramIndex.SidecarPath(Path.ChangeExtension(dbPath, ".dat"));
            using var tgm = File.Exists(tgmPath) ? new TrigramIndex.Reader(tgmPath) : null;
            Console.WriteLine($"trigram sidecar: {(tgm != null ? "present" : "ABSENT (current path will scan)")}");

            using var fm = new FmIndex.Reader(fmPath);
            using var segConn = FmIndexBench.Open(dbPath);
            var queries = SampleQueries(terms, perBucket);
            var termOfRowId = new Dictionary<int, string>(terms.Count);
            for (int i = 0; i < terms.Count; i++) termOfRowId[rowIds[i]] = terms[i];

            string[] methods = { "current", "fmIds", "fmFetch" };
            var results = new List<(Query q, int hits, double[] ms, string route)>();
            int mismatches = 0;
            for (int pass = 0; pass < 2; pass++) // pass 0 warms caches + JIT; pass 1 is measured
            {
                results.Clear(); mismatches = 0;
                foreach (var q in queries)
                {
                    int[] truth = BruteForce(terms, rowIds, q);
                    var ms = new double[methods.Length];

                    sw.Restart(); int[] current = CurrentFuzzy(segConn, tgm, q, out string route); ms[0] = sw.Elapsed.TotalMilliseconds;

                    sw.Restart();
                    var fmHits = fm.FindFuzzy(q.Text, q.MaxDistance);
                    int[] fmIds = fmHits.Select(h => h.RowId).ToArray();
                    ms[1] = sw.Elapsed.TotalMilliseconds;
                    int[] fetched = FmIndexBench.FetchByRowIds(segConn, fmIds); ms[2] = sw.Elapsed.TotalMilliseconds;

                    bool distancesRight = fmHits.All(h => termOfRowId.TryGetValue(h.RowId, out string t) && Levenshtein.Distance(q.Text, t) == h.Distance);
                    if (!FmIndexBench.Same(truth, current) || !FmIndexBench.Same(truth, fmIds) ||
                        !FmIndexBench.Same(truth, fetched) || !distancesRight)
                        mismatches++;
                    results.Add((q, truth.Length, ms, route));
                }
            }

            Console.WriteLine($"queries {queries.Count}   result mismatches vs brute force: {mismatches}");
            Console.WriteLine();
            Console.WriteLine("median / p95 / max ms per bucket (warm)");
            Console.Write($"{"bucket",-8}{"n",4}{"avgHits",9}{"maxHits",9}");
            foreach (string m in methods) Console.Write($"{m,26}");
            Console.WriteLine();
            foreach (var bucket in results.GroupBy(x => (x.q.MaxDistance, x.q.Text.Length)).OrderBy(g => g.Key.MaxDistance).ThenBy(g => g.Key.Length))
            {
                var list = bucket.ToList();
                Console.Write($"{$"d{bucket.Key.MaxDistance}L{bucket.Key.Length}",-8}{list.Count,4}{list.Average(x => x.hits),9:F0}{list.Max(x => x.hits),9}");
                for (int m = 0; m < methods.Length; m++)
                {
                    var t = list.Select(x => x.ms[m]).OrderBy(x => x).ToArray();
                    Console.Write($"{FmIndexBench.Fmt(FmIndexBench.Pct(t, 0.5)),8}/{FmIndexBench.Fmt(FmIndexBench.Pct(t, 0.95)),8}/{FmIndexBench.Fmt(t[^1]),8}");
                }
                Console.WriteLine();
            }
            Console.WriteLine("current routes: " + string.Join("  ", results.GroupBy(x => x.route).Select(g => $"{g.Key}={g.Count()}")));
        }

        // ── Queries ───────────────────────────────────────────────────

        sealed class Query { public string Text; public int MaxDistance; }

        /// <summary>A random term of the bucket length with one random edit (substitute,
        /// insert or delete; letters drawn from other terms). Bucket length = query length.
        /// Skips ASCII letters and LIKE metacharacters, as fmbench does.</summary>
        static List<Query> SampleQueries(List<string> terms, int perBucket)
        {
            var rng = new Random(424242);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<Query>();
            foreach (int maxDistance in new[] { 1, 2 })
                foreach (int len in new[] { 3, 4, 5, 6, 7, 8 })
                {
                    int got = 0, tries = 0;
                    while (got < perBucket && tries++ < perBucket * 10000)
                    {
                        string t = terms[rng.Next(terms.Count)];
                        string donor = terms[rng.Next(terms.Count)];
                        char letter = donor[rng.Next(donor.Length)];
                        string q;
                        switch (rng.Next(3))
                        {
                            case 0:  if (t.Length != len) continue; int i = rng.Next(len); q = t.Substring(0, i) + letter + t.Substring(i + 1); break;
                            case 1:  if (t.Length != len - 1) continue; int k = rng.Next(len); q = t.Insert(k, letter.ToString()); break;
                            default: if (t.Length != len + 1) continue; q = t.Remove(rng.Next(t.Length), 1); break;
                        }
                        if (q.Any(ch => ch < 128 && (char.IsLetter(ch) || ch == '%' || ch == '_' || ch == '\\'))) continue;
                        if (!seen.Add(maxDistance + ":" + q)) continue;
                        list.Add(new Query { Text = q, MaxDistance = maxDistance }); got++;
                    }
                }
            return list;
        }

        // ── Methods ───────────────────────────────────────────────────

        static int[] BruteForce(List<string> terms, List<int> rowIds, Query q)
        {
            var hits = new List<int>();
            for (int i = 0; i < terms.Count; i++)
                if (Math.Abs(terms[i].Length - q.Text.Length) <= q.MaxDistance &&
                    Levenshtein.Distance(q.Text, terms[i], q.MaxDistance) <= q.MaxDistance)
                    hits.Add(rowIds[i]);
            hits.Sort();
            return hits.ToArray();
        }

        /// <summary>Mirrors FuzzyExpander.Expand candidate generation + Levenshtein confirm.</summary>
        static int[] CurrentFuzzy(SqliteConnection conn, TrigramIndex.Reader tgm, Query q, out string route)
        {
            int d = q.MaxDistance, len = q.Text.Length;
            int n = len >= 3 * (d + 1) ? 3 : len >= 2 * (d + 1) ? 2 : 0;

            using var cmd = conn.CreateCommand();
            if (n > 0)
            {
                var grams = FuzzyExpander.BuildNgrams(q.Text, n);
                var like = new StringBuilder();
                for (int i = 0; i < grams.Count; i++)
                {
                    if (i > 0) like.Append(" OR ");
                    like.Append("term LIKE @t").Append(i).Append(" ESCAPE '\\'");
                    cmd.Parameters.AddWithValue("@t" + i, "%" + grams[i] + "%");
                }

                HashSet<int> union = null;
                if (n == 3 && tgm != null)
                {
                    union = new HashSet<int>();
                    foreach (var g in grams)
                    {
                        foreach (int id in tgm.Lookup(g)) union.Add(id);
                        if (union.Count > TrigramCandidateCap) { union = null; break; }
                    }
                }

                if (union != null)
                {
                    route = "sidecar";
                    if (union.Count == 0) return Array.Empty<int>();
                    cmd.CommandText = FmIndexBench.Columns + "rowid IN (" + string.Join(",", union) + ") AND (" + like + ")";
                }
                else
                {
                    route = n == 2 ? "bigramScan" : "trigramScan";
                    cmd.CommandText = FmIndexBench.Columns + like;
                }
            }
            else
            {
                route = "lengthWindowScan";
                cmd.CommandText = FmIndexBench.Columns + "length(term) BETWEEN @lo AND @hi";
                cmd.Parameters.AddWithValue("@lo", Math.Max(1, len - d));
                cmd.Parameters.AddWithValue("@hi", len + d);
            }

            var hits = new List<int>();
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                {
                    string term = r.GetString(1);
                    _ = r.GetInt64(2); _ = r.GetInt32(3); _ = r.GetInt64(4); _ = r.GetInt32(5); _ = r.GetInt32(6);
                    if (Levenshtein.Distance(q.Text, term, d) <= d) hits.Add((int)r.GetInt64(0));
                }
            hits.Sort();
            return hits.ToArray();
        }
    }
}
