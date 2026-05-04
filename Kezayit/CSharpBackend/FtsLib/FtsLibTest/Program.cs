using FtsLib.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Usage:
    ///   FtsLibTest.exe                          — run all tiers
    ///   FtsLibTest.exe 500k|1m|3m|full         — run one tier
    ///   FtsLibTest.exe validate [dir]           — validate existing index
    ///   FtsLibTest.exe search &lt;dir&gt; &lt;terms...&gt;  — AND search
    ///   FtsLibTest.exe searchall [dir]          — full AND/OR/mixed suite, both multi-seg and merged
    /// </summary>
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string resultsFile = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "test_results.txt");

            if (args.Length == 0)
            {
                using (var log = new StreamWriter(resultsFile, append: false, Encoding.UTF8))
                {
                    log.WriteLine($"=== FtsLib Test Run — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    log.WriteLine();
                    foreach (var tier in IndexTest.Tiers)
                    {
                        IndexTest.RunTier(tier.label, tier.limit, log);
                        log.WriteLine();
                        Console.WriteLine();
                    }
                    log.WriteLine("=== All tiers complete ===");
                }
                Console.WriteLine($"\nFull results written to: {resultsFile}");
                return;
            }

            string cmd = args[0].ToLowerInvariant();

            if (cmd == "validate")
            {
                string dir = args.Length > 1 ? args[1]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index_test_500k");
                IndexTest.Validate(dir);
                return;
            }

            if (cmd == "build")
            {
                if (args.Length < 2) { Console.WriteLine("Usage: build <dir> [limit]"); return; }
                string buildDir   = args[1];
                int    buildLimit = args.Length > 2 ? int.Parse(args[2]) : 0;
                Console.WriteLine($"Building → {buildDir}  limit={(buildLimit == 0 ? "all" : buildLimit.ToString("N0"))}");
                var sw = Stopwatch.StartNew();
                long n = 0;
                using (var db     = new FtsLib.Misc.ZayitDb(""))
                using (var writer = new IndexWriter(buildDir))
                {
                    var tok = new FtsLib.Tokenizer();
                    foreach (var row in db.ReadLines(buildLimit))
                    {
                        foreach (var token in tok.Extract(row.Content))
                            writer.Add(row.Id, token);
                        if (++n % 100_000 == 0)
                            Console.WriteLine($"  {n:N0} lines  {sw.Elapsed:mm\\:ss}");
                    }
                }
                Console.WriteLine($"Done: {n:N0} lines in {sw.Elapsed:mm\\:ss\\.ff}");
                return;
            }

            if (cmd == "search")
            {
                if (args.Length < 3) { Console.WriteLine("Usage: search <dir> <term1> [term2...]"); return; }
                string dir   = args[1];
                var    terms = args.Skip(2).ToArray();
                Console.WriteLine($"Index : {dir}");
                using (var reader = new IndexReader(dir))
                {
                    foreach (var t in terms)
                        Console.WriteLine($"  '{t}' → {reader.GetTermCount(t):N0} postings");
                    var sw    = Stopwatch.StartNew();
                    int count = reader.Search(terms).Count();
                    sw.Stop();
                    Console.WriteLine($"Results: {count:N0}  ({sw.ElapsedMilliseconds} ms)");
                }
                return;
            }

            if (cmd == "searchall")
            {
                string dir = args.Length > 1 ? args[1]
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index_test_500k");
                SearchAllTests(dir);
                return;
            }

            foreach (var tier in IndexTest.Tiers)
            {
                if (tier.label.ToLowerInvariant() == cmd)
                {
                    using (var log = new StreamWriter(resultsFile, append: true, Encoding.UTF8))
                    {
                        log.WriteLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                        IndexTest.RunTier(tier.label, tier.limit, log);
                        log.WriteLine();
                    }
                    Console.WriteLine($"\nResults appended to: {resultsFile}");
                    return;
                }
            }

            Console.WriteLine($"Unknown command '{args[0]}'.");
        }

        // ── Search test suite ─────────────────────────────────────────

        private static void SearchAllTests(string indexDir)
        {
            // Pass 1: multi-segment index as produced by the writer
            Console.WriteLine($"=== Pass 1 — multi-segment: {indexDir} ===");
            Console.WriteLine();
            RunAllQueries(indexDir);

            // Pass 2: force-merge into one segment, re-run identical queries
            string mergedDir = indexDir + "_merged";
            if (!Directory.Exists(mergedDir))
            {
                Console.WriteLine();
                Console.WriteLine($"Building force-merged copy → {mergedDir}");
                CopyDirectory(indexDir, mergedDir);
                using (var writer = new IndexWriter(mergedDir))
                    writer.Optimize();
                Console.WriteLine("Force-merge complete.");
            }
            Console.WriteLine();
            Console.WriteLine($"=== Pass 2 — force-merged: {mergedDir} ===");
            Console.WriteLine();
            RunAllQueries(mergedDir);
        }

        private static void RunAllQueries(string indexDir)
        {
            using (var reader = new IndexReader(indexDir))
            using (var db     = new FtsLib.Misc.ZayitDb(""))
            {
                Console.WriteLine("── AND ──────────────────────────────────────");
                RunAnd(reader, db, "כי", "ביצחק");
                RunAnd(reader, db, "שויתי", "לנגדי", "תמיד");
                RunAnd(reader, db, "תורה", "מצוה");
                RunAnd(reader, db, "אברהם", "יצחק", "יעקב");
                RunAnd(reader, db, "אבל", "בן", "אין", "לה");
                RunAnd(reader, db, "וידבר", "משה", "כן", "אל", "בני");
                RunAnd(reader, db, "nonexistentword123");

                Console.WriteLine("── OR ───────────────────────────────────────");
                RunOr(reader, db, "כי", "יצחק");
                RunOr(reader, db, "תורה", "מצוה", "מצות");
                RunOr(reader, db, "אברהם", "יצחק");

                Console.WriteLine("── Wildcard ─────────────────────────────────");
                // prefix: כי *יצחק  — any term ending in יצחק, AND כי
                RunWildcard(reader, db, "כי *יצחק",       new[] { "כי" },    "*יצחק");
                // suffix: משה* AND תורה  — any term starting with משה, AND תורה
                RunWildcard(reader, db, "משה* תורה",      new[] { "תורה" },  "משה*");
                // infix: *לנגד* AND שויתי  — any term containing לנגד, AND שויתי
                RunWildcard(reader, db, "שויתי *לנגד*",   new[] { "שויתי" }, "*לנגד*");
                // prefix only: בני*  — all terms starting with בני
                RunWildcard(reader, db, "בני*",            new string[0],     "בני*");
                // suffix only: *ישראל  — all terms ending with ישראל
                RunWildcard(reader, db, "*ישראל",          new string[0],     "*ישראל");
                // infix only: *אבר*  — all terms containing אבר
                RunWildcard(reader, db, "*אבר*",           new string[0],     "*אבר*");

                Console.WriteLine("── Mixed ────────────────────────────────────");
                RunMixed(reader, db, "(כי OR יצחק) AND ביצחק",
                    new[] { "כי", "יצחק" }, new[] { "ביצחק" });
                RunMixed(reader, db, "(כי OR אשר) AND (ביצחק OR ביעקב)",
                    new[] { "כי", "אשר" }, new[] { "ביצחק", "ביעקב" });
                RunMixed(reader, db, "(תורה OR מצוה) AND (ישראל OR עם)",
                    new[] { "תורה", "מצוה" }, new[] { "ישראל", "עם" });
            }
        }

        // ── Runners ───────────────────────────────────────────────────

        private static void RunAnd(IndexReader reader, FtsLib.Misc.ZayitDb db, params string[] terms)
        {
            var sw     = Stopwatch.StartNew();
            var ids    = reader.Search(terms).ToList();
            sw.Stop();
            var (ok, bogus, missing) = ValidateAll(db, ids, terms, orMode: false);
            Report("AND", string.Join(", ", terms), ids.Count, sw.ElapsedMilliseconds, ok, bogus, missing);
        }

        private static void RunOr(IndexReader reader, FtsLib.Misc.ZayitDb db, params string[] terms)
        {
            var sw  = Stopwatch.StartNew();
            var ids = reader.SearchOr(terms).ToList();
            sw.Stop();
            var (ok, bogus, missing) = ValidateAll(db, ids, terms, orMode: true);
            Report("OR ", string.Join(", ", terms), ids.Count, sw.ElapsedMilliseconds, ok, bogus, missing);
        }

        private static void RunMixed(IndexReader reader, FtsLib.Misc.ZayitDb db,
            string label, params string[][] groups)
        {
            var sw  = Stopwatch.StartNew();
            var ids = reader.Search(groups.Select(g => (IEnumerable<string>)g)).ToList();
            sw.Stop();

            int ok = 0, bogus = 0, missing = 0;
            foreach (int id in ids)
            {
                string content = db.GetLineContent(id);
                if (content == null) { missing++; continue; }
                string clean = StripHtmlAndDiacritics(content);
                bool valid = groups.All(g =>
                    g.Any(t => clean.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0));
                if (valid) ok++;
                else
                {
                    bogus++;
                    if (bogus <= 3)
                        Console.WriteLine($"    [BOGUS] id={id} content={Truncate(clean, 100)}");
                }
            }
            Report("MIX", label, ids.Count, sw.ElapsedMilliseconds, ok, bogus, missing);
        }

        // ── Wildcard runner ───────────────────────────────────────────

        /// <summary>
        /// Runs a wildcard-aware AND search.
        /// <paramref name="wildcardPattern"/> is expanded to all matching terms in the index;
        /// those terms form one OR group. Each entry in <paramref name="literalTerms"/> is a
        /// separate AND group (must all be present). Validates that every returned line
        /// contains at least one term matching the wildcard's stripped root, plus all literals.
        /// </summary>
        private static void RunWildcard(IndexReader reader, FtsLib.Misc.ZayitDb db,
            string label, string[] literalTerms, string wildcardPattern)
        {
            // Expand wildcard → OR group
            var expanded = reader.ExpandWildcard(wildcardPattern);
            string stripped = WildcardExpander_StripWildcard(wildcardPattern);

            if (expanded.Count == 0)
            {
                Console.WriteLine($"  WLD [{label}]");
                Console.WriteLine($"      0 results  (no terms matched pattern '{wildcardPattern}')");
                return;
            }

            // Build groups: wildcard OR-group first, then one group per literal
            var groups = new List<IEnumerable<string>>();
            groups.Add(expanded);
            foreach (var lit in literalTerms)
                groups.Add(new[] { lit });

            var sw  = Stopwatch.StartNew();
            var ids = reader.Search(groups).ToList();
            sw.Stop();

            // Validate: each result must contain the stripped wildcard root AND all literals
            int ok = 0, bogus = 0, missing = 0;
            foreach (int id in ids)
            {
                string content = db.GetLineContent(id);
                if (content == null) { missing++; continue; }
                string clean = StripHtmlAndDiacritics(content);

                bool wildcardOk = expanded.Any(t =>
                    clean.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                bool literalsOk = literalTerms.All(t =>
                    clean.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);

                if (wildcardOk && literalsOk) ok++;
                else
                {
                    bogus++;
                    if (bogus <= 3)
                        Console.WriteLine($"    [BOGUS] id={id} content={Truncate(clean, 100)}");
                }
            }

            string expandInfo = expanded.Count <= 6
                ? string.Join(", ", expanded)
                : string.Join(", ", expanded.Take(6)) + $"… (+{expanded.Count - 6} more)";
            Console.WriteLine($"  WLD [{label}]  expanded={expanded.Count}: {expandInfo}");
            string status = bogus > 0 ? $"  *** {bogus} BOGUS ***" : "  ✓";
            Console.WriteLine($"      {ids.Count:N0} results  {sw.ElapsedMilliseconds} ms  ok={ok} bogus={bogus} missing={missing}{status}");
        }

        // Inline helper — avoids a dependency on the internal WildcardExpander class
        private static string WildcardExpander_StripWildcard(string pattern)
            => pattern.Replace("*", string.Empty);

        // ── Validation ────────────────────────────────────────────────

        private static (int ok, int bogus, int missing) ValidateAll(
            FtsLib.Misc.ZayitDb db, List<int> ids, string[] terms, bool orMode)
        {
            int ok = 0, bogus = 0, missing = 0;
            foreach (int id in ids)
            {
                string content = db.GetLineContent(id);
                if (content == null) { missing++; continue; }
                string clean = StripHtmlAndDiacritics(content);
                bool valid = orMode
                    ? terms.Any(t => clean.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                    : terms.All(t => clean.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
                if (valid) ok++;
                else
                {
                    bogus++;
                    if (bogus <= 3)
                        Console.WriteLine($"    [BOGUS] id={id} [{string.Join(",", terms)}] {Truncate(clean, 100)}");
                }
            }
            return (ok, bogus, missing);
        }

        private static void Report(string mode, string label, int count,
            long ms, int ok, int bogus, int missing)
        {
            string status = bogus > 0 ? $"  *** {bogus} BOGUS ***" : "  ✓";
            Console.WriteLine($"  {mode} [{label}]");
            Console.WriteLine($"      {count:N0} results  {ms} ms  ok={ok} bogus={bogus} missing={missing}{status}");
        }

        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Strips HTML tags and Hebrew diacritics so the validator sees the
        /// same text the tokenizer sees. Inline tags like &lt;b&gt; are invisible
        /// to the tokenizer but split words in the raw string.
        /// </summary>
        private static string StripHtmlAndDiacritics(string s)
        {
            var  sb    = new StringBuilder(s.Length);
            bool inTag = false;
            foreach (char c in s)
            {
                if (c == '<') { inTag = true;  continue; }
                if (c == '>') { inTag = false; continue; }
                if (inTag) continue;
                if (c >= '\u0591' && c <= '\u05C7') continue; // nikud + cantillation
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max) + "…";

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
            foreach (var d in Directory.GetDirectories(src))
                CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }
}
