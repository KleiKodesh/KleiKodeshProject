using Dapper;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KezayitLib.Db
{
    /// <summary>
    /// Background indexer: extracts headwords from the 4 dictionary books in the
    /// main DB and inserts them into dictionary.db.
    ///
    /// Resume logic: per-book meta keys "db_book_{bookId}" track completion.
    /// If interrupted mid-run, the next launch resumes from the first incomplete book.
    /// Final meta key "db_indexed" = "1" when all books are done.
    /// </summary>
    public class DictionaryIndexer
    {
        private readonly string _dictDbPath;
        private readonly string _mainDbPath;

        private static readonly (int bookId, int source, string filter, bool multiWord)[] Books =
        {
            (473,  10, "content LIKE '%<big>%' AND content NOT LIKE '<h%'",                                    false),
            (471,  11, "content LIKE '%<b>%'   AND content NOT LIKE '<h%'",                                    false),
            (6105, 12, "content LIKE '<h3>%'   AND content NOT LIKE '<h3>הקדמה%'",                             false),
            (472,  13, "lineIndex >= 4",                                                                        false),
            (462,  14, "content LIKE '%<b>%' AND content NOT LIKE '<h%' AND lineIndex >= 10",                  true),
            (463,  15, "content LIKE '%<b>%' AND content NOT LIKE '<h%' AND lineIndex > 10",                   false),
            (465,  16, "content LIKE '%<b>%' AND content NOT LIKE '<h%' AND lineIndex > 10",                   false),
            (466,  17, "content LIKE '%<b>%' AND content NOT LIKE '<h%' AND lineIndex > 14",                   false),
        };

        public DictionaryIndexer(string dictDbPath, string mainDbPath)
        {
            _dictDbPath = dictDbPath;
            _mainDbPath = mainDbPath;
        }

        /// <summary>Starts indexing on a background thread if not already complete.</summary>
        public void RunIfNeeded()
        {
            // Quick check on calling thread before spinning up a task
            try
            {
                using (var conn = new SQLiteConnection("Data Source=" + _dictDbPath + ";Version=3;Read Only=True;"))
                {
                    conn.Open();
                    string done = conn.ExecuteScalar<string>(
                        "SELECT value FROM meta WHERE key = 'db_indexed'");
                    if (done == "1") return;
                }
            }
            catch { return; }

            Task.Run(() =>
            {
                try { Index(); }
                catch (Exception ex)
                {
                    Console.WriteLine("[DictionaryIndexer] Error: " + ex.Message);
                }
            });
        }

        private void Index()
        {
            Console.WriteLine("[DictionaryIndexer] Starting...");

            using (var mainConn = new SQLiteConnection(
                "Data Source=" + _mainDbPath + ";Version=3;Read Only=True;"))
            using (var dictConn = new SQLiteConnection(
                "Data Source=" + _dictDbPath + ";Version=3;"))
            {
                mainConn.Open();
                dictConn.Open();
                dictConn.Execute("PRAGMA journal_mode = WAL;");
                dictConn.Execute("PRAGMA synchronous = NORMAL;");

                foreach (var (bookId, source, filter, multiWord) in Books)
                {
                    string metaKey = "db_book_" + bookId;
                    string bookDone = dictConn.ExecuteScalar<string>(
                        "SELECT value FROM meta WHERE key = @k", new { k = metaKey });

                    if (bookDone == "1")
                    {
                        Console.WriteLine("[DictionaryIndexer] Book " + bookId + " already done.");
                        continue;
                    }

                    dictConn.Execute("DELETE FROM entry WHERE bookId = @b", new { b = bookId });

                    var rows = mainConn.Query<LineRow>(
                        "SELECT id, lineIndex, content FROM line WHERE bookId = @b AND " + filter + " ORDER BY lineIndex",
                        new { b = bookId });

                    int count = 0;
                    using (var tx = dictConn.BeginTransaction())
                    {
                        foreach (var row in rows)
                        {
                            if (multiWord)
                            {
                                // Extract all bold words — each becomes a separate entry
                                var matches = Regex.Matches(row.content, @"<b>([^<]+)</b>");
                                foreach (System.Text.RegularExpressions.Match m in matches)
                                {
                                    string headword = m.Groups[1].Value.Trim();
                                    if (string.IsNullOrEmpty(headword) || !ContainsHebrew(headword)) continue;
                                    if (headword.Length > 20) continue; // skip section headers
                                    string definition = StripHtml(row.content);
                                    if (definition.Length > 500) definition = definition.Substring(0, 500);
                                    dictConn.Execute(
                                        "INSERT INTO entry (headword, nikud, definition, source, bookId, lineIndex) VALUES (@h, NULL, @d, @s, @b, @li)",
                                        new { h = headword, d = definition, s = source, b = bookId, li = row.lineIndex }, tx);
                                    count++;
                                }
                            }
                            else
                            {
                                string headword = ExtractHeadword(row.content, bookId);
                                if (string.IsNullOrEmpty(headword) || !ContainsHebrew(headword)) continue;
                                string definition = StripHtml(row.content);
                                if (definition.Length > 500) definition = definition.Substring(0, 500);
                                dictConn.Execute(
                                    "INSERT INTO entry (headword, nikud, definition, source, bookId, lineIndex) VALUES (@h, NULL, @d, @s, @b, @li)",
                                    new { h = headword, d = definition, s = source, b = bookId, li = row.lineIndex }, tx);
                                count++;
                            }
                        }
                        tx.Commit();
                    }

                    dictConn.Execute(
                        "INSERT OR REPLACE INTO meta (key, value) VALUES (@k, '1')",
                        new { k = metaKey });

                    Console.WriteLine("[DictionaryIndexer] Book " + bookId + ": " + count + " entries inserted.");
                }

                dictConn.Execute(
                    "INSERT OR REPLACE INTO meta (key, value) VALUES ('db_indexed', '1')");

                Console.WriteLine("[DictionaryIndexer] Complete.");
            }
        }

        private static string ExtractHeadword(string content, int bookId)
        {
            if (bookId == 6105)
            {
                var m = Regex.Match(content, @"<h3>([^<]+)</h3>");
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }
            if (bookId == 473)
            {
                var big = Regex.Match(content, @"<big>([^<]+)</big>");
                if (big.Success) return big.Groups[1].Value.Trim();
            }
            var bold = Regex.Match(content, @"<b>([^<]+)</b>");
            return bold.Success ? bold.Groups[1].Value.Trim() : null;
        }

        private static string StripHtml(string s)
        {
            return Regex.Replace(s, @"<[^>]+>", " ").Trim();
        }

        private static bool ContainsHebrew(string s)
        {
            foreach (char c in s)
                if (c >= '\u05D0' && c <= '\u05EA') return true;
            return false;
        }

        private class LineRow
        {
            public long id { get; set; }
            public int lineIndex { get; set; }
            public string content { get; set; }
        }
    }
}
