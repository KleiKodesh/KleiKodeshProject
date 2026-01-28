using System;
using System.Linq;

namespace MinimalIndexer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var api = new Fts();
            bool auto = args.Length > 0 && (args[0] == "--auto" || args[0] == "-a");

            // --- Run from arguments ---
            if (args.Length > 0)
            {
                foreach (var cmd in args.Skip(auto ? 1 : 0))
                    Execute(cmd, api, auto);
                return;
            }

            // --- Interactive mode ---
            while (true)
            {
                Console.WriteLine("\n1 - Create index");
                Console.WriteLine("2 - Search");
                Console.WriteLine("0 - Exit");

                Execute(Console.ReadLine(), api, false);
            }
        }

        static void Execute(string cmd, Fts api, bool silent)
        {
            try
            {
                if (cmd != null && cmd.StartsWith("search:"))
                {
                    api.Search(cmd.Substring(7));
                    return;
                }

                switch (cmd)
                {
                    case "1":
                        api.CreateIndex(silent);
                        break;

                    case "2":
                        Console.Write("Search: ");
                        api.Search(Console.ReadLine());
                        break;

                    case "0":
                        Environment.Exit(0);
                        break;

                    default:
                        Console.WriteLine("Invalid command.");
                        if (silent) Environment.Exit(1);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: " + ex.Message);
                if (silent) Environment.Exit(1);
            }
        }
    }
}
