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
    /// <c>fmbench [indexDir] [patternsPerBucket=100]</c>
    ///
    /// FM-index sidecar vs the production trigram sidecar, per segment of a built index.
    /// For infix (*x*) and suffix (*x) patterns of length 2..8 sampled from the segment's own
    /// terms (deterministic seed), times:
    ///   likeScan      — full SQLite LIKE scan (production fallback)
    ///   trigramRouted — production routing: trigram candidates + SQLite rowid-IN LIKE confirm,
    ///                   or likeScan when the run is &lt; 3 chars or candidates &gt; 8000
    ///   fmIds         — FM-index ON DISK (seek+read, no mmap): backward search + one read
    ///                   of the packed row→rowid range; exact rowids
    ///   fmFetch       — fmIds + fetching the production columns by rowid IN (...)
    /// Every method's rowid set is checked against a brute-force ordinal scan. Output is
    /// numbers only (patterns are never printed); per-pattern rows go to a CSV.
    /// Writes its sidecars into &lt;indexDir&gt;_fmbench\, leaving the index untouched.
    /// </summary>
    internal static class FmIndexBench
    {
        const int TrigramCandidateCap = 8000; // same as HebrewWildcardExpander
        const string Columns = "SELECT rowid, term, skip_offset, skip_count, offset, length, count FROM term_index WHERE ";

        public static void Run(string[] args)
        {
            string indexDir = args.Length > 1 ? args[1] : TestHelpers.IndexDir("full");
            int perBucket = args.Length > 2 ? int.Parse(args[2]) : 100;
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

            // Terms.
            var terms = new List<string>(); var rowIds = new List<int>();
            using (var conn = Open(dbPath))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT rowid, term FROM term_index ORDER BY rowid";
                using var r = cmd.ExecuteReader();
                while (r.Read()) { rowIds.Add((int)r.GetInt64(0)); terms.Add(r.GetString(1)); }
            }
            Console.WriteLine($"terms {terms.Count:N0}  chars {terms.Sum(t => (long)t.Length):N0}");

            // Build both sidecars into outDir.
            string fmPath = Path.Combine(outDir, name + ".fmi");
            string tgmPath = Path.Combine(outDir, name + ".tgm");
            var sw = Stopwatch.StartNew();
            FmIndex.Build(fmPath, terms, rowIds);
            double fmBuildMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            TrigramIndex.Build(tgmPath, terms, rowIds);
            double tgmBuildMs = sw.Elapsed.TotalMilliseconds;
            long dbBytes = new FileInfo(dbPath).Length;
            Console.WriteLine($"build ms     fm {fmBuildMs,8:F0}   tgm {tgmBuildMs,8:F0}   (terms already in memory)");
            Console.WriteLine($"size  MB     fm {Mb(new FileInfo(fmPath).Length),8:F2} (incl. row->rowid section)   tgm {Mb(new FileInfo(tgmPath).Length),8:F2}   term_index db {Mb(dbBytes):F2}");

            sw.Restart(); using var fm = new FmIndex.Reader(fmPath); double fmOpenMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart(); using var tgm = new TrigramIndex.Reader(tgmPath); double tgmOpenMs = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"fm parts MB  wavelet tree {Mb(fm.FileBytes - fm.RowIdSectionBytes):F2}   row->rowid {Mb(fm.RowIdSectionBytes):F2}");
            Console.WriteLine($"open  ms     fm {fmOpenMs:F2}   tgm {tgmOpenMs:F2}");

            var patterns = SamplePatterns(terms, perBucket);

            using var segConn = Open(dbPath);
            string[] methods = { "likeScan", "trigramRouted", "fmIds", "fmFetch" };
            var results = new List<(Pattern p, int matches, double[] ms, string route)>();
            int mismatches = 0;

            for (int pass = 0; pass < 2; pass++) // pass 0 warms OS cache + JIT; pass 1 is measured
            {
                results.Clear(); mismatches = 0;
                foreach (var p in patterns)
                {
                    int[] truth = BruteForce(terms, rowIds, p);
                    var ms = new double[methods.Length];

                    sw.Restart(); int[] like = LikeScan(segConn, p); ms[0] = sw.Elapsed.TotalMilliseconds;

                    sw.Restart(); int[] trg = TrigramRouted(segConn, tgm, p, out string route); ms[1] = sw.Elapsed.TotalMilliseconds;

                    sw.Restart(); int[] fmIds = fm.FindRowIds(p.Text, p.IsSuffix); ms[2] = sw.Elapsed.TotalMilliseconds;
                    int[] fetched = FetchByRowIds(segConn, fmIds); ms[3] = sw.Elapsed.TotalMilliseconds;

                    if (!Same(truth, like) || !Same(truth, trg) || !Same(truth, fmIds) || !Same(truth, fetched))
                        mismatches++;
                    results.Add((p, truth.Length, ms, route));
                }
            }

            Console.WriteLine($"patterns {patterns.Count}   result mismatches vs brute force: {mismatches}");
            Console.WriteLine();
            Console.WriteLine("median / p95 / max ms per bucket (warm)");
            Console.Write($"{"bucket",-10}{"n",4}{"avgHits",9}{"maxHits",9}");
            foreach (string m in methods) Console.Write($"{m,26}");
            Console.WriteLine();
            foreach (var bucket in results.GroupBy(x => (x.p.IsSuffix, x.p.Text.Length)).OrderBy(g => g.Key.IsSuffix).ThenBy(g => g.Key.Length))
            {
                var list = bucket.ToList();
                string label = (bucket.Key.IsSuffix ? "suffix" : "infix") + bucket.Key.Length;
                Console.Write($"{label,-10}{list.Count,4}{list.Average(x => x.matches),9:F0}{list.Max(x => x.matches),9}");
                for (int m = 0; m < methods.Length; m++)
                {
                    var t = list.Select(x => x.ms[m]).OrderBy(x => x).ToArray();
                    Console.Write($"{Fmt(Pct(t, 0.5)),8}/{Fmt(Pct(t, 0.95)),8}/{Fmt(t[^1]),8}");
                }
                Console.WriteLine();
            }
            var routes = results.GroupBy(x => x.route).Select(g => $"{g.Key}={g.Count()}");
            Console.WriteLine("trigram routes: " + string.Join("  ", routes));

            string csv = Path.Combine(outDir, name + "_fmbench.csv");
            var sb = new StringBuilder("patternIndex,kind,length,hits,route," + string.Join(",", methods) + "\n");
            for (int i = 0; i < results.Count; i++)
            {
                var x = results[i];
                sb.Append(i).Append(',').Append(x.p.IsSuffix ? "suffix" : "infix").Append(',').Append(x.p.Text.Length)
                  .Append(',').Append(x.matches).Append(',').Append(x.route);
                foreach (double v in x.ms) sb.Append(',').Append(v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append('\n');
            }
            File.WriteAllText(csv, sb.ToString());
            Console.WriteLine("per-pattern csv: " + csv);

        }

        // ── Patterns ──────────────────────────────────────────────────

        sealed class Pattern { public string Text; public bool IsSuffix; }

        /// <summary>Substrings of random terms. Skips ASCII letters (SQLite LIKE folds their case,
        /// ordinal truth does not) and LIKE metacharacters.</summary>
        static List<Pattern> SamplePatterns(List<string> terms, int perBucket)
        {
            var rng = new Random(12345);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<Pattern>();
            foreach (bool suffix in new[] { false, true })
                foreach (int len in new[] { 2, 3, 4, 5, 6, 8 })
                {
                    int got = 0, tries = 0;
                    while (got < perBucket && tries++ < perBucket * 1000)
                    {
                        string t = terms[rng.Next(terms.Count)];
                        if (t.Length < len + (suffix ? 0 : 1)) continue;
                        int start = suffix ? t.Length - len : rng.Next(t.Length - len + 1);
                        string s = t.Substring(start, len);
                        if (s.Any(ch => ch < 128 && (char.IsLetter(ch) || ch == '%' || ch == '_' || ch == '\\'))) continue;
                        if (!seen.Add((suffix ? "s:" : "i:") + s)) continue;
                        list.Add(new Pattern { Text = s, IsSuffix = suffix }); got++;
                    }
                }
            return list;
        }

        // ── Methods ───────────────────────────────────────────────────

        static int[] BruteForce(List<string> terms, List<int> rowIds, Pattern p)
        {
            var hits = new List<int>();
            for (int i = 0; i < terms.Count; i++)
                if (p.IsSuffix ? terms[i].EndsWith(p.Text, StringComparison.Ordinal)
                               : terms[i].Contains(p.Text, StringComparison.Ordinal))
                    hits.Add(rowIds[i]);
            hits.Sort();
            return hits.ToArray();
        }

        static string LikePattern(Pattern p) => "%" + p.Text + (p.IsSuffix ? "" : "%");

        static int[] LikeScan(SqliteConnection conn, Pattern p)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = Columns + "term LIKE @p ESCAPE '\\'";
            cmd.Parameters.AddWithValue("@p", LikePattern(p));
            return ReadRowIds(cmd);
        }

        /// <summary>Mirrors HebrewWildcardExpander.TrySetupTrigramConfirm + its fallback.</summary>
        static int[] TrigramRouted(SqliteConnection conn, TrigramIndex.Reader tgm, Pattern p, out string route)
        {
            if (p.Text.Length < TrigramIndex.MinRun) { route = "short"; return LikeScan(conn, p); }
            var grams = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            TrigramIndex.AddTrigrams(p.Text, grams, seen);
            int[] acc = null;
            foreach (string g in grams)
            {
                int[] l = tgm.Lookup(g);
                if (l.Length == 0) { acc = Array.Empty<int>(); break; }
                acc = acc == null ? l : Intersect(acc, l);
                if (acc.Length == 0) break;
            }
            if (acc.Length > TrigramCandidateCap) { route = "overCap"; return LikeScan(conn, p); }
            route = "sidecar";
            if (acc.Length == 0) return acc;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = Columns + "rowid IN (" + string.Join(",", acc) + ") AND term LIKE @p ESCAPE '\\'";
            cmd.Parameters.AddWithValue("@p", LikePattern(p));
            return ReadRowIds(cmd);
        }

        static int[] FetchByRowIds(SqliteConnection conn, int[] rowIds)
        {
            if (rowIds.Length == 0) return rowIds;
            using var cmd = conn.CreateCommand();
            cmd.CommandText = Columns + "rowid IN (" + string.Join(",", rowIds) + ")";
            return ReadRowIds(cmd);
        }

        /// <summary>Reads every production column (like the expander's consume loop) and
        /// returns the sorted rowids.</summary>
        static int[] ReadRowIds(SqliteCommand cmd)
        {
            var ids = new List<int>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                ids.Add((int)r.GetInt64(0));
                _ = r.GetString(1); _ = r.GetInt64(2); _ = r.GetInt32(3); _ = r.GetInt64(4); _ = r.GetInt32(5); _ = r.GetInt32(6);
            }
            ids.Sort();
            return ids.ToArray();
        }

        // ── Helpers ───────────────────────────────────────────────────

        static SqliteConnection Open(string dbPath)
        {
            var csb = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            var c = new SqliteConnection(csb.ConnectionString); c.Open(); return c;
        }

        static int[] Intersect(int[] a, int[] b)
        {
            var r = new List<int>(Math.Min(a.Length, b.Length)); int i = 0, j = 0;
            while (i < a.Length && j < b.Length) { int x = a[i], y = b[j]; if (x == y) { r.Add(x); i++; j++; } else if (x < y) i++; else j++; }
            return r.ToArray();
        }

        static bool Same(int[] a, int[] b) => a.AsSpan().SequenceEqual(b);
        static double Mb(long bytes) => bytes / 1048576.0;
        static double Pct(double[] sorted, double q) => sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
        static string Fmt(double ms) => ms < 10 ? ms.ToString("F2") : ms.ToString("F0");
    }
}
