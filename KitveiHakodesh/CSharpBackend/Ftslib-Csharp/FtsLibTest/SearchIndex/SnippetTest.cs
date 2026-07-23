using FtsLib.Snippets;
using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Unit tests for <see cref="SnippetBuilder"/> — covers snippet text content,
    /// highlight placement, window selection, context expansion, and edge cases.
    ///
    /// No index or database required — all content is supplied inline.
    ///
    /// Usage:
    ///   FtsLibTest.exe snippettest
    /// </summary>
    internal static class SnippetTest
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine();
            Console.WriteLine("╔══ SNIPPET TESTS ══");

            int passed = 0, failed = 0;

            // ─────────────────────────────────────────────────────────
            // 1. BASIC HIGHLIGHT — matched terms are wrapped in <mark>
            // ─────────────────────────────────────────────────────────

            CheckContains(ref passed, ref failed,
                name:    "single term is highlighted",
                content: "שישים גבורים סביב לה",
                groups:  G("שישים"),
                mustContain: "<mark>שישים</mark>");

            CheckContains(ref passed, ref failed,
                name:    "both terms highlighted in two-word query",
                content: "שישים גבורים סביב לה",
                groups:  G("שישים", "גבורים"),
                mustContain: "<mark>שישים</mark>");

            CheckContains(ref passed, ref failed,
                name:    "second term also highlighted",
                content: "שישים גבורים סביב לה",
                groups:  G("שישים", "גבורים"),
                mustContain: "<mark>גבורים</mark>");

            // ─────────────────────────────────────────────────────────
            // 2. SNIPPET WINDOW — the matched terms appear in the output
            // ─────────────────────────────────────────────────────────

            CheckContains(ref passed, ref failed,
                name:    "matched term appears in snippet text",
                content: "שישים גבורים סביב לה מגבורי ישראל",
                groups:  G("שישים", "גבורים"),
                mustContain: "שישים");

            CheckContains(ref passed, ref failed,
                name:    "second matched term appears in snippet text",
                content: "שישים גבורים סביב לה מגבורי ישראל",
                groups:  G("שישים", "גבורים"),
                mustContain: "גבורים");

            // ─────────────────────────────────────────────────────────
            // 3. NO MATCH — term absent from content
            // ─────────────────────────────────────────────────────────

            CheckNoMatch(ref passed, ref failed,
                name:    "absent term → IsMatch false",
                content: "שלום עולם",
                groups:  G("תורה"));

            CheckNoMatch(ref passed, ref failed,
                name:    "one term present, one absent → IsMatch false",
                content: "שישים אנשים הלכו",
                groups:  G("שישים", "גבורים"));

            // ─────────────────────────────────────────────────────────
            // 3b. INLINE TAGS & ENTITIES — tokenizer word-boundary rules
            //     (regression for the 2026-07-22 scanner fixes: inline
            //     formatting is word-TRANSPARENT, block tags and sup/sub
            //     break words, every entity is a separator)
            // ─────────────────────────────────────────────────────────

            // A word interrupted by <b> emphasis is ONE word (previously it was
            // shredded into unfindable fragments — ~1% of corpus lines).
            CheckMatch(ref passed, ref failed,
                name:    "word split by <b> tokenizes as one word",
                content: "ורא<b>ה</b> מן המקום",
                groups:  G("וראה"),
                expectMatch: true);

            // The highlight around a tag-spanning token strips the inner tags —
            // no unbalanced <b> may leak into the snippet HTML.
            CheckContains(ref passed, ref failed,
                name:    "tag-spanning token highlighted with tags stripped",
                content: "ורא<b>ה</b> מן המקום",
                groups:  G("וראה"),
                mustContain: "<mark>וראה</mark>");

            CheckMatch(ref passed, ref failed,
                name:    "word starting inside <b> tokenizes as one word",
                content: "נזכר ב<b>תוספות</b> שם",
                groups:  G("בתוספות"),
                expectMatch: true);

            // sup/sub content is a footnote marker, NOT part of the word.
            CheckMatch(ref passed, ref failed,
                name:    "<sup> footnote marker does not join the word",
                content: "אורות<sup>ב</sup> גדולים",
                groups:  G("אורות"),
                expectMatch: true);

            CheckNoMatch(ref passed, ref failed,
                name:    "<sup> content not glued onto preceding word",
                content: "אורות<sup>ב</sup> גדולים",
                groups:  G("אורותב"));

            // Block-level tags still break words.
            CheckNoMatch(ref passed, ref failed,
                name:    "<br> still breaks the word",
                content: "שב<br>ת קודש",
                groups:  G("שבת"));

            // Entities are separators — never invisible joiners.
            CheckMatch(ref passed, ref failed,
                name:    "&nbsp; separates words",
                content: "שלום&nbsp;תורה",
                groups:  G("שלום", "תורה"),
                expectMatch: true);

            CheckMatch(ref passed, ref failed,
                name:    "&thinsp; separates words (was invisible → joined)",
                content: "שלום&thinsp;תורה",
                groups:  G("שלום", "תורה"),
                expectMatch: true);

            CheckMatch(ref passed, ref failed,
                name:    "&amp; separates words (was invisible → joined)",
                content: "שלום&amp;תורה",
                groups:  G("שלום", "תורה"),
                expectMatch: true);

            CheckNoMatch(ref passed, ref failed,
                name:    "entity no longer glues fragments into one term",
                content: "שלום&amp;תורה",
                groups:  G("שלוםתורה"));

            // REGRESSION (2026-07-23): an entity that ENDS the matched word must not
            // leak into the snippet as a truncated entity. SkipEntity advanced the raw
            // cursor to the ';', and Flush used that as the word's RawEnd — so the token
            // span pulled in the entity body WITHOUT its ';' (e.g. "&thinsp"), which the
            // renderer copied verbatim and v-html then showed as literal text. The '&'
            // must end the word so the WHOLE entity stays outside every token span.
            CheckContains(ref passed, ref failed,
                name:    "entity after matched word: full &thinsp; survives in snippet",
                content: "וראה כי ידעתיו&thinsp;למען אשר יצוה",
                groups:  G("ידעתיו"),
                mustContain: "&thinsp;");

            CheckContains(ref passed, ref failed,
                name:    "entity after matched word: match closes before the '&'",
                content: "וראה כי ידעתיו&thinsp;למען אשר יצוה",
                groups:  G("ידעתיו"),
                mustContain: "<mark>ידעתיו</mark>");

            CheckNotContains(ref passed, ref failed,
                name:    "entity after matched word: no truncated entity inside <mark>",
                content: "וראה כי ידעתיו&thinsp;למען אשר יצוה",
                groups:  G("ידעתיו"),
                mustNotContain: "&thinsp</mark>");

            // The word BEFORE the entity is the last context token at the window edge:
            // snapEnd = RawEnd must sit at the '&', so no partial entity is appended.
            CheckNotContains(ref passed, ref failed,
                name:    "entity at trailing window edge: no bare '&thinsp' without ';'",
                content: "ראש שני שלישי רביעי חמישי ידעתיו&thinsp;",
                groups:  G("ראש"),
                mustNotContain: "&thinsp<");

            // ─────────────────────────────────────────────────────────
            // 4. NIKUD IN CONTENT — tokenizer strips nikud, terms match
            // ─────────────────────────────────────────────────────────

            // Use כִּי בְיִצְחָק — same words as the WordDistanceTest nikud case,
            // known to tokenize correctly after nikud stripping.
            CheckMatch(ref passed, ref failed,
                name:    "nikud in content — IsMatch true",
                content: "כִּי בְיִצְחָק יִקָּרֵא לְךָ זָרַע",
                groups:  G("כי", "ביצחק"),
                expectMatch: true);

            CheckContains(ref passed, ref failed,
                name:    "nikud in content — term highlighted",
                content: "כִּי בְיִצְחָק יִקָּרֵא לְךָ זָרַע",
                groups:  G("כי", "ביצחק"),
                mustContain: "<mark>");

            // ─────────────────────────────────────────────────────────
            // 5. HTML TAGS IN CONTENT — tags stripped from output text
            // ─────────────────────────────────────────────────────────

            CheckMatch(ref passed, ref failed,
                name:    "term inside <b> tag — IsMatch true",
                content: "<b>שישים</b> גבורים סביב לה",
                groups:  G("שישים", "גבורים"),
                expectMatch: true);

            CheckNotContains(ref passed, ref failed,
                name:    "output does not contain raw <b> tag from content",
                content: "<b>שישים</b> גבורים",
                groups:  G("שישים", "גבורים"),
                mustNotContain: "<b>");

            // BUG: RawEnd points past the closing tag of the surrounding element,
            // so the raw slice fed into <mark> includes the </b> tag.
            // Expected: <mark>שישים</mark>
            // Actual:   <mark>שישים</b></mark>
            // This test documents the bug — it currently fails.
            CheckContains(ref passed, ref failed,
                name:    "term inside <b> tag is highlighted without leaking closing tag  [BUG: currently fails]",
                content: "<b>שישים</b> גבורים",
                groups:  G("שישים", "גבורים"),
                mustContain: "<mark>שישים</mark>");

            // ─────────────────────────────────────────────────────────
            // 6. PARAGRAPH MARKERS — {X} stripped from output
            // ─────────────────────────────────────────────────────────

            CheckNotContains(ref passed, ref failed,
                name:    "{א} paragraph marker stripped from output",
                content: "{א} שישים גבורים סביב לה",
                groups:  G("שישים"),
                mustNotContain: "{א}");

            CheckContains(ref passed, ref failed,
                name:    "term after paragraph marker still highlighted",
                content: "{א} שישים גבורים סביב לה",
                groups:  G("שישים"),
                mustContain: "<mark>שישים</mark>");

            // ─────────────────────────────────────────────────────────
            // 7. ELLIPSIS — long lines get trimmed with ellipsis markers
            // ─────────────────────────────────────────────────────────

            // Use a SnippetBuilder with a small snippetLength so the window
            // is forced to trim. contextWords=2 keeps the window tight.
            // The match "שישים גבורים" is placed after a long prefix.
            string ellipsisPrefix = "אחד שתים שלש ארבע חמש שש שבע שמונה תשע עשר ";
            string ellipsisSuffix = " אחד שתים שלש ארבע חמש שש שבע שמונה תשע עשר";
            string ellipsisContent = ellipsisPrefix + "שישים גבורים" + ellipsisSuffix;

            var ellipsisBuilder = new SnippetBuilder(contextWords: 1);
            var ellipsisResult = ellipsisBuilder.Build(
                ellipsisContent,
                (IReadOnlyList<IReadOnlyCollection<string>>)new[] { new[] { "שישים" }, new[] { "גבורים" } });

            if (ellipsisResult.IsMatch && ellipsisResult.Html.Contains("…"))
            {
                passed++;
                Console.WriteLine($"║  ✓  long line: ellipsis present when content exceeds snippetLength");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  long line: ellipsis present when content exceeds snippetLength");
                Console.WriteLine($"║       IsMatch={ellipsisResult.IsMatch}");
                Console.WriteLine($"║       Html=\"{ellipsisResult.Html}\"");
            }

            if (ellipsisResult.IsMatch && ellipsisResult.Html.Contains("שישים"))
            {
                passed++;
                Console.WriteLine($"║  ✓  long line: matched terms still appear in trimmed snippet");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  long line: matched terms still appear in trimmed snippet");
                Console.WriteLine($"║       Html=\"{ellipsisResult.Html}\"");
            }

            // ─────────────────────────────────────────────────────────
            // 8. SHORT CONTENT — no ellipsis when content fits in window
            // ─────────────────────────────────────────────────────────

            CheckNotContains(ref passed, ref failed,
                name:    "short content — no ellipsis",
                content: "שישים גבורים סביב לה",
                groups:  G("שישים", "גבורים"),
                mustNotContain: "…");

            // ─────────────────────────────────────────────────────────
            // 9. OR GROUPS — any alternative in a group satisfies it
            // ─────────────────────────────────────────────────────────

            CheckMatch(ref passed, ref failed,
                name:    "OR group: first alternative present → match",
                content: "שישים גבורים סביב לה",
                groups:  new[] { new[] { "שישים", "שבעים" }, new[] { "גבורים" } },
                expectMatch: true);

            CheckMatch(ref passed, ref failed,
                name:    "OR group: second alternative present → match",
                content: "שבעים גבורים סביב לה",
                groups:  new[] { new[] { "שישים", "שבעים" }, new[] { "גבורים" } },
                expectMatch: true);

            CheckNoMatch(ref passed, ref failed,
                name:    "OR group: neither alternative present → no match",
                content: "מאה גבורים סביב לה",
                groups:  new[] { new[] { "שישים", "שבעים" }, new[] { "גבורים" } });

            // ─────────────────────────────────────────────────────────
            // 10. TIGHTEST WINDOW — when terms appear multiple times,
            //     the closest occurrence is chosen for the match window
            // ─────────────────────────────────────────────────────────

            // "שישים" appears twice: once far from "גבורים", once adjacent.
            // The window finder should pick the adjacent pair (lower score).
            // Both matched terms must appear highlighted in the output.
            string twoOccurrences =
                "שישים אנשים הלכו בדרך ובדרך ובדרך ובדרך ובדרך שישים גבורים";

            var twoOccurrencesResult = new SnippetBuilder().Build(
                twoOccurrences,
                (IReadOnlyList<IReadOnlyCollection<string>>)new[] { new[] { "שישים" }, new[] { "גבורים" } });

            // The score of the tightest window should be small (adjacent pair).
            // Adjacent "שישים גבורים" span ≈ 12 chars; distant pair span >> 12.
            if (twoOccurrencesResult.IsMatch && twoOccurrencesResult.WordDistance == 0)
            {
                passed++;
                Console.WriteLine($"║  ✓  tightest window: adjacent occurrence chosen (WordDistance=0)");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  tightest window: adjacent occurrence chosen (WordDistance=0)");
                Console.WriteLine($"║       IsMatch={twoOccurrencesResult.IsMatch}  WordDistance={twoOccurrencesResult.WordDistance}");
                Console.WriteLine($"║       Html=\"{twoOccurrencesResult.Html}\"");
            }

            // ─────────────────────────────────────────────────────────
            // 11. SINGLE-CHAR WORDS — tokenizer drops them (length > 1)
            //     so single-letter queries never match
            // ─────────────────────────────────────────────────────────

            CheckNoMatch(ref passed, ref failed,
                name:    "single Hebrew letter query → no match (tokenizer drops single-char tokens)",
                content: "א ב ג ד ה",
                groups:  G("א"));

            // ─────────────────────────────────────────────────────────
            // 12. EMPTY / NULL CONTENT
            // ─────────────────────────────────────────────────────────

            CheckNoMatch(ref passed, ref failed,
                name:    "empty content → no match",
                content: "",
                groups:  G("שישים"));

            // ─────────────────────────────────────────────────────────
            // 13. HIGHLIGHT DOES NOT BLEED INTO SURROUNDING TEXT
            //     The <mark> tag must close before the next word starts.
            // ─────────────────────────────────────────────────────────

            // After the closing </mark> the next character must not be a letter
            // that belongs to the next word — i.e. no run-on like <mark>שישים</mark>גבורים.
            CheckNotContains(ref passed, ref failed,
                name:    "highlight closes before next word (no run-on)",
                content: "שישים גבורים",
                groups:  G("שישים"),
                mustNotContain: "</mark>ג");

            // ─────────────────────────────────────────────────────────
            // 14. CONTEXT WORDS — non-matched words around the match
            //     appear in the snippet
            // ─────────────────────────────────────────────────────────

            // "סביב" is not a query term but sits right next to the match.
            // With contextWords >= 1 it must appear in the output.
            CheckContains(ref passed, ref failed,
                name:    "context word adjacent to match appears in snippet",
                content: "שישים גבורים סביב לה מגבורי ישראל",
                groups:  G("שישים", "גבורים"),
                mustContain: "סביב");

            // ─────────────────────────────────────────────────────────
            // 15. SCORE — tighter window scores lower (smaller raw span)
            // ─────────────────────────────────────────────────────────

            var builderA = new SnippetBuilder();
            var builderB = new SnippetBuilder();

            var resultAdjacent = builderA.Build(
                "שישים גבורים",
                (IReadOnlyList<IReadOnlyCollection<string>>)new[] { new[] { "שישים" }, new[] { "גבורים" } });

            var resultDistant = builderB.Build(
                "שישים אחד שתים שלש ארבע חמש שש שבע שמונה תשע גבורים",
                (IReadOnlyList<IReadOnlyCollection<string>>)new[] { new[] { "שישים" }, new[] { "גבורים" } });

            if (resultAdjacent.IsMatch && resultDistant.IsMatch
                && resultAdjacent.Score < resultDistant.Score)
            {
                passed++;
                Console.WriteLine($"║  ✓  adjacent window scores lower than distant window  " +
                                  $"(adj={resultAdjacent.Score}, dist={resultDistant.Score})");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  adjacent window scores lower than distant window");
                Console.WriteLine($"║       adjacent IsMatch={resultAdjacent.IsMatch} score={resultAdjacent.Score}");
                Console.WriteLine($"║       distant  IsMatch={resultDistant.IsMatch} score={resultDistant.Score}");
            }

            // ─────────────────────────────────────────────────────────
            // 16. LONG LINE WITH DISTANT TERMS — window must not trim
            //     past the match tokens (regression for ExpandWindow bug)
            // ─────────────────────────────────────────────────────────

            // Build a line where the two matched terms are far apart and the
            // total visible length far exceeds snippetLength. The trimming loop
            // in ExpandWindow must clamp at iLeft/iRight, not trim past them.
            var longLineBuilder = new SnippetBuilder(contextWords: 2);
            string filler = "אחד שתים שלש ארבע חמש שש שבע שמונה תשע עשר " +
                            "אחד שתים שלש ארבע חמש שש שבע שמונה תשע עשר " +
                            "אחד שתים שלש ארבע חמש שש שבע שמונה תשע עשר ";
            string distantContent = filler + "שישים" + " " + filler + "גבורים" + " " + filler;
            var distantResult = longLineBuilder.Build(
                distantContent,
                (IReadOnlyList<IReadOnlyCollection<string>>)new[] { new[] { "שישים" }, new[] { "גבורים" } });

            if (distantResult.IsMatch && distantResult.Html.Contains("שישים")
                                      && distantResult.Html.Contains("גבורים"))
            {
                passed++;
                Console.WriteLine($"║  ✓  distant terms on long line: both terms present in snippet");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  distant terms on long line: both terms present in snippet");
                Console.WriteLine($"║       IsMatch={distantResult.IsMatch}");
                Console.WriteLine($"║       Html=\"{distantResult.Html}\"");
            }

            Console.WriteLine("║");
            string overall = failed == 0
                ? $"✓  All {passed} tests passed"
                : $"✗  {failed} FAILED  /  {passed + failed} total";
            Console.WriteLine($"║  {overall}");
            Console.WriteLine("╚══ SNIPPET TESTS DONE ══");
            Console.WriteLine();

            if (failed > 0)
                Environment.Exit(1);
        }

        // ── Assertion helpers ─────────────────────────────────────────

        /// <summary>Asserts IsMatch equals expectMatch.</summary>
        private static void CheckMatch(
            ref int                                    passed,
            ref int                                    failed,
            string                                     name,
            string                                     content,
            IReadOnlyList<IReadOnlyCollection<string>> groups,
            bool                                       expectMatch)
        {
            var result = new SnippetBuilder().Build(content, groups);

            if (result.IsMatch == expectMatch)
            {
                passed++;
                Console.WriteLine($"║  ✓  {name}");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                Console.WriteLine($"║       expected IsMatch={expectMatch}, got IsMatch={result.IsMatch}");
                Console.WriteLine($"║       Html=\"{result.Html}\"");
            }
        }

        /// <summary>Asserts IsMatch is false.</summary>
        private static void CheckNoMatch(
            ref int                                    passed,
            ref int                                    failed,
            string                                     name,
            string                                     content,
            IReadOnlyList<IReadOnlyCollection<string>> groups)
        {
            var result = new SnippetBuilder().Build(content, groups);

            if (!result.IsMatch)
            {
                passed++;
                Console.WriteLine($"║  ✓  {name}  (IsMatch=false as expected)");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                Console.WriteLine($"║       expected IsMatch=false, got IsMatch=true");
                Console.WriteLine($"║       Html=\"{result.Html}\"");
            }
        }

        /// <summary>Asserts the snippet Html contains the given substring.</summary>
        private static void CheckContains(
            ref int                                    passed,
            ref int                                    failed,
            string                                     name,
            string                                     content,
            IReadOnlyList<IReadOnlyCollection<string>> groups,
            string                                     mustContain)
        {
            var result = new SnippetBuilder().Build(content, groups);

            if (result.IsMatch && result.Html.Contains(mustContain))
            {
                passed++;
                Console.WriteLine($"║  ✓  {name}");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                Console.WriteLine($"║       IsMatch={result.IsMatch}");
                Console.WriteLine($"║       expected Html to contain: \"{mustContain}\"");
                Console.WriteLine($"║       actual Html: \"{result.Html}\"");
            }
        }

        /// <summary>Asserts the snippet Html does NOT contain the given substring.</summary>
        private static void CheckNotContains(
            ref int                                    passed,
            ref int                                    failed,
            string                                     name,
            string                                     content,
            IReadOnlyList<IReadOnlyCollection<string>> groups,
            string                                     mustNotContain)
        {
            var result = new SnippetBuilder().Build(content, groups);

            if (!result.Html.Contains(mustNotContain))
            {
                passed++;
                Console.WriteLine($"║  ✓  {name}");
            }
            else
            {
                failed++;
                Console.WriteLine($"║  ✗  {name}");
                Console.WriteLine($"║       Html must NOT contain: \"{mustNotContain}\"");
                Console.WriteLine($"║       actual Html: \"{result.Html}\"");
            }
        }

        // ── Query group builder ───────────────────────────────────────

        /// <summary>Each string becomes its own single-alternative AND group.</summary>
        private static IReadOnlyList<IReadOnlyCollection<string>> G(params string[] terms)
        {
            var groups = new List<IReadOnlyCollection<string>>(terms.Length);
            foreach (var t in terms)
                groups.Add(new[] { t });
            return groups;
        }
    }
}
