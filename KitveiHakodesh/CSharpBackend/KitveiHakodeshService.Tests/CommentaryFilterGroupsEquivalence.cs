// Commentary filter tree: the old frontend algorithm (buildStaticCommentaryFilterGroups +
// buildCommentaryGroupsFromEntries, ported here onto the two per-RPC Core queries it called)
// is the judge; SeforimDbQueries.GetCommentaryFilterGroups must give the same groups, in the
// same order, with the same labels, categories and connection types. Also times both.
//
// Usage: dotnet run -c Release -- --filter-groups [--db <seforim.db>] [--ref-db <seforim.db>] [--new-first]
//   --ref-db: the sample is given as book ids of the reference DB and resolved BY TITLE in --db.
using System.Diagnostics;
using KitveiHakodesh.Core.SeforimDb;

namespace KitveiHakodeshService.Tests;

internal static class CommentaryFilterGroupsEquivalence
{
    // Bavli Berakhot, Rif Berakhot, Genesis, Mishna Berura, SA Orach Chaim, 7414, Rashi and
    // Shiltei on Berakhot, Mishna Berakhot, Exodus, a commentary on SA OC, and a spread of others.
    private static readonly int[] SampleBooks = { 103, 2460, 1, 3860, 382, 7414, 2182, 2231, 40, 2, 7504, 5, 300, 1000, 5000 };

    public static int Run(string dbPath, string? refDbPath, bool newFirst)
    {
        using (var p = Process.GetCurrentProcess()) p.PriorityClass = ProcessPriorityClass.Idle;
        var db = new SeforimDbQueries(dbPath);
        var sw = Stopwatch.StartNew();
        db.GetAllBooks();
        db.GetAllCategories();
        Console.WriteLine($"catalog load {sw.Elapsed.TotalMilliseconds:F0} ms (paid once per DB by both paths)");

        var sample = new List<(int RefId, int Id)>();
        if (refDbPath != null)
        {
            var refDb = new SeforimDbQueries(refDbPath);
            var byTitle = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var b in db.GetAllBooks().OrderBy(b => b.Id)) byTitle.TryAdd(b.Title ?? "", b.Id);
            foreach (int refId in SampleBooks)
            {
                string? title = refDb.GetAllBooks().FirstOrDefault(b => b.Id == refId)?.Title;
                if (title != null && byTitle.TryGetValue(title, out int id)) sample.Add((refId, id));
                else Console.WriteLine($"ref book {refId}: no book with that title here");
            }
        }
        else sample.AddRange(SampleBooks.Select(id => (id, id)));

