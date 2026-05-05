using System;
using System.Collections.Generic;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// CLI entry point.
    ///
    /// Commands:
    ///   build   [tier]  — build index, open HTML report
    ///   search  [tier]  — search existing index, open HTML report
    ///   runall  [tier]  — build + search in one run, open single combined HTML report
    ///
    /// Tiers: 500k (default) | 1m | 3m | full
    ///
    /// Examples:
    ///   FtsLibTest.exe                  → runall 500k
    ///   FtsLibTest.exe build            → build 500k
    ///   FtsLibTest.exe build full       → build full DB
    ///   FtsLibTest.exe search 3m        → search 3m index
    ///   FtsLibTest.exe runall 1m        → build + search 1m, combined report
    /// </summary>
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "runall";

            switch (cmd)
            {
                case "build":
                    BuildTest.Run(args);
                    return;

                case "search":
                    SearchTest.Run(args);
                    return;

                case "runall":
                    RunAll(args);
                    return;

                case "query":
                    QueryTest.Run(args);
                    return;

                case "verify":
                    VerifyTest.Run(args);
                    return;

                case "speed":
                    SpeedTest.Run(args);
                    return;

                case "expand":
                    ExpandDiag.Run(args);
                    return;

                case "lookup":
                    LookupDiag.Run(args);
                    return;

                case "wdiag":
                    WildcardDiag.Run(args);
                    return;

                case "prefixlen":
                    PrefixLenDiag.Run(args);
                    return;

                case "dbquery":
                    DbQuery.Run(args);
                    return;

                default:
                    PrintUsage();
                    return;
            }
        }

        // ── Combined build + search ───────────────────────────────────

        private static void RunAll(string[] args)
        {
            string tierLabel = args.Length > 1 ? args[1] : "full";
            string label;
            int limit;
            try
            {
                var tier = TestHelpers.ResolveTier(tierLabel);
                label = tier.Label;
                limit = tier.Limit;
            }
            catch (ArgumentException ex) { Console.WriteLine(ex.Message); return; }

            string dbPath = BuildTest.ResolveDbPath();
            string indexDir = TestHelpers.IndexDir(label);
            string path = BuildTest.TempReportPath("full", label);

            var fragments = new List<string>();

            var (buildFragment, index) =
                BuildTest.RunAndGetFragment(label, dbPath, indexDir);
            fragments.Add(buildFragment);

            string searchFragment =
                SearchTest.RunAndGetFragment(label, dbPath, indexDir, index);
            fragments.Add(searchFragment);

            HtmlReport.CombineAndOpen(
                $"FTS Full Report — {label.ToUpper()}",
                fragments,
                path);
        }

        // ── Usage ─────────────────────────────────────────────────────

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  FtsLibTest.exe                        — runall 500k (default)");
            Console.WriteLine("  FtsLibTest.exe build  [tier]          — build index");
            Console.WriteLine("  FtsLibTest.exe search [tier]          — search index");
            Console.WriteLine("  FtsLibTest.exe runall [tier]          — build + search, combined report");
            Console.WriteLine("  FtsLibTest.exe query  [tier] \"query\"  — ad-hoc query with snippets");
            Console.WriteLine("  FtsLibTest.exe wdiag  [tier] query    — wildcard expansion diagnostic");
            Console.WriteLine();
            Console.WriteLine("Tiers: 500k (default) | 1m | 3m | full");
            Console.WriteLine();
            Console.WriteLine("Query syntax:  word  word*  word~  word~2");
        }
    }
}