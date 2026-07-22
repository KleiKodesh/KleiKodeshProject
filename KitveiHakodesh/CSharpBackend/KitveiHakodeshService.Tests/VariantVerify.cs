// Ad-hoc live verification for the two ported frontend mechanisms:
//   1. ה-prefix stripping (הרמבן ↔ רמבן)
//   2. חסר/מלא skeleton variant matching (נידה ↔ נדה)
// Builds a minimal synthetic seforim.db (just enough schema for CatalogTocIndex to
// build from) and runs real Search() calls through the real Lucene index.
using System;
using System.IO;
using System.Linq;
using KitveiHakodeshService.Catalog;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Tests
{
    public static class VariantVerify
    {
        public static int Run()
        {
            int failures0 = 0;
            void Fail0(string msg) { failures0++; Console.Error.WriteLine("  FAIL: " + msg); }

            // "היד החזקה" (with ה) must resolve through the SAME abbreviation key as
            // "יד החזקה" (יד החזקה -> משנה תורה) via the ה-aware abbreviation matcher.
            {
                var tokens = CatalogTocTextRules.TokenizeQuery("היד החזקה");
                if (tokens.Count != 1 || string.Join(",", tokens[0].Alternatives[0]) != "משנה,תורה")
                    Fail0($"he-aware abbrev: 'היד החזקה' tokenized as {tokens.Count} token(s): " +
                          string.Join(" | ", tokens.Select(t => string.Join(" / ", t.Alternatives.Select(a => string.Join(",", a))))));
                else
                    Console.WriteLine("  OK: he-aware abbrev: 'היד החזקה' resolves to משנה תורה");
            }

            string dir = Path.Combine(Path.GetTempPath(), $"varianttest-{Environment.ProcessId}");
            Directory.CreateDirectory(dir);
            string dbPath = Path.Combine(dir, "seforim.db");
            string indexPath = Path.Combine(dir, "index");
            int failures = 0;
            void Fail(string msg) { failures++; Console.Error.WriteLine("  FAIL: " + msg); }
            void Ok(string msg) { Console.WriteLine("  OK: " + msg); }

            try
            {
                BuildSyntheticDb(dbPath);

                string hash = CatalogTocIndex.ComputeDbHash(dbPath);
                var index = new CatalogTocIndex(indexPath, dbPath);
                Directory.CreateDirectory(indexPath);
                int docCount = index.BuildAndSwitch(hash);
                Console.WriteLine($"  info: built {docCount} docs, DocCount()={index.DocCount()}");

                // Sanity: plain exact search must work at all.
                var sanity = index.Search("הרמבן");
                Console.WriteLine($"  info: sanity search 'הרמבן' -> {sanity.Count} hits: {Dump(sanity)}");
                var sanity2 = index.Search("נידה");
                Console.WriteLine($"  info: sanity search 'נידה' -> {sanity2.Count} hits: {Dump(sanity2)}");

                // ── 1. ה-prefix stripping ────────────────────────────────────────
                // Book "הרמב""ן" (title starts with ה) must be findable by the stripped
                // query "רמבן" and vice versa.
                {
                    var byStripped = index.Search("רמבן");
                    bool foundViaStripped = byStripped.Exists(h => h.FullTocPath.Contains("הרמבן"));
                    if (!foundViaStripped) Fail($"he-prefix: query 'רמבן' did not find 'הרמבן' book ({byStripped.Count} hits: {Dump(byStripped)})");
                    else Ok("he-prefix: query 'רמבן' found the 'הרמבן' book");

                    var byFull = index.Search("הרמבן");
                    bool foundDirect = byFull.Exists(h => h.FullTocPath.Contains("הרמבן"));
                    if (!foundDirect) Fail($"he-prefix: query 'הרמבן' did not find itself ({byFull.Count} hits)");
                    else Ok("he-prefix: query 'הרמבן' found itself (baseline)");
                }

                // ── 2. חסר/מלא skeleton variants ─────────────────────────────────
                // Book "נידה" (מלא, with י) must be findable by the חסר query "נדה".
                {
                    var byChaser = index.Search("נדה");
                    bool foundViaSkeleton = byChaser.Exists(h => h.FullTocPath.Contains("נידה"));
                    if (!foundViaSkeleton) Fail($"skeleton: query 'נדה' did not find 'נידה' book ({byChaser.Count} hits: {Dump(byChaser)})");
                    else Ok("skeleton: query 'נדה' (חסר) found the 'נידה' (מלא) book");

                    var byMale = index.Search("נידה");
                    bool foundDirect = byMale.Exists(h => h.FullTocPath.Contains("נידה"));
                    if (!foundDirect) Fail($"skeleton: query 'נידה' did not find itself ({byMale.Count} hits)");
                    else Ok("skeleton: query 'נידה' found itself (baseline)");
                }

                // ── 3. Negative control: incompatible vowel sets must NOT match ─────
                // שבועות vs שביעית share a skeleton but have incompatible vowel sets —
                // must NOT cross-match (ported directly from the frontend's own claim).
                {
                    var hits = index.Search("שביעית");
                    bool wronglyMatched = hits.Exists(h => h.FullTocPath.Contains("שבועות"));
                    if (wronglyMatched) Fail($"skeleton: 'שביעית' wrongly matched 'שבועות' (incompatible vowel sets) ({Dump(hits)})");
                    else Ok("skeleton: 'שביעית' did NOT wrongly match 'שבועות' (incompatible vowel sets respected)");
                }

                // ── 4. ה-prefix also fires in reverse: bare query finds the ה-indexed word ──
                // "הלכות" is indexed; querying the bare form "לכות" (no ה typed) must find it
                // too — same symmetry the frontend gets from indexing both forms up front.
                {
                    var hits = index.Search("לכות");
                    bool found = hits.Exists(h => h.FullTocPath.Contains("הלכות"));
                    if (!found) Fail($"he-prefix: query 'לכות' did not find 'הלכות' book ({hits.Count} hits: {Dump(hits)})");
                    else Ok("he-prefix: query 'לכות' (bare) found the 'הלכות' book");
                }

                int total = failures + failures0;
                Console.WriteLine();
                Console.WriteLine(total == 0 ? "VARIANT VERIFY: PASS" : $"VARIANT VERIFY: FAIL ({total})");
                return total == 0 ? 0 : 1;
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        private static string Dump(System.Collections.Generic.List<CatalogTocHit> hits)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var h in hits) parts.Add(h.FullTocPath);
            return string.Join(" | ", parts);
        }

        private static void BuildSyntheticDb(string dbPath)
        {
            using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ConnectionString);
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    CREATE TABLE category(id INTEGER PRIMARY KEY, parentId INTEGER, title TEXT, level INTEGER, orderIndex INTEGER);
                    CREATE TABLE book(id INTEGER PRIMARY KEY, categoryId INTEGER, title TEXT, orderIndex INTEGER);
                    CREATE TABLE author(id INTEGER PRIMARY KEY, name TEXT);
                    CREATE TABLE book_author(bookId INTEGER, authorId INTEGER);
                    CREATE TABLE line(id INTEGER PRIMARY KEY, bookId INTEGER, lineIndex INTEGER, content TEXT);
                    CREATE TABLE tocText(id INTEGER PRIMARY KEY, text TEXT);
                    CREATE TABLE tocEntry(id INTEGER PRIMARY KEY, bookId INTEGER, parentId INTEGER, textId INTEGER, lineId INTEGER);
                    CREATE TABLE alt_toc_structure(id INTEGER PRIMARY KEY, bookId INTEGER, title TEXT, heTitle TEXT);
                    CREATE TABLE alt_toc_entry(id INTEGER PRIMARY KEY, structureId INTEGER, parentId INTEGER, textId INTEGER, lineId INTEGER);
                ";
                cmd.ExecuteNonQuery();
            }

            // One root category holding all test books.
            Exec(conn, "INSERT INTO category(id, parentId, title, level, orderIndex) VALUES (1, NULL, 'ספרים', 0, 0)");

            // Books: (id, title)
            // 1: הרמב"ן (starts with ה)   — ה-prefix test
            // 2: נידה (מלא, has mid-word י) — skeleton test
            // 3: שבועות (מלא, has mid-word ו×2) — negative control
            // 4: שביעית (מלא, has mid-word י×2, different vowel positions/letters) — must NOT match #3
            // 5: הלכות (starts with ה, real word) — negative control for false-positive stripping
            InsertBook(conn, 1, 1, "הרמבן");
            InsertBook(conn, 2, 1, "נידה");
            InsertBook(conn, 3, 1, "שבועות");
            InsertBook(conn, 4, 1, "שביעית");
            InsertBook(conn, 5, 1, "הלכות");

            // One line per book (so LoadFirstLines has something).
            foreach (int bookId in new[] { 1, 2, 3, 4, 5 })
                Exec(conn, $"INSERT INTO line(bookId, lineIndex, content) VALUES ({bookId}, 0, 'x')");
        }

        private static void InsertBook(SqliteConnection conn, int id, int categoryId, string title)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO book(id, categoryId, title, orderIndex) VALUES (@id, @cat, @title, @id)";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@cat", categoryId);
            cmd.Parameters.AddWithValue("@title", title);
            cmd.ExecuteNonQuery();
        }

        private static void Exec(SqliteConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }
}
