using FtsLib.Search;
using FtsLib.Snippets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Quoted phrases (Otzaria's smartSearchQuotedPhrases rule): the parser's phrase groups
    /// and literal words, and the line check the engine runs on each candidate.
    /// In-memory only.
    ///
    /// Usage:
    ///   FtsLibTest.exe phrasetest
    /// </summary>
    internal static class QuotedPhraseTest
    {
        const string A = "אבג", B = "דהו", C = "זחט", F = "יכל";
        const string Acronym = "רמב\"ם";

        static int _passed, _failed;

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            _passed = _failed = 0;

            // ── Parser ──
            var whole = QueryParser.Parse("\"" + A + " " + B + "\"");
            Expect("whole phrase: two groups", whole.Groups.Count == 2);
            Expect("whole phrase: flagged", whole.IsWholePhrase);
            Expect("whole phrase: one phrase of groups 0,1", Phrases(whole) == "0,1");

            var mixed = QueryParser.Parse(F + " ״" + A + " " + B + "״ " + C);
            Expect("mixed: four groups", mixed.Groups.Count == 4);
            Expect("mixed: not whole", !mixed.IsWholePhrase);
            Expect("mixed: phrase of groups 1,2", Phrases(mixed) == "1,2");

            var typographic = QueryParser.Parse("„" + A + " " + B + "“ " + C);
            Expect("typographic quotes delimit", Phrases(typographic) == "0,1");

            var acronym = QueryParser.Parse(Acronym + " " + A);
            Expect("acronym gershayim quotes nothing", acronym.Phrases.Count == 0 && !acronym.IsWholePhrase);
            var acronymMarked = QueryParser.Parse("רָמבּ\"ם " + A);
            Expect("acronym with nikud quotes nothing", acronymMarked.Phrases.Count == 0);
            var acronymInside = QueryParser.Parse("\"" + Acronym + " " + A + "\" " + B);
            Expect("acronym inside a phrase", Phrases(acronymInside) == "0,1" && !acronymInside.IsWholePhrase);

            var unclosed = QueryParser.Parse("\"" + A + " " + B);
            Expect("unclosed quote ignored", unclosed.Phrases.Count == 0 && !unclosed.IsWholePhrase);

            var single = QueryParser.Parse("\"" + A + "\" " + B);
            Expect("one-word phrase: no phrase constraint", single.Phrases.Count == 0 && !single.IsWholePhrase);

            var literal = QueryParser.Parse("\"" + A + "* %" + B + " " + C + "~\"");
            Expect("phrase words are literal",
                literal.Groups.Count == 3
                && literal.Groups.All(g => !g.IsWildcard && !g.IsFuzzy && !g.IsGrammar)
                && literal.Groups[0].Pattern == A && literal.Groups[1].Pattern == B && literal.Groups[2].Pattern == C);
            var outside = QueryParser.Parse(A + "* \"" + B + " " + C + "\"");
            Expect("operators outside the phrase kept", outside.Groups[0].IsWildcard && Phrases(outside) == "1,2");

            // ── Line check ──
            var phrase = PreparedQueryGroups.FromGroups(new List<IReadOnlyCollection<string>> { new[] { A }, new[] { B } });
            var locator = new SnippetWindowLocator();
            Expect("adjacent in order", locator.ContainsPhrase(C + " " + A + " " + B + " " + F, phrase));
            Expect("reversed is not the phrase", !locator.ContainsPhrase(B + " " + A + " " + C, phrase));
            Expect("a word between is not the phrase", !locator.ContainsPhrase(A + " " + C + " " + B, phrase));
            Expect("a prefixed word is not the phrase", !locator.ContainsPhrase(F + A + " " + B, phrase));
            Expect("markup between is transparent", locator.ContainsPhrase("<b>" + A + "</b> " + B, phrase));

            Console.WriteLine();
            Console.WriteLine($"Quoted phrase tests: {_passed} passed, {_failed} failed");
            if (_failed > 0) Environment.ExitCode = 1;
        }

        static string Phrases(ParsedQuery q)
            => string.Join(";", q.Phrases.Select(p => string.Join(",", p)));

        static void Expect(string name, bool ok)
        {
            if (ok) _passed++;
            else _failed++;
            Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + name);
        }
    }
}
