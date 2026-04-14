using BloomSearchEngineLib;
using MinimalIndexer;
using System;
using System.IO;

namespace DictWordChecker
{
    /// <summary>
    /// Reads headwords from stdin (one per line), checks each against the Bloom filter,
    /// writes "1" (found in Torah DB) or "0" (not found) to stdout per word.
    ///
    /// Must be run from the directory that contains the BloomFilters/ folder,
    /// OR pass that directory as args[0].
    /// </summary>
    class Program
    {
        static int Main(string[] args)
        {
            // If a BloomFilters dir is passed, temporarily change base dir by copying lines.dat
            // Actually: BloomFilterCollectionReader uses AppDomain.CurrentDomain.BaseDirectory.
            // Easiest fix: just run the exe from the right folder, or pass the path and we
            // use a symlink/junction. Instead, we accept the path and use Directory.SetCurrentDirectory.
            if (args.Length > 0)
            {
                string dir = args[0];
                if (!Directory.Exists(dir))
                {
                    Console.Error.WriteLine("ERROR: dir not found: " + dir);
                    return 1;
                }
                Directory.SetCurrentDirectory(dir);
            }

            BloomFilterCollectionReader reader;
            try
            {
                reader = new BloomFilterCollectionReader("lines");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR opening Bloom filter: " + ex.Message);
                Console.Error.WriteLine("Make sure BloomFilters/lines.dat exists in: " + AppDomain.CurrentDomain.BaseDirectory);
                return 1;
            }

            using (reader)
            {
                string line;
                while ((line = Console.In.ReadLine()) != null)
                {
                    string word = line.Trim();
                    if (word.Length == 0) { Console.WriteLine("0"); continue; }

                    string norm = word.NormalizeText();
                    SearchResult[] hits = reader.Search(new string[] { norm });
                    Console.WriteLine(hits.Length > 0 ? "1" : "0");
                }
            }

            return 0;
        }
    }
}
