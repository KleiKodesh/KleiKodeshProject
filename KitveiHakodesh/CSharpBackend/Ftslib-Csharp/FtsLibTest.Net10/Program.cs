using System;
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
                case "fetchbench":        FetchBenchTest.Run(args); return;
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
                case "sortdiag":          SortDiag.Run(args); return;
                case "ketivtest":         KetivExpanderTest.Run(args); return;
                case "ketivquery":        KetivQueryTest.Run(args); return;
                case "verify":            VerifyTest.Run(args); return;
                case "filtertest":        FilterTest.Run(args); return;
                case "probe":             ProbeSearch.Run(args); return;
                case "dumpids":           DumpIds.Run(args); return;
                case "monitor":           MonitorTest.Run(args); return;
                case "interrupttest":     InterruptTest.Run(args); return;
                case "mergetest":         MergeTest.Run(args); return;
                case "crashmergetest":    CrashMergeTest.Run(args); return;
                case "searchduringmerge": SearchDuringMergeTest.Run(args); return;
                default:
                    Console.WriteLine("net10 test port. Commands: bench fetchbench build buildfresh search speed perf query parsertest orderedtest worddist snippettest snippetdiag ketivtest ketivquery verify filtertest probe dumpids monitor interrupttest mergetest crashmergetest searchduringmerge");
                    return;
            }
        }
    }
}
