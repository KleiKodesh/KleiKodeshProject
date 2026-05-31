using System;
using System.Linq;

namespace LuceneTest
{
    /// <summary>
    /// Entry point. Usage:
    ///   LuceneTest build              — index all rows from the default db path
    ///   LuceneTest build &lt;dbPath&gt;     — index from a specific db file
    ///   LuceneTest search &lt;query&gt;     — search the index
    /// </summary>
    internal static class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            bool deleteExistingIndex = !args.Any(a => a.Equals("--keep", StringComparison.OrdinalIgnoreCase));

            switch (args[0].ToLowerInvariant())
            {
                case "build":
                    IndexingTest.RunBuild(
                        dbPath: args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null,
                        deleteExistingIndex: deleteExistingIndex);
                    break;

                case "search":
                    if (args.Length < 2) { Console.WriteLine("search requires a query argument."); return; }
                    IndexingTest.RunSearch(args[1], args.Length > 2 ? args[2] : null);
                    break;

                case "check":
                    if (args.Length < 2) { Console.WriteLine("check requires a line id argument."); return; }
                    if (!int.TryParse(args[1], out int lineId)) { Console.WriteLine("Invalid line id."); return; }
                    IndexingTest.CheckLine(lineId, args.Length > 2 ? args[2] : null);
                    break;

                case "diag":
                    if (args.Length < 2) { Console.WriteLine("diag requires a subcommand: tokenize|query|terms|hits|verify|snippet"); return; }
                    string indexDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lucene_index");
                    switch (args[1].ToLowerInvariant())
                    {
                        case "tokenize":
                            if (args.Length < 3) { Console.WriteLine("diag tokenize <text>"); return; }
                            TokenizeTest.Run(args[2]);
                            break;
                        case "query":
                            if (args.Length < 3) { Console.WriteLine("diag query <text>"); return; }
                            QueryParseTest.Run(args[2]);
                            break;
                        case "rewrite":
                            if (args.Length < 3) { Console.WriteLine("diag rewrite <text>"); return; }
                            QueryParseTest.RunRewrite(indexDir, args[2]);
                            break;
                        case "terms":
                            TermsTest.Run(indexDir, args.Length > 2 ? args[2] : null);
                            break;
                        case "hits":
                            if (args.Length < 3) { Console.WriteLine("diag hits <query>"); return; }
                            HitsTest.Run(indexDir, args[2]);
                            break;
                        case "verify":
                            if (args.Length < 3) { Console.WriteLine("diag verify <query> [dbPath]"); return; }
                            VerifyTest.Run(indexDir, args[2], args.Length > 3 ? args[3] : null);
                            break;
                        case "snippet":
                            if (args.Length < 3) { Console.WriteLine("diag snippet <query> [dbPath]"); return; }
                            SnippetTest.Run(indexDir, args[2], args.Length > 3 ? args[3] : null);
                            break;
                        case "subset":
                            if (args.Length < 4) { Console.WriteLine("diag subset <literalQuery> <wildcardQuery>"); return; }
                            SubsetTest.Run(indexDir, args[2], args[3]);
                            break;
                        default:
                            Console.WriteLine("Unknown diag subcommand. Use: tokenize|query|terms|hits|verify|snippet|subset");
                            break;
                    }
                    break;

                default:
                    PrintUsage();
                    break;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  LuceneTest build [dbPath] [--keep]");
            Console.WriteLine("  LuceneTest search <query> [dbPath]");
            Console.WriteLine("  LuceneTest check <lineId> [dbPath]");
            Console.WriteLine();
            Console.WriteLine("Flags:");
            Console.WriteLine("  --keep    Don't delete existing index before building");
        }
    }
}
