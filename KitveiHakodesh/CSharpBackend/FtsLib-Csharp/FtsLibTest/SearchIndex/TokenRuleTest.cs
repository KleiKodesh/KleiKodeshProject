using FtsLib.Tokenization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Otzaria's word rule in the scanner (HtmlWordScanner via TokenStream / Tokenizer):
    /// one case set per rule, and the single-pass scanner checked against WordRule (Otzaria's
    /// functions on plain text) on generated strings. Hebrew is written as \u escapes.
    /// </summary>
    internal static class TokenRuleTest
    {
        static int _passed, _failed;

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine();
            Console.WriteLine("== TOKEN RULE TESTS ==");
            _passed = _failed = 0;

            const string A = "א", B = "ב", G = "ג", D = "ד", H = "ה", V = "ו";

            // rule 1: period, brackets, bidi / zero-width chars join; other separators split
            Words("period joins", A + "." + B + "." + G, A + B + G);
            Words("period after a word is outside it", A + B + ". " + G, A + B, G);
            Words("brackets join", A + "[" + B + "]" + G, A + B + G);
            Words("bidi and zero-width chars join", A + "‏" + B + "​" + G + "‫" + D, A + B + G + D);
            Words("* ~ ^ $ \\ + backtick join", A + "*" + B + "~" + G + "^$\\+`" + D, A + B + G + D);
            Words("separators still split", A + "," + B + "-" + G + "־" + D + "?" + H, A, B, G, D, H);
            Spans("period after a word: span ends before it", A + B + ". " + G, (0, 2), (4, 5));

            // rule 2: no Hebrew/Latin split, other scripts are word chars, Unicode lowercase
            Words("no script split", "abc" + D + H + " " + D + H + "ABC", "abc" + D + H, D + H + "abc");
            Words("other scripts lowercased", "CafÉ Αβ Жж", "café", "αβ", "жж");
            Words("combining accent continues, never starts", "éx ́y", "ex", "y");
            Words("supplementary letters", "𝐀𝐁 x", "𝐀𝐁", "x");

            // rule 3: quotes (terms are quote-free); a trailing geresh belongs to its word
            Words("inner geresh / gershayim", A + "'" + B + " " + A + "״" + B + " " + A + "''" + B, A + B, A + B, A + B);
            Words("a run of three quotes splits", A + "'\"" + B, A, B);
            Spans("trailing geresh is inside the span", A + B + "' " + G + D + "\" " + H, (0, 3), (4, 6), (8, 9));
            Spans("only the first geresh of a trailing run", A + B + ".''", (0, 4));

            // rule 4: typographic quotes and presentation forms fold
            Words("typographic quotes", A + "”" + B + " " + G + "’" + D + " " + A + "’’" + B, A + B, G + D, A + B);
            Spans("typographic trailing geresh", A + "’ " + B, (0, 2), (3, 4));
            Words("presentation forms", "שׂלום ﭏ בּ", "שלום", "אל", "ב");

            // rule 5: paired readings share a position
            Positions("(X) [Y] shares", H + " (" + A + B + ") [" + G + D + "] " + V, 0, 1, 1, 2);
            Positions("punctuation and tags inside the pair", "(" + A + ".) <b>[" + B + ":]</b> " + G, 0, 0, 1);
            Positions("no closing bracket", "(" + A + ") [" + B + " " + G, 0, 1, 2);
            Positions("a word between", "(" + A + ") " + G + " [" + B + "]", 0, 1, 2);
            Positions("at the end of the text", "(" + A + ") [" + B + "]", 0, 0);

            // rule 6: a term of 128 bytes or more is not indexed but keeps its position
            var tok = new Tokenizer();
            var terms = tok.Extract(new string('א', 63) + " " + new string('ב', 64) + " " + G);
            Check("63 letters indexed, 64 not", terms.Contains(new string('א', 63)) && !terms.Contains(new string('ב', 64)) && terms.Contains(G), string.Join(",", terms.Select(t => t.Length)));
            Positions("a long word keeps its position", new string('ב', 64) + " " + G, 0, 1);

            // the scanner equals WordRule (Otzaria's functions) on generated plain text
            Generated(20000);

            // FM index over an alphabet of more than 255 chars (every script is a word char)
            FmLargeAlphabet();

            // an unterminated '<' is a plain separator, the rest of the line is indexed (Otzaria)
            Words("unterminated < keeps the rest", A + " <" + B + " " + G, A, B, G);
            Words("< closed later is still a tag", A + "<b " + B + ">" + G, A + G);
            Words("< after the last > is plain", A + "<i>" + B + "</i> <" + G, A + B, G);

            // <sup> holding a vocalized letter mid-word is transparent (issue #2 H58); other sups break
            const string Q = "ָ", SH = "ְ";
            Words("sup letter+mark mid-word joins", A + Q + "<sup>" + B + SH + "</sup>" + G + " " + D, A + B + G, D);
            Spans("sup joined: span covers the whole word", A + Q + "<sup>" + B + SH + "</sup>" + G + " " + D, (0, 16), (17, 18));
            Words("SUP with attributes joins", A + "<SUP class=\"x\">" + B + SH + "</SUP>" + G, A + B + G);
            Words("sup digit marker breaks", A + Q + "<sup>1</sup>" + G, A, "1", G);
            Words("sup unmarked letter mid-word breaks", A + Q + "<sup>" + B + "</sup>" + G, A, B, G);
            Words("sup marker at word end breaks", A + "<sup>" + B + SH + "</sup> " + G, A, B, G);
            Words("sup with a tag inside breaks", A + "<sup><b>" + B + SH + "</b></sup>" + G, A, B, G);

            // fuzzy: a swap of two adjacent letters is ONE edit (Otzaria's transposition)
            Check("swap is one edit", FtsLib.Search.Levenshtein.Distance(A + B + G + D, B + A + G + D, 2) == 1, "");
            Check("swap at the end is one edit", FtsLib.Search.Levenshtein.Distance(A + B + G + D, A + B + D + G, 2) == 1, "");
            Check("two swaps are two edits", FtsLib.Search.Levenshtein.Distance(A + B + G + D, B + A + D + G, 2) == 2, "");
            Check("plain edits unchanged", FtsLib.Search.Levenshtein.Distance(A + B + G, A + G, 2) == 1
                  && FtsLib.Search.Levenshtein.Distance(A + B + G, A + D + G, 2) == 1
                  && FtsLib.Search.Levenshtein.Distance(A + B + G + D + H, D + G + H, 1) == 2, "");
            FmFuzzySwap(A + B + G + D, B + A + G + D, A + B + D + G, B + A + D + G);

            Console.WriteLine(_failed == 0 ? $"  All {_passed} tests passed" : $"  {_failed} FAILED / {_passed + _failed} total");
            Console.WriteLine("== TOKEN RULE TESTS DONE ==");
            if (_failed > 0) Environment.Exit(1);
        }

        static void Check(string name, bool ok, string detail)
        {
            if (ok) { _passed++; return; }
            _failed++;
            Console.WriteLine($"  FAIL {name}: {Esc(detail)}");
        }

        static string Esc(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(c < 128 ? c.ToString() : $"\\u{(int)c:x4}");
            return sb.ToString();
        }

        static List<TextToken> Tokens(string text) => new TokenStream().Tokenize(text).ToList();

        static void Words(string name, string text, params string[] expected)
        {
            var got = Tokens(text).Select(t => t.Normalized).ToArray();
            Check(name, got.SequenceEqual(expected), string.Join(" | ", got));
        }

        static void Spans(string name, string text, params (int, int)[] expected)
        {
            var got = Tokens(text).Select(t => (t.RawStart, t.RawEnd)).ToArray();
            Check(name, got.SequenceEqual(expected), string.Join(" ", got));
        }

        static void Positions(string name, string text, params int[] expected)
        {
            var got = Tokens(text).Select(t => t.Position).ToArray();
            Check(name, got.SequenceEqual(expected), string.Join(" ", got));
        }

        /// <summary>WordRule on plain text: (start, end, quote-free term, position).</summary>
        static List<(int, int, string, int)> Reference(string text)
        {
            var list = new List<(int, int, string, int)>();
            int pos = 0, count = 0;
            bool share = false;
            while (WordRule.NextTokenBoundaries(text, pos, false, out int s, out int e))
            {
                string norm = WordRule.Normalize(text.Substring(s, e - s), false, out _).Replace("'", "").Replace("\"", "");
                int position = share ? count - 1 : count++;
                share = WordRule.PairedReadingAfter(text, s, e, out _, out _, out _);
                list.Add((s, e, norm, position));
                pos = e;
            }
            return list;
        }

        /// <summary>FM fuzzy finds a one-swap term at distance 1, a two-swap term only at 2,
        /// and agrees with Levenshtein.Distance on every term.</summary>
        static void FmFuzzySwap(string query, string swapFirst, string swapLast, string twoSwaps)
        {
            var sorted = new List<string> { query, swapFirst, swapLast, twoSwaps, query + "x", "zz" + query }
                .Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList();
            var rowIds = Enumerable.Range(1, sorted.Count).ToList();
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fm_swap_" + Guid.NewGuid().ToString("N") + ".fmi");
            try
            {
                FtsLib.Fts5.FmIndex.Build(path, sorted, rowIds);
                using var fm = new FtsLib.Fts5.FmIndex.Reader(path);
                bool ok = true;
                for (int d = 1; d <= 2; d++)
                {
                    var got = fm.FindFuzzy(query, d).Select(h => (sorted[h.RowId - 1], h.Distance)).OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
                    var want = sorted.Select(s => (s, FtsLib.Search.Levenshtein.Distance(query, s, d))).Where(x => x.Item2 <= d).ToList();
                    ok &= got.SequenceEqual(want);
                }
                var d1 = fm.FindFuzzy(query, 1).Select(h => sorted[h.RowId - 1]).ToList();
                ok &= d1.Contains(swapFirst) && d1.Contains(swapLast) && !d1.Contains(twoSwaps);
                Check("FM fuzzy: a swap is one edit", ok, "");
            }
            finally
            {
                try { System.IO.File.Delete(path); } catch { }
            }
        }

        static void FmLargeAlphabet()
        {
            var terms = new List<string>();
            var ids = new List<int>();
            for (int i = 0; i < 400; i++)
            {
                char a = (char)(0x0400 + i), b = (char)(0x0400 + (i * 7) % 400);
                terms.Add($"{a}{b}{a}x{i}");
                ids.Add(i + 1);
            }
            // the dictionary is in ordinal order
            var order = Enumerable.Range(0, terms.Count).OrderBy(i => terms[i], StringComparer.Ordinal).ToList();
            var sorted = order.Select(i => terms[i]).ToList();
            var rowIds = Enumerable.Range(1, sorted.Count).ToList();
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fm_large_alphabet_" + Guid.NewGuid().ToString("N") + ".fmi");
            try
            {
                FtsLib.Fts5.FmIndex.Build(path, sorted, rowIds);
                using var fm = new FtsLib.Fts5.FmIndex.Reader(path);
                bool ok = true;
                for (int k = 0; k < sorted.Count && ok; k++)
                {
                    string infix = sorted[k].Substring(1, 2);
                    var want = Enumerable.Range(0, sorted.Count).Where(r => sorted[r].Contains(infix)).Select(r => r + 1).ToArray();
                    var got = fm.FindRowIds(infix, false).OrderBy(x => x).ToArray();
                    ok = got.SequenceEqual(want) && fm.FindRowIds(sorted[k], true).Contains(k + 1);
                }
                Check("FM index over 400+ chars", ok, "");
            }
            finally
            {
                try { System.IO.File.Delete(path); } catch { }
            }
        }

        static void Generated(int count)
        {
            string[] pieces =
            {
                "א", "ב", "ת", "ך", "a", "Z", "1", "É", "ж", "٣", "𝐀", "İ",
                "ְ", "ִ", "֑", "ּ", "ׁ", "ׇ", "׀", "׃", "׆", "־",
                "́", "ﬞ", "שׂ", "ﭏ", "בּ", "\"", "'", "׳", "״", "‘", "’",
                "“", "”", ".", "[", "]", "(", ")", "*", "~", "^", "\\", "`", "‏", "​", "﻿", "‫",
                " ", "  ", "\t", "\n", ",", ":", "?", "-", " ", "(אב) [גד]", ") [",
                "אבגדהוז",
            };
            var rng = new Random(12345);
            int bad = 0;
            for (int i = 0; i < count && bad < 5; i++)
            {
                var sb = new StringBuilder();
                int n = rng.Next(40);
                for (int k = 0; k < n; k++) sb.Append(pieces[rng.Next(pieces.Length)]);
                string text = sb.ToString();
                var got = Tokens(text).Select(t => (t.RawStart, t.RawEnd, t.Normalized, t.Position)).ToList();
                var want = Reference(text);
                if (!got.SequenceEqual(want))
                {
                    bad++;
                    Check("generated " + Esc(text), false,
                          "got " + string.Join(" ", got.Select(x => $"[{x.Item1},{x.Item2},{x.Item3},{x.Item4}]")) +
                          " want " + string.Join(" ", want.Select(x => $"[{x.Item1},{x.Item2},{x.Item3},{x.Item4}]")));
                }
            }
            Check($"scanner equals WordRule on {count} generated strings", bad == 0, "");
        }
    }
}
