using FtsLib.SeforimDb;
using FtsLib.Snippets;
using FtsLib.Tokenization;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Diagnostic: runs a query, then for every result dumps the raw line content,
    /// the matched terms, and the snippet HTML side-by-side so snippet bugs are
    /// immediately visible.
    ///
    /// Usage:
    ///   FtsLibTest.exe snippetdiag [tier] "query"
    ///
    /// Example:
    ///   FtsLibTest.exe snippetdiag 500k "שישים~ גבורים~"
    /// </summary>
    internal static class SnippetDiag
    {
        private const int MaxResults = 50;

        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length < 3)
            {
                Console.WriteLine("Usage: FtsLibTest.exe snippetdiag [tier] \"query\"");
                return;
            }

            string tierLabel = args[1];
            string query     = string.Join(" ", args, 2, args.Length - 2);

            string label;
            try   { label = TestHelpers.ResolveTier(tierLabel).Label; }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string dbPath   = BuildTest.ResolveDbPath();
            string indexDir = TestHelpers.IndexDir(label);

            if (!Directory.Exists(indexDir) ||
                Directory.GetFiles(indexDir, "seg_*.dat").Length == 0)
            {
                Console.WriteLine($"No index found at: {indexDir}");
                Console.WriteLine($"Run 'build {label}' first.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"╔══ SNIPPET DIAG: \"{query}\"  [{label.ToUpper()}] ══");

            var index   = new SeforimIndex(indexDir, dbPath);
            var results = new List<SearchResult>();
            foreach (var r in index.Search(query))
            {
                results.Add(r);
                if (results.Count >= MaxResults) break;
            }

            Console.WriteLine($"║  {results.Count} result(s) (capped at {MaxResults})");
            Console.WriteLine("║");

            var tokenStream = new TokenStream();
            int bugs        = 0;

            for (int i = 0; i < results.Count; i++)
            {
                var r       = results[i];
                var snippet = index.GenerateSnippet(r);

                // Collect all matched terms flat
                var allTerms = new HashSet<string>(StringComparer.Ordinal);
                foreach (var g in r.MatchedGroups)
                    foreach (var t in g)
                        allTerms.Add(t);

                // Check: does the snippet HTML actually contain at least one matched term?
                // Tokenize the snippet (strip tags first) and see which terms appear.
                string snippetPlain = StripTags(snippet.Html);
                var    snippetTokens = tokenStream.Tokenize(snippetPlain);
                var    foundInSnippet = new HashSet<string>(StringComparer.Ordinal);
                foreach (var tok in snippetTokens)
                    if (allTerms.Contains(tok.Normalized))
                        foundInSnippet.Add(tok.Normalized);

                bool snippetHasMatch = foundInSnippet.Count > 0;
                bool markPresent     = snippet.Html.Contains("<mark>");

                // Also check: does the raw content actually contain the matched terms?
                var rawTokens = tokenStream.Tokenize(r.Content);
                var foundInRaw = new HashSet<string>(StringComparer.Ordinal);
                foreach (var tok in rawTokens)
                    if (allTerms.Contains(tok.Normalized))
                        foundInRaw.Add(tok.Normalized);

                // Classify the result
                string status;
                bool   isBug = false;

                if (!snippet.IsMatch)
                {
                    status = "NO-MATCH (false positive filtered)";
                }
                else if (!snippetHasMatch)
                {
                    status = "BUG: snippet window misses matched terms";
                    isBug  = true;
                    bugs++;
                }
                else if (!markPresent)
                {
                    status = "BUG: IsMatch=true but no <mark> in Html";
                    isBug  = true;
                    bugs++;
                }
                else
                {
                    status = "OK";
                }

                // Print everything for bugs and the first 5 OK results
                bool print = isBug || !snippet.IsMatch || i < 5;
                if (!print) continue;

                Console.WriteLine($"║  [{i + 1}] LineId={r.LineId}  Book={TestHelpers.Truncate(r.BookTitle, 30)}");
                Console.WriteLine($"║      Status  : {status}");
                Console.WriteLine($"║      IsMatch : {snippet.IsMatch}  Score={snippet.Score}  WordDist={snippet.WordDistance}");
                Console.WriteLine($"║      Terms   : [{string.Join(", ", allTerms)}]");
                Console.WriteLine($"║      InRaw   : [{string.Join(", ", foundInRaw)}]");
                Console.WriteLine($"║      InSnip  : [{string.Join(", ", foundInSnippet)}]");

                // Raw content — first 200 visible chars
                string rawPlain = StripTags(r.Content);
                Console.WriteLine($"║      Raw     : {TestHelpers.Truncate(rawPlain, 200)}");

                // Snippet HTML with marks visible
                Console.WriteLine($"║      Snippet : {TestHelpers.Truncate(snippet.Html, 300)}");

                // For bugs: dump the token list from the raw content so we can see
                // what the tokenizer actually produces
                if (isBug)
                {
                    Console.WriteLine($"║      Tokens  :");
                    var rawToks = tokenStream.Tokenize(r.Content);
                    foreach (var tok in rawToks)
                    {
                        string marker = allTerms.Contains(tok.Normalized) ? " ◄ MATCH" : "";
                        Console.WriteLine($"║        [{tok.RawStart,5}–{tok.RawEnd,5}] vis={tok.VisibleStart,4}  \"{tok.Normalized}\"{marker}");
                    }
                }

                Console.WriteLine("║");
            }

            // Summary
            int shown = 0;
            foreach (var r in results)
            {
                var s = index.GenerateSnippet(r);
                if (s.IsMatch) shown++;
            }

            Console.WriteLine($"║  Total results : {results.Count}");
            Console.WriteLine($"║  IsMatch=true  : {shown}");
            Console.WriteLine($"║  Bugs found    : {bugs}");
            Console.WriteLine("╚══ SNIPPET DIAG DONE ══");
            Console.WriteLine();
        }

        // ── Helpers ───────────────────────────────────────────────────

        private static string StripTags(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var  sb    = new StringBuilder(html.Length);
            bool inTag = false;
            foreach (char c in html)
            {
                if (c == '<') { inTag = true;  continue; }
                if (c == '>') { inTag = false; continue; }
                if (!inTag) sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
