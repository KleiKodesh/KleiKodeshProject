using FtsLib.Search;
using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Unit tests for <see cref="QueryParser"/> — covers the OR ('|') syntax
    /// added alongside the existing literal / wildcard / fuzzy paths.
    ///
    /// No index or database required — all assertions are purely in-memory.
    ///
    /// Usage:
    ///   FtsLibTest.exe parsertest
    /// </summary>
    internal static class QueryParserTest
    {
        // ── Entry point ───────────────────────────────────────────────

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine();
            Console.WriteLine("╔══ QUERY PARSER TESTS ══");

            int passed = 0, failed = 0;

            // ── Baseline: existing behaviour unchanged ────────────────

            Check(ref passed, ref failed,
                "empty query → no groups",
                query: "",
                expected: new string[0][]);

            Check(ref passed, ref failed,
                "single literal",
                query: "תורה",
                expected: new[] { new[] { "תורה" } });

            Check(ref passed, ref failed,
                "two literals → two AND groups",
                query: "משה תורה",
                expected: new[] { new[] { "משה" }, new[] { "תורה" } });

            Check(ref passed, ref failed,
                "three literals → three AND groups",
                query: "אברהם יצחק יעקב",
                expected: new[] { new[] { "אברהם" }, new[] { "יצחק" }, new[] { "יעקב" } });

            Check(ref passed, ref failed,
                "wildcard token preserved",
                query: "ישר*",
                expected: new[] { new[] { "ישר*" } },
                checkWildcard: new[] { true });

            Check(ref passed, ref failed,
                "fuzzy token preserved",
                query: "יצחק~",
                expected: new[] { new[] { "יצחק" } },
                checkFuzzy: new[] { true },
                checkFuzzyDist: new[] { 1 });

            Check(ref passed, ref failed,
                "fuzzy distance 2",
                query: "משה~2",
                expected: new[] { new[] { "משה" } },
                checkFuzzy: new[] { true },
                checkFuzzyDist: new[] { 2 });

            Check(ref passed, ref failed,
                "nikud stripped",
                query: "שָׁלוֹם",
                expected: new[] { new[] { "שלום" } });

            Check(ref passed, ref failed,
                "english lowercased",
                query: "Torah",
                expected: new[] { new[] { "torah" } });

            Check(ref passed, ref failed,
                "whitespace-only query → no groups",
                query: "   \t  ",
                expected: new string[0][]);

            // ── OR: basic two-alternative group ──────────────────────

            Check(ref passed, ref failed,
                "two alternatives: a | b",
                query: "א | ב",
                expected: new[] { new[] { "א", "ב" } });

            Check(ref passed, ref failed,
                "three alternatives: a | b | c",
                query: "א | ב | ג",
                expected: new[] { new[] { "א", "ב", "ג" } });

            // ── OR: mixed with AND ────────────────────────────────────

            Check(ref passed, ref failed,
                "OR group then AND term: a | b c",
                query: "א | ב ג",
                expected: new[] { new[] { "א", "ב" }, new[] { "ג" } });

            Check(ref passed, ref failed,
                "AND term then OR group: a b | c",
                query: "א ב | ג",
                expected: new[] { new[] { "א" }, new[] { "ב", "ג" } });

            Check(ref passed, ref failed,
                "AND term, OR group, AND term: a b | c d",
                query: "א ב | ג ד",
                expected: new[] { new[] { "א" }, new[] { "ב", "ג" }, new[] { "ד" } });

            Check(ref passed, ref failed,
                "two separate OR groups: a | b c | d",
                query: "א | ב ג | ד",
                expected: new[] { new[] { "א", "ב" }, new[] { "ג", "ד" } });

            Check(ref passed, ref failed,
                "three-word OR group between two literals: x a | b | c y",
                query: "ת א | ב | ג ד",
                expected: new[] { new[] { "ת" }, new[] { "א", "ב", "ג" }, new[] { "ד" } });

            // ── OR: edge cases ────────────────────────────────────────

            Check(ref passed, ref failed,
                "leading pipe ignored: | a b",
                query: "| א ב",
                expected: new[] { new[] { "א" }, new[] { "ב" } });

            Check(ref passed, ref failed,
                "trailing pipe ignored: a b |",
                query: "א ב |",
                expected: new[] { new[] { "א" }, new[] { "ב" } });

            Check(ref passed, ref failed,
                "double pipe treated as one separator: a || b",
                query: "א || ב",
                expected: new[] { new[] { "א", "ב" } });

            Check(ref passed, ref failed,
                "pipe-only query → no groups",
                query: "|",
                expected: new string[0][]);

            Check(ref passed, ref failed,
                "multiple pipes only → no groups",
                query: "| | |",
                expected: new string[0][]);

            Check(ref passed, ref failed,
                "single token with surrounding pipes: | a |",
                query: "| א |",
                expected: new[] { new[] { "א" } });

            // ── OR: with wildcards and fuzzy ──────────────────────────

            Check(ref passed, ref failed,
                "wildcard in OR group: a* | b",
                query: "א* | ב",
                expected: new[] { new[] { "א*", "ב" } },
                checkWildcard: new[] { true, false });

            Check(ref passed, ref failed,
                "fuzzy in OR group: a~ | b",
                query: "א~ | ב",
                expected: new[] { new[] { "א", "ב" } },
                checkFuzzy: new[] { true, false });

            Check(ref passed, ref failed,
                "wildcard and fuzzy in same OR group: a* | b~2",
                query: "א* | ב~2",
                expected: new[] { new[] { "א*", "ב" } },
                checkWildcard:  new[] { true,  false },
                checkFuzzy:     new[] { false, true  },
                checkFuzzyDist: new[] { 1,     2     });

            Check(ref passed, ref failed,
                "OR group with AND literal: a* | b~ c",
                query: "א* | ב~ ג",
                expected: new[] { new[] { "א*", "ב" }, new[] { "ג" } },
                checkWildcard: new[] { true, false, false },
                checkFuzzy:    new[] { false, true, false });

            // ── OR: nikud stripped in alternatives ────────────────────

            Check(ref passed, ref failed,
                "nikud stripped in OR alternatives",
                query: "שָׁלוֹם | תּוֹרָה",
                expected: new[] { new[] { "שלום", "תורה" } });

            // ── OR: duplicate alternatives collapsed ──────────────────

            // The parser itself does NOT deduplicate — that happens at expansion time.
            // Verify the parser faithfully preserves both (dedup is the expander's job).
            Check(ref passed, ref failed,
                "duplicate alternatives kept by parser: a | a",
                query: "א | א",
                expected: new[] { new[] { "א", "א" } });

            // ── Grammar expansion (%) ─────────────────────────────────

            Check(ref passed, ref failed,
                "prefix only: %word → GrammarPrefix=true, GrammarSuffix=false",
                query: "%תורה",
                expected: new[] { new[] { "תורה" } },
                checkGrammarPrefix: new[] { true },
                checkGrammarSuffix: new[] { false });

            Check(ref passed, ref failed,
                "suffix only: word% → GrammarPrefix=false, GrammarSuffix=true",
                query: "תורה%",
                expected: new[] { new[] { "תורה" } },
                checkGrammarPrefix: new[] { false },
                checkGrammarSuffix: new[] { true });

            Check(ref passed, ref failed,
                "both: %word% → GrammarPrefix=true, GrammarSuffix=true",
                query: "%תורה%",
                expected: new[] { new[] { "תורה" } },
                checkGrammarPrefix: new[] { true },
                checkGrammarSuffix: new[] { true });

            Check(ref passed, ref failed,
                "grammar AND literal: %ישראל כי",
                query: "%ישראל כי",
                expected: new[] { new[] { "ישראל" }, new[] { "כי" } },
                checkGrammarPrefix: new[] { true, false },
                checkGrammarSuffix: new[] { false, false });

            Check(ref passed, ref failed,
                "grammar with optional char: %שלו?ם",
                query: "%שלו?ם",
                expected: new[] { new[] { "שלו?ם" } },
                checkGrammarPrefix: new[] { true },
                checkGrammarSuffix: new[] { false });

            Check(ref passed, ref failed,
                "star overrides %: %word* → IsWildcard=true, grammar cleared",
                query: "%תורה*",
                expected: new[] { new[] { "תורה*" } },
                checkWildcard:      new[] { true  },
                checkGrammarPrefix: new[] { false },
                checkGrammarSuffix: new[] { false });

            Check(ref passed, ref failed,
                "star overrides %: *word% → IsWildcard=true, grammar cleared",
                query: "*תורה%",
                expected: new[] { new[] { "*תורה" } },
                checkWildcard:      new[] { true  },
                checkGrammarPrefix: new[] { false },
                checkGrammarSuffix: new[] { false });

            Check(ref passed, ref failed,
                "grammar in OR group: %word | literal",
                query: "%תורה | מצוה",
                expected: new[] { new[] { "תורה", "מצוה" } },
                checkGrammarPrefix: new[] { true, false },
                checkGrammarSuffix: new[] { false, false });

            Check(ref passed, ref failed,
                "nikud stripped in grammar token: %שָׁלוֹם",
                query: "%שָׁלוֹם",
                expected: new[] { new[] { "שלום" } },
                checkGrammarPrefix: new[] { true },
                checkGrammarSuffix: new[] { false });

            // ── Summary ───────────────────────────────────────────────

            Console.WriteLine("║");
            string overall = failed == 0
                ? $"✓  All {passed} tests passed"
                : $"✗  {failed} FAILED  /  {passed + failed} total";
            Console.WriteLine($"║  {overall}");
            Console.WriteLine("╚══ PARSER TESTS DONE ══");
            Console.WriteLine();

            if (failed > 0)
                Environment.Exit(1);
        }

        // ── Assertion helper ──────────────────────────────────────────

        /// <summary>
        /// Parses <paramref name="query"/> and asserts the resulting groups match
        /// <paramref name="expected"/>.
        ///
        /// <paramref name="expected"/> is a jagged array:
        ///   expected[groupIndex][altIndex] = pattern string
        ///
        /// Optional parallel arrays (indexed over all alternatives in order,
        /// flattened across groups):
        ///   checkWildcard  — expected IsWildcard per alternative
        ///   checkFuzzy     — expected IsFuzzy per alternative
        ///   checkFuzzyDist — expected FuzzyDistance per alternative
        /// </summary>
        private static void Check(
            ref int    passed,
            ref int    failed,
            string     name,
            string     query,
            string[][] expected,
            bool[]     checkWildcard      = null,
            bool[]     checkFuzzy         = null,
            int[]      checkFuzzyDist     = null,
            bool[]     checkGrammarPrefix = null,
            bool[]     checkGrammarSuffix = null)
        {
            var    pq      = QueryParser.Parse(query);
            var    errors  = new List<string>();

            // ── Group count ───────────────────────────────────────────
            if (pq.Groups.Count != expected.Length)
            {
                errors.Add(
                    $"group count: expected {expected.Length}, got {pq.Groups.Count}");
            }
            else
            {
                // ── Per-group alternative count and patterns ──────────
                int altIndex = 0; // flat index across all alternatives

                for (int g = 0; g < expected.Length; g++)
                {
                    var group    = pq.Groups[g];
                    var expAlts  = expected[g];

                    if (group.Alternatives.Count != expAlts.Length)
                    {
                        errors.Add(
                            $"group[{g}] alt count: expected {expAlts.Length}, " +
                            $"got {group.Alternatives.Count}");
                        altIndex += expAlts.Length;
                        continue;
                    }

                    for (int a = 0; a < expAlts.Length; a++, altIndex++)
                    {
                        var alt = group.Alternatives[a];

                        // Pattern
                        if (alt.Pattern != expAlts[a])
                            errors.Add(
                                $"group[{g}].alt[{a}].Pattern: " +
                                $"expected \"{expAlts[a]}\", got \"{alt.Pattern}\"");

                        // IsWildcard
                        if (checkWildcard != null && altIndex < checkWildcard.Length)
                        {
                            if (alt.IsWildcard != checkWildcard[altIndex])
                                errors.Add(
                                    $"group[{g}].alt[{a}].IsWildcard: " +
                                    $"expected {checkWildcard[altIndex]}, got {alt.IsWildcard}");
                        }

                        // IsFuzzy
                        if (checkFuzzy != null && altIndex < checkFuzzy.Length)
                        {
                            if (alt.IsFuzzy != checkFuzzy[altIndex])
                                errors.Add(
                                    $"group[{g}].alt[{a}].IsFuzzy: " +
                                    $"expected {checkFuzzy[altIndex]}, got {alt.IsFuzzy}");
                        }

                        // FuzzyDistance
                        if (checkFuzzyDist != null && altIndex < checkFuzzyDist.Length
                            && alt.IsFuzzy)
                        {
                            if (alt.FuzzyDistance != checkFuzzyDist[altIndex])
                                errors.Add(
                                    $"group[{g}].alt[{a}].FuzzyDistance: " +
                                    $"expected {checkFuzzyDist[altIndex]}, got {alt.FuzzyDistance}");
                        }

                        // GrammarPrefix
                        if (checkGrammarPrefix != null && altIndex < checkGrammarPrefix.Length)
                        {
                            if (alt.GrammarPrefix != checkGrammarPrefix[altIndex])
                                errors.Add(
                                    $"group[{g}].alt[{a}].GrammarPrefix: " +
                                    $"expected {checkGrammarPrefix[altIndex]}, got {alt.GrammarPrefix}");
                        }

                        // GrammarSuffix
                        if (checkGrammarSuffix != null && altIndex < checkGrammarSuffix.Length)
                        {
                            if (alt.GrammarSuffix != checkGrammarSuffix[altIndex])
                                errors.Add(
                                    $"group[{g}].alt[{a}].GrammarSuffix: " +
                                    $"expected {checkGrammarSuffix[altIndex]}, got {alt.GrammarSuffix}");
                        }
                    }
                }
            }

            // ── Report ────────────────────────────────────────────────
            if (errors.Count == 0)
            {
                passed++;
                Console.WriteLine($"║  ✓  {name}");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                foreach (var e in errors)
                    Console.WriteLine($"║       {e}");
            }
        }
    }
}
