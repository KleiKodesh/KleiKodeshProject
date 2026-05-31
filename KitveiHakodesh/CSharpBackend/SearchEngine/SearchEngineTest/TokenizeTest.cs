using System;
using System.IO;
using Lucene.Net.Analysis.TokenAttributes;
using SearchEngine.Tokenization;

namespace SearchEngineTest
{
    internal static class TokenizeTest
    {
        public static void Run(string text)
        {
            Console.WriteLine($"=== TOKENIZE: {text} ===");
            using (var analyzer = new HebrewAnalyzer())
            using (var ts = analyzer.GetTokenStream("text", new StringReader(text)))
            {
                var termAttr = ts.GetAttribute<ICharTermAttribute>();
                ts.Reset();
                int i = 0;
                while (ts.IncrementToken())
                    Console.WriteLine($"  [{i++}] \"{termAttr}\"");
                ts.End();
                if (i == 0)
                    Console.WriteLine("  (no tokens produced)");
            }
        }
    }
}
