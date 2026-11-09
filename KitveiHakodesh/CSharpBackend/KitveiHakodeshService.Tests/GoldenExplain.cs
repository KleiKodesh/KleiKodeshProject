using System.Text;
using KitveiHakodesh.Core.SeforimDbCatalog;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Shows WHY two golden tags order a query differently: for each query index given, the
/// baseline's top hits and the tag's top hits with their TOC paths and ranking keys (read by
/// re-running the query on the tag's index; a baseline id the tag no longer returns is
/// printed as its id only).
///
///   --golden-explain &lt;outDir&gt; &lt;tag&gt; &lt;q,q,...&gt;
/// </summary>
internal static class GoldenExplain
{
    public static int Run(string dbPath, string outDir, string tag, string queryIndices)
    {
        var queries = File.ReadAllLines(Path.Combine(outDir, "queries.txt"), Encoding.UTF8);
        var baseline = ReadResults(Path.Combine(outDir, "results-baseline.txt"));
        var index = new CatalogTocIndex(Path.Combine(outDir, "index-" + tag), dbPath);
        if (!index.TryOpenActive()) { Console.Error.WriteLine("no index for tag " + tag); return 2; }

        foreach (var part in queryIndices.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int q = int.Parse(part);
            var hits = index.Search(queries[q]);
            var byId = new Dictionary<string, CatalogTocHit>();
            foreach (var h in hits) byId.TryAdd($"{h.BookId}:{h.LineIndex}:{h.Level}", h);
            Console.WriteLine($"=== q{q} [{queries[q]}]  baseline {baseline[q].Count} hits, {tag} {hits.Count} hits");
            Console.WriteLine("  baseline:");
            foreach (var id in baseline[q].Take(6))
                Console.WriteLine("    " + (byId.TryGetValue(id, out var h) ? Describe(h) : id + "  (not in the new top list)"));
            Console.WriteLine($"  {tag}:");
            foreach (var h in hits.Take(6)) Console.WriteLine("    " + Describe(h));
        }
        index.Dispose();
        return 0;
    }

    private static string Describe(CatalogTocHit h)
    {
        var k = h.Keys;
        var flags = new StringBuilder();
        if (k.IsLiteral) flags.Append(" lit");
        if (k.MatchesTitleOrAuthor) flags.Append(" title/author");
        if (k.TitleCovered) flags.Append($" titleCovered({k.TitleDistinctWords},gap{k.TitleLengthGap})");
        if (k.LeafCovered) flags.Append(" leafCovered");
        if (k.LeafAddsWord) flags.Append(" leafAdds");
        if (k.QuotedFormMatch) flags.Append(" quoted");
        flags.Append($" matched{k.WordsMatched}");
        if (k.WordsFound > 0) flags.Append($" found{k.WordsFound}{(k.QueryInOrder ? " inOrder" : " outOfOrder")} span{k.WordSpan}");
        return $"{h.BookId}:{h.LineIndex}:{h.Level} s{h.StructureId} r{h.TreeOrder >> 24}  {h.FullTocPath}  [{flags.ToString().Trim()}]";
    }

    private static List<List<string>> ReadResults(string file)
    {
        var perQuery = new List<List<string>>();
        foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
        {
            var p = line.Split('\t');
            if (p[1] != "S") continue;
            perQuery.Add(p[2].Length == 0 ? [] : p[2].Split(',').ToList());
        }
        return perQuery;
    }
}
