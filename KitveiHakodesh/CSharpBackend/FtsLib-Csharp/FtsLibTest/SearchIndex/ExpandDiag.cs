using FtsLib.Indexing;
using FtsLib.Search;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Shows exactly what fuzzy/wildcard expansion produces for a given term:
    /// candidate count from trigram/bigram scan, final term count after
    /// Levenshtein filter, and the actual expanded terms.
    ///
    /// Usage:
    ///   FtsLibTest.exe expand [tier] term [maxDist]
    ///   FtsLibTest.exe expand 500k ישראל 1
    ///   FtsLibTest.exe expand 500k ישראל 2
    ///   FtsLibTest.exe expand 500k תורה 1
    ///   FtsLibTest.exe expand 500k אנב 1
    /// </summary>
    internal static class ExpandDiag
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: FtsLibTest.exe expand [tier] term [maxDist]");
                return;
            }

            string label   = TestHelpers.ResolveTier(args[1]).Label;
            string term    = args[2];
            int    maxDist = args.Length > 3 && int.TryParse(args[3], out int d) ? d : 1;
            string indexDir = TestHelpers.IndexDir(label);

            if (!Directory.Exists(indexDir))
            { Console.WriteLine($"No index at: {indexDir}"); return; }

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
                int n = term.Length >= 4 ? 3 : term.Length == 3 ? 2 : 1;
                var ngrams = FuzzyExpander.BuildNgrams(term, n);

                Console.WriteLine($"Term     : {term}");
                Console.WriteLine($"MaxDist  : {maxDist}");
                Console.WriteLine($"N-gram n : {n}  →  [{string.Join(", ", ngrams)}]");
                Console.WriteLine();

                // Phase 1: trigram/bigram candidates
                var candidates = new System.Collections.Generic.HashSet<string>(
                    System.StringComparer.Ordinal);
                foreach (var seg in segments)
                {
                    var sb = new StringBuilder("SELECT term FROM term_index WHERE ");
                    for (int i = 0; i < ngrams.Count; i++)
                    {
                        if (i > 0) sb.Append(" OR ");
                        sb.Append("term LIKE @t").Append(i).Append(" ESCAPE '\\'");
                    }
                    using (var cmd = seg.Conn.CreateCommand())
                    {
                        cmd.CommandText = sb.ToString();
                        for (int i = 0; i < ngrams.Count; i++)
                        {
                            string esc = ngrams[i].Replace("\\","\\\\")
                                                   .Replace("%","\\%")
                                                   .Replace("_","\\_");
                            cmd.Parameters.Add($"@t{i}", System.Data.DbType.String).Value
                                = "%" + esc + "%";
                        }
                        using (var r = cmd.ExecuteReader())
                            while (r.Read()) candidates.Add(r.GetString(0));
                    }
                }

                Console.WriteLine($"Phase 1 (n-gram scan)  : {candidates.Count:N0} candidates");

                // Phase 2: Levenshtein filter
                var expanded = new List<string>();
                foreach (var c in candidates)
                    if (Levenshtein.Distance(term, c, maxDist) <= maxDist)
                        expanded.Add(c);

                expanded.Sort(System.StringComparer.Ordinal);
                Console.WriteLine($"Phase 2 (Levenshtein)  : {expanded.Count} terms after filter");
                Console.WriteLine($"Filter ratio           : {(double)expanded.Count/candidates.Count:P1} of candidates kept");
                Console.WriteLine();

                // Show all expanded terms
                Console.WriteLine("Expanded terms:");
                foreach (var t in expanded)
                {
                    int lev = Levenshtein.Distance(term, t, maxDist);
                    Console.WriteLine($"  dist={lev}  {t}");
                }

                // Show result count
                Console.WriteLine();
                using (var reader = new IndexReader(indexDir))
                {
                    var groups = new List<System.Collections.Generic.IEnumerable<string>>
                        { expanded };
                    int count = 0;
                    foreach (var _ in reader.Search(groups)) count++;
                    Console.WriteLine($"Result count (OR of all expanded terms): {count:N0}");
                }
            }
            finally
            {
                foreach (var s in segments) s.Dispose();
            }
        }
    }
}
