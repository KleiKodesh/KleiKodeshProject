using LuceneIndexer;
using MinimalIndexer;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace LuceneIndexerTest
{
    internal class Program
    {
        static void Main()
        {
            string indexPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Index");
            if (!Directory.Exists(indexPath))
                Directory.CreateDirectory(indexPath);

            Console.WriteLine("1 - Create Index\n2 - Search\nOption: ");
            string choice = Console.ReadLine();

            if (choice == "1") CreateIndex(indexPath);
            else if (choice == "2") SearchIndex(indexPath);
        }

        static void CreateIndex(string indexPath)
        {
            if (Directory.Exists(indexPath))
                Directory.Delete(indexPath, true);
            Directory.CreateDirectory(indexPath);

            using (var db = new DbManager())
            using (var indexer = new Indexer(indexPath))
            {
                int total = db.GetLineCount();
                int indexed = 0;
                Stopwatch sw = Stopwatch.StartNew();

                Console.WriteLine("Indexing {0} lines with metadata...", total);

                foreach (var line in db.StreamAllLinesWithMetadata())
                {
                    indexer.CreateDocument(line.LineId, line.Content, line.BookTitle, line.TocText);
                    indexed++;

                    if (indexed % 1000 == 0 || indexed == total)
                    {
                        double percent = indexed * 100.0 / total;

                        double elapsedSeconds = sw.Elapsed.TotalSeconds;
                        double avgPerItem = elapsedSeconds / indexed;
                        double remainingSeconds = avgPerItem * (total - indexed);

                        TimeSpan eta = TimeSpan.FromSeconds(remainingSeconds);

                        Console.Write($"\rIndexed {indexed}/{total} ({percent:0.0}%) - ETA: {eta:hh\\:mm\\:ss}");
                    }
                }

                Console.WriteLine("\nFinalizing Index... Total: {0}", indexed);
            }

            Console.WriteLine("Indexing complete!");
        }


        static void SearchIndex(string indexPath)
        {
            if (!Directory.Exists(indexPath) || Directory.GetFiles(indexPath).Length == 0)
            {
                Console.WriteLine("Index not found. Please create it first.");
                return;
            }

            string htmlFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "search_results.html");

            using (var searcher = new IndexSearcher(indexPath))
            using (var db = new DbManager())
            {
                Console.WriteLine("Type query and Enter ('exit' to quit):");

                while (true)
                {
                    Console.Write("\nQuery: ");
                    string query = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(query) || query.Equals("exit", StringComparison.OrdinalIgnoreCase))
                        break;

                    try
                    {
                        Stopwatch sw = Stopwatch.StartNew();

                        // 1. Search Lucene for all results (int.MaxValue) with metadata
                        var results = searcher.Search(query, int.MaxValue);

                        // 2. Get line IDs to fetch content from DB
                        var lineIds = new System.Collections.Generic.List<int>(results.Count);
                        foreach (var result in results)
                            lineIds.Add(result.LineId);

                        // 3. Fetch full line content from DB
                        var lines = db.GetLinesByIds(lineIds);

                        // 4. Build HTML with metadata
                        var stb = new StringBuilder();
                        stb.AppendLine("<!DOCTYPE html>");
                        stb.AppendLine("<html><head><meta charset=\"utf-8\"><style>");
                        stb.AppendLine("body { font-family: 'Times New Roman', serif; direction: rtl; max-width: 900px; margin: 20px auto; }");
                        stb.AppendLine(".result { border-bottom: 1px solid #ddd; padding: 15px 0; }");
                        stb.AppendLine(".metadata { color: #666; font-size: 0.9em; margin-bottom: 8px; }");
                        stb.AppendLine(".line-id { color: #999; font-size: 0.8em; }");
                        stb.AppendLine(".book-title { font-weight: bold; color: #333; }");
                        stb.AppendLine(".toc-text { font-style: italic; }");
                        stb.AppendLine("mark { background-color: #ffff00; font-weight: bold; }");
                        stb.AppendLine("</style></head><body>");
                        stb.AppendLine($"<h1>תוצאות חיפוש: {results.Count}</h1>");

                        // 5. Combine results with metadata
                        for (int i = 0; i < results.Count; i++)
                        {
                            var result = results[i];
                            var line = lines[i];

                            string normalizedContent = TextNormalizer.Normalize(line.Content);

                            stb.AppendLine("<div class=\"result\">");

                            // Metadata section
                            stb.AppendLine("<div class=\"metadata\">");
                            stb.AppendLine($"<span class=\"line-id\">#{result.LineId}</span> | ");
                            stb.AppendLine($"<span class=\"book-title\">{result.BookTitle}</span>");
                            if (!string.IsNullOrWhiteSpace(result.TocText))
                            {
                                stb.AppendLine($" - <span class=\"toc-text\">{result.TocText}</span>");
                            }
                            stb.AppendLine("</div>");

                            // Content
                            stb.AppendLine($"<div class=\"content\">{normalizedContent}</div>");
                            stb.AppendLine("</div>");
                        }

                        // 6. Highlight search terms
                        foreach (var term in query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string normalizedTerm = TextNormalizer.Normalize(term);
                            if (!string.IsNullOrWhiteSpace(normalizedTerm))
                            {
                                stb.Replace(normalizedTerm, $"<mark>{normalizedTerm}</mark>");
                            }
                        }

                        stb.AppendLine("</body></html>");

                        // 7. Save and display
                        File.WriteAllText(htmlFile, stb.ToString(), Encoding.UTF8);

                        sw.Stop();
                        Console.WriteLine("Found {0} lines in {1:0.00}s. Results saved to {2}",
                            results.Count, sw.Elapsed.TotalSeconds, htmlFile);

                        OpenBrowser(htmlFile);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error: " + ex.Message);
                        Console.WriteLine(ex.StackTrace);
                    }
                }
            }
        }

        static void OpenBrowser(string filePath)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = filePath;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch
            {
                Console.WriteLine("Could not open browser automatically. Please open manually: {0}", filePath);
            }
        }
    }
}