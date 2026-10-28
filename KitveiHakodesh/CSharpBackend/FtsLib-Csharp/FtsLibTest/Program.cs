using System;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Test runner entry point: the first argument names the test or benchmark to run.
    /// An unknown command prints the list.
    /// </summary>
    internal static class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            switch (cmd)
            {
                case "parsertest":        QueryParserTest.Run(args); return;
                case "phrasetest":        QuotedPhraseTest.Run(args); return;
                case "worddist":          WordDistanceTest.Run(args); return;
                case "ketivtest":         KetivExpanderTest.Run(args); return;
                case "tokentest":         TokenRuleTest.Run(args); return;
                case "tokdiag":           TokenDiag.Run(args); return;
                case "tokbench":          TokBench.Run(args); return;
                case "charbench":         CharBench.Run(args); return;
                case "decodebench":       DecodeBench.Run(args); return;
                case "trgmbench":         TrgmBench.Run(args); return;
                case "trgmidx":           TrgmIdx.Run(args); return;
                case "trgmlive":          TrgmLive.Run(args); return;
                case "trgmfull":          TrgmFull.Run(args); return;
                case "fmbench":           FmIndexBench.Run(args); return;
                case "fuzzybench":        FuzzyBench.Run(args); return;
                case "fts5engine":        Fts5Engine.Fts5EngineBench.Run(args); return;

                case "fts5buildat":
                {
                    // Builds a SQLite FTS5 (detail=none, external-content) index from the
                    // same seforim DB, using the REAL production Tokenizer for terms — so
                    // the AND-of-terms comparison is apples-to-apples
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

                default:
                    Console.WriteLine("Tests: parsertest phrasetest worddist ketivtest tokentest");
                    Console.WriteLine("Benchmarks: tokdiag tokbench charbench decodebench trgmbench trgmidx trgmlive trgmfull fmbench fuzzybench");
                    Console.WriteLine("SQLite FTS5: fts5engine fts5buildat fts5queryat fts5trigramat");
                    return;
            }
        }
    }
}
