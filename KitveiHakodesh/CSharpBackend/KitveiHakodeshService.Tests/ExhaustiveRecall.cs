using System.Diagnostics;
using KitveiHakodesh.Core.SeforimDbCatalog;
using Microsoft.Data.Sqlite;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Exhaustive recall of the catalog TOC search against the real library: what the user
/// expects when typing an address is the address itself, first.
///
///   B   every book:            query = title                     → hit #1 is that book (level 0)
///   T1  every TOC entry:       query = title + leaf text          → hit #1 is that entry (book + line)
///   T2  every TOC entry:       query = title + whole path         → hit #1 is that entry
///
///   --exhaustive out.tsv [--exh-levels N] [--exh-every K]
///     N = deepest TOC level tested (default 2); level-0 TOC rows are the title itself and
///     are not indexed as entries. K = test every K-th entry at levels above 1 (default 1).
///
/// Output is ASCII only (ids, levels, ranks), one row per query:
///   kind bookId tocId level rank hits firstBookId firstLevel firstLine expectedLine
/// rank = position of the expected hit (0 = first, -1 = absent). Only rows with rank != 0
/// are written, plus a final "# summary" block.
/// </summary>
public static class ExhaustiveRecall
{
    private sealed class Toc { public int Id, BookId, Level, LineIndex; public int? ParentId; public string Text = ""; }

    private static string LastSegment(string tocPath)
    {
        int i = tocPath.LastIndexOf(" / ", StringComparison.Ordinal);
        return (i < 0 ? tocPath : tocPath.Substring(i + 3)).Trim();
    }

