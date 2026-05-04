using FtsLib.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FtsLibTest
{
    /// <summary>
    /// Usage:
    ///   FtsLibTest.exe                        — run all tiers, write results to test_results.txt
    ///   FtsLibTest.exe 500k                   — run 500k tier only
    ///   FtsLibTest.exe 1m                     — run 1M tier only
    ///   FtsLibTest.exe 3m                     — run 3M tier only
    ///   FtsLibTest.exe full                   — run full DB tier only
    ///   FtsLibTest.exe validate [dir]         — validate existing index
    ///   FtsLibTest.exe search &lt;dir&gt; &lt;terms...&gt; — search an existing index
    /// </summary>
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            string resultsFile = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "test_results.txt");

            if (args.Length > 0)
            {
                string cmd = args[0].ToLowerInvariant();

                if (cmd == "validate")
                {
                    string dir = args.Length > 1
                        ? args[1]
                        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index_test_500k");
                    IndexTest.Validate(dir);
                    return;
                }

                if (cmd == "build")
                {
                    // build <dir> [lineLimit]
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: build <indexDir> [lineLimit]");
                        return;
                    }
                    string buildDir   = args[1];
                    int    buildLimit = args.Length > 2 ? int.Parse(args[2]) : 0;
                    Console.WriteLine($"Building index → {buildDir}  limit={(buildLimit == 0 ? "all" : buildLimit.ToString("N0"))}");
                    var swBuild = Stopwatch.StartNew();
                    long linesBuilt = 0;
                    using (var db     = new FtsLib.Misc.ZayitDb(""))
                    using (var writer = new FtsLib.Core.IndexWriter(buildDir))
                    {
                        var tok = new FtsLib.Tokenizer();
                        foreach (var row in db.ReadLines(buildLimit))
                        {
                            foreach (var token in tok.Extract(row.Content))
                                writer.Add(row.Id, token);
                            linesBuilt++;
                            if (linesBuilt % 100_000 == 0)
                                Console.WriteLine($"  {linesBuilt:N0} lines  {swBuild.Elapsed:mm\\:ss}");
                        }
                    }
                    swBuild.Stop();
                    Console.WriteLine($"Done: {linesBuilt:N0} lines in {swBuild.Elapsed:mm\\:ss\\.ff}");
                    return;
                }

                if (cmd == "search")
                {
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: search <indexDir> <term1> [term2] ...");
                        return;
                    }
                    string dir   = args[1];
                    var    terms = args.Skip(2).ToArray();
                    Console.WriteLine($"Index : {dir}");
                    Console.WriteLine($"Terms : [{string.Join(", ", terms)}]");
                    using (var reader = new IndexReader(dir))
                    {
                        foreach (var t in terms)
                            Console.WriteLine($"  '{t}' → {reader.GetTermCount(t):N0} postings");
                        var sw      = Stopwatch.StartNew();
                        int count   = reader.Search(terms).Count();
                        sw.Stop();
                        Console.WriteLine($"Results: {count:N0}  ({sw.ElapsedMilliseconds} ms)");
                    }
                    return;
                }

                foreach (var tier in IndexTest.Tiers)
                {
                    if (tier.label.ToLowerInvariant() == cmd)
                    {
                        using (var log = new StreamWriter(resultsFile, append: true,
                                                          System.Text.Encoding.UTF8))
                        {
                            log.WriteLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                            IndexTest.RunTier(tier.label, tier.limit, log);
                            log.WriteLine();
                        }
                        Console.WriteLine($"\nResults appended to: {resultsFile}");
                        return;
                    }
                }

                Console.WriteLine($"Unknown command '{args[0]}'.");
                Console.WriteLine("Valid: 500k | 1m | 3m | full | validate [dir] | search <dir> <terms...>");
                return;
            }

            // No args — run all tiers, write everything to results file
            using (var log = new StreamWriter(resultsFile, append: false,
                                              System.Text.Encoding.UTF8))
            {
                log.WriteLine($"=== FtsLib Test Run — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                log.WriteLine();

                foreach (var tier in IndexTest.Tiers)
                {
                    IndexTest.RunTier(tier.label, tier.limit, log);
                    log.WriteLine();
                    Console.WriteLine();
                }

                log.WriteLine("=== All tiers complete ===");
            }

            Console.WriteLine($"\nFull results written to: {resultsFile}");
        }
    }
}
