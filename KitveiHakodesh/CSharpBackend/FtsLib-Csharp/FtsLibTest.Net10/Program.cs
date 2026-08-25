using System;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// net10 entry point for the shared test suite (the net48 project has its own Program.cs).
    /// Dispatches only the commands whose source compiles cleanly on net10 (i.e. everything
    /// except the System.Data.SQLite-specific diagnostics). Same test code, net10 runtime —
    /// so `bench`/`speed`/`perf` numbers are directly comparable to the net48 build.
    /// </summary>
    internal static class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "bench";
            switch (cmd)
            {
                case "bench":             BenchTest.Run(args); return;
                case "embellishbench":    EmbellishBenchTest.Run(args); return;
                case "forcemergebug":     ForceMergeBugTest.Run(args); return;
                case "build":             BuildTest.Run(args); return;
                case "buildfresh":        BuildFreshTest.Run(args); return;
                case "search":            SearchTest.Run(args); return;
                case "speed":             SpeedTest.Run(args); return;
                case "perf":              PerformanceTest.Run(args); return;
                case "query":             QueryTest.Run(args); return;
                case "parsertest":        QueryParserTest.Run(args); return;
                case "orderedtest":       OrderedSearchTest.Run(args); return;
                case "worddist":          WordDistanceTest.Run(args); return;
                case "snippettest":       SnippetTest.Run(args); return;
                case "snippetdiag":       SnippetDiag.Run(args); return;
                case "tokdiag":           TokenDiag.Run(args); return;
                case "tokbench":          TokBench.Run(args); return;
                case "charbench":         CharBench.Run(args); return;
                case "decodebench":       DecodeBench.Run(args); return;
                case "snipbench":         SnipBench.Run(args); return;
                case "trgmbench":         TrgmBench.Run(args); return;
                case "trgmidx":           TrgmIdx.Run(args); return;
                case "trgmlive":          TrgmLive.Run(args); return;
                case "trgmfull":          TrgmFull.Run(args); return;
                case "sortdiag":          SortDiag.Run(args); return;
                case "ketivtest":         KetivExpanderTest.Run(args); return;
                case "ketivquery":        KetivQueryTest.Run(args); return;
                case "verify":            VerifyTest.Run(args); return;
                case "filtertest":        FilterTest.Run(args); return;
                case "probe":             ProbeSearch.Run(args); return;
                case "dumpids":           DumpIds.Run(args); return;
                case "monitor":           MonitorTest.Run(args); return;
                case "docsource":         DocSourceTest.Run(args); return;
                case "forcemerge":
                {
                    // Force-merge an existing tier index to a single segment — puts a
                    // fresh build into the same topology as a production
                    // (post-forceMergeOnComplete) index before perf comparisons.
                    string tier = args.Length > 1 ? args[1] : "full";
                    var idx = new FtsLib.SeforimDb.SeforimIndex(
                        TestHelpers.IndexDir(tier), BuildTest.ResolveDbPath());
                    idx.ForceMerge();
                    Console.WriteLine("Force merge complete.");
                    return;
                }
                case "interrupttest":     InterruptTest.Run(args); return;
                case "mergetest":         MergeTest.Run(args); return;
                case "crashmergetest":    CrashMergeTest.Run(args); return;
                case "searchduringmerge": SearchDuringMergeTest.Run(args); return;

                case "buildat":
                {
                    // Explicit-path build: bypasses ResolveDbPath() so a specific DB file
                    // can be indexed regardless of what other seforim.db candidates exist
                    // on the machine. Usage: buildat <dbPath> [tier=full] [indexDir]
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: buildat <dbPath> [tier=full] [indexDir]");
                        return;
                    }
                    string dbPath   = args[1];
                    string tier     = args.Length > 2 ? args[2] : "full";
                    string indexDir = args.Length > 3 ? args[3]
                        : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"index_{tier}_custom");
                    BuildTest.RunAndGetFragment(tier, dbPath, indexDir);
                    Console.WriteLine("buildat: done.");
                    return;
                }

                case "forcemergeat":
                {
                    // Explicit-path force-merge — same idea as buildat. Collapses every
                    // live segment into one, which is the topology production search runs
                    // against (fresh builds leave several unmerged L0/L1 segments).
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: forcemergeat <indexDir> <dbPath>");
                        return;
                    }
                    string indexDir = args[1];
                    string dbPath   = args[2];
                    var idx = new FtsLib.SeforimDb.SeforimIndex(indexDir, dbPath);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    idx.ForceMerge();
                    sw.Stop();
                    Console.WriteLine($"Force merge complete in {sw.Elapsed.TotalSeconds:F1}s.");
                    return;
                }

                case "queryidsat":
                {
                    // Same as queryat but calls SearchIds() — skips the per-row DB content
                    // fetch entirely. Isolates pure index-intersection time from total
                    // pipeline time, to tell apart "the index is slow" from "fetching
                    // 18k rows of content from SQLite is slow".
                    if (args.Length < 4)
                    {
                        Console.WriteLine("Usage: queryidsat <indexDir> <dbPath> \"query\" [\"query2\" ...]");
                        return;
                    }
                    string indexDir = args[1];
                    string dbPath   = args[2];
                    var idx = new FtsLib.SeforimDb.SeforimIndex(indexDir, dbPath);
                    for (int i = 3; i < args.Length; i++)
                    {
                        string q = args[i];
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        int cnt = 0;
                        foreach (var _ in idx.SearchIds(q)) cnt++;
                        sw.Stop();
                        Console.WriteLine($"{q,-30}  {cnt,10:N0} ids  {sw.ElapsedMilliseconds,7} ms");
                    }
                    return;
                }

                case "fts5buildat":
                {
                    // Builds a SQLite FTS5 (detail=none, external-content) index from the
                    // same seforim DB, using the REAL production Tokenizer for terms — so
                    // the AND-of-terms comparison against SeforimIndex is apples-to-apples
                    // (same terms, same machine, same runtime), isolating "posting-list
                    // engine" differences from tokenizer differences.
                    // Usage: fts5buildat <dbPath> <outDbPath>
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: fts5buildat <dbPath> <outDbPath>");
                        return;
                    }
                    Fts5Compare.Build(args[1], args[2]);
                    return;
                }

                case "fts5queryat":
                {
                    // ids-only AND query against the FTS5 db built by fts5buildat — no
                    // content fetch, so it's directly comparable to FtsLib's SearchIds.
                    // Usage: fts5queryat <outDbPath> "term1 term2" ["term3 term4" ...]
                    if (args.Length < 3)
                    {
                        Console.WriteLine("Usage: fts5queryat <outDbPath> \"term1 term2\" [\"term3 term4\" ...]");
                        return;
                    }
                    Fts5Compare.Query(args[1], args[2..]);
                    return;
                }

                case "fts5trigramat":
                {
                    // Adds a trigram-tokenized FTS5 table alongside the word-based one
                    // built by fts5buildat, over the same outDbPath — measures how much
                    // extra disk the trigram index costs on top of the word index.
                    // Usage: fts5trigramat <outDbPath>
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Usage: fts5trigramat <outDbPath>  (run fts5buildat on it first)");
                        return;
                    }
                    Fts5Compare.BuildTrigram(args[1]);
                    return;
                }

                case "querycapat":
                {
                    // Same as queryat but with a result cap — mirrors how a real UI
                    // consumes search results (a page of hits, not the whole match set).
                    if (args.Length < 5)
                    {
                        Console.WriteLine("Usage: querycapat <indexDir> <dbPath> <cap> \"query\" [\"query2\" ...]");
                        return;
                    }
                    string indexDir = args[1];
                    string dbPath   = args[2];
                    int cap = int.Parse(args[3]);
                    var idx = new FtsLib.SeforimDb.SeforimIndex(indexDir, dbPath);
                    for (int i = 4; i < args.Length; i++)
                    {
                        string q = args[i];
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        int cnt = 0;
                        foreach (var _ in idx.Search(q, cap: cap)) cnt++;
                        sw.Stop();
                        Console.WriteLine($"{q,-30}  {cnt,10:N0} fetched  {sw.ElapsedMilliseconds,7} ms");
                    }
                    return;
                }

                case "queryat":
                {
                    // Explicit-path ad-hoc query with timing — same measurement as
                    // BuildTest's smoke search (full enumeration of index.Search()).
                    if (args.Length < 4)
                    {
                        Console.WriteLine("Usage: queryat <indexDir> <dbPath> \"query\" [\"query2\" ...]");
                        return;
                    }
                    string indexDir = args[1];
                    string dbPath   = args[2];
                    var idx = new FtsLib.SeforimDb.SeforimIndex(indexDir, dbPath);
                    for (int i = 3; i < args.Length; i++)
                    {
                        string q = args[i];
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        int cnt = 0;
                        foreach (var _ in idx.Search(q)) cnt++;
                        sw.Stop();
                        Console.WriteLine($"{q,-30}  {cnt,10:N0} results  {sw.ElapsedMilliseconds,7} ms");
                    }
                    return;
                }
                default:
                    Console.WriteLine("net10 test port. Commands: bench fetchbench build buildfresh search speed perf query parsertest orderedtest worddist snippettest snippetdiag ketivtest ketivquery verify filtertest probe dumpids monitor docsource interrupttest mergetest crashmergetest searchduringmerge");
                    Console.WriteLine("Explicit-path commands (index dir + db path given on the command line): buildat forcemergeat queryat queryidsat querycapat");
                    Console.WriteLine("SQLite FTS5 comparison: fts5buildat fts5queryat");
                    return;
            }
        }
    }
}
