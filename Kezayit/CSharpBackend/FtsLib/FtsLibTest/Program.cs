using System;
using System.Diagnostics;

namespace FtsLibTest
{
    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Stopwatch sw = Stopwatch.StartNew();
            Console.WriteLine("Creating Index");
            CostumeTest.CreateIndex();
            Console.WriteLine("Index Complete At" + sw.Elapsed + " minutes");
            sw.Restart();
            CostumeTest.Search();
            Console.WriteLine("Search Elapsed: " + sw.Elapsed + "seconds");
            Console.ReadKey();
        }
    }
}
