using System.Diagnostics;
using System.Text;
using KitveiHakodesh.Core.LocalFiles;
using KitveiHakodesh.Core.UserSpace;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Golden-result rig for the local-file searches, the file-index twin of <see cref="GoldenRig"/>:
/// opens an existing file index (build one with --userspace-copyindex), runs a fixed query set
/// through the unscoped search (as LocateAsync orders it) and the folder-scoped search (as the
/// UserSpace search orders it) under the index's three largest top folders, and writes the
/// full ordered paths to &lt;outDir&gt;/results-&lt;tag&gt;.txt (diff two tags by file). The query
/// set is generated once (queries.txt, deterministic seed) from the index's own paths, then
/// reused by every later tag.
///
///   --files-golden &lt;indexDir&gt; &lt;outDir&gt; &lt;tag&gt;
/// </summary>
internal static class FilesGoldenRig
{
    private const int MaxListed = 300;   // per query and search: enough to see every reorder that matters

    public static int Run(string indexDir, string outDir, string tag)
    {
        Directory.CreateDirectory(outDir);
        using var index = new LocalFileIndex(indexDir);
        var paths = AllPaths(index);
        Console.WriteLine($"docs: {paths.Count}");

        string folderFile = Path.Combine(outDir, "folders.txt");
        if (!File.Exists(folderFile))
            File.WriteAllLines(folderFile, LargestTopFolders(paths, 3), new UTF8Encoding(false));
        var folders = File.ReadAllLines(folderFile, Encoding.UTF8);

        string queryFile = Path.Combine(outDir, "queries.txt");
        if (!File.Exists(queryFile))
            File.WriteAllLines(queryFile, GenerateQueries(paths), new UTF8Encoding(false));
        var queries = File.ReadAllLines(queryFile, Encoding.UTF8);
        Console.WriteLine($"queries: {queries.Length}  folders: {string.Join(" | ", folders)}");

        var roots = folders.Select(f => new UserSpaceRoot(null, f)).ToList();
        var sb = new StringBuilder();
        var unscopedMs = new List<double>();
        var scopedMs = new List<double>();
        for (int q = 0; q < queries.Length; q++)
        {
            var sw = Stopwatch.StartNew();
            var entries = index.Search(queries[q], null, 0, out int total);
            unscopedMs.Add(sw.Elapsed.TotalMilliseconds);
            sb.Append(q).Append("\tU\t").Append(total).Append('\t');
            AppendPaths(sb, entries.Select(e => e.Path));

            sw.Restart();
            var scoped = index.Search(queries[q], null, folders, 0, out int scopedTotal);
            var ranked = UserSpaceSearch.Rank(queries[q], roots, null, scoped, CancellationToken.None);
            scopedMs.Add(sw.Elapsed.TotalMilliseconds);
            sb.Append(q).Append("\tF\t").Append(scopedTotal).Append('\t');
            AppendPaths(sb, ranked.Select(h => h.FilePath));
        }
        File.WriteAllText(Path.Combine(outDir, "results-" + tag + ".txt"), sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"unscoped ms: {Stats(unscopedMs)}");
        Console.WriteLine($"scoped   ms: {Stats(scopedMs)}");
        Slowest("unscoped", unscopedMs, queries);
        Slowest("scoped", scopedMs, queries);
        return 0;
    }

    private static void Slowest(string kind, List<double> ms, string[] queries)
    {
        var worst = Enumerable.Range(0, ms.Count).OrderByDescending(i => ms[i]).Take(5);
        Console.WriteLine($"slowest {kind}: " + string.Join("  ", worst.Select(i => $"q{i} [{queries[i]}] {ms[i]:F0} ms")));
    }

    private static void AppendPaths(StringBuilder sb, IEnumerable<string> paths)
    {
        int n = 0;
        foreach (var p in paths)
        {
            if (n++ == MaxListed) break;
            if (n > 1) sb.Append('|');
            sb.Append(p);
        }
        sb.Append('\n');
    }

    private static List<string> AllPaths(LocalFileIndex index)
    {
        // Every path, in index order: a search for the drive separator matches them all.
        var all = index.Search(":\\", null, 0, out _);
        return all.Select(e => e.Path).ToList();
    }

    /// <summary>The <paramref name="count"/> three-level folders ("c:\users\admin") holding the most files.</summary>
    private static IEnumerable<string> LargestTopFolders(List<string> paths, int count)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            var parts = p.Split('\\');
            if (parts.Length < 4) continue;
            string key = string.Join('\\', parts, 0, 3);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(count).Select(kv => kv.Key);
    }

    private static List<string> GenerateQueries(List<string> paths)
    {
        var rnd = new Random(42);
        T Pick<T>(List<T> list) => list[rnd.Next(list.Count)];
        var names = paths.Select(p => LocalFileNameWords.NameTokens(p)).Where(t => t.Count > 0).ToList();
        var words = names.SelectMany(t => t).Where(w => w.Length >= 3).Distinct(StringComparer.Ordinal).OrderBy(w => w, StringComparer.Ordinal).ToList();
        var hebWords = words.Where(w => w.All(c => c is >= 'א' and <= 'ת')).ToList();
        var multi = names.Where(t => t.Count >= 2).ToList();

        var qs = new List<string>
        {
            "pdf", "doc", "otzaria", "program files", "users admin", "kitvei hakodesh", "readme", "log",
            "שבת", "תורה", "הלכות שבת", "ספר", "משנה ברורה", "a", "xy", "in\\",
        };
        for (int i = 0; i < 40; i++) qs.Add(Pick(words));                                    // one whole word
        for (int i = 0; i < 20; i++) qs.Add(Pick(hebWords));                                 // one Hebrew word
        for (int i = 0; i < 40; i++) { var t = Pick(multi); qs.Add(t[0] + " " + t[^1]); }  // first + last word of one name, typed order
        for (int i = 0; i < 20; i++) { var t = Pick(multi); qs.Add(t[^1] + " " + t[0]); }  // same words, reversed
        for (int i = 0; i < 20; i++) { var t = Pick(multi); qs.Add(string.Join(' ', t)); } // the whole name
        for (int i = 0; i < 20; i++) { var w = Pick(words.Where(w => w.Length >= 6).ToList()); qs.Add(w.Substring(1, w.Length - 2)); } // mid-word substring
        for (int i = 0; i < 20; i++) qs.Add(Pick(words) + " " + Pick(words));               // two unrelated words
        return qs.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string Stats(List<double> ms)
    {
        var s = ms.OrderBy(x => x).ToList();
        return $"total {s.Sum():F0} median {s[s.Count / 2]:F2} p95 {s[(int)(s.Count * 0.95)]:F2} max {s[^1]:F1}";
    }
}
