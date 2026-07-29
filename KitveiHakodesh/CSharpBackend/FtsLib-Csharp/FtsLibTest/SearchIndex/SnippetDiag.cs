using FtsLib.SeforimDb;
using FtsLib.Tokenization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Diagnostic: runs a query, then for every result dumps the raw line content,
    /// the matched terms, and the snippet HTML side-by-side so snippet bugs are
    /// immediately visible.
    ///
    /// Also prints a word-distance histogram and shows which results would be
    /// filtered at various maxWordDistance thresholds.
    ///
    /// Usage:
    ///   FtsLibTest.exe snippetdiag [tier] "query"
    ///
    /// Example:
    ///   FtsLibTest.exe snippetdiag 500k "שישים~ גבורים~"
    /// </summary>
    internal static class SnippetDiag
    {
        private const int MaxResults = 201;

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

            // Collect all snippets for histogram
            var snippets = new List<(SearchResult result, FtsLib.SeforimDb.SnippetResult snippet)>(results.Count);
            foreach (var r in results)
                snippets.Add((r, index.GenerateSnippet(r)));

            // ── Per-result detail (bugs + first 5 OK) ────────────────
            for (int i = 0; i < snippets.Count; i++)
            {
                var (r, snippet) = snippets[i];

                var allTerms = new HashSet<string>(StringComparer.Ordinal);
                foreach (var g in r.MatchedGroups)
                    foreach (var t in g)
                        allTerms.Add(t);

                string snippetPlain  = StripTags(snippet.Html);
                var    snippetTokens = tokenStream.Tokenize(snippetPlain);
                var    foundInSnippet = new HashSet<string>(StringComparer.Ordinal);
                foreach (var tok in snippetTokens)
                    if (allTerms.Contains(tok.Normalized))
                        foundInSnippet.Add(tok.Normalized);

                bool snippetHasMatch = foundInSnippet.Count > 0;
                bool markPresent     = snippet.Html.Contains("<mark>");

                var rawTokens  = tokenStream.Tokenize(r.Content);
                var foundInRaw = new HashSet<string>(StringComparer.Ordinal);
                foreach (var tok in rawTokens)
                    if (allTerms.Contains(tok.Normalized))
                        foundInRaw.Add(tok.Normalized);

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

                bool print = isBug || !snippet.IsMatch || i < 5;
                if (!print) continue;

                Console.WriteLine($"║  [{i + 1}] LineId={r.LineId}  Book={TestHelpers.Truncate(r.BookTitle, 30)}");
                Console.WriteLine($"║      Status  : {status}");
                Console.WriteLine($"║      IsMatch : {snippet.IsMatch}  Score={snippet.Score}  WordDist={snippet.WordDistance}");
                Console.WriteLine($"║      Terms   : [{string.Join(", ", allTerms)}]");
                Console.WriteLine($"║      InRaw   : [{string.Join(", ", foundInRaw)}]");
                Console.WriteLine($"║      InSnip  : [{string.Join(", ", foundInSnippet)}]");
                Console.WriteLine($"║      Raw     : {TestHelpers.Truncate(StripTags(r.Content), 200)}");
                Console.WriteLine($"║      Snippet : {TestHelpers.Truncate(snippet.Html, 300)}");

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

            // ── Word-distance histogram ───────────────────────────────
            Console.WriteLine("║");
            Console.WriteLine("║  ── Word-distance distribution ──────────────────────────");

            var distBuckets = new SortedDictionary<int, int>();
            int noMatchCount = 0;
            foreach (var (_, snippet) in snippets)
            {
                if (!snippet.IsMatch) { noMatchCount++; continue; }
                int bucket = snippet.WordDistance <= 0  ? 0
                           : snippet.WordDistance <= 2  ? 2
                           : snippet.WordDistance <= 5  ? 5
                           : snippet.WordDistance <= 10 ? 10
                           : snippet.WordDistance <= 20 ? 20
                           : snippet.WordDistance <= 50 ? 50
                           : 999;
                if (!distBuckets.ContainsKey(bucket)) distBuckets[bucket] = 0;
                distBuckets[bucket]++;
            }

            string BucketLabel(int b) => b == 0   ? "dist=0 (adjacent)"
                                       : b == 2   ? "dist 1–2"
                                       : b == 5   ? "dist 3–5"
                                       : b == 10  ? "dist 6–10"
                                       : b == 20  ? "dist 11–20"
                                       : b == 50  ? "dist 21–50"
                                       : "dist >50";

            int matchTotal = snippets.Count - noMatchCount;
            foreach (var kv in distBuckets)
            {
                int pct = matchTotal > 0 ? kv.Value * 100 / matchTotal : 0;
                Console.WriteLine($"║    {BucketLabel(kv.Key),-22}  {kv.Value,4} results  ({pct,3}%)");
            }
            if (noMatchCount > 0)
                Console.WriteLine($"║    {"no match (filtered)",-22}  {noMatchCount,4} results");

            // ── maxWordDistance filter simulation ─────────────────────
            Console.WriteLine("║");
            Console.WriteLine("║  ── maxWordDistance filter simulation ───────────────────");
            int[] thresholds = { 0, 2, 5, 10, 20, 50 };
            foreach (int threshold in thresholds)
            {
                int kept = 0;
                foreach (var (_, snippet) in snippets)
                    if (snippet.IsMatch && snippet.WordDistance <= threshold)
                        kept++;
                int filteredOut = matchTotal - kept;
                Console.WriteLine($"║    maxWordDistance={threshold,-3}  keeps {kept,4}/{matchTotal}  (filters {filteredOut})");
            }

            // ── Summary ───────────────────────────────────────────────
            Console.WriteLine("║");
            Console.WriteLine($"║  Total results : {results.Count}");
            Console.WriteLine($"║  IsMatch=true  : {matchTotal}");
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
