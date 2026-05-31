using System;
using System.IO;

namespace SearchEngineTest
{
    /// <summary>
    /// Legacy diagnostics test class. Tests have been refactored into individual files:
    /// - TokenizeTest.cs
    /// - QueryParseTest.cs
    /// - TermsTest.cs
    /// - HitsTest.cs
    /// - VerifyTest.cs
    /// - SnippetTest.cs
    /// 
    /// This class is kept for backward compatibility only.
    /// </summary>
    internal static class DiagnosticsTest
    {
        private static readonly string IndexDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lucene_index");

        // Deprecated: use TokenizeTest.Run instead
        public static void RunTokenize(string text) => TokenizeTest.Run(text);

        // Deprecated: use QueryParseTest.Run instead
        public static void RunQueryParse(string queryText) => QueryParseTest.Run(queryText);

        // Deprecated: use TermsTest.Run instead
        public static void RunTerms(string prefix = null, int maxPrint = 50) =>
            TermsTest.Run(IndexDir, prefix, maxPrint);

        // Deprecated: use HitsTest.Run instead
        public static void RunHits(string queryText, int maxPrint = 10) =>
            HitsTest.Run(IndexDir, queryText, maxPrint);

        // Deprecated: use VerifyTest.Run instead
        public static void RunVerify(string queryText, string dbPath = null) =>
            VerifyTest.Run(IndexDir, queryText, dbPath);

        // Deprecated: use SnippetTest.Run instead
        public static void RunSnippetTest(string queryText, string dbPath = null) =>
            SnippetTest.Run(IndexDir, queryText, dbPath);

        // Deprecated: wildcard test removed (use SnippetTest instead)
        public static void RunWildcardTest(string queryText, string dbPath = null)
        {
            Console.WriteLine("RunWildcardTest is deprecated. Use SnippetTest instead.");
        }
    }
}
