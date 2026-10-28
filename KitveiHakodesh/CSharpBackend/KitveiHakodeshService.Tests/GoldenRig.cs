using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Golden-result rig for the catalog searches: builds the catalog index from the real
/// seforim.db into &lt;outDir&gt;/index-&lt;tag&gt;, runs a fixed query set through BOTH
/// SearchTitles and Search, and writes the full ordered result ids to
/// &lt;outDir&gt;/results-&lt;tag&gt;.txt (diff two tags by file). The query set is generated once
/// (queries.txt, deterministic seed) from the db titles, the index's own TOC paths and the
/// abbreviation table, then reused by every later tag. Prints counts and timings only.
///
///   --golden &lt;outDir&gt; &lt;tag&gt;                  build (if missing) + run all queries
///   --golden-cold &lt;outDir&gt; &lt;tag&gt; titles|toc   fresh-process first-call latency
/// </summary>
internal static class GoldenRig
{
    public static int Run(string dbPath, string outDir, string tag)
    {
        Directory.CreateDirectory(outDir);
        string indexPath = Path.Combine(outDir, "index-" + tag);
        var index = new CatalogTocIndex(indexPath, dbPath);
        if (!index.TryOpenActive())
        {
            Directory.CreateDirectory(indexPath);
            var sw0 = Stopwatch.StartNew();
            int docs = index.BuildAndSwitch(CatalogTocIndex.ComputeDbHash(dbPath));
            Console.WriteLine($"built {docs} docs in {sw0.ElapsedMilliseconds} ms");
        }

        string queryFile = Path.Combine(outDir, "queries.txt");
        if (!File.Exists(queryFile))
            File.WriteAllLines(queryFile, GenerateQueries(dbPath, index), new UTF8Encoding(false));
        var queries = File.ReadAllLines(queryFile, Encoding.UTF8);
        Console.WriteLine($"queries: {queries.Length}");

        var sb = new StringBuilder();
        var titleMs = new List<double>();
        var tocMs = new List<double>();
        for (int q = 0; q < queries.Length; q++)
        {
            var sw = Stopwatch.StartNew();
            var titles = index.SearchTitles(queries[q]);
            titleMs.Add(sw.Elapsed.TotalMilliseconds);
            sw.Restart();
            var toc = index.Search(queries[q]);
            tocMs.Add(sw.Elapsed.TotalMilliseconds);
            sb.Append(q).Append("\tT\t").AppendJoin(',', titles).Append('\n');
            sb.Append(q).Append("\tS\t");
            for (int i = 0; i < toc.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(toc[i].BookId).Append(':').Append(toc[i].LineIndex).Append(':').Append(toc[i].Level);
            }
            sb.Append('\n');
        }
        File.WriteAllText(Path.Combine(outDir, "results-" + tag + ".txt"), sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"titles warm ms: {Stats(titleMs)}");
        Console.WriteLine($"toc    warm ms: {Stats(tocMs)}");
        index.Dispose();
        return 0;
    }

    public static int Cold(string dbPath, string outDir, string tag, string kind)
    {
        var sw = Stopwatch.StartNew();
        var index = new CatalogTocIndex(Path.Combine(outDir, "index-" + tag), dbPath);
        index.TryOpenActive();
        double open = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        // The first daily-learning title query / an ordinary TOC query.
        string query = kind == "titles" ? "תהילים" : "שולחן ערוך אורח חיים סימן א";
        int n = kind == "titles" ? index.SearchTitles(query).Count : index.Search(query).Count;
        double first = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        n += kind == "titles" ? index.SearchTitles("משנה ברכות").Count : index.Search("משנה ברכות פרק ב").Count;
        double second = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        n += kind == "titles" ? index.SearchTitles(query).Count : index.Search(query).Count;
        Console.WriteLine($"{tag} {kind}: reader open {open:F1} ms, cold first call {first:F1} ms, other query {second:F1} ms, first query again {sw.Elapsed.TotalMilliseconds:F1} ms (hits {n})");
        index.Dispose();
        return 0;
    }

    private static string Stats(List<double> ms)
    {
        var s = ms.OrderBy(x => x).ToList();
        return $"total {s.Sum():F0} median {s[s.Count / 2]:F2} p95 {s[(int)(s.Count * 0.95)]:F2} max {s[^1]:F1}";
    }

