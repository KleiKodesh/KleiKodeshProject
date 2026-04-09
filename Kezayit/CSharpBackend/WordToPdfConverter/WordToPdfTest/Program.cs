using System;
using System.IO;
using WordToPdfLib;

namespace WordToPdfTest
{
    class Program
    {
        static void Main(string[] args)
        {
            var docx = Path.GetFullPath("TestDoc.docx");
            var pdf  = Path.Combine(Path.GetDirectoryName(docx), "test_output.pdf");

            Console.WriteLine("Input:  " + docx);
            Console.WriteLine("Output: " + pdf);
            Console.WriteLine("Converting...");

            try
            {
                var logPath = Path.Combine(Path.GetDirectoryName(docx), "conversion.log");
                using (var log = new StreamWriter(logPath, false, System.Text.Encoding.UTF8))
                {
                    new WordToPdfConverter().Convert(docx, pdf, log);
                }
                Console.WriteLine("SUCCESS — PDF written to: " + pdf);
                Console.WriteLine("Log written to: " + Path.Combine(Path.GetDirectoryName(docx), "conversion.log"));
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED: " + ex.GetType().Name);
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }

            Console.WriteLine("\nDone.");
        }
    }
}
