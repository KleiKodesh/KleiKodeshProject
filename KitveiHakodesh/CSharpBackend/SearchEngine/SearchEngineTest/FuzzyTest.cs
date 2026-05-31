using System;
using System.Collections.Generic;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using SearchEngine.Indexing;
using SearchEngine.Search;
using SearchEngine.SeforimDb;
using SearchEngine.Tokenization;

namespace SearchEngineTest
{
    /// <summary>
    /// Exercises fuzzy query support in <see cref="HebrewQueryBuilder"/>.
    ///
    /// Usage (via Program.cs):
    ///   LuceneTest diag fuzzy &lt;query&gt; [dbPath]
    ///
    /// What it checks:
    ///   1. Query parse — prints the Lucene query object so you can confirm
    ///      FuzzyQuery nodes appear with the right maxEdits.
    ///   2. Hit count — total matching documents in the index.
    ///   3. Sample results — first N row IDs with their content from the DB,
    ///      so you can visually verify the fuzzy neighbours are sensible.
    ///   4. Edit-distance spot-check — for each unique term matched by the
    ///      FuzzyQuery, prints the Levenshtein distance from the query term
    ///      so you can confirm the distance cap is respected.
    /// </summary>
    internal static class FuzzyTest
    {
        public static void Run(string indexDir, string queryText,
                               string dbPath = null, int maxShow = 20)
        {
            Console.WriteLine($"=== FUZZY TEST: {queryText} ===");

            if (!System.IO.Directory.Exists(indexDir))
            {
                Console.WriteLine("Index not found — run build first.");
                return;
            }

            // ── 1. Parse ──────────────────────────────────────────────
            Query query;
            using (var analyzer = new HebrewAnalyzer())
                query = HebrewQueryBuilder.Build(queryText, analyzer);

            if (query == null)
            {
                Console.WriteLine("  (empty / invalid query — nothing to search)");
                return;
            }
            Console.WriteLine($"  Parsed query type : {query.GetType().Name}");
            Console.WriteLine($"  Parsed query      : {query}");
            Console.WriteLine();

            // ── 2. Hit count + sample results ─────────────────────────
            using (var dir      = FSDirectory.Open(indexDir))
            using (var reader   = DirectoryReader.Open(dir))
            using (var db       = new ZayitDb(dbPath))
            {
                var searcher = new IndexSearcher(reader);

                var counter = new TotalHitCountCollector();
                searcher.Search(query, counter);
                int total = counter.TotalHits;
                Console.WriteLine($"  Total hits: {total:N0}");

                if (total == 0)
                {
                    Console.WriteLine("  No results.");
                    return;
                }

                int fetch = Math.Min(maxShow, total);
                TopDocs top = searcher.Search(query, fetch);

                Console.WriteLine($"  Showing first {fetch} results:");
                Console.WriteLine();

                for (int i = 0; i < top.ScoreDocs.Length; i++)
                {
                    var doc   = searcher.Doc(top.ScoreDocs[i].Doc);
                    var field = doc.GetField(LuceneIndexWriter.FieldRowId);
                    if (field == null) continue;

                    int    rowId   = field.GetInt32Value().Value;
                    string content = db.IsOpen ? (db.GetLineById(rowId) ?? "(not found)") : "(db not open)";

                    // Truncate long lines for readability
                    string display = content.Length > 120
                        ? content.Substring(0, 120) + "…"
                        : content;

                    Console.WriteLine($"  [{i + 1}] rowId={rowId}");
                    Console.WriteLine($"       {display}");
                }

                // ── 3. Edit-distance spot-check ───────────────────────
                // Extract the base query term(s) from the raw query text so we can
                // compute distances. We strip the '~N' suffix and normalise.
                var baseTerms = ExtractBaseTerms(queryText);
                if (baseTerms.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("  Edit-distance check (matched terms vs query term):");

                    // Collect the unique terms that the rewritten FuzzyQuery matched.
                    Query rewritten = query.Rewrite(reader);
                    var matchedTerms = new HashSet<string>(StringComparer.Ordinal);
                    CollectTerms(rewritten, matchedTerms);

                    foreach (var baseTerm in baseTerms)
                    {
                        Console.WriteLine($"    Base term: \"{baseTerm}\"");
                        foreach (var matched in matchedTerms)
                        {
                            int dist = Levenshtein(baseTerm, matched);
                            Console.WriteLine($"      \"{matched}\"  dist={dist}");
                        }
                    }
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Extracts the normalised base term(s) from a fuzzy query string by
        /// stripping the trailing '~' / '~N' suffix and any leading markers.
        /// </summary>
        private static List<string> ExtractBaseTerms(string queryText)
        {
            var result = new List<string>();
            foreach (var raw in queryText.Split(new[] { ' ', '\t' },
                                                StringSplitOptions.RemoveEmptyEntries))
            {
                string token = raw;
                // Strip leading ~ (ketiv) and %
                while (token.Length > 0 && (token[0] == '~' || token[0] == '%'))
                    token = token.Substring(1);
                while (token.Length > 0 && token[token.Length - 1] == '%')
                    token = token.Substring(0, token.Length - 1);

                // Strip trailing ~ or ~N
                int tilde = token.LastIndexOf('~');
                if (tilde >= 0)
                {
                    string suffix = token.Substring(tilde + 1);
                    bool valid = suffix.Length == 0
                              || (suffix.Length == 1 && suffix[0] >= '1' && suffix[0] <= '9');
                    if (valid) token = token.Substring(0, tilde);
                }

                // Normalise: strip nikud, keep Hebrew + ASCII letters
                var sb = new System.Text.StringBuilder(token.Length);
                foreach (char c in token)
                {
                    if (c >= '\u0591' && c <= '\u05C7') continue;
                    if (c >= '\u05D0' && c <= '\u05EA') { sb.Append(c); continue; }
                    if (c >= 'a' && c <= 'z') { sb.Append(c); continue; }
                    if (c >= 'A' && c <= 'Z') { sb.Append((char)(c | 32)); continue; }
                }
                string norm = sb.ToString();
                if (norm.Length >= HebrewQueryBuilder.MinFuzzyTermLength)
                    result.Add(norm);
            }
            return result;
        }

        /// <summary>
        /// Recursively collects all leaf terms from a (possibly rewritten) query.
        /// </summary>
        private static void CollectTerms(Query q, HashSet<string> terms)
        {
            if (q is TermQuery tq)
            {
                terms.Add(tq.Term.Text);
                return;
            }
            if (q is BooleanQuery bq)
            {
                foreach (var clause in bq.Clauses)
                    CollectTerms(clause.Query, terms);
                return;
            }
            // For other query types (ConstantScoreQuery wrapping a TermsQuery after
            // FuzzyQuery rewrite), extract via ExtractTerms.
            var extracted = new HashSet<Lucene.Net.Index.Term>();
            try { q.ExtractTerms(extracted); } catch { }
            foreach (var t in extracted)
                terms.Add(t.Text);
        }

        /// <summary>Simple Levenshtein distance for the spot-check display.</summary>
        private static int Levenshtein(string a, string b)
        {
            if (a == b) return 0;
            if (a.Length == 0) return b.Length;
            if (b.Length == 0) return a.Length;

            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }

            return d[a.Length, b.Length];
        }
    }
}
