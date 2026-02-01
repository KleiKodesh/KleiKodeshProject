//using LuceneIndexer;
//using MinimalIndexer;
//using System;
//using System.Diagnostics;
//using System.IO;
//using System.Threading;
//using System.Threading.Tasks;

//namespace LuceneIndexerTest
//{
//    internal class Program
//    {
//        static int _indexed;
//        static int _total;
//        static int _progressLine;
//        static Stopwatch _sw;

//        static void Main(string[] args)
//        {
//            string indexPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Index");
//            if (!Directory.Exists(indexPath))
//                Directory.CreateDirectory(indexPath);

//            Console.WriteLine("Choose an option:");
//            Console.WriteLine("1 - Create Index");
//            Console.WriteLine("2 - Search");
//            Console.Write("Option: ");
//            string choice = Console.ReadLine();

//            if (choice == "1")
//            {
//                CreateIndex(indexPath);
//            }
//            else if (choice == "2")
//            {
//                SearchIndex(indexPath);
//            }
//            else
//            {
//                Console.WriteLine("Invalid option. Exiting.");
//            }
//        }

//        static void CreateIndex(string indexPath)
//        {
//            // Clear previous index
//            if (Directory.Exists(indexPath))
//                Directory.Delete(indexPath, true);
//            Directory.CreateDirectory(indexPath);

//            using (var db = new DbManager())
//            using (var indexer = new HebrewIndexer(indexPath))
//            {
//                _total = db.GetLineCount();

//                Console.WriteLine("Total lines to index: " + _total);
//                Console.WriteLine("Loading...");
//                Console.WriteLine(); // reserve progress line

//                _progressLine = Console.CursorTop - 1;
//                _sw = Stopwatch.StartNew();

//                using (var timer = new Timer(_ => DrawProgress(), null, 0, 1000))
//                {
//                    var batches = db.StreamAllLinesInBatches();

//                    Parallel.ForEach(batches, batch =>
//                    {
//                        foreach (var line in batch)
//                        {
//                            indexer.IndexLine(line);
//                            Interlocked.Increment(ref _indexed);
//                        }
//                    });

//                    _sw.Stop();
//                    DrawProgress(); // final redraw
//                }

//                Console.SetCursorPosition(0, _progressLine + 1);
//                Console.WriteLine("Indexing complete.");
//                Console.WriteLine("Total time: " + _sw.Elapsed.TotalSeconds.ToString("F2") + " seconds");
//            }
//        }

//        static void DrawProgress()
//        {
//            int barWidth = 30;
//            int current = Volatile.Read(ref _indexed);

//            double percent = _total == 0 ? 1.0 : (double)current / _total;
//            int filled = (int)(percent * barWidth);

//            TimeSpan eta;
//            if (current == 0)
//            {
//                eta = TimeSpan.Zero;
//            }
//            else
//            {
//                double secondsPerItem = _sw.Elapsed.TotalSeconds / current;
//                double remainingSeconds = secondsPerItem * (_total - current);
//                eta = TimeSpan.FromSeconds(remainingSeconds);
//            }

//            string text =
//                "[" +
//                new string('#', filled) +
//                new string('-', barWidth - filled) +
//                "] " +
//                current + "/" + _total +
//                " " + (percent * 100).ToString("F1") +
//                "% ETA " + eta.ToString(@"hh\:mm\:ss");

//            Console.SetCursorPosition(0, _progressLine);

//            // clear line completely
//            Console.Write(new string(' ', Console.BufferWidth - 1));
//            Console.SetCursorPosition(0, _progressLine);

//            Console.ForegroundColor = ConsoleColor.Green;
//            Console.Write(text);
//            Console.ResetColor();
//        }

//        static void SearchIndex(string indexPath)
//        {
//            if (!Directory.Exists(indexPath) || Directory.GetFiles(indexPath).Length == 0)
//            {
//                Console.WriteLine("Index not found. Please create the index first.");
//                return;
//            }

//            Console.WriteLine("=== Hebrew Search UI ===");
//            Console.WriteLine("Type a query and press Enter. Type 'exit' to quit.");

//            using (var searcher = new HebrewSearcher(indexPath))
//            {
//                while (true)
//                {
//                    Console.Write("\nQuery: ");
//                    string query = Console.ReadLine();
//                    if (string.IsNullOrWhiteSpace(query))
//                        continue;
//                    if (query.Equals("exit", StringComparison.OrdinalIgnoreCase))
//                        break;

//                    try
//                    {
//                        var results = searcher.Search(query, 50); // top 50 results
//                        if (results.Length == 0)
//                        {
//                            Console.WriteLine("No results found.");
//                        }
//                        else
//                        {
//                            // write HTML
//                            string htmlFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SearchResults.html");
//                            using (var writer = new StreamWriter(htmlFile, false, System.Text.Encoding.UTF8))
//                            {
//                                writer.WriteLine("<html><head><meta charset='UTF-8'><title>Search Results</title></head><body>");
//                                writer.WriteLine("<h2>Results for query: " + System.Net.WebUtility.HtmlEncode(query) + "</h2>");
//                                writer.WriteLine("<hr />");

//                                foreach (var r in results)
//                                {
//                                    writer.WriteLine("<div style='margin-bottom:20px;'>");
//                                    writer.WriteLine("<b>Book:</b> " + System.Net.WebUtility.HtmlEncode(r.BookTitle) + "<br />");
//                                    writer.WriteLine("<b>TOC:</b> " + System.Net.WebUtility.HtmlEncode(r.Toc) + "<br />");
//                                    writer.WriteLine("<b>Score:</b> " + r.Score.ToString("F2") + "<br />");
//                                    writer.WriteLine("<p>" + r.HighlightedContent + "</p>");
//                                    writer.WriteLine("</div>");
//                                    writer.WriteLine("<hr />");
//                                }

//                                writer.WriteLine("</body></html>");
//                            }

//                            Console.WriteLine($"Results written to {htmlFile}");
//                            Process.Start(new ProcessStartInfo(htmlFile) { UseShellExecute = true });
//                        }
//                    }
//                    catch (Exception ex)
//                    {
//                        Console.WriteLine("Error: " + ex.Message);
//                    }
//                }
//            }
//        }
//    }
//}
