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
            // (Placeholder tokens are 2 letters: the parser now drops literals
            //  shorter than the index's 2-char minimum — tested separately below.)

            Check(ref passed, ref failed,
                "two alternatives: a | b",
                query: "אב | בג",
                expected: new[] { new[] { "אב", "בג" } });

            Check(ref passed, ref failed,
                "three alternatives: a | b | c",
                query: "אב | בג | גד",
                expected: new[] { new[] { "אב", "בג", "גד" } });

            // ── OR: mixed with AND ────────────────────────────────────

            Check(ref passed, ref failed,
                "OR group then AND term: a | b c",
                query: "אב | בג גד",
                expected: new[] { new[] { "אב", "בג" }, new[] { "גד" } });

            Check(ref passed, ref failed,
                "AND term then OR group: a b | c",
                query: "אב בג | גד",
                expected: new[] { new[] { "אב" }, new[] { "בג", "גד" } });

            Check(ref passed, ref failed,
                "AND term, OR group, AND term: a b | c d",
                query: "אב בג | גד דה",
                expected: new[] { new[] { "אב" }, new[] { "בג", "גד" }, new[] { "דה" } });

            Check(ref passed, ref failed,
                "two separate OR groups: a | b c | d",
                query: "אב | בג גד | דה",
                expected: new[] { new[] { "אב", "בג" }, new[] { "גד", "דה" } });

            Check(ref passed, ref failed,
                "three-word OR group between two literals: x a | b | c y",
                query: "תו אב | בג | גד דה",
                expected: new[] { new[] { "תו" }, new[] { "אב", "בג", "גד" }, new[] { "דה" } });

            // ── OR: edge cases ────────────────────────────────────────

            Check(ref passed, ref failed,
                "leading pipe ignored: | a b",
                query: "| אב בג",
                expected: new[] { new[] { "אב" }, new[] { "בג" } });

            Check(ref passed, ref failed,
                "trailing pipe ignored: a b |",
                query: "אב בג |",
                expected: new[] { new[] { "אב" }, new[] { "בג" } });

            Check(ref passed, ref failed,
                "double pipe treated as one separator: a || b",
                query: "אב || בג",
                expected: new[] { new[] { "אב", "בג" } });

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
                query: "| אב |",
                expected: new[] { new[] { "אב" } });

            // ── OR: with wildcards and fuzzy ──────────────────────────

            Check(ref passed, ref failed,
                "wildcard in OR group: a* | b",
                query: "א* | בג",
                expected: new[] { new[] { "א*", "בג" } },
                checkWildcard: new[] { true, false });

            Check(ref passed, ref failed,
                "fuzzy in OR group: a~ | b",
                query: "אב~ | בג",
                expected: new[] { new[] { "אב", "בג" } },
                checkFuzzy: new[] { true, false });

            Check(ref passed, ref failed,
                "wildcard and fuzzy in same OR group: a* | b~2",
                query: "א* | בג~2",
                expected: new[] { new[] { "א*", "בג" } },
                checkWildcard:  new[] { true,  false },
                checkFuzzy:     new[] { false, true  },
                checkFuzzyDist: new[] { 1,     2     });

            Check(ref passed, ref failed,
                "OR group with AND literal: a* | b~ c",
                query: "א* | בג~ גד",
                expected: new[] { new[] { "א*", "בג" }, new[] { "גד" } },
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
                query: "אב | אב",
                expected: new[] { new[] { "אב", "אב" } });

            // ── Index-separator parity (regression: maqaf query mismatch) ──
            // The indexer SPLITS words on maqaf, hyphens, digits, and punctuation;
            // the parser used to DELETE these mid-token, gluing the fragments into
            // one term that cannot exist in the index (pasted "יום־טוב" returned
            // zero results). The parser must split exactly where the indexer does.

            Check(ref passed, ref failed,
                "maqaf splits into two AND groups",
                query: "יום־טוב",
                expected: new[] { new[] { "יום" }, new[] { "טוב" } });

            Check(ref passed, ref failed,
                "ASCII hyphen splits into two AND groups",
                query: "יום-טוב",
                expected: new[] { new[] { "יום" }, new[] { "טוב" } });

            Check(ref passed, ref failed,
                "mid-token digits split like the indexer",
                query: "אב3גד",
                expected: new[] { new[] { "אב" }, new[] { "גד" } });

            Check(ref passed, ref failed,
                "surrounding punctuation stripped as separators",
                query: "(שלום)",
                expected: new[] { new[] { "שלום" } });

            Check(ref passed, ref failed,
                "maqaf inside a fuzzy token: fuzzy applies to the last fragment",
                query: "יום־טוב~2",
                expected: new[] { new[] { "יום" }, new[] { "טוב" } },
                checkFuzzy:     new[] { false, true },
                checkFuzzyDist: new[] { 1,     2    });

            Check(ref passed, ref failed,
                "intra-word quote is transparent (matches the indexer)",
                query: "רש\"י",
                expected: new[] { new[] { "רשי" } });

            Check(ref passed, ref failed,
                "mixed-script token splits at the script boundary",
                query: "abדה",
                expected: new[] { new[] { "ab" }, new[] { "דה" } });

            Check(ref passed, ref failed,
                "1-char script fragment dropped after the split",
                query: "bדה",
                expected: new[] { new[] { "דה" } });

            // ── Unindexable-length literals dropped (regression) ──────
            // The index stores only 2..29-letter words; a 1-char (or ≥30-char)
            // literal can never match and used to poison the whole AND query
            // into guaranteed-zero results.

            Check(ref passed, ref failed,
                "1-char literal dropped, rest of query survives",
                query: "ב שלום",
                expected: new[] { new[] { "שלום" } });

            Check(ref passed, ref failed,
                "30-char literal dropped, rest of query survives",
                query: new string('א', 30) + " שלום",
                expected: new[] { new[] { "שלום" } });

            Check(ref passed, ref failed,
                "1-char FUZZY token kept (can match 2-char index terms)",
                query: "א~ שלום",
                expected: new[] { new[] { "א" }, new[] { "שלום" } },
                checkFuzzy: new[] { true, false });

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
            bool[]     checkWildcard  = null,
            bool[]     checkFuzzy     = null,
            int[]      checkFuzzyDist = null)
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
