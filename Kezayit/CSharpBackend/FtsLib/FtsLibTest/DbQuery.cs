using System;
using System.Data.SQLite;
using System.IO;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Ad-hoc DB query: finds lines containing a given substring and dumps
    /// the raw Unicode codepoints around the match.
    ///
    /// Usage: FtsLibTest.exe dbquery
    ///        FtsLibTest.exe dbquery _ "custom phrase"
    /// </summary>
    internal static class DbQuery
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // Default phrase: the merged token produced by the sof-pasuq bug
            // בשעה + ׃ (U+05C3) + שישראל  stripped to  בשעהשישראל
            string phrase = args.Length > 2
                ? args[2]
                : "\u05D1\u05E9\u05E2\u05D4\u05E9\u05D9\u05E9\u05E8\u05D0\u05DC";

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dbPath  = Path.Combine(appData,
                "io.github.kdroidfilter.seforimapp", "databases", "seforim.db");

            if (!File.Exists(dbPath))
            {
                Console.WriteLine("DB not found: " + dbPath);
                return;
            }

            Console.WriteLine("Searching for: " + phrase);
            Console.WriteLine("DB: " + dbPath);
            Console.WriteLine();

            using (var conn = new SQLiteConnection("Data Source=" + dbPath + ";Version=3;"))
            {
                conn.Open();

                // ── 1. Find rows containing the merged phrase ─────────────
                using (var cmd = conn.CreateCommand())
                {
                    string escaped = phrase
                        .Replace("\\", "\\\\")
                        .Replace("%",  "\\%")
                        .Replace("_",  "\\_");

                    cmd.CommandText =
                        "SELECT l.id, b.title, l.heRef, l.content " +
                        "FROM line l JOIN book b ON b.id = l.bookId " +
                        "WHERE l.content LIKE @p ESCAPE '\\' LIMIT 20";
                    cmd.Parameters.AddWithValue("@p", "%" + escaped + "%");

                    int count = 0;
                    using (var r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            count++;
                            long   id      = r.GetInt64(0);
                            string book    = r.IsDBNull(1) ? "" : r.GetString(1);
                            string heRef   = r.IsDBNull(2) ? "" : r.GetString(2);
                            string content = r.IsDBNull(3) ? "" : r.GetString(3);

                            int idx = content.IndexOf(phrase, StringComparison.Ordinal);

                            // Context window
                            int winS = Math.Max(0, idx - 30);
                            int winE = Math.Min(content.Length, idx + phrase.Length + 30);
                            Console.WriteLine("ID:      " + id);
                            Console.WriteLine("Book:    " + book);
                            Console.WriteLine("Ref:     " + heRef);
                            Console.WriteLine("Context: ..." + content.Substring(winS, winE - winS) + "...");

                            // Hex dump of chars around the match
                            int hexS = Math.Max(0, idx - 3);
                            int hexE = Math.Min(content.Length, idx + phrase.Length + 3);
                            var sb = new StringBuilder("Hex:     ");
                            for (int i = hexS; i < hexE; i++)
                            {
                                if (i == idx)                    sb.Append("[ ");
                                sb.AppendFormat("U+{0:X4} ", (int)content[i]);
                                if (i == idx + phrase.Length - 1) sb.Append("] ");
                            }
                            Console.WriteLine(sb.ToString());
                            Console.WriteLine();
                        }
                    }

                    if (count == 0)
                        Console.WriteLine("No rows found with that literal token.");
                    else
                        Console.WriteLine("Found " + count + " row(s).");
                }

                // ── 2. Dump every occurrence of בשעה in line 531809 ───────
                Console.WriteLine();
                Console.WriteLine("=== Hex dump of all 'בשעה' occurrences in line 531809 ===");
                using (var cmd2 = conn.CreateCommand())
                {
                    cmd2.CommandText = "SELECT content FROM line WHERE id = 531809";
                    var raw = cmd2.ExecuteScalar() as string;
                    if (raw == null)
                    {
                        Console.WriteLine("Line 531809 not found.");
                    }
                    else
                    {
                        // בשעה = U+05D1 U+05E9 U+05E2 U+05D4
                        string target = "\u05D1\u05E9\u05E2\u05D4";
                        int pos = raw.IndexOf(target, StringComparison.Ordinal);
                        int hit = 0;
                        while (pos >= 0)
                        {
                            hit++;
                            int dS = Math.Max(0, pos - 2);
                            int dE = Math.Min(raw.Length, pos + 20);
                            var sb = new StringBuilder("  hit " + hit + " pos " + pos + ": ");
                            for (int i = dS; i < dE; i++)
                                sb.AppendFormat("U+{0:X4} ", (int)raw[i]);
                            Console.WriteLine(sb.ToString());
                            pos = raw.IndexOf(target, pos + 1, StringComparison.Ordinal);
                        }
                        if (hit == 0) Console.WriteLine("  'בשעה' not found in line 531809.");
                    }
                }
            }
        }
    }
}
