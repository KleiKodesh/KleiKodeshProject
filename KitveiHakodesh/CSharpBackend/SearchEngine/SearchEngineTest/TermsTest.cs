using System;
using Lucene.Net.Index;
using Lucene.Net.Store;
using SearchEngine.Indexing;

namespace SearchEngineTest
{
    internal static class TermsTest
    {
        public static void Run(string indexDir, string prefix = null, int maxPrint = 50)
        {
            Console.WriteLine($"=== INDEX TERMS (field='{LuceneIndexWriter.FieldText}'" +
                              (prefix != null ? $", prefix='{prefix}'" : "") + ") ===");

            if (!System.IO.Directory.Exists(indexDir))
            {
                Console.WriteLine("Index not found — run build first.");
                return;
            }

            using (var dir = FSDirectory.Open(indexDir))
            using (var reader = DirectoryReader.Open(dir))
            {
                var fields = MultiFields.GetFields(reader);
                if (fields == null) { Console.WriteLine("  No fields found."); return; }

                var terms = fields.GetTerms(LuceneIndexWriter.FieldText);
                if (terms == null) { Console.WriteLine("  Field not found in index."); return; }

                var te = terms.GetEnumerator(null);
                int count = 0;
                int printed = 0;
                while (te.MoveNext())
                {
                    count++;
                    string termStr = te.Term.Utf8ToString();
                    if (prefix == null || termStr.StartsWith(prefix))
                    {
                        if (printed < maxPrint)
                        {
                            Console.WriteLine($"  \"{termStr}\"  (docFreq={te.DocFreq})");
                            printed++;
                        }
                    }
                }
                Console.WriteLine($"  Total terms in field: {count}  (printed {printed})");
            }
        }
    }
}
