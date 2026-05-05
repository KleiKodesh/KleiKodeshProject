using FtsLib.Core;
using System;
using System.IO;
using System.Linq;
using FtsLib.Misc;
using System.Diagnostics;


namespace FtsLibTest
{
    internal static class CostumeTest
    {
        const string DbPath = ""; //empty on purpose
        static string IndexPathA =>  Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index1"); 
        static string IndexPathB => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index1");
        
        internal static void CreateIndex()
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            using (var db = new ZayitDb(DbPath))
            {
                long counter = 0;

                var tokenizer = new Tokenizer();
                var writer = new IndexWriter(IndexPathA);
                foreach (var row in db.ReadLines(0))
                {
                    var tokens = tokenizer.Extract(row.Content);
                    foreach (var token in tokens) 
                        writer.Add(row.Id, token);
                    
                    counter++;

                    if (counter % 1000 == 0)
                        Console.WriteLine("indexed: " + counter + " in " + stopWatch.Elapsed);
                }
                writer.Dispose();
            }
        }

        internal static void Search()
        {
            string sampleSearch = "כי ביצחק";
            using (var indexReader = new IndexReader(IndexPathA))
            {
                var results = indexReader.Search(sampleSearch.Split(' '));
                Console.WriteLine("Results count: " + results.Count());
            }

            //using (var indexReader = new IndexReader(IndexPathB))
            //{
            //    var results = indexReader.Search(sampleSearch.Split(' '));
            //    Console.WriteLine("Results count: " + results.Count());
            //}
        }
    }
}