    private static List<string> GenerateQueries(string dbPath, CatalogTocIndex index)
    {
        var rnd = new Random(42);
        var titles = new List<string>();
        using (var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT title FROM book ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) if (!r.IsDBNull(0)) titles.Add(r.GetString(0));
        }
        T Pick<T>(List<T> list) => list[rnd.Next(list.Count)];
        var words = titles.SelectMany(t => t.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(w => w.Length >= 2).Distinct(StringComparer.Ordinal).OrderBy(w => w, StringComparer.Ordinal).ToList();
        var hebWords = words.Where(w => w.All(c => c is >= 'א' and <= 'ת')).ToList();

        var qs = new List<string>();
        for (int i = 0; i < 120; i++) qs.Add(Pick(titles));
        for (int i = 0; i < 60; i++) qs.Add(Pick(hebWords));
        string[] prefixes = ["ה", "ו", "ב", "ל", "מ", "ש"];
        for (int i = 0; i < 40; i++) qs.Add(prefixes[rnd.Next(prefixes.Length)] + Pick(hebWords));
        for (int i = 0; i < 40; i++)
        {
            // he-stripped forms
            var w = Pick(hebWords.Where(x => x.StartsWith('ה') && x.Length > 3).ToList());
            qs.Add(w[1..]);
        }
        // plene / defective: drop a mid-word yud/vav, or insert one.
        var withMater = hebWords.Where(w => w.Length > 3 && w[1..^1].Any(c => c is 'י' or 'ו')).ToList();
        for (int i = 0; i < 40; i++)
        {
            var w = Pick(withMater);
            int k = Enumerable.Range(1, w.Length - 2).Where(j => w[j] is 'י' or 'ו').First();
            qs.Add(w.Remove(k, 1));
        }
        for (int i = 0; i < 30; i++)
        {
            var w = Pick(hebWords.Where(x => x.Length > 3).ToList());
            int k = 1 + rnd.Next(w.Length - 2);
            qs.Add(w.Insert(k, rnd.Next(2) == 0 ? "י" : "ו"));
        }
        var keys = CatalogTocTextRules.AbbreviationEntries.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        for (int i = 0; i < 60; i++)
        {
            var k = Pick(keys);
            qs.Add(k.Length >= 2 && !k.Contains(' ') && rnd.Next(2) == 0 ? k[..^1] + "\"" + k[^1..] : k);
        }
        // multi-word keys partly typed
        var multiKeys = keys.Where(k => k.Contains(' ') && k.Length > 5).ToList();
        for (int i = 0; i < 20 && multiKeys.Count > 0; i++) { var k = Pick(multiKeys); qs.Add(k[..^2]); }
        // multi-word: title + a TOC segment from the index
        for (int i = 0; i < 50; i++)
        {
            var t = Pick(titles);
            var hits = index.Search(t).Where(h => h.Level > 0).ToList();
            if (hits.Count == 0) { qs.Add(t + " " + Pick(hebWords)); continue; }
            var h = hits[rnd.Next(Math.Min(hits.Count, 50))];
            var segs = h.TocPath.Split(" / ");
            qs.Add(t + " " + segs[^1]);
        }
        // partial last word
        for (int i = 0; i < 40; i++)
        {
            var t = Pick(titles);
            if (t.Length > 4) qs.Add(t[..^(1 + rnd.Next(2))]);
        }
        for (int i = 0; i < 20; i++) { var w = Pick(hebWords); qs.Add(w[..Math.Max(1, w.Length / 2)]); }
        // numbers / daf refs
        string[] counters = ["דף", "סימן", "פרק", "סעיף", "הלכה", "משנה"];
        string[] nums = ["א", "ב", "ג", "יד", "טו", "טז", "כ", "קכ", "1", "12", "2:3", "ב.", "ב:"];
        for (int i = 0; i < 30; i++) qs.Add(counters[rnd.Next(counters.Length)] + " " + nums[rnd.Next(nums.Length)]);
        for (int i = 0; i < 20; i++) qs.Add(Pick(titles) + " " + counters[rnd.Next(counters.Length)] + " " + nums[rnd.Next(nums.Length)]);
        // single letters
        for (char c = 'א'; c <= 'ת'; c++) qs.Add(c.ToString());
        // home daily learning
        qs.AddRange(["בראשית", "שמות", "ויקרא", "במדבר", "דברים", "תהילים", "תהלים", "משנה ברכות", "משנה שבת",
            "משנה בבא קמא", "משנה עוקצין", "יהושע", "שמואל א", "מלכים ב", "דברי הימים א", "משנה תורה", "רמבם",
            "שולחן ערוך", "קיצור שולחן ערוך", "חפץ חיים", "שמירת הלשון", "תניא", "משנה ברורה", "גמרא ברכות", "ירושלמי ברכות"]);
        return qs.Select(q => q.Trim()).Where(q => q.Length > 0).ToList();
    }
}