        var old = new OldAlgorithm(db);
        int mismatches = 0, groupsTotal = 0;
        var timing = new List<string>();
        foreach (var (refId, bookId) in sample)
        {
            double oldCold, oldWarm, newCold, newWarm;
            List<string> a, b;
            if (newFirst)
            {
                (newCold, b) = TimeNew(db, bookId); (newWarm, _) = TimeNew(db, bookId);
                (oldCold, a) = TimeOld(old, bookId); (oldWarm, _) = TimeOld(old, bookId);
            }
            else
            {
                (oldCold, a) = TimeOld(old, bookId); (oldWarm, _) = TimeOld(old, bookId);
                (newCold, b) = TimeNew(db, bookId); (newWarm, _) = TimeNew(db, bookId);
            }
            groupsTotal += b.Count;
            bool equal = a.SequenceEqual(b);
            if (!equal)
            {
                mismatches++;
                Console.WriteLine($"MISMATCH book {bookId}: old {a.Count} groups, new {b.Count}");
                for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
                {
                    string x = i < a.Count ? a[i] : "-", y = i < b.Count ? b[i] : "-";
                    if (x != y) Console.WriteLine($"  #{i} old {x}\n      new {y}");
                }
            }
            timing.Add($"{refId,6} {bookId,6} | {b.Count,5} | {oldCold,8:F1} | {oldWarm,8:F1} | {newCold,8:F1} | {newWarm,8:F1} | {(equal ? "equal" : "DIFF")}");
        }
        Console.WriteLine();
        Console.WriteLine("ref id     id | groups | old cold | old warm | new cold | new warm   (ms, Core-direct; old = 2 queries + grouping, RPC overhead not included)");
        foreach (var t in timing) Console.WriteLine(t);
        Console.WriteLine($"books {sample.Count}, groups {groupsTotal}, mismatches {mismatches}");
        return mismatches == 0 ? 0 : 1;
    }

    private static (double, List<string>) TimeOld(OldAlgorithm old, int bookId)
    {
        var sw = Stopwatch.StartNew();
        var r = old.Build(bookId);
        return (sw.Elapsed.TotalMilliseconds, r);
    }

    private static (double, List<string>) TimeNew(SeforimDbQueries db, int bookId)
    {
        var sw = Stopwatch.StartNew();
        var r = db.GetCommentaryFilterGroups(bookId);
        double ms = sw.Elapsed.TotalMilliseconds;
        return (ms, r.Groups.Select(g => Describe(g.BookId, g.BookTitle, g.Path, g.ConnectionTypes, g.Category, g.SectionLabel, g.SubSectionLabel)).ToList());
    }

    private static string Describe(int id, string title, string path, List<string> types, string? cat, string? section, string? sub) =>
        $"{id}|{Ascii(title)}|{Ascii(path)}|{string.Join(",", types)}|{Ascii(cat)}|{Ascii(section)}|{Ascii(sub)}";

    private static string Ascii(string? s) => s == null ? "null"
        : string.Concat(s.Select(ch => ch < 128 ? ch.ToString() : ((int)ch).ToString("X4")));

    // === The old frontend algorithm ===

    private sealed class OldAlgorithm
    {
        private readonly SeforimDbQueries _db;
        private readonly Dictionary<int, string> _typeNames;
        private readonly List<int> _typeIds;

        public OldAlgorithm(SeforimDbQueries db)
        {
            _db = db;
            _typeNames = db.GetAllConnectionTypes().ToDictionary(r => r.Id, r => r.Name);
            // getBaseTextReverseConnectionTypeIds
            _typeIds = new[] { "COMMENTARY", "SUPER_COMMENTARY", "TARGUM", "MIDRASH", "PARSHANUT", "DIBUR_HAMATCHIL" }
                .Select(n => _typeNames.FirstOrDefault(kv => kv.Value == n)).Where(kv => kv.Value != null).Select(kv => kv.Key).ToList();
        }

        private static readonly string[] Priority = { "SOURCE", "TARGUM", "COMMENTARY", "EIN_MISHPAT", "MESORAH_HASHAS", "OTHER", "REFERENCE" };
        private static string Primary(List<string> types) { foreach (var t in Priority) if (types.Contains(t)) return t; return types.Count > 0 ? types[0] : "OTHER"; }
        private static string Normalize(string n) => n switch
        {
            "SOURCE" => "SOURCE", "TARGUM" => "TARGUM", "COMMENTARY" or "SUPER_COMMENTARY" or "PARSHANUT" or "MIDRASH" => "COMMENTARY",
            "REFERENCE" or "MISHNAH_IN_TALMUD" => "REFERENCE", "EIN_MISHPAT" => "EIN_MISHPAT", "MESORAH_HASHAS" => "MESORAH_HASHAS", _ => "OTHER",
        };
        private static readonly Dictionary<string, string> SectionLabels = new()
        {
            ["SOURCE"] = "מקושרים", ["TARGUM"] = "תרגומים",
            ["COMMENTARY"] = "מפרשים", ["EIN_MISHPAT"] = "עין משפט",
            ["MESORAH_HASHAS"] = "מסורת הש״ס", ["OTHER"] = "קשרים",
            ["REFERENCE"] = "ציונים",
        };

        private sealed class Entry { public int Id; public string Title = ""; public List<string> Types = new(); public string Cat = ""; public int Tree; public string Primary = ""; }

        public List<string> Build(int bookId)
        {
            // The two RPCs (each its own connection, as each RPC was).
            var forward = _db.GetStaticFilterBooks(bookId, _typeIds);
            var reverse = _db.GetReverseBooks(bookId, _typeIds);
            var result = new List<string>();
            if (forward.Count == 0 && reverse.Count == 0) return result;
            var books = _db.GetAllBooks().ToDictionary(b => b.Id);
            var cats = _db.GetAllCategories().ToDictionary(c => c.Id);

            var byBook = new Dictionary<int, List<string>>();
            var order = new List<int>();
            foreach (var row in forward)
            {
                if (!byBook.TryGetValue(row.TargetBookId, out var set)) { byBook[row.TargetBookId] = set = new(); order.Add(row.TargetBookId); }
                var canon = Normalize(_typeNames.TryGetValue(row.ConnectionTypeId, out var n) ? n : row.ConnectionTypeId.ToString());
                if (!set.Contains(canon)) set.Add(canon);
            }
            Entry E(int id, List<string> types, string primary)
            {
                books.TryGetValue(id, out var b);
                return new Entry
                {
                    Id = id,
                    // resolveBookTitle / commentaryDisplayTitle
                    Title = !string.IsNullOrEmpty(b?.Title) ? SeforimDbQueries.CommentaryDisplayTitle(b!.Title) : id.ToString(),
                    Types = types,
                    // resolveCategory over findCategoryMeta (ported in SeforimDbBookCommentaryCategory)
                    Cat = b == null ? SeforimDbBookCommentaryCategory.OtherCategory : SeforimDbBookCommentaryCategory.Resolve(b.CategoryId, cats),
                    Tree = b?.TreeOrder ?? 999999,
                    Primary = primary,
                };
            }
            var entries = new List<Entry>();
            foreach (var r in reverse) entries.Add(E(r.SourceBookId, new List<string> { "SOURCE" }, "SOURCE"));
            foreach (var id in order) entries.Add(E(id, byBook[id], Primary(byBook[id])));

            // buildCommentaryGroupsFromEntries (JS sort is stable, as OrderBy is)
            void Flat(string type)
            {
                string label = SectionLabels[type];
                foreach (var e in entries.Where(e => e.Primary == type).OrderBy(e => e.Tree))
                    result.Add(Describe(e.Id, e.Title, $"{e.Title} · {label}", e.Types, e.Cat, label, null));
            }
            void Merged(string type)
            {
                string label = SectionLabels[type];
                var items = entries.Where(e => e.Primary == type).ToList();
                var catOrder = new List<string>();
                foreach (var e in items) if (!catOrder.Contains(e.Cat)) catOrder.Add(e.Cat);
                foreach (var c in catOrder.OrderBy(SeforimDbBookCommentaryCategory.Rank))
                    foreach (var e in items.Where(e => e.Cat == c).OrderBy(e => e.Tree))
                        result.Add(Describe(e.Id, e.Title, $"{e.Title} · {label} · {c}", e.Types, c, label, c));
            }
            Flat("SOURCE"); Flat("TARGUM"); Merged("COMMENTARY"); Flat("EIN_MISHPAT"); Merged("OTHER"); Flat("REFERENCE");
            return result;
        }
    }
}
