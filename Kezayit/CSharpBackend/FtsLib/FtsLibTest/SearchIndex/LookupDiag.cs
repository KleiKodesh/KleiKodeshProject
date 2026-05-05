using FtsLib.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Directly tests whether IndexReader can find a specific term and return its doc IDs.
    /// Usage: FtsLibTest.exe lookup [tier] term
    /// </summary>
    internal static class LookupDiag
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 3) { Console.WriteLine("Usage: lookup [tier] term"); return; }

            string label    = TestHelpers.ResolveTier(args[1]).Label;
            string term     = args[2];
            string indexDir = TestHelpers.IndexDir(label);

            Console.WriteLine($"Term     : [{term}]  ({term.Length} chars)");
            Console.WriteLine($"UTF8 hex : {BytesToHex(Encoding.UTF8.GetBytes(term))}");
            Console.WriteLine($"IndexDir : {indexDir}");
            Console.WriteLine();

            // Step 1: check each segment's term_index directly
            var datFiles = Directory.GetFiles(indexDir, "seg_*.dat");
            Array.Sort(datFiles);

            foreach (var dat in datFiles)
            {
                string db = Path.ChangeExtension(dat, ".db");
                if (!File.Exists(db)) continue;

                Console.WriteLine($"Segment: {Path.GetFileName(dat)}");
                using (var seg = new SegmentHandle(dat, db))
                {
                    // Direct lookup via prepared statement
                    seg.Lookup.Parameters["@t"].Value = term;
                    using (var r = seg.Lookup.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            long offset = r.GetInt64(0);
                            int  length = r.GetInt32(1);
                            int  count  = r.GetInt32(2);
                            Console.WriteLine($"  Found: offset={offset} length={length} count={count}");

                            // Read and decode the posting data
                            var buf = new byte[length];
                            int totalRead = 0;
                            seg.DataStream.Seek(offset, SeekOrigin.Begin);
                            while (totalRead < length)
                            {
                                int n = seg.DataStream.Read(buf, totalRead, length - totalRead);
                                if (n == 0) break;
                                totalRead += n;
                            }
                            Console.WriteLine($"  Bytes ({totalRead}/{length}): {BytesToHex(buf)}");

                            // Decode via PostingIterator
                            var iter = new PostingIterator(buf, totalRead, null, 0);
                            var ids  = new List<int>();
                            while (iter.MoveNext()) ids.Add(iter.Current);
                            Console.WriteLine($"  Doc IDs: [{string.Join(", ", ids)}]");
                        }
                        else
                        {
                            Console.WriteLine("  NOT FOUND in term_index");
                        }
                    }

                    // Also try raw SQL to rule out prepared statement issues
                    using (var cmd = seg.Conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM term_index WHERE term = @t";
                        cmd.Parameters.Add("@t", System.Data.DbType.String).Value = term;
                        long cnt = (long)cmd.ExecuteScalar();
                        Console.WriteLine($"  Raw SQL count: {cnt}");
                    }
                }
                Console.WriteLine();
            }

            // Step 2: use IndexReader
            Console.WriteLine("IndexReader.Search result:");
            using (var reader = new IndexReader(indexDir))
            {
                int count = 0;
                foreach (var id in reader.Search(new[] { term }))
                {
                    Console.WriteLine($"  id={id}");
                    count++;
                    if (count >= 10) { Console.WriteLine("  (truncated)"); break; }
                }
                if (count == 0) Console.WriteLine("  (no results)");
            }
        }

        private static string BytesToHex(byte[] b)
            => string.Join(" ", Array.ConvertAll(b, x => x.ToString("X2")));
    }
}