    public static int Run(string dbPath, string indexPath, string outPath, int maxLevel, int every, int shard = 0, int shards = 1)
    {
        var index = new SeforimDbCatalogIndex(indexPath, dbPath);
        if (!index.TryOpenActive()) { Console.Error.WriteLine("no index at " + indexPath); return 1; }

        var books = new Dictionary<int, string>();
        var toc = new Dictionary<int, Toc>();
        using (var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ConnectionString))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT id, title FROM book";
                using var r = cmd.ExecuteReader();
                while (r.Read()) books[r.GetInt32(0)] = r.IsDBNull(1) ? "" : r.GetString(1);
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT te.id, te.bookId, te.parentId, te.level, tt.text, l.lineIndex
                                    FROM tocEntry te JOIN tocText tt ON tt.id = te.textId
                                    LEFT JOIN line l ON l.id = te.lineId
                                    WHERE te.level <= @max ORDER BY te.id";
                cmd.Parameters.AddWithValue("@max", maxLevel);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    toc[r.GetInt32(0)] = new Toc
                    {
                        Id = r.GetInt32(0), BookId = r.GetInt32(1),
                        ParentId = r.IsDBNull(2) ? null : r.GetInt32(2),
                        Level = r.GetInt32(3), Text = r.IsDBNull(4) ? "" : r.GetString(4),
                        LineIndex = r.IsDBNull(5) ? -1 : r.GetInt32(5),
                    };
            }
        }

        using var w = new StreamWriter(outPath);
        w.WriteLine("kind\tbookId\ttocId\tlevel\trank\thits\tfirstBookId\tfirstLevel\tfirstLine\texpectedLine");
        var sw = Stopwatch.StartNew();
        long queries = 0, failures = 0;
        var byKind = new Dictionary<string, (long Total, long Fail)>();

        void Record(string kind, int bookId, int tocId, int level, List<CatalogTocHit> hits, Func<CatalogTocHit, bool> isExpected, int expectedLine)
        {
            queries++;
            int rank = hits.FindIndex(h => isExpected(h));
            var s = byKind.GetValueOrDefault(kind);
            s.Total++;
            if (rank != 0)
            {
                s.Fail++; failures++;
                var f = hits.Count > 0 ? hits[0] : null;
                w.WriteLine($"{kind}\t{bookId}\t{tocId}\t{level}\t{rank}\t{hits.Count}\t{f?.BookId ?? -1}\t{f?.Level ?? -1}\t{f?.LineIndex ?? -1}\t{expectedLine}");
            }
            byKind[kind] = s;
            if (queries % 5000 == 0)
                Console.Error.WriteLine($"  {queries} queries, {failures} failures, {sw.Elapsed.TotalSeconds:F0}s");
        }

        // B: every book by its title.
        long seq = 0;
        foreach (var (id, title) in books)
        {
            if (string.IsNullOrWhiteSpace(title)) continue;
            if ((seq++ % shards) != shard) continue;
            var hits = index.Search(title);
            Record("B", id, 0, 0, hits, h => h.BookId == id && h.Level == 0, 0);
        }

        // T1/T2: TOC entries at levels 1..maxLevel.
        // The path a user would type: a root entry that only repeats the title is left out
        // (the index strips it too), so the title is never typed twice.
        string PathOf(Toc t, string title)
        {
            var parts = new List<string>();
            for (Toc? cur = t; cur != null; cur = cur.ParentId is { } pid && toc.TryGetValue(pid, out var p) ? p : null)
            {
                if (cur.Level <= 0) continue;
                bool isRoot = cur.ParentId is not { } rp || !toc.TryGetValue(rp, out var rpe) || rpe.Level <= 0;
                if (isRoot && cur != t && SeforimDbCatalogTextNormalizer.IsTitleRepeat(title, cur.Text)) continue;
                parts.Add(cur.Text);
            }
            parts.Reverse();
            return string.Join(" ", parts);
        }
        static bool EndsWith(List<string> a, List<string> b)
        {
            if (b.Count == 0 || a.Count < b.Count) return false;
            for (int i = 0; i < b.Count; i++) if (a[a.Count - b.Count + i] != b[i]) return false;
            return true;
        }
        int n = 0;
        foreach (var t in toc.Values)
        {
            if (t.Level < 1 || t.LineIndex < 0) continue;
            if (t.Level > 1 && (n++ % every) != 0) continue;
            if ((seq++ % shards) != shard) continue;
            if (!books.TryGetValue(t.BookId, out var title) || string.IsNullOrWhiteSpace(title)) continue;
            // An entry that only repeats the title is not indexed: the book itself is the answer.
            bool titleRepeat = t.Level == 1 && SeforimDbCatalogTextNormalizer.IsTitleRepeat(title, t.Text);
            string path = PathOf(t, title);
            var pathTokens = SeforimDbCatalogTextNormalizer.Tokenize(path);
            var leafTokens = SeforimDbCatalogTextNormalizer.Tokenize(t.Text);
            // Same address, compared as normalized words: a data duplicate differing by a space,
            // or a daf whose amud the index splits into a child, is still what was typed.
            bool Expected(CatalogTocHit h) => h.BookId == t.BookId
                && (titleRepeat || h.LineIndex == t.LineIndex || EndsWith(SeforimDbCatalogTextNormalizer.Tokenize(h.TocPath), pathTokens)
                    || (EndsWith(SeforimDbCatalogTextNormalizer.Tokenize(h.TocPath), leafTokens) && EndsWith(pathTokens, SeforimDbCatalogTextNormalizer.Tokenize(h.TocPath))));
            // "title + leaf" repeats across parents (perek 1 halacha 4, perek 3 halacha 4; daf 128
            // in each Zohar volume): any entry of the same book whose path ends in that leaf.
            bool ExpectedLeaf(CatalogTocHit h) => h.BookId == t.BookId
                && (titleRepeat || h.LineIndex == t.LineIndex || EndsWith(SeforimDbCatalogTextNormalizer.Tokenize(h.TocPath), leafTokens)
                    || EndsWith(SeforimDbCatalogTextNormalizer.Tokenize(h.TocPath), [.. leafTokens, "\u05E2\u05DE\u05D5\u05D3", "\u05D0"])
                    || EndsWith(SeforimDbCatalogTextNormalizer.Tokenize(h.TocPath), [.. leafTokens, "\u05E2\u05DE\u05D5\u05D3", "\u05D1"]));

            var hits1 = index.Search(title + " " + t.Text);
            Record("T1", t.BookId, t.Id, t.Level, hits1, ExpectedLeaf, t.LineIndex);

            if (t.Level > 1)
            {
                var hits2 = index.Search(title + " " + path);
                Record("T2", t.BookId, t.Id, t.Level, hits2, Expected, t.LineIndex);
            }
        }

        w.WriteLine("# summary");
        foreach (var (kind, s) in byKind) w.WriteLine($"# {kind}\ttotal={s.Total}\tfail={s.Fail}");
        w.WriteLine($"# queries={queries} failures={failures} seconds={sw.Elapsed.TotalSeconds:F0}");
        Console.WriteLine($"exhaustive: {queries} queries, {failures} failures, {sw.Elapsed.TotalSeconds:F0}s -> {outPath}");
        foreach (var (kind, s) in byKind) Console.WriteLine($"  {kind}: {s.Fail}/{s.Total}");
        return 0;
    }
}
