using FtsLib.Indexing;
using FtsLib.Search;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Exhaustive equivalence verifier for the F06 prefix range scan.
    ///
    /// For every sampled prefix it runs BOTH queries against EVERY segment:
    ///   old:  SELECT term FROM term_index WHERE term LIKE 'prefix%' ESCAPE '\'
    ///   new:  SELECT term FROM term_index WHERE term &gt;= @lo AND term &lt; @hi
    /// and requires the two term sets to be exactly equal. Any difference —
    /// one missing term anywhere — fails the run.
    ///
    /// Prefixes are derived from REAL index terms (every K-th row of each
    /// segment, prefix lengths 2..5), plus hand-picked edge cases (boundary
    /// characters, digits, final letters, terms containing quote characters).
    /// Prefixes that TryGetPrefixRange declares ineligible are reported as
    /// "fallback" (they keep the LIKE path in production, so equivalence is
    /// trivially preserved) and counted separately.
    ///
    /// Usage:
    ///   FtsLibTest.exe verifyprefix [tier] [samplesPerSegment]
    /// </summary>
    internal static class VerifyPrefixScan
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string label = args.Length > 1 ? args[1] : "500k";
            int samples  = args.Length > 2 && int.TryParse(args[2], out int s) ? s : 150;
            try { label = TestHelpers.ResolveTier(label).Label; }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string indexDir = TestHelpers.IndexDir(label);
            if (!Directory.Exists(indexDir))
            { Console.WriteLine($"No index at: {indexDir}"); return; }

            Console.WriteLine();
            Console.WriteLine($"╔══ PREFIX RANGE-SCAN VERIFIER — {label.ToUpper()} ══");
            Console.WriteLine($"║  Index  : {indexDir}");

            // Open segments directly (read-only, same handles production uses).
            var handles = new List<SegmentHandle>();
            foreach (var dat in Directory.GetFiles(indexDir, "seg_*.dat"))
            {
                string db = Path.ChangeExtension(dat, ".db");
                if (File.Exists(db)) handles.Add(new SegmentHandle(dat, db));
            }
            Console.WriteLine($"║  Segments: {handles.Count}");

            try
            {
                // ── Build the prefix corpus from real terms ───────────────────
                var prefixes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var seg in handles)
                {
                    using (var cmd = seg.Conn.CreateCommand())
                    {
                        // Even sampling across the whole term space.
                        cmd.CommandText =
                            "SELECT term FROM term_index WHERE rowid % (SELECT MAX(1, COUNT(*)/" +
                            samples + ") FROM term_index) = 0 LIMIT " + samples;
                        using (var r = cmd.ExecuteReader())
                            while (r.Read())
                            {
                                string t = r.GetString(0);
                                for (int len = 2; len <= 5 && len <= t.Length; len++)
                                    prefixes.Add(t.Substring(0, len));
                            }

                        // Edge cases: lexicographically first and last terms.
                        cmd.CommandText = "SELECT MIN(term), MAX(term) FROM term_index";
                        using (var r = cmd.ExecuteReader())
                            if (r.Read())
                            {
                                for (int i = 0; i < 2; i++)
                                {
                                    if (r.IsDBNull(i)) continue;
                                    string t = r.GetString(i);
                                    for (int len = 2; len <= 5 && len <= t.Length; len++)
                                        prefixes.Add(t.Substring(0, len));
                                }
                            }

                        // Terms containing characters adjacent to interesting
                        // boundaries: digits, quote chars, ת (last Hebrew letter).
                        cmd.CommandText =
                            "SELECT term FROM term_index WHERE term LIKE 'ת%' LIMIT 25";
                        using (var r = cmd.ExecuteReader())
                            while (r.Read())
                            {
                                string t = r.GetString(0);
                                for (int len = 2; len <= 4 && len <= t.Length; len++)
                                    prefixes.Add(t.Substring(0, len));
                            }
                    }
                }

                // Hand-picked synthetic edges (may match nothing — empty == empty
                // must also hold): highest Hebrew letter runs, digits, mixed.
                prefixes.Add("תת");
                prefixes.Add("תתת");
                prefixes.Add("99");
                prefixes.Add("10");
                prefixes.Add("אא");
                prefixes.Add("תתתת"); // תתתת

                Console.WriteLine($"║  Prefixes to verify: {prefixes.Count}");

                // ── Verify each prefix on every segment ───────────────────────
                int verified = 0, fallback = 0, mismatches = 0;
                long likeMsTotal = 0, rangeMsTotal = 0;
                var failures = new List<string>();
                var sw = new Stopwatch();

                foreach (var prefix in prefixes)
                {
                    if (!HebrewWildcardExpander.TryGetPrefixRange(prefix + "*", out string lo, out string hi))
                    {
                        fallback++;
                        continue;
                    }

                    foreach (var seg in handles)
                    {
                        var likeSet  = new HashSet<string>(StringComparer.Ordinal);
                        var rangeSet = new HashSet<string>(StringComparer.Ordinal);

                        sw.Restart();
                        using (var cmd = seg.Conn.CreateCommand())
                        {
                            cmd.CommandText =
                                "SELECT term FROM term_index WHERE term LIKE @p ESCAPE '\\'";
                            cmd.Parameters.Add("@p", Microsoft.Data.Sqlite.SqliteType.Text).Value =
                                EscapeLike(prefix) + "%";
                            using (var r = cmd.ExecuteReader())
                                while (r.Read()) likeSet.Add(r.GetString(0));
                        }
                        sw.Stop(); likeMsTotal += sw.ElapsedMilliseconds;

                        sw.Restart();
                        using (var cmd = seg.Conn.CreateCommand())
                        {
                            cmd.CommandText =
                                "SELECT term FROM term_index WHERE term >= @lo AND term < @hi";
                            cmd.Parameters.Add("@lo", Microsoft.Data.Sqlite.SqliteType.Text).Value = lo;
                            cmd.Parameters.Add("@hi", Microsoft.Data.Sqlite.SqliteType.Text).Value = hi;
                            using (var r = cmd.ExecuteReader())
                                while (r.Read()) rangeSet.Add(r.GetString(0));
                        }
                        sw.Stop(); rangeMsTotal += sw.ElapsedMilliseconds;

                        if (!likeSet.SetEquals(rangeSet))
                        {
                            mismatches++;
                            if (failures.Count < 10)
                            {
                                int onlyLike  = 0, onlyRange = 0;
                                string sample = "";
                                foreach (var t in likeSet)
                                    if (!rangeSet.Contains(t)) { onlyLike++; if (sample == "") sample = t; }
                                foreach (var t in rangeSet)
                                    if (!likeSet.Contains(t)) onlyRange++;
                                failures.Add(
                                    $"\"{prefix}*\" seg={Path.GetFileNameWithoutExtension(seg.DatPath)}: " +
                                    $"LIKE={likeSet.Count} RANGE={rangeSet.Count} " +
                                    $"onlyLike={onlyLike} onlyRange={onlyRange} e.g. \"{sample}\"");
                            }
                        }
                    }
                    verified++;
                }

                Console.WriteLine($"║  Verified : {verified} prefixes × {handles.Count} segment(s)");
                Console.WriteLine($"║  Fallback : {fallback} (ineligible — keep LIKE path, trivially identical)");
                Console.WriteLine($"║  LIKE total  : {likeMsTotal:N0}ms   RANGE total: {rangeMsTotal:N0}ms" +
                    (rangeMsTotal > 0 ? $"   ({(double)likeMsTotal / Math.Max(1, rangeMsTotal):F0}x)" : ""));
                Console.WriteLine($"║  Mismatches: {mismatches}");
                foreach (var f in failures) Console.WriteLine($"║    ✗ {f}");
                Console.WriteLine($"║  {(mismatches == 0 ? "✓  PASS — range scan is exactly equivalent to LIKE on every sampled prefix" : "✗  FAIL — RANGE SCAN DROPS OR ADDS RESULTS")}");
                Console.WriteLine("╚════════════════════════════════════════════════════════════════════");
            }
            finally
            {
                foreach (var h in handles) h.Dispose();
            }
        }

        private static string EscapeLike(string s)
            => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
