using FtsLib.Core;
using FtsLib.Misc;
using FtsLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FtsLibTest
{
    internal static class IndexTest
    {
        private const string DbPath = "";

        public static readonly string[] Searches = new[]
        {
            "כי ביצחק",
            "שויתי לנגדי תמיד",
            "אבל בן אין לה",
            "וידבר משה כן אל בני"
        };

        public static readonly (string label, int limit)[] Tiers = new[]
        {
            ("500k",  500_000),
            ("1M",    1_000_000),
            ("3M",    3_000_000),
            ("Full",  0),
        };

        // ── Run a single tier ────────────────────────────────────────

        public static void RunTier(string label, int limit, TextWriter log = null)
        {
            string indexDir = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                $"index_test_{label}");

            Log(log, $"── Tier: {label} ({(limit == 0 ? "all" : limit.ToString("N0"))} lines) ──");

            // ── Build ────────────────────────────────────────────────
            var swBuild = Stopwatch.StartNew();
            long linesIndexed = 0;

            try
            {
                using (var db     = new ZayitDb(DbPath))
                using (var writer = new IndexWriter(indexDir))
                {
                    var tokenizer = new Tokenizer();
                    foreach (var row in db.ReadLines(limit))
                    {
                        var tokens = tokenizer.Extract(row.Content);
                        foreach (var token in tokens)
                            writer.Add(row.Id, token);

                        linesIndexed++;
                        if (linesIndexed % 100_000 == 0)
                            Log(log, $"  indexed {linesIndexed:N0} in {swBuild.Elapsed:mm\\:ss}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log(log, $"  BUILD FAILED: {ex}");
                return;
            }

            swBuild.Stop();
            Log(log, $"  Build done: {linesIndexed:N0} lines in {swBuild.Elapsed:mm\\:ss\\.ff}");

            // ── Search ───────────────────────────────────────────────
            try
            {
                using (var reader = new IndexReader(indexDir))
                {
                    foreach (var query in Searches)
                    {
                        var swSearch = Stopwatch.StartNew();
                        int count    = reader.Search(query.Split(' ')).Count();
                        swSearch.Stop();
                        Log(log, $"  Search \"{query}\" → {count:N0} results  ({swSearch.ElapsedMilliseconds} ms)");
                    }
                }
            }
            catch (Exception ex)
            {
                Log(log, $"  SEARCH FAILED: {ex.Message}");
                return;
            }

            // ── Validate ─────────────────────────────────────────────
            Validate(indexDir, log: log);

            Log(log, $"  Index at: {indexDir}");
        }

        // ── Validate ─────────────────────────────────────────────────

        public static void Validate(string indexDir, int sampleSize = 20, TextWriter log = null)
        {
            Log(log, $"  [Validate] index: {indexDir}");

            if (!Directory.Exists(indexDir))
            {
                Log(log, "  [Validate] Index directory not found.");
                return;
            }

            try
            {
                using (var reader = new IndexReader(indexDir))
                using (var db     = new ZayitDb(DbPath))
                {
                    foreach (var query in Searches)
                    {
                        string[] terms   = query.Split(' ');
                        var      results = reader.Search(terms).Take(sampleSize).ToList();
                        int ok = 0, bogus = 0, missing = 0;

                        foreach (int lineId in results)
                        {
                            string content = db.GetLineContent(lineId);
                            if (content == null) { missing++; continue; }

                            string stripped = StripDiacritics(content);
                            bool allFound = true;
                            foreach (var term in terms)
                            {
                                if (stripped.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                                {
                                    Log(log, $"  [BOGUS] id={lineId} term=\"{term}\" line={Truncate(content, 80)}");
                                    allFound = false;
                                    bogus++;
                                    break;
                                }
                            }
                            if (allFound) ok++;
                        }

                        Log(log, $"  [Validate] \"{query}\" — {ok} OK, {bogus} bogus, {missing} missing (sample {results.Count})");
                    }
                }
            }
            catch (Exception ex)
            {
                Log(log, $"  [Validate] FAILED: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        public static string StripDiacriticsPublic(string s) => StripDiacritics(s);

        private static string StripDiacritics(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c >= '\u0591' && c <= '\u05C7') continue;
                if (c > 127 && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Truncate(string s, int max) =>
            s.Length <= max ? s : s.Substring(0, max) + "…";

        private static void Log(TextWriter log, string msg)
        {
            Console.WriteLine(msg);
            log?.WriteLine(msg);
            log?.Flush();
        }
    }
}
