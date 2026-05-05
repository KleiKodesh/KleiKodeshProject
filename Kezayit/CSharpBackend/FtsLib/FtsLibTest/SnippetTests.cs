using FtsLib.Core;
using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Unit-style tests for TokenStream, ProximityWindow (via SnippetBuilder),
    /// and SnippetBuilder itself. No DB or index required — all inputs are inline strings.
    /// Run via: FtsLibTest.exe snippet
    /// </summary>
    internal static class SnippetTests
    {
        private static int _pass;
        private static int _fail;

        public static void Run()
        {
            Console.OutputEncoding = Encoding.UTF8;
            _pass = _fail = 0;

            Console.WriteLine("=== SnippetTests ===");
            Console.WriteLine();

            Diag();
            TestTokenStream();
            TestProximityWindow();
            TestSnippetBuilder();

            Console.WriteLine();
            Console.WriteLine($"Results: {_pass} passed, {_fail} failed.");
            if (_fail > 0)
                Console.WriteLine("*** FAILURES DETECTED ***");
        }

        private static void Diag()
        {
            Console.WriteLine("── Diagnostics ──────────────────────────────");
            var ts = new TokenStream();

            // rawStart1: how many chars is "שלום " (5 Hebrew letters + space)?
            var t1 = ts.Tokenize("שלום עולם");
            Console.WriteLine($"  'שלום עולם' token[1].RawStart = {t1[1].RawStart}  (expected 6)");

            // both highlighted: what does the HTML look like?
            var sb = new SnippetBuilder(snippetLength: 9999);
            var r2 = sb.Build("תורה ומצוה", Terms("תורה", "מצוה"));
            Console.WriteLine($"  'תורה ומצוה' html = {r2.Html}");

            // long ellipsis
            string longText = new string('א', 100) + " תורה " + new string('ב', 100);
            var r3 = new SnippetBuilder(snippetLength: 40, contextMargin: 5).Build(longText, Terms("תורה"));
            Console.WriteLine($"  long html[0..80] = {r3.Html.Substring(0, Math.Min(80, r3.Html.Length))}");
            Console.WriteLine($"  long starts with ellipsis: {r3.Html.StartsWith("…")}");

            // window centered
            string text4 = new string('ד', 200) + " תורה";
            var r4 = new SnippetBuilder(snippetLength: 40, contextMargin: 5).Build(text4, Terms("תורה"));
            Console.WriteLine($"  centered html[0..80] = {r4.Html.Substring(0, Math.Min(80, r4.Html.Length))}");
            Console.WriteLine($"  centered starts with ellipsis: {r4.Html.StartsWith("…")}");
            Console.WriteLine();        }

        // ── TokenStream ──────────────────────────────────────────────

        private static void TestTokenStream()
        {
            Console.WriteLine("── TokenStream ──────────────────────────────");
            var ts = new TokenStream();

            // Basic: plain Hebrew words
            {
                var tokens = ts.Tokenize("שלום עולם");
                Assert("plain Hebrew: count",    tokens.Count == 2);
                Assert("plain Hebrew: word 0",   tokens[0].Normalized == "שלום");
                Assert("plain Hebrew: word 1",   tokens[1].Normalized == "עולם");
                Assert("plain Hebrew: rawStart0", tokens[0].RawStart == 0);
                Assert("plain Hebrew: rawStart1", tokens[1].RawStart == 5); // 4 Hebrew chars + 1 space
            }

            // Nikud stripping: normalized form drops nikud, rawStart points to original
            {
                var tokens = ts.Tokenize("שָׁלוֹם");
                Assert("nikud: count",      tokens.Count == 1);
                Assert("nikud: normalized", tokens[0].Normalized == "שלום");
                Assert("nikud: rawStart",   tokens[0].RawStart == 0);
                // rawEnd should be past the last nikud char
                Assert("nikud: rawEnd > 4", tokens[0].RawEnd > 4);
            }

            // HTML tags: inline tags are invisible, block tags act as separators
            {
                var tokens = ts.Tokenize("<b>תורה</b> <p>מצוה</p>");
                Assert("html inline: count",  tokens.Count == 2);
                Assert("html inline: word 0", tokens[0].Normalized == "תורה");
                Assert("html inline: word 1", tokens[1].Normalized == "מצוה");
            }

            // HTML tags: rawStart skips past the opening tag
            {
                var tokens = ts.Tokenize("<b>אמת</b>");
                Assert("html rawStart past tag", tokens.Count == 1);
                Assert("html rawStart value",    tokens[0].RawStart == 3); // past "<b>"
            }

            // ASCII lowercasing
            {
                var tokens = ts.Tokenize("Hello World");
                Assert("ascii lower: count",  tokens.Count == 2);
                Assert("ascii lower: word 0", tokens[0].Normalized == "hello");
                Assert("ascii lower: word 1", tokens[1].Normalized == "world");
            }

            // Maqaf acts as word separator
            {
                var tokens = ts.Tokenize("בית־ספר");
                Assert("maqaf: count",  tokens.Count == 2);
                Assert("maqaf: word 0", tokens[0].Normalized == "בית");
                Assert("maqaf: word 1", tokens[1].Normalized == "ספר");
            }

            // Single-char words are filtered (length <= 1)
            {
                var tokens = ts.Tokenize("א ב תורה");
                Assert("single char filtered", tokens.Count == 1);
                Assert("single char: kept",    tokens[0].Normalized == "תורה");
            }

            // Empty / null input
            {
                var tokens = ts.Tokenize("");
                Assert("empty input: count", tokens.Count == 0);
            }

            // &nbsp; acts as word separator
            {
                var tokens = ts.Tokenize("תורה&nbsp;מצוה");
                Assert("nbsp separator: count",  tokens.Count == 2);
                Assert("nbsp separator: word 0", tokens[0].Normalized == "תורה");
                Assert("nbsp separator: word 1", tokens[1].Normalized == "מצוה");
            }

            Console.WriteLine();
        }

        // ── ProximityWindow (tested via SnippetBuilder.Score) ────────

        private static void TestProximityWindow()
        {
            Console.WriteLine("── ProximityWindow ──────────────────────────");
            var sb = new SnippetBuilder(snippetLength: 9999); // large enough to never clip

            // Both terms adjacent — tight window
            {
                var r = sb.Build("אברהם יצחק", Terms("אברהם", "יצחק"));
                Assert("adjacent: IsMatch",      r.IsMatch);
                Assert("adjacent: score small",  r.Score < 20);
            }

            // Both terms far apart — large window
            {
                string far = "אברהם " + new string('א', 200) + " יצחק";
                var r = sb.Build(far, Terms("אברהם", "יצחק"));
                Assert("far apart: IsMatch",     r.IsMatch);
                Assert("far apart: score large", r.Score > 100);
            }

            // Adjacent score < far-apart score (tighter = better)
            {
                var rClose = sb.Build("תורה מצוה ישראל", Terms("תורה", "מצוה"));
                var rFar   = sb.Build("תורה " + new string('ב', 300) + " מצוה", Terms("תורה", "מצוה"));
                Assert("close < far score", rClose.Score < rFar.Score);
            }

            // Missing term — IsMatch false, Score = MaxValue
            {
                var r = sb.Build("תורה מצוה", Terms("תורה", "נעדר"));
                Assert("missing term: not match",    !r.IsMatch);
                Assert("missing term: max score",    r.Score == int.MaxValue);
            }

            // Single term — always a tight window
            {
                var r = sb.Build("הנה תורה הנה", Terms("תורה"));
                Assert("single term: IsMatch", r.IsMatch);
                Assert("single term: score",   r.Score < 10);
            }

            // Multiple occurrences — picks the tightest pair
            {
                // "תורה" appears twice; second occurrence is right next to "מצוה"
                string text = "תורה " + new string('ג', 200) + " תורה מצוה";
                var r = sb.Build(text, Terms("תורה", "מצוה"));
                Assert("multi-occurrence: IsMatch",      r.IsMatch);
                Assert("multi-occurrence: tight window", r.Score < 30);
            }

            Console.WriteLine();
        }

        // ── SnippetBuilder ───────────────────────────────────────────

        private static void TestSnippetBuilder()
        {
            Console.WriteLine("── SnippetBuilder ───────────────────────────");
            var sb = new SnippetBuilder(snippetLength: 60, contextMargin: 10);

            // Highlight appears in output
            {
                var r = sb.Build("תורה ומצוה", Terms("תורה"));
                Assert("highlight present", r.Html.Contains("<mark>"));
                Assert("highlight wraps term", r.Html.Contains("<mark>תורה</mark>"));
            }

            // Both terms highlighted — note: "ומצוה" with vav prefix tokenizes as one word,
            // so query term "מצוה" won't match it. Use a space-separated form instead.
            {
                var r = sb.Build("תורה מצוה", Terms("תורה", "מצוה"));
                Assert("both highlighted",
                    r.Html.Contains("<mark>תורה</mark>") &&
                    r.Html.Contains("<mark>מצוה</mark>"));
            }

            // Short content — no ellipsis
            {
                var r = new SnippetBuilder(snippetLength: 9999).Build("תורה מצוה", Terms("תורה"));
                Assert("short: no leading ellipsis",  !r.Html.StartsWith("…"));
                Assert("short: no trailing ellipsis", !r.Html.EndsWith("…"));
            }

            // Long content — ellipsis added when snippet is a sub-range
            {
                // 100 'א' chars + " תורה " + 100 'ב' chars = 207 chars total
                // snippetLength=40, contextMargin=5 → margin = min((40-5)/2, 5) = 5
                // window ~5 chars, expanded by 5 each side → ~15 chars, well under 207
                string longText = new string('א', 100) + " תורה " + new string('ב', 100);
                var r = new SnippetBuilder(snippetLength: 40, contextMargin: 5).Build(longText, Terms("תורה"));
                Assert("long: has ellipsis",          r.Html.Contains("…"));
                Assert("long: term highlighted",      r.Html.Contains("<mark>"));
            }

            // HTML tags pass through verbatim — not encoded
            {
                var r = sb.Build("<b>תורה</b> מצוה", Terms("תורה"));
                Assert("html tag preserved", r.Html.Contains("<b>"));
                Assert("html tag not encoded", !r.Html.Contains("&lt;b&gt;"));
            }

            // Nikud in source preserved in highlighted span
            {
                var r = sb.Build("שָׁלוֹם עוֹלָם", Terms("שלום"));
                Assert("nikud preserved in mark",
                    r.Html.Contains("<mark>") && r.Html.Contains("שָׁלוֹם"));
            }

            // No match — returns plain encoded content, IsMatch false
            {
                var r = sb.Build("תורה מצוה", Terms("נעדר"));
                Assert("no match: IsMatch false", !r.IsMatch);
                Assert("no match: no mark tag",   !r.Html.Contains("<mark>"));
            }

            // Snippet is centered on the best window, not always the start
            {
                // Term is near the end; with a short snippet the start should have ellipsis
                string text = new string('ד', 200) + " תורה";
                var r = new SnippetBuilder(snippetLength: 40, contextMargin: 5).Build(text, Terms("תורה"));
                Assert("window centered: leading ellipsis", r.Html.StartsWith("…"));
                Assert("window centered: term highlighted",  r.Html.Contains("<mark>"));
            }

            // Custom pre/post tags
            {
                var custom = new SnippetBuilder(preTag: "<em>", postTag: "</em>", snippetLength: 9999);
                var r = custom.Build("תורה מצוה", Terms("תורה"));
                Assert("custom tags: em present",   r.Html.Contains("<em>תורה</em>"));
                Assert("custom tags: no mark",      !r.Html.Contains("<mark>"));
            }

            Console.WriteLine();
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static IReadOnlyCollection<string> Terms(params string[] terms)
            => terms;

        private static void Assert(string name, bool condition)
        {
            if (condition)
            {
                Console.WriteLine($"  PASS  {name}");
                _pass++;
            }
            else
            {
                Console.WriteLine($"  FAIL  {name}  *** FAILED ***");
                _fail++;
            }
        }

        // Diagnostic helper — prints actual value alongside the assertion
        private static void AssertEq(string name, object actual, object expected)
        {
            bool ok = Equals(actual, expected);
            if (ok)
            {
                Console.WriteLine($"  PASS  {name}");
                _pass++;
            }
            else
            {
                Console.WriteLine($"  FAIL  {name}  expected={expected}  actual={actual}  *** FAILED ***");
                _fail++;
            }
        }
    }
}
