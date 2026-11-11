// Related-books dropdown: the old frontend algorithm (BookViewRelatedBooksDropdown.vue before
// it moved to Core, ported here line for line onto the per-RPC Core queries it called) is the
// judge; SeforimDbQueries.GetRelatedBooks / ResolveRelatedTarget must give the same list and
// the same target line on a fixed sample. Also times both.
//
// Usage: dotnet run -c Release -- --related-books [--db <seforim.db>] [--quick]
using System.Diagnostics;
using KitveiHakodesh.Core.SeforimDb;

namespace KitveiHakodeshService.Tests;

internal static class RelatedBooksEquivalence
{
    // Bavli Berakhot, Rif Berakhot, Rashi on Berakhot (chain case), Shiltei on Berakhot,
    // Mishna Berakhot, Genesis, Exodus, SA Orach Chaim, Mishna Berura, a commentary on SA OC.
    private static readonly int[] SampleBooks = { 103, 2460, 2182, 2231, 40, 1, 2, 382, 3860, 7504 };
    private static readonly double[] SamplePositions = { 0.03, 0.47, 0.91 };
    /// <summary>Old RPC round trip (8-11 ms measured); added to the old timings as an estimate.</summary>
    private const double RpcOverheadMs = 9.0;
    private const int OldTimedClicksPerBook = 10;
    private const int MaxClicksPerPosition = 25;

