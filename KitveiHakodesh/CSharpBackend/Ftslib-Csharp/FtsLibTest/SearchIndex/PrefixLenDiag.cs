using FtsLib.Indexing;
using FtsLib.Search;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Shows the distribution of wildcard-portion lengths for a given suffix/prefix/infix
    /// pattern across all segments.  Used to decide what MaxWildcardChars should be.
    ///
    /// Usage:
    ///   FtsLibTest.exe prefixlen [tier] pattern
    ///
    /// Examples:
    ///   FtsLibTest.exe prefixlen full "*ישראל"
    ///   FtsLibTest.exe prefixlen full "תורה*"
    ///   FtsLibTest.exe prefixlen full "*שמ*"
    /// </summary>
    internal static class PrefixLenDiag
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: FtsLibTest.exe prefixlen [tier] pattern");
                return;
            }

            string label    = TestHelpers.ResolveTier(args[1]).Label;
            string pattern  = args[2];
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
                int anchorLen = HebrewWildcardExpander.AnchorLength(pattern);
                string like   = HebrewWildcardExpander.ToLikePattern(pattern);

                Console.WriteLine($"Pattern    : {pattern}");
                Console.WriteLine($"LIKE       : {like}");
                Console.WriteLine($"Anchor len : {anchorLen}");
                Console.WriteLine();

                // Collect all matching terms across all segments
                var all = new System.Collections.Generic.HashSet<string>(
                    StringComparer.Ordinal);
                foreach (var seg in segments)
                {
                    using (var cmd = seg.Conn.CreateCommand())
                    {
                        cmd.CommandText =
                            "SELECT term FROM term_index WHERE term LIKE @p ESCAPE '\\'";
                        cmd.Parameters.Add("@p", System.Data.DbType.String).Value = like;
                        using (var r = cmd.ExecuteReader())
                            while (r.Read()) all.Add(r.GetString(0));
                    }
                }

                // Group by wildcard-portion length
                var byLen = new SortedDictionary<int, List<string>>();
                foreach (var term in all)
                {
                    int wlen = term.Length - anchorLen;
                    if (!byLen.ContainsKey(wlen)) byLen[wlen] = new List<string>();
                    byLen[wlen].Add(term);
                }

                Console.WriteLine($"{"WC len",-8} {"Count",-8}  Terms");
                Console.WriteLine(new string('─', 72));
                foreach (var kv in byLen)
                {
                    kv.Value.Sort(StringComparer.Ordinal);
                    string sample = string.Join(", ", kv.Value.Count <= 8
                        ? kv.Value
                        : kv.Value.GetRange(0, 8));
                    string more = kv.Value.Count > 8 ? $"  … +{kv.Value.Count - 8}" : "";
                    Console.WriteLine($"{kv.Key,-8} {kv.Value.Count,-8}  {sample}{more}");
                }

                Console.WriteLine();
                Console.WriteLine($"Total: {all.Count} terms");
            }
            finally
            {
                foreach (var s in segments) s.Dispose();
            }
        }
    }
}
