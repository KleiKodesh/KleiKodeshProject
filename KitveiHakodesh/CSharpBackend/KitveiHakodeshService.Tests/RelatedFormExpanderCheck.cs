using KitveiHakodesh.Core.SeforimDbFullTextSearch;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Checks for Core's SeforimDbFtsRelatedFormExpander against throwaway artifacts in both fold
/// schemas: the old one (surface PK, no weight) and the multi-lemma one (PK(surface, lemma),
/// weight).
///
///   --expander-selftest
/// </summary>
public static class RelatedFormExpanderCheck
{
    // Hebrew test words spelled in ASCII: 'a'..'v' map onto alef..tav in order.
    private static string H(string ascii)
    {
        var chars = new char[ascii.Length];
        for (int i = 0; i < ascii.Length; i++) chars[i] = (char)(0x05D0 + (ascii[i] - 'a'));
        return new string(chars);
    }

    public static int SelfTest()
    {
        int failures = 0;
        void Expect(string actual, string expected, string what)
        {
            if (actual != expected)
            {
                failures++;
                Console.Error.WriteLine("  FAIL: " + what);
                Console.Error.WriteLine("        got  " + Ascii(actual));
                Console.Error.WriteLine("        want " + Ascii(expected));
            }
            else Console.WriteLine("  ok:   " + what);
        }
        string Rewrite(string path, string word) => new SeforimDbFtsRelatedFormExpander(path).RewriteQuery("+" + H(word));
        // The typed token is kept verbatim, marker included; alternatives follow it.
        string Or(params string[] words) => "+" + string.Join(" | ", words.Select(H));

        string dir = Path.Combine(Path.GetTempPath(), "khs-expander-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // OLD schema: one reading; synonym from the untrusted side and the word itself skipped.
            string oldDb = Path.Combine(dir, "old.db");
            Build(oldDb, weighted: false,
                fold: [("ab", "ag", 0)],
                exp: [("ag", "ab", "inf"), ("ag", "ac", "inf"), ("ag", "ad", "syn-bavli"), ("ag", "ae", "inf"),
                      ("ag", "af", "syn"), ("ag", "ah", "inf"), ("ag", "ai", "inf"), ("ag", "ak", "inf")]);
            Expect(Rewrite(oldDb, "ab"), Or("ab", "ac", "ae", "af", "ah", "ai"), "old schema: single lemma, policy + budget 5");
            Expect(Rewrite(oldDb, "ba"), "+" + H("ba"), "old schema: no fold row, no forms -> unchanged");

            // NEW schema.
            string newDb = Path.Combine(dir, "new.db");
            Build(newDb, weighted: true,
                fold: [("ab", "ga", 0.7), ("ab", "gb", 0.3), ("ab", "gc", 0.05),   // 4/1 split, gc dropped
                       ("bb", "ga", 0.6), ("bb", "gz", 0.4),                        // gz has no forms
                       ("cb", "gc", 0.1),                                           // only reading, kept
                       ("db", "ga", 0.5), ("db", "gd", 0.5)],                       // shared form
                exp: [("ga", "ka", "inf"), ("ga", "kb", "inf"), ("ga", "kc", "inf"), ("ga", "kd", "inf"),
                      ("ga", "ke", "inf"), ("ga", "kf", "inf"),
                      ("gb", "la", "inf"), ("gb", "lb", "inf"),
                      ("gc", "ma", "inf"),
                      ("gd", "ka", "inf"), ("gd", "na", "inf"), ("gd", "nb", "inf"), ("gd", "nc", "inf")]);
            Expect(Rewrite(newDb, "ab"), Or("ab", "ka", "la", "kb", "kc", "kd"), "new schema: 0.7/0.3 -> 4+1 round-robin, 0.05 dropped");
            Expect(Rewrite(newDb, "bb"), Or("bb", "ka", "kb", "kc", "kd", "ke"), "new schema: short reading's budget goes to the other");
            Expect(Rewrite(newDb, "cb"), Or("cb", "ma"), "new schema: lone low-weight reading kept");
            Expect(Rewrite(newDb, "db"), Or("db", "ka", "na", "kb", "nb", "kc"), "new schema: form shared by two readings counted once");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        Console.WriteLine(failures == 0 ? "expander self-test: all passed" : $"expander self-test: {failures} FAILED");
        return failures;
    }

    private static void Build(string path, bool weighted,
        (string Surface, string Lemma, double Weight)[] fold, (string Lemma, string Form, string Channel)[] exp)
    {
        using var connection = new SqliteConnection("Data Source=" + path);
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = weighted
                ? "CREATE TABLE fold(surface TEXT, lemma TEXT, weight REAL, source TEXT, PRIMARY KEY(surface, lemma));"
                : "CREATE TABLE fold(surface TEXT PRIMARY KEY, lemma TEXT, source TEXT);";
            create.CommandText += "CREATE TABLE exp(lemma TEXT, rank INTEGER, form TEXT, channel TEXT, source TEXT, PRIMARY KEY(lemma, rank));";
            create.ExecuteNonQuery();
        }
        foreach (var (surface, lemma, weight) in fold)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = weighted
                ? "INSERT INTO fold VALUES(@s, @l, @w, 'test')"
                : "INSERT INTO fold VALUES(@s, @l, 'test')";
            insert.Parameters.AddWithValue("@s", H(surface));
            insert.Parameters.AddWithValue("@l", H(lemma));
            if (weighted) insert.Parameters.AddWithValue("@w", weight);
            insert.ExecuteNonQuery();
        }
        int rank = 0;
        foreach (var (lemma, form, channel) in exp)
        {
            // "syn" rows are trusted (tanach); "syn-bavli" is a syn row from the untrusted side.
            bool untrusted = channel == "syn-bavli";
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO exp VALUES(@l, @r, @f, @c, @src)";
            insert.Parameters.AddWithValue("@l", H(lemma));
            insert.Parameters.AddWithValue("@r", rank++);
            insert.Parameters.AddWithValue("@f", H(form));
            insert.Parameters.AddWithValue("@c", untrusted ? "syn" : channel);
            insert.Parameters.AddWithValue("@src", untrusted ? "bavli" : "tanach");
            insert.ExecuteNonQuery();
        }
    }

    private static string Ascii(string s) =>
        string.Concat(s.Select(c => c >= 0x05D0 && c <= 0x05EA ? ((char)('a' + (c - 0x05D0))).ToString() : c.ToString()));
}
