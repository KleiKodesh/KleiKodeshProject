using System;

namespace FtsLibTest
{
    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            TokenizerTests.RunAll();
            SkipListTest.Run();
            QuickTest.Run();
        }
    }
}
