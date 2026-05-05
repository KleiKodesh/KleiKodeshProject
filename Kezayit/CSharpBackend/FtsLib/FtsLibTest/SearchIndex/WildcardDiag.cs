using FtsLib.Core;
using FtsLib.Seforim;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Diagnostic for wildcard query problems.
    ///
    /// Breaks a query down token-by-token and reports:
    ///   - How QueryParser normalises each token
    ///   - How many terms each wildcard expands to
    ///   - The LIKE pattern used for each wildcard
    ///   - How long expansion takes per token
    ///   - The final result count and search time
    ///
    /// Usage:
    ///   FtsLibTest.exe wdiag [tier] query terms...
    ///
    /// Example:
    ///   FtsLibTest.exe wdiag 500k וידבר מש* כן *ל בני ישראל
    /// </summary>
    internal static class WildcardDiag
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length < 3)
            {
                Console.WriteLine("Usage: FtsLibTest.exe wdiag [tier] query terms...");
                Console.WriteLine("  e.g. FtsLibTest.exe wdiag 500k וידבר מש* כן *ל בני ישראל");
                return;
            }

            string tierLabel = args[1];
            string query     = string.Join(" ", args, 2, args.Length - 2);

            string label;
            try   { label = TestHelpers.ResolveTier(tierLabel).Label; }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string indexDir = TestHelpers.IndexDir(label);

            if (!Directory.Exists(indexDir) ||
                Directory.GetFiles(indexDir, "seg_*.dat").Length == 0)
            {
                Console.WriteLine($"No index found at: {indexDir}");
                Console.WriteLine($"Run 'build {label}' first.");
                return;
            }

            string dbPath = BuildTest.ResolveDbPath();

            Console.WriteLine();
            Console.WriteLine($"╔══ WILDCARD DIAGNOSTIC ══════════════════════════════════════");
            Console.WriteLine($"║  Query : \"{query}\"");
            Console.WriteLine($"║  Tier  : {label.ToUpper()}");
            Console.WriteLine($"║  Index : {indexDir}");
            Console.WriteLine($"╠══ TOKEN ANALYSIS ═══════════════════════════════════════════");

            // ── Step 1: parse and show normalised tokens ──────────────

            var parsed = QueryParser.Parse(query);

            if (parsed.IsEmpty)
            {
                Console.WriteLine("║  QueryParser returned no tokens — query is empty after normalisation.");
                Console.WriteLine("╚══ DONE ══");
                return;
            }

            Console.WriteLine($"║  {parsed.Groups.Count} token(s) after parsing:");
            Console.WriteLine($"║");

            // Open segments once for all expansions
            var datFiles = Directory.GetFiles(indexDir, "seg_*.dat");
            Array.Sort(datFiles);
            var segments = new List<SegmentHandle>();
            foreach (var dat in datFiles)
            {
                string db = Path.ChangeExtension(dat, ".db");
                if (File.Exists(db)) segments.Add(new SegmentHandle(dat, db));
            }

            try
            {
                var expandedGroups = new List<(string label2, List<string> terms)>();

                foreach (var group in parsed.Groups)
                {
                    string kind = group.IsWildcard ? "WILDCARD"
                                : group.IsFuzzy   ? $"FUZZY~{group.FuzzyDistance}"
                                :                   "LITERAL";

                    Console.WriteLine($"║  Pattern : \"{group.Pattern}\"  [{kind}]");

                    if (group.IsWildcard)
                    {
                        bool hasOptional = group.Pattern.IndexOf('?') >= 0;
                        bool hasStar     = group.Pattern.IndexOf('*') >= 0;

                        if (hasOptional)
                        {
                            Console.WriteLine($"║    Shape        : OPTIONAL-CHAR (contains '?')");
                        }
                        else
                        {
                            string likePattern = WildcardExpander.ToLikePattern(group.Pattern);
                            Console.WriteLine($"║    LIKE pattern : \"{likePattern}\"");

                            // Classify the wildcard shape
                            bool prefixOnly  = group.Pattern.EndsWith("*") && !group.Pattern.StartsWith("*");
                            bool suffixOnly  = group.Pattern.StartsWith("*") && !group.Pattern.EndsWith("*");
                            bool infixOrBoth = !prefixOnly && !suffixOnly;

                            string shape = prefixOnly  ? "PREFIX  (term*)"
                                         : suffixOnly  ? "SUFFIX  (*term)  ⚠ may expand to many terms"
                                         : infixOrBoth ? "INFIX   (*term*) ⚠ may expand to many terms"
                                         :               "UNKNOWN";
                            Console.WriteLine($"║    Shape        : {shape}");
                        }

                        var sw = Stopwatch.StartNew();
                        var expanded = WildcardExpander.Expand(group.Pattern, segments);
                        sw.Stop();

                        if (hasOptional)
                        {
                            // For '?' patterns the "raw DB count" concept doesn't apply cleanly
                            // (each sub-pattern has its own LIKE query). Just show the final count.
                            Console.WriteLine($"║    Expanded to  : {expanded.Count:N0} term(s)  ({sw.ElapsedMilliseconds} ms)");
                        }
                        else
                        {
                            // Also show raw DB count vs. post-filter count for transparency
                            // (re-run the LIKE without the length filter to get the raw number)
                            string likeRaw = WildcardExpander.ToLikePattern(group.Pattern);
                            var rawSet = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                            foreach (var seg in segments)
                            {
                                using (var cmd2 = seg.Conn.CreateCommand())
                                {
                                    cmd2.CommandText = "SELECT term FROM term_index WHERE term LIKE @p ESCAPE '\\'";
                                    cmd2.Parameters.Add("@p", System.Data.DbType.String).Value = likeRaw;
                                    using (var r2 = cmd2.ExecuteReader())
                                        while (r2.Read()) rawSet.Add(r2.GetString(0));
                                }
                            }
                            int rawCount = rawSet.Count;
                            int filtered = rawCount - expanded.Count;

                            Console.WriteLine($"║    DB returned  : {rawCount:N0} term(s)  ({sw.ElapsedMilliseconds} ms)");
                            if (filtered > 0)
                                Console.WriteLine($"║    After filter : {expanded.Count:N0} term(s)  ({filtered:N0} discarded — wildcard portion > {WildcardExpander.MaxWildcardChars} chars)");
                            else
                                Console.WriteLine($"║    After filter : {expanded.Count:N0} term(s)  (none discarded)");
                        }

                        if (expanded.Count == 0)
                        {
                            string fallback = WildcardExpander.StripWildcard(group.Pattern);
                            Console.WriteLine($"║    Fallback     : \"{fallback}\" (literal, no wildcard matches)");
                            expanded = new List<string> { fallback };
                        }
                        else if (expanded.Count <= 30)
                        {
                            // Show all terms when the list is small
                            expanded.Sort(StringComparer.Ordinal);
                            Console.WriteLine($"║    Terms        : {string.Join(", ", expanded)}");
                        }
                        else
                        {
                            // Show a sample when the list is large
                            expanded.Sort(StringComparer.Ordinal);
                            var sample = expanded.GetRange(0, 20);
                            Console.WriteLine($"║    First 20     : {string.Join(", ", sample)}");
                            Console.WriteLine($"║    … and {expanded.Count - 20:N0} more");
                        }

                        expandedGroups.Add((group.Pattern, expanded));
                    }
                    else if (group.IsFuzzy)
                    {
                        var sw = Stopwatch.StartNew();
                        var expanded = FuzzyExpander.Expand(group.Pattern, group.FuzzyDistance, segments);
                        sw.Stop();

                        Console.WriteLine($"║    Expanded to  : {expanded.Count:N0} term(s)  ({sw.ElapsedMilliseconds} ms)");
                        if (expanded.Count <= 30)
                        {
                            expanded.Sort(StringComparer.Ordinal);
                            Console.WriteLine($"║    Terms        : {string.Join(", ", expanded)}");
                        }
                        expandedGroups.Add((group.Pattern, expanded));
                    }
                    else
                    {
                        // Literal — check if it exists in the index
                        using (var reader = new IndexReader(indexDir))
                        {
                            int count = reader.GetTermCount(group.Pattern);
                            Console.WriteLine($"║    In index     : {(count > 0 ? $"YES — {count:N0} doc(s)" : "NO — term not found in index")}");
                        }
                        expandedGroups.Add((group.Pattern, new List<string> { group.Pattern }));
                    }

                    Console.WriteLine($"║");
                }

                // ── Step 2: run the full query and time it ────────────

                Console.WriteLine($"╠══ FULL QUERY SEARCH ════════════════════════════════════════");

                var index = new SeforimIndex(indexDir, dbPath);
                var swFull = Stopwatch.StartNew();
                var results = new List<SearchResult>();
                foreach (var r in index.Search(query)) results.Add(r);
                swFull.Stop();

                Console.WriteLine($"║  Result count : {results.Count:N0}");
                Console.WriteLine($"║  Search time  : {swFull.ElapsedMilliseconds} ms");

                if (results.Count == 0)
                {
                    Console.WriteLine($"║");
                    Console.WriteLine($"║  ⚠ ZERO RESULTS — possible causes:");
                    Console.WriteLine($"║    1. A wildcard expanded to terms that don't co-occur with the other tokens");
                    Console.WriteLine($"║    2. A literal term is not in the index");
                    Console.WriteLine($"║    3. A fuzzy term found no candidates within the edit distance");
                }
                else if (results.Count <= 5)
                {
                    Console.WriteLine($"║");
                    Console.WriteLine($"║  First {results.Count} result(s):");
                    foreach (var r in results)
                        Console.WriteLine($"║    [{r.LineId}] {TestHelpers.Truncate(r.BookTitle, 30)} — {TestHelpers.Truncate(TestHelpers.StripHtmlAndDiacritics(r.Content), 80)}");
                }
                else
                {
                    Console.WriteLine($"║");
                    Console.WriteLine($"║  First 5 result(s):");
                    for (int i = 0; i < 5 && i < results.Count; i++)
                    {
                        var r = results[i];
                        Console.WriteLine($"║    [{r.LineId}] {TestHelpers.Truncate(r.BookTitle, 30)} — {TestHelpers.Truncate(TestHelpers.StripHtmlAndDiacritics(r.Content), 80)}");
                    }
                }

                // ── Step 3: per-group result counts ───────────────────

                Console.WriteLine($"║");
                Console.WriteLine($"╠══ PER-TOKEN RESULT COUNTS ══════════════════════════════════");
                Console.WriteLine($"║  (how many docs each expanded group matches individually)");
                Console.WriteLine($"║");

                using (var reader = new IndexReader(indexDir))
                {
                    foreach (var (lbl, terms) in expandedGroups)
                    {
                        int count = 0;
                        foreach (var _ in reader.SearchOr(terms)) count++;
                        Console.WriteLine($"║  \"{lbl}\" → {count:N0} doc(s)");
                    }
                }

                Console.WriteLine($"║");
                Console.WriteLine($"╚══ DONE ══");
            }
            finally
            {
                foreach (var s in segments) s.Dispose();
            }
        }
    }
}