    public static int Run(string dbPath, bool quick)
    {
        using (var p = Process.GetCurrentProcess()) p.PriorityClass = ProcessPriorityClass.Idle;
        var db = new SeforimDbQueries(dbPath);
        db.GetAllBooks();
        db.GetAllCategories();
        if (Environment.GetEnvironmentVariable("RB_PROFILE") is string prof)
        {
            foreach (var id in prof.Split(',').Select(int.Parse))
                for (int k = 0; k < 2; k++) { var w = Stopwatch.StartNew(); db.GetRelatedBooks(id); Console.WriteLine($"book {id} total {w.Elapsed.TotalMilliseconds:F0}"); }
            return 0;
        }
        if (Environment.GetEnvironmentVariable("RB_CLICK") is string click)
        {
            var parts = click.Split(',').Select(int.Parse).ToArray();
            var list = db.GetRelatedBooks(parts[0]);
            double clickTotal = 0;
            foreach (var book in list.Sections.SelectMany(s => s.Books).Take(parts.Length > 2 ? parts[2] : 6))
                { var w = Stopwatch.StartNew(); db.ResolveRelatedTarget(parts[0], parts[1], book); clickTotal += w.Elapsed.TotalMilliseconds; }
            Console.WriteLine($"clicks total {clickTotal:F0} ms");
            return 0;
        }
        // The in-memory Seifim merge decision vs GetAllTocEntries, on every book with a Seifim structure.
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + dbPath + ";Mode=ReadOnly"))
        {
            conn.Open();
            var ids = new List<int>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT bookId FROM alt_toc_structure WHERE key = 'Seifim'";
                using var r = cmd.ExecuteReader();
                while (r.Read()) ids.Add(r.GetInt32(0));
            }
            int bad = 0, mergedCount = 0;
            foreach (int id in ids)
            {
                bool viaCore = db.GetAllTocEntries(id).Any(e => e.AltStructureId != null);
                bool viaLookup = TocSectionLookup.LoadMergedSeifim(conn, id) != null;
                if (viaCore) mergedCount++;
                if (viaCore != viaLookup) { bad++; Console.WriteLine($"MERGE MISMATCH book {id}: core {viaCore} lookup {viaLookup}"); }
            }
            Console.WriteLine($"seifim merge check: {ids.Count} books, {mergedCount} merged, mismatches {bad}");
        }
        var old = new OldAlgorithm(db);
        int listMismatch = 0, resolveMismatch = 0, resolveCount = 0;
        var mismatchLines = new List<string>();
        var timing = new List<string>();

        foreach (int bookId in SampleBooks)
        {
            Console.WriteLine($"book {bookId} ...");
            // -- list --
            old.ResetCounters();
            var sw = Stopwatch.StartNew();
            var oldList = old.List(bookId);
            double oldListMs = sw.Elapsed.TotalMilliseconds;
            int oldListRpcs = old.Rpcs;
            sw.Restart();
            var newList = db.GetRelatedBooks(bookId);
            double newListCold = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            newList = db.GetRelatedBooks(bookId);
            double newListWarm = sw.Elapsed.TotalMilliseconds;
            var a = Describe(oldList);
            var b = Describe(newList);
            if (!a.SequenceEqual(b))
            {
                listMismatch++;
                mismatchLines.Add($"LIST book {bookId}:\n  old: {string.Join(" | ", a)}\n  new: {string.Join(" | ", b)}");
            }
            int books = newList.Sections.Sum(s => s.Books.Count);
            Console.WriteLine($"  list: {books} books, {newList.Sections.Count} sections, equal={a.SequenceEqual(b)}");

            // -- clicks --
            int total = old.LineCount(bookId);
            double oldResolveMs = 0, newResolveColdMs = 0, newResolveWarmMs = 0;
            int oldResolveRpcs = 0, clicks = 0, oldTimed = 0;
            // A fixed spread of at most MaxClicksPerPosition related books (every k-th), so a run stays minutes long.
            var all = oldList.Sections.SelectMany(s => s.Books).ToList();
            int step = Math.Max(1, (all.Count + MaxClicksPerPosition - 1) / MaxClicksPerPosition);
            var related = all.Where((_, i) => i % step == 0).ToList();
            var positions = (quick ? SamplePositions.Take(1) : SamplePositions).Select(f => (int)(total * f)).ToList();
            foreach (int top in positions)
            {
                foreach (var book in related)
                {
                    // The first clicks per book are timed as the old code ran them: nothing memoized.
                    bool timeOld = oldTimed < OldTimedClicksPerBook;
                    if (timeOld) old.ClearMemo();
                    old.ResetCounters();
                    sw.Restart();
                    int? expected;
                    try { expected = old.Resolve(bookId, top, book); } catch (Exception e) { expected = null; Console.WriteLine("  old threw " + e.Message); }
                    if (timeOld)
                    {
                        oldResolveMs += sw.Elapsed.TotalMilliseconds;
                        oldResolveRpcs += old.Rpcs;
                        oldTimed++;
                    }
                    sw.Restart();
                    int? actual = db.ResolveRelatedTarget(bookId, top, book);
                    newResolveColdMs += sw.Elapsed.TotalMilliseconds;
                    sw.Restart();
                    db.ResolveRelatedTarget(bookId, top, book);
                    newResolveWarmMs += sw.Elapsed.TotalMilliseconds;
                    clicks++;
                    resolveCount++;
                    if (expected != actual)
                    {
                        resolveMismatch++;
                        mismatchLines.Add($"RESOLVE from {bookId} line {top} -> {book.BookId} (via {book.ViaSourceBookId}, chain {book.ChainBaseBookId}, base {book.IsBaseText}): old {expected?.ToString() ?? "null"} new {actual?.ToString() ?? "null"}");
                    }
                }
                Console.WriteLine($"  line {top}: {related.Count} clicks done, mismatches so far {resolveMismatch}");
            }
            timing.Add(string.Format(
                "{0,6} | {1,8:F1} ({2,3} rpc, ~{3,7:F0} with rpc) | {4,7:F1} | {5,7:F1} | {6,6} | {7,7:F1} ({8,5:F1} rpc/click, ~{9,6:F0} w/ rpc) | {10,6:F2} | {11,6:F2}",
                bookId, oldListMs, oldListRpcs, oldListMs + oldListRpcs * RpcOverheadMs, newListCold, newListWarm,
                clicks, oldTimed == 0 ? 0 : oldResolveMs / oldTimed, oldTimed == 0 ? 0 : (double)oldResolveRpcs / oldTimed,
                oldTimed == 0 ? 0 : (oldResolveMs + oldResolveRpcs * RpcOverheadMs) / oldTimed,
                clicks == 0 ? 0 : newResolveColdMs / clicks, clicks == 0 ? 0 : newResolveWarmMs / clicks));
        }

        Console.WriteLine();
        Console.WriteLine("book   | old list ms (Core-direct)                 | new cold | new warm | clicks | old ms/click                          | new cold/click | new warm/click");
        foreach (var t in timing) Console.WriteLine(t);
        Console.WriteLine();
        Console.WriteLine($"lists: {SampleBooks.Length}, mismatches {listMismatch}; clicks: {resolveCount}, mismatches {resolveMismatch}");
        foreach (var m in mismatchLines.Take(80)) Console.WriteLine(m);
        return listMismatch + resolveMismatch == 0 ? 0 : 1;
    }

    private static List<string> Describe(RelatedBooksList list) => list.Sections.Select(s =>
        (s.CommentariesOfBookId != null ? $"[of {s.CommentariesOfBookId}:{Ascii(s.CommentariesOfTitle)}]" : $"[{Ascii(s.Label)}]")
        + string.Join(",", s.Books.Select(b => $"{b.BookId}{(b.IsBaseText ? "B" : "")}{(b.ViaSourceBookId is int v ? "v" + v : "")}{(b.ChainBaseBookId is int c ? "c" + c : "")}:{Ascii(b.Title)}"))).ToList();

    private static string Ascii(string? s) => s == null ? "null"
        : string.Concat(s.Select(ch => ch < 128 ? ch.ToString() : ((int)ch).ToString("X4")));

    // \u2550\u2550\u2550 The old frontend algorithm \u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550

    private sealed class OldAlgorithm
    {
        private readonly SeforimDbQueries _db;
        private readonly Dictionary<int, List<TocEntryRow>> _tocMemo = new();
        private readonly Dictionary<int, List<(string Key, List<TocEntryRow> Entries)>> _altMemo = new();
        private readonly List<int> _baseTypeIds;
        private readonly Dictionary<int, string> _typeNames;
        public int Rpcs;

        private readonly Microsoft.Data.Sqlite.SqliteConnection _conn;

        /// <summary>The old getLinkTargetForSourceLineAndBook RPC (its Core method went with it).</summary>
        private List<(int TargetLineId, int LineIndex)> LinkTarget(int sourceLineId, int targetBookId)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = SeforimDbSqlStrings.GetLinkTargetForSourceLineAndBook;
            cmd.Parameters.AddWithValue("@sourceLineId", sourceLineId);
            cmd.Parameters.AddWithValue("@targetBookId", targetBookId);
            using var r = cmd.ExecuteReader();
            var list = new List<(int, int)>();
            while (r.Read()) list.Add((r.GetInt32(0), r.IsDBNull(1) ? 0 : r.GetInt32(1)));
            return list;
        }

        public OldAlgorithm(SeforimDbQueries db)
        {
            _db = db;
            _conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + db.DatabasePath + ";Mode=ReadOnly");
            _conn.Open();
            _typeNames = db.GetAllConnectionTypes().ToDictionary(r => r.Id, r => r.Name);
            _baseTypeIds = new[] { "COMMENTARY", "SUPER_COMMENTARY", "TARGUM", "MIDRASH", "PARSHANUT", "DIBUR_HAMATCHIL" }
                .Select(n => _typeNames.FirstOrDefault(kv => kv.Value == n)).Where(kv => kv.Value != null).Select(kv => kv.Key).ToList();
        }

        public void ResetCounters() => Rpcs = 0;
        public void ClearMemo() { _tocMemo.Clear(); _altMemo.Clear(); }
        public int LineCount(int bookId) => _db.GetBookById(bookId)?.TotalLines ?? 0;

        // -- list: buildStaticCommentaryFilterGroups + sections + loadOwnCommentaryGroups --

        private sealed class Group { public int BookId; public string Title = ""; public string? Section; public string? Sub; public List<string> Types = new(); }

        private static readonly string[] Priority = { "SOURCE", "TARGUM", "COMMENTARY", "EIN_MISHPAT", "MESORAH_HASHAS", "OTHER", "REFERENCE" };
        private static string Primary(List<string> types) { foreach (var t in Priority) if (types.Contains(t)) return t; return types.Count > 0 ? types[0] : "OTHER"; }
        private static string Normalize(string n) => n switch
        {
            "SOURCE" => "SOURCE", "TARGUM" => "TARGUM", "COMMENTARY" or "SUPER_COMMENTARY" or "PARSHANUT" or "MIDRASH" => "COMMENTARY",
            "REFERENCE" or "MISHNAH_IN_TALMUD" => "REFERENCE", "EIN_MISHPAT" => "EIN_MISHPAT", "MESORAH_HASHAS" => "MESORAH_HASHAS", _ => "OTHER",
        };
        private static readonly Dictionary<string, string> SectionLabels = new()
        {
            ["SOURCE"] = "\u05DE\u05E7\u05D5\u05E9\u05E8\u05D9\u05DD", ["TARGUM"] = "\u05EA\u05E8\u05D2\u05D5\u05DE\u05D9\u05DD",
            ["COMMENTARY"] = "\u05DE\u05E4\u05E8\u05E9\u05D9\u05DD", ["EIN_MISHPAT"] = "\u05E2\u05D9\u05DF \u05DE\u05E9\u05E4\u05D8",
            ["MESORAH_HASHAS"] = "\u05DE\u05E1\u05D5\u05E8\u05EA \u05D4\u05E9\u05F4\u05E1", ["OTHER"] = "\u05E7\u05E9\u05E8\u05D9\u05DD",
            ["REFERENCE"] = "\u05E6\u05D9\u05D5\u05E0\u05D9\u05DD",
        };

        private List<Group> StaticGroups(int bookId)
        {
            Rpcs += 2;
            var forward = _db.GetStaticFilterBooks(bookId, _baseTypeIds);
            var reverse = _db.GetReverseBooks(bookId, _baseTypeIds);
            if (forward.Count == 0 && reverse.Count == 0) return new List<Group>();
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
            var entries = new List<(int Id, string Title, List<string> Types, string Cat, int Tree, string Primary)>();
            (int, string, List<string>, string, int, string) E(int id, List<string> types, string primary)
            {
                books.TryGetValue(id, out var b);
                string title = !string.IsNullOrEmpty(b?.Title) ? SeforimDbQueries.CommentaryDisplayTitle(b!.Title) : id.ToString();
                string cat = b == null ? SeforimDbBookCommentaryCategory.OtherCategory : SeforimDbBookCommentaryCategory.Resolve(b.CategoryId, cats);
                return (id, title, types, cat, b?.TreeOrder ?? 999999, primary);
            }
            foreach (var r in reverse) entries.Add(E(r.SourceBookId, new List<string> { "SOURCE" }, "SOURCE"));
            foreach (var id in order) entries.Add(E(id, byBook[id], Primary(byBook[id])));

            var result = new List<Group>();
            void Flat(string type)
            {
                foreach (var e in entries.Where(e => e.Primary == type).OrderBy(e => e.Tree))
                    result.Add(new Group { BookId = e.Id, Title = e.Title, Section = SectionLabels[type], Types = e.Types });
            }
            void Merged(string type)
            {
                var items = entries.Where(e => e.Primary == type).ToList();
                var cats2 = new List<string>();
                foreach (var e in items) if (!cats2.Contains(e.Cat)) cats2.Add(e.Cat);
                foreach (var c in cats2.OrderBy(SeforimDbBookCommentaryCategory.Rank))
                    foreach (var e in items.Where(e => e.Cat == c).OrderBy(e => e.Tree))
                        result.Add(new Group { BookId = e.Id, Title = e.Title, Section = SectionLabels[type], Sub = c, Types = e.Types });
            }
            Flat("SOURCE"); Flat("TARGUM"); Merged("COMMENTARY"); Flat("EIN_MISHPAT"); Merged("OTHER"); Flat("REFERENCE");
            return result;
        }

        public RelatedBooksList List(int bookId)
        {
            var filterGroups = StaticGroups(bookId);
            var sectionMap = new Dictionary<string, List<RelatedBook>>();
            var sectionOrder = new List<string>();
            var commentariesOf = new Dictionary<string, (int, string)>();
            var seen = new HashSet<int>();
            foreach (var g in filterGroups)
            {
                string label = !string.IsNullOrEmpty(g.Sub) ? g.Sub! : g.Section ?? "";
                if (!sectionMap.ContainsKey(label)) { sectionMap[label] = new(); sectionOrder.Add(label); }
                if (!seen.Add(g.BookId)) continue;
                sectionMap[label].Add(new RelatedBook { BookId = g.BookId, Title = g.Title, IsBaseText = Primary(g.Types) == "SOURCE" });
            }
            var sources = filterGroups.Where(g => Primary(g.Types) == "SOURCE").ToList();
            var unique = sources.Where((g, i) => sources.FindIndex(o => o.BookId == g.BookId) == i).ToList();
            int lastSource = -1;
            for (int i = 0; i < sectionOrder.Count; i++) if (sectionMap[sectionOrder[i]].Any(b => b.IsBaseText)) lastSource = i;
            int insertAt = lastSource + 1;
            foreach (var source in unique)
            {
                var groups = StaticGroups(source.BookId);
                var primary = groups.Select(g => { Rpcs++; var r = _db.GetReverseBooks(g.BookId, _baseTypeIds); return r.Count > 0 ? (int?)r[0].SourceBookId : null; }).ToList();
                var onSource = new HashSet<int>(groups.Where((_, i) => primary[i] == source.BookId).Select(g => g.BookId));
                var own = groups.Select((g, i) => (g, b: primary[i])).Where(x => x.b == null || x.b == source.BookId || onSource.Contains(x.b.Value)).ToList();
                string label = "\u0001of " + source.BookId;
                var books = new List<RelatedBook>();
                foreach (var (g, baseId) in own)
                {
                    if (g.BookId == bookId || seen.Contains(g.BookId)) continue;
                    if (Primary(g.Types) == "SOURCE") continue;
                    seen.Add(g.BookId);
                    books.Add(new RelatedBook { BookId = g.BookId, Title = g.Title, ViaSourceBookId = source.BookId, ChainBaseBookId = baseId != null && baseId != source.BookId ? baseId : null });
                }
                sectionMap[label] = books;
                commentariesOf[label] = (source.BookId, source.Title);
                sectionOrder.Insert(insertAt++, label);
            }
            return new RelatedBooksList
            {
                Sections = sectionOrder.Where(l => sectionMap[l].Count > 0).Select(l => commentariesOf.TryGetValue(l, out var of)
                    ? new RelatedBooksSection { CommentariesOfBookId = of.Item1, CommentariesOfTitle = of.Item2, Books = sectionMap[l] }
                    : new RelatedBooksSection { Label = l, Books = sectionMap[l] }).ToList(),
            };
        }

        // -- resolve --

        private int? LineIdAt(int bookId, int lineIndex)
        {
            if (lineIndex < 0) return null;
            var ids = _db.GetLineIds(bookId, lineIndex, lineIndex + 1);
            return ids.Length == 1 && ids[0] != 0 ? ids[0] : null;
        }

        private (int LineId, int LineIndex)? ResolveLinkedLine(int fromBookId, int fromLineId, int fromLineIndex, int targetBookId)
        {
            Rpcs++;
            var direct = LinkTarget(fromLineId, targetBookId);
            if (direct.Count > 0) return direct[0];
            Rpcs++;
            var fwd = _db.GetSectionWithCommentary(fromBookId, targetBookId, fromLineIndex, true);
            if (fwd.Count > 0) { Rpcs++; var t = LinkTarget(fwd[0].Id, targetBookId); if (t.Count > 0) return t[0]; }
            Rpcs++;
            var back = _db.GetSectionWithCommentary(fromBookId, targetBookId, fromLineIndex, false);
            if (back.Count > 0) { Rpcs++; var t = LinkTarget(back[0].Id, targetBookId); if (t.Count > 0) return t[0]; }
            return null;
        }

        private (int LineId, int LineIndex)? ResolveBaseLine(int fromBookId, int top, int baseBookId)
        {
            for (int d = 0; d <= 30; d++)
                foreach (int li in d == 0 ? new[] { top } : new[] { top + d, top - d })
                {
                    var id = LineIdAt(fromBookId, li);
                    if (id == null) continue;
                    Rpcs++;
                    var hit = _db.GetReverseLineData(new List<int> { id.Value }, _baseTypeIds).FirstOrDefault(r => r.SourceBookId == baseBookId);
                    if (hit != null) return (hit.SourceLineId, hit.LineIndex);
                }
            return null;
        }

        private List<TocEntryRow> Toc(int bookId)
        {
            Rpcs++;
            if (!_tocMemo.TryGetValue(bookId, out var e)) _tocMemo[bookId] = e = _db.GetAllTocEntries(bookId);
            return e;
        }

        private List<(string, List<TocEntryRow>)> Alt(int bookId)
        {
            Rpcs++;
            if (!_altMemo.TryGetValue(bookId, out var e))
                _altMemo[bookId] = e = _db.GetAltTocStructures(bookId).Select(s => (s.Key, _db.GetAllAltTocEntries(s.Id))).ToList();
            Rpcs += e.Count;
            return e;
        }

        private static string Trim(string s) => TocSectionLookup.JsTrim(s);

        private static TocEntryRow? HeadingAt(List<TocEntryRow> entries, int li)
        {
            TocEntryRow? found = null;
            foreach (var e in entries)
            {
                if (e.LineIndex == null || e.LineIndex > li) continue;
                if (found == null || e.LineIndex > found.LineIndex || (e.LineIndex == found.LineIndex && e.Level > found.Level)) found = e;
            }
            return found;
        }

        private static (int Start, int? End)? Range(TocEntryRow entry, List<TocEntryRow> entries)
        {
            int? start = entry.LineIndex;
            if (start == null)
            {
                var children = new Dictionary<int, List<TocEntryRow>>();
                foreach (var e in entries) if (e.ParentId is int p) { if (!children.TryGetValue(p, out var l)) children[p] = l = new(); l.Add(e); }
                var stack = new Stack<TocEntryRow>(children.TryGetValue(entry.Id, out var k) ? k : new());
                while (stack.Count > 0)
                {
                    var e = stack.Pop();
                    if (e.LineIndex != null && (start == null || e.LineIndex < start)) start = e.LineIndex;
                    if (children.TryGetValue(e.Id, out var kk)) foreach (var x in kk) stack.Push(x);
                }
            }
            if (start == null) return null;
            int? end = null;
            foreach (var e in entries)
            {
                if (e.LineIndex == null || e.LineIndex <= start || e.Level > entry.Level) continue;
                if (end == null || e.LineIndex < end) end = e.LineIndex;
            }
            return (start.Value, end);
        }

        private static string Pair(TocEntryRow e, Dictionary<int, TocEntryRow> byId) =>
            (e.ParentId is int p && byId.TryGetValue(p, out var parent) ? Trim(parent.Text) : "") + " / " + Trim(e.Text);

        private static (int Start, int? End)? Cascade(List<TocEntryRow> current, List<TocEntryRow> target, int li)
        {
            var curById = new Dictionary<int, TocEntryRow>(); foreach (var e in current) curById[e.Id] = e;
            var tgtById = new Dictionary<int, TocEntryRow>(); foreach (var e in target) tgtById[e.Id] = e;
            var heading = HeadingAt(current, li);
            while (heading != null)
            {
                string label = Trim(heading.Text);
                var same = target.Where(e => Trim(e.Text) == label).ToList();
                string pair = Pair(heading, curById);
                var matches = same.Count > 1 ? same.Where(e => Pair(e, tgtById) == pair).ToList() : same;
                if (matches.Count == 1 && Range(matches[0], target) is { } r) return r;
                heading = heading.ParentId is int p && curById.TryGetValue(p, out var parent) ? parent : null;
            }
            return null;
        }

        private (int Start, int? End)? FindMatchingTocSection(int from, int li, int target)
        {
            var main = Cascade(Toc(from), Toc(target), li);
            if (main != null) return main;
            var fa = Alt(from); var ta = Alt(target);
            foreach (var (key, entries) in fa)
            {
                var t = ta.FirstOrDefault(x => x.Item1 == key);
                if (t.Item2 == null) continue;
                var alt = Cascade(entries, t.Item2, li);
                if (alt != null) return alt;
            }
            return null;
        }

        private bool DivisionMatches(int targetBookId, (int Start, int? End) section, (int Book, int Li) position)
        {
            var entries = Toc(position.Book);
            var heading = HeadingAt(entries, position.Li);
            var range = heading == null ? null : Range(heading, entries);
            if (range == null) return false;
            bool In(int x) => x >= range.Value.Start && (range.Value.End == null || x < range.Value.End);
            int count = section.End == null ? 3 : Math.Min(3, section.End.Value - section.Start);
            Rpcs++;
            var probes = _db.GetLinesPaged(targetBookId, count, section.Start);
            Rpcs += 1 + probes.Count;
            var rev = probes.Count == 0 ? new List<ReverseLineRow>() : _db.GetReverseLineData(probes.Select(p => p.Id).ToList(), _baseTypeIds);
            var fwd = probes.SelectMany(p => LinkTarget(p.Id, position.Book)).ToList();
            return rev.Any(r => r.SourceBookId == position.Book && In(r.LineIndex)) || fwd.Any(r => In(r.LineIndex));
        }

        public int? Resolve(int fromBookId, int top, RelatedBook book)
        {
            var (linked, positions) = Linked(fromBookId, top, book);
            foreach (var pos in positions)
            {
                var section = FindMatchingTocSection(pos.Book, pos.Li, book.BookId);
                if (section == null) continue;
                bool inside = linked != null && linked >= section.Value.Start && (section.Value.End == null || linked < section.Value.End);
                if (inside) return linked;
                if (DivisionMatches(book.BookId, section.Value, pos)) return section.Value.Start;
            }
            return linked;
        }

        private (int? Linked, List<(int Book, int Li)> Positions) Linked(int fromBookId, int top, RelatedBook book)
        {
            var topPos = (fromBookId, top);
            var topLine = LineIdAt(fromBookId, top);
            if (topLine == null) return (null, new() { topPos });
            if (book.ViaSourceBookId is int via && book.ChainBaseBookId != fromBookId)
            {
                var sourceLine = ResolveBaseLine(fromBookId, top, via);
                if (sourceLine == null) return (null, new() { topPos });
                if (book.ChainBaseBookId is int chain)
                {
                    var chainLine = ResolveLinkedLine(via, sourceLine.Value.LineId, sourceLine.Value.LineIndex, chain);
                    if (chainLine == null) return (null, new());
                    var t = ResolveLinkedLine(chain, chainLine.Value.LineId, chainLine.Value.LineIndex, book.BookId);
                    return (t?.LineIndex, new() { (chain, chainLine.Value.LineIndex) });
                }
                var target = ResolveLinkedLine(via, sourceLine.Value.LineId, sourceLine.Value.LineIndex, book.BookId);
                return (target?.LineIndex, new() { topPos, (via, sourceLine.Value.LineIndex) });
            }
            if (book.IsBaseText)
            {
                var baseLine = ResolveBaseLine(fromBookId, top, book.BookId);
                return (baseLine?.LineIndex, baseLine != null ? new() { topPos, (book.BookId, baseLine.Value.LineIndex) } : new() { topPos });
            }
            var direct = ResolveLinkedLine(fromBookId, topLine.Value, top, book.BookId);
            return (direct?.LineIndex, new() { topPos });
        }
    }
}
