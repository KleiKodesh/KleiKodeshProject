using System;
using System.IO;
using System.Text;

namespace LuceneIndexBenchmark
{
    /// <summary>
    /// Just hit F5. Edit the three constants in the Config block below, then run.
    /// No command-line arguments needed.
    ///
    /// The app will:
    ///   1. Delete any existing index at IndexDirectory and build a fresh one.
    ///   2. Print progress every second (lines indexed, %, ETA, RAM used).
    ///   3. After the build, run a set of test queries and print hit counts + latency.
    ///   4. Write a full copy of all output to lucene-benchmark-results.txt next to the exe.
    /// </summary>
    internal static class Program
    {
        // =====================================================================
        // CONFIG — edit these three values, then press F5
        // =====================================================================

        /// <summary>Path to your Zayit SQLite database.</summary>
        private const string DatabasePath = @"C:\Users\Admin\AppData\Local\Temp\tmp9ffcdm5c.db";

        /// <summary>
        /// Where to write the Lucene index.
        /// Deleted and recreated on every run so timing is always a cold build.
        /// </summary>
        private const string IndexDirectory = @"C:\Users\Admin\AppData\Local\Temp\lucene-benchmark-index";

        /// <summary>
        /// How much RAM (MB) Lucene may use before flushing a segment.
        /// Goal: set this high enough that NO intermediate flushes happen —
        /// only the final ForceMerge(1) write at the end.
        /// Start with 512. If you see "merging" lines before the final merge, increase it.
        /// </summary>
        private const double RamBufferMegabytes = 512.0;

        // =====================================================================

        private static int Main(string[] unusedArguments)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string resultsFilePath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "lucene-benchmark-results.txt");

            // Replace Console.Out with a writer that echoes to both console and file
            var originalOut = Console.Out;
            using (var fileWriter = new StreamWriter(resultsFilePath, append: false, encoding: Encoding.UTF8) { AutoFlush = true })
            using (var tee = new TeeWriter(originalOut, fileWriter))
            {
                Console.SetOut(tee);

                Console.WriteLine("=== Lucene Index Benchmark ===");
                Console.WriteLine("Started   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Console.WriteLine("Database  : " + DatabasePath);
                Console.WriteLine("Index dir : " + IndexDirectory);
                Console.WriteLine("RAM buffer: " + RamBufferMegabytes + " MB");
                Console.WriteLine("CPU cores : " + Environment.ProcessorCount);
                Console.WriteLine();

                // ── Validate config ───────────────────────────────────────────
                if (!File.Exists(DatabasePath))
                {
                    Console.WriteLine("ERROR: DatabasePath is not set or the file does not exist.");
                    Console.WriteLine("Edit the CONFIG block at the top of Program.cs and rebuild.");
                    WaitForKeyPress(resultsFilePath);
                    return 1;
                }

                // ── Build ─────────────────────────────────────────────────────
                if (Directory.Exists(IndexDirectory))
                {
                    Console.WriteLine("Deleting existing index at " + IndexDirectory);
                    Directory.Delete(IndexDirectory, recursive: true);
                }
                Directory.CreateDirectory(IndexDirectory);

                var builder = new LuceneIndexBuilder(IndexDirectory, RamBufferMegabytes);
                TimeSpan buildTime;

                try
                {
                    buildTime = builder.Build(DatabasePath);
                }
                catch (Exception exception)
                {
                    Console.WriteLine("BUILD FAILED: " + exception);
                    WaitForKeyPress(resultsFilePath);
                    return 1;
                }

                Console.WriteLine();
                Console.WriteLine("=== Build Summary ===");
                Console.WriteLine("Build time : " + buildTime);
                Console.WriteLine("Index size : " + FormatBytes(GetDirectorySize(IndexDirectory)));
                Console.WriteLine();

                // ── Search ────────────────────────────────────────────────────
                try
                {
                    using (var searcher = new LuceneSearcher(IndexDirectory))
                    {
                        searcher.RunBenchmark(new[]
                        {
                            // Single-word terms
                            "שבת",
                            "תורה",
                            "ישראל",
                            "משנה",
                            "תלמוד",
                            "הלכה",
                            "מצוה",

                            // Multi-word AND — all terms must appear in the line
                            "שבת קודש",
                            "תורה מן השמים",
                            "כבוד אב ואם",

                            // Same term with nikud — must match the plain form after normalization
                            "שַׁבָּת",
                        });

                        // Phrase search — words must appear adjacent in order
                        // This is the key capability Bloom filters cannot provide
                        searcher.RunPhraseSearch("שלחן ערוך");
                        searcher.RunPhraseSearch("תורה מן השמים");
                        searcher.RunPhraseSearch("כבוד אב ואם");
                        searcher.RunPhraseSearch("גמרא תלמוד בבלי");
                    }
                }
                catch (Exception exception)
                {
                    Console.WriteLine("SEARCH FAILED: " + exception);
                    WaitForKeyPress(resultsFilePath);
                    return 1;
                }

                Console.WriteLine();
                Console.WriteLine("=== Done ===");
                Console.WriteLine("Finished  : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                Console.WriteLine("Results   : " + resultsFilePath);
            }

            // Restore original out before the final key-press prompt
            Console.SetOut(originalOut);
            WaitForKeyPress(resultsFilePath);
            return 0;
        }

        private static void WaitForKeyPress(string resultsFilePath)
        {
            Console.WriteLine();
            Console.WriteLine("Results saved to: " + resultsFilePath);
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static long GetDirectorySize(string path)
        {
            long total = 0;
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* ignore locked files */ }
            }
            return total;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024)
                return string.Format("{0:F2} GB", bytes / (1024.0 * 1024 * 1024));
            if (bytes >= 1024 * 1024)
                return string.Format("{0:F1} MB", bytes / (1024.0 * 1024));
            if (bytes >= 1024)
                return string.Format("{0:F0} KB", bytes / 1024.0);
            return bytes + " B";
        }
    }

    /// <summary>
    /// TextWriter that writes every character to two underlying writers simultaneously —
    /// the original Console.Out and a log file. Replacing Console.Out with this means
    /// every Console.WriteLine in the app automatically goes to both destinations.
    /// </summary>
    internal sealed class TeeWriter : TextWriter
    {
        private readonly TextWriter _consoleWriter;
        private readonly TextWriter _fileWriter;

        public TeeWriter(TextWriter consoleWriter, TextWriter fileWriter)
        {
            _consoleWriter = consoleWriter;
            _fileWriter    = fileWriter;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            _consoleWriter.Write(value);
            _fileWriter.Write(value);
        }

        public override void Write(string value)
        {
            _consoleWriter.Write(value);
            _fileWriter.Write(value);
        }

        public override void WriteLine(string value)
        {
            _consoleWriter.WriteLine(value);
            _fileWriter.WriteLine(value);
        }

        public override void WriteLine()
        {
            _consoleWriter.WriteLine();
            _fileWriter.WriteLine();
        }

        public override void Flush()
        {
            _consoleWriter.Flush();
            _fileWriter.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            // Do NOT dispose _consoleWriter — we don't own the original Console.Out.
            // _fileWriter is owned by the using block in Main, also not ours to dispose here.
            base.Dispose(disposing);
        }
    }
}
