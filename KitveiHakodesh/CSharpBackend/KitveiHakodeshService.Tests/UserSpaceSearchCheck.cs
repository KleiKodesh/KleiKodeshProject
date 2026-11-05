using KitveiHakodesh.Core.LocalFiles;
using KitveiHakodesh.Core.SeforimDbCatalog;
using KitveiHakodesh.Core.UserSpace;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// UserSpace search self-test (--userspace-selftest): the file index's folder filter, the
/// folder-coverage check, and the merged ranking of file hits (and a synthetic catalog hit).
/// No seforim.db, no real index: a temp file index of a handful of paths. Hebrew names are
/// written as \u escapes. Returns the failure count.
/// </summary>
internal static class UserSpaceSearchCheck
{
    private static int _failures;

    private static void Check(bool ok, string name)
    {
        if (ok) Console.WriteLine("  PASS " + name);
        else { _failures++; Console.Error.WriteLine("  FAIL " + name); }
    }

    // Hebrew words, escaped.
    private const string Berachot = "\u05D1\u05E8\u05DB\u05D5\u05EA";          // berachot
    private const string Bava = "\u05D1\u05D1\u05D0";                           // bava
    private const string Kamma = "\u05E7\u05DE\u05D0";                          // kamma
    private const string Gemara = "\u05D2\u05DE\u05E8\u05D0";                   // gemara
    private const string Notes = "\u05D4\u05E2\u05E8\u05D5\u05EA";              // notes

    /// <summary>
    /// --userspace-bench &lt;indexDir&gt; &lt;folder&gt; &lt;query...&gt;: times the folder-scoped file query
    /// and the merged ranking against a COPY of a real file index. Dev measurement only.
    /// </summary>
    public static int Bench(string indexDir, string folder, string[] queries)
    {
        using var index = new LocalFileIndex(indexDir);
        var roots = new List<UserSpaceRoot> { new(null, folder) };
        Console.WriteLine($"docs: {index.Count()}  folder: {folder}");
        foreach (var q in queries)
        {
            for (int round = 0; round < 3; round++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var entries = index.Search(q, null, [folder], 0, out int total);
                long luceneMs = sw.ElapsedMilliseconds;
                var hits = UserSpaceSearch.Rank(q, roots, null, entries, CancellationToken.None);
                Console.WriteLine($"  q={Ascii(q),-14} round {round}: matches {total,6}  window {hits.Count,5}  lucene {luceneMs,4} ms  total {sw.ElapsedMilliseconds,4} ms");
            }
        }
        return 0;
    }

    /// <summary>
    /// --userspace-copyindex &lt;oldIndexDir&gt; &lt;newIndexDir&gt;: writes a fresh, current-format
    /// file index from the paths stored in another one (any format) - for benchmarking the
    /// current format on a real machine's paths without crawling the drive. Dev only.
    /// </summary>
    public static int CopyIndex(string oldDir, string newDir)
    {
        var paths = new List<string>();
        using (var dir = Lucene.Net.Store.FSDirectory.Open(oldDir))
        using (var reader = Lucene.Net.Index.DirectoryReader.Open(dir))
        {
            var live = Lucene.Net.Index.MultiFields.GetLiveDocs(reader);
            for (int d = 0; d < reader.MaxDoc; d++)
                if (live == null || live.Get(d))
                    paths.Add(reader.Document(d).Get("path"));
        }
        Console.WriteLine($"read {paths.Count} paths");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var index = new LocalFileIndex(newDir);
        index.RebuildFrom(paths);
        Console.WriteLine($"wrote {index.Count()} docs in {sw.ElapsedMilliseconds} ms");
        return 0;
    }

    public static int Run()
    {
        _failures = 0;
        Console.WriteLine("== LocalFileIndex folder filter");
        FolderFilter();
        Console.WriteLine("== UserSpaceFolderCheck");
        FolderCheck();
        Console.WriteLine("== UserSpaceSearch ranking");
        Ranking();
        Console.WriteLine("== LocalFileIndex Lucene pre-cap window");
        Window();
        Console.WriteLine(_failures == 0 ? "USERSPACE: PASS" : $"USERSPACE: FAIL ({_failures})");
        return _failures;
    }

    // ── Folder filter ────────────────────────────────────────────────────────

    private static void FolderFilter()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"userspace-fileindex-{Environment.ProcessId}");
        try
        {
            using var index = new LocalFileIndex(dir);
            index.RebuildFrom([
                @"C:\Foo\a.pdf",
                @"C:\Foo\Sub\b.pdf",
                @"C:\FooBar\c.pdf",
                @"D:\Books\d.pdf",
                @"C:\Other\Foo\e.pdf",
            ]);

            List<string> Under(params string[] folders)
                => index.Search("pdf", null, folders, 0, out _).Select(e => e.Path).OrderBy(p => p).ToList();
            string Show(List<string> l) => "[" + string.Join(", ", l) + "]";

            var foo = Under(@"C:\Foo");
            Check(foo.SequenceEqual([@"c:\foo\a.pdf", @"c:\foo\sub\b.pdf"]), "only files under C:\\Foo " + Show(foo));
            Check(!foo.Contains(@"c:\foobar\c.pdf"), "sibling prefix C:\\FooBar not matched");
            Check(!foo.Contains(@"c:\other\foo\e.pdf"), "folder name elsewhere not matched");
            Check(Under(@"c:\FOO\").SequenceEqual(foo), "case-insensitive + trailing backslash");
            Check(Under("C:/Foo").SequenceEqual(foo), "forward slashes");
            var two = Under(@"C:\Foo", @"D:\Books");
            Check(two.Count == 3 && two.Contains(@"d:\books\d.pdf"), "multiple folders any-of " + Show(two));
            Check(Under().Count == 0, "empty folder list = nothing");
            Check(Under(@"C:\").Count == 4, "drive root folder C:\\");
            Check(index.Search("pdf", null, 0, out int total).Count == 5 && total == 5, "old overload unscoped");
            Check(index.Search("pdf", 'd', 0, out _).Count == 1, "old overload drive scope");
            // Query words still AND within the scope.
            Check(index.Search("sub", null, [@"C:\Foo"], 0, out _).Count == 1, "words AND with folder scope");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    // ── Pre-cap window ───────────────────────────────────────────────────────

    /// <summary>
    /// More matches than LocalFileIndex.MaterializeCap under one folder, the best ones LAST in
    /// path order ("zzz"): Lucene's own ordering must put them in the window and first.
    /// </summary>
    private static void Window()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"userspace-window-{Environment.ProcessId}");
        try
        {
            const int fillers = 1500;
            var paths = new List<string>();
            for (int i = 0; i < fillers; i++)
            {
                paths.Add($@"C:\Lib\a{i:D4}\xberachotx.pdf");              // en: substring only
                paths.Add($@"C:\Lib\h{i:D4}\{Notes}{Berachot}.pdf");       // he: substring only
            }
            string[] enBest = [@"C:\Lib\zzz\berachot.docx", @"C:\Lib\zzz\berachot.pdf", @"C:\Lib\zzz\berachot notes.pdf"];
            string[] heBest = [$@"C:\Lib\zzz\{Berachot}.pdf", $@"C:\Lib\zzz\{Berachot} {Notes}.pdf"];
            paths.AddRange(enBest);
            paths.AddRange(heBest);
            paths.Add(@"C:\Elsewhere\berachot.pdf");                      // outside the folder
            // Dotless i: upper-casing then lower-casing does not give it back, so the window must
            // read this path from the stored fields, not from the sort key.
            const string oddPath = "c:\\odd\\\u0131 odd.pdf";
            paths.Add(oddPath);
            string realDir = dir + "-files";
            Directory.CreateDirectory(realDir);
            string realFile = Path.Combine(realDir, "realfile.pdf");
            File.WriteAllText(realFile, "x");
            paths.Add(realFile);

            using (var index = new LocalFileIndex(dir))
            {
                index.RebuildFrom(paths);
                string[] lib = [@"C:\Lib"];
                var libRoots = new List<UserSpaceRoot> { new(null, @"C:\Lib") };

                void Expect(string label, string query, string[] best, int wantTotal)
                {
                    var window = index.Search(query, null, lib, 0, out int total);
                    var want = best.Select(b => b.ToLowerInvariant()).ToList();
                    Check(total == wantTotal, $"{label}: total = every match ({total} == {wantTotal})");
                    Check(window.Count == LocalFileIndex.MaterializeCap, $"{label}: window is the cap ({window.Count})");
                    var head = window.Take(want.Count).Select(e => e.Path).ToList();
                    Check(head.SequenceEqual(want), $"{label}: Lucene order puts the best first, before the cap: " + string.Join(" | ", head.Select(Ascii)));
                    var ranked = UserSpaceSearch.Rank(query, libRoots, null, window, CancellationToken.None)
                        .Take(want.Count).Select(h => h.FilePath).ToList();
                    Check(ranked.SequenceEqual(want), $"{label}: merged ranking keeps them first: " + string.Join(" | ", ranked.Select(Ascii)));
                }

                // Exact titles (path order between them), then the whole-word title, all ahead of
                // 1500 substring-only files that sort before them by path.
                Expect("en", "berachot", enBest, fillers + enBest.Length);
                Expect("he", Berachot, heBest, fillers + heBest.Length);

                // The tie at the cut is decided by path: the fillers tie on every key, so the
                // window's fillers are the first ones by path.
                var en = index.Search("berachot", null, lib, 0, out _);
                Check(en[enBest.Length].Path == @"c:\lib\a0000\xberachotx.pdf"
                      && en[^1].Path == $@"c:\lib\a{LocalFileIndex.MaterializeCap - enBest.Length - 1:D4}\xberachotx.pdf",
                    "tie at the cut: smallest paths kept, in path order");

                var odd = index.Search("odd", null, [@"C:\Odd"], 0, out _);
                Check(odd.Count == 1 && odd[0].Path == oddPath, "window path exact when the sort key cannot give it back: " + string.Join(" | ", odd.Select(e => Ascii(e.Path))));
                var dated = index.Search("realfile", null, [realDir], 0, out _);
                var storedDate = index.Search("realfile", null, 0, out _);
                Check(dated.Count == 1 && dated[0].DateMs != "0" && dated[0].DateMs == storedDate[0].DateMs,
                    "window date (doc-values) equals the stored date");

                // Unscoped search: unchanged - every match, no window.
                var all = index.Search("berachot", null, 0, out int allTotal);
                Check(all.Count == fillers + enBest.Length + 1 && allTotal == all.Count, $"unscoped: every match, uncapped ({all.Count})");

                // Excluded folders apply in the query: total and window both exclusion-free.
                var excl = index.SearchUnderFolders("berachot", null, lib, [@"C:\Lib\zzz"], 0, out int exclTotal);
                Check(exclTotal == fillers && excl.All(e => !e.Path.StartsWith(@"c:\lib\zzz\")), $"excluded folder dropped in the query ({exclTotal})");

                // A live update (AddOrUpdate) writes the ordering fields too.
                index.AddOrUpdate(@"C:\Lib\zzzz\berachot.txt");
                var upd = index.Search("berachot", null, lib, 0, out int updTotal);
                Check(updTotal == fillers + enBest.Length + 1 && upd.Take(3).Any(e => e.Path == @"c:\lib\zzzz\berachot.txt"),
                    "AddOrUpdate doc carries the ordering fields (exact title in the top 3)");
            }

            // An index stamped with another format starts over.
            File.WriteAllText(Path.Combine(dir, "format-version.txt"), "1");
            File.WriteAllText(Path.Combine(dir, "usn_cursor_C.bin"), "x");
            using (var index = new LocalFileIndex(dir))
            {
                Check(index.Count() == 0 && !File.Exists(Path.Combine(dir, "usn_cursor_C.bin"))
                      && File.ReadAllText(Path.Combine(dir, "format-version.txt")) == LocalFileIndex.IndexFormatVersion,
                    "old format: index emptied, cursors dropped, stamp rewritten");
            }

            // Reopening a current-format index keeps it and the stamp.
            using (var index = new LocalFileIndex(dir))
            {
                index.AddOrUpdate(@"C:\Lib\kept.pdf");
            }
            using (var index = new LocalFileIndex(dir))
            {
                Check(index.Count() == 1 && File.Exists(Path.Combine(dir, "format-version.txt")),
                    "current format: reopened index kept");
            }
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
            try { Directory.Delete(dir + "-files", true); } catch { }
        }
    }

    // ── Folder check ─────────────────────────────────────────────────────────

    private static void FolderCheck()
    {
        string existing = @"C:\Users\Public\Documents";
        string child = Path.Combine(existing, "KleiKodeshProject");
        string Reason(string f, params string[] excl) => UserSpaceFolderCheck.Check(f, excl.ToList()).Reason;

        Check(UserSpaceFolderCheck.Check(child, []).Covered, "plain user folder is covered");
        Check(Reason(@"C:\definitely\not\here\x9") == "notFound", "missing folder -> notFound");
        Check(Reason("relative\\path") == "notFound", "relative path -> notFound");
        Check(Reason("") == "notFound", "empty -> notFound");
        Check(Reason(child, existing) == "excluded", "under excluded folder -> excluded");
        Check(Reason(existing, existing + "\\") == "excluded", "the excluded folder itself (trailing slash) -> excluded");
        Check(Reason(child, @"C:\Users\Public\Doc") == "", "excluded name-prefix boundary is not a parent");
        Check(Reason(@"C:\Windows\System32") == "systemFolder", "C:\\Windows\\System32 -> systemFolder");
        Check(Reason(Path.GetTempPath()) == "systemFolder", "temp folder -> systemFolder");
        var nonNtfs = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.DriveFormat != "NTFS");
        if (nonNtfs != null)
            Check(Reason(nonNtfs.RootDirectory.FullName) == "notIndexedDrive", $"non-NTFS {nonNtfs.Name} -> notIndexedDrive");
        else
            Console.WriteLine("  SKIP notIndexedDrive (no ready non-NTFS drive on this machine)");
    }

    // ── Ranking ──────────────────────────────────────────────────────────────

    private static List<string> RankFiles(string query, string[] rootFolders, params string[] files)
    {
        var roots = rootFolders.Select(f => new UserSpaceRoot(null, f)).ToList();
        var entries = files.Select(f => new PathEntry(f.ToLowerInvariant(), "0")).ToList();
        return UserSpaceSearch.Rank(query, roots, null, entries, CancellationToken.None)
            .Select(h => h.FilePath).ToList();
    }

    private static void Order(string name, string query, string[] roots, string[] expected, params string[] input)
    {
        var got = RankFiles(query, roots, input);
        var want = expected.Select(e => e.ToLowerInvariant()).ToList();
        bool ok = got.SequenceEqual(want);
        Check(ok, name);
        if (!ok)
        {
            Console.Error.WriteLine("     want: " + string.Join(" | ", want.Select(Ascii)));
            Console.Error.WriteLine("     got:  " + string.Join(" | ", got.Select(Ascii)));
        }
    }

    /// <summary>Hebrew printed as \u escapes so the console stays ASCII.</summary>
    private static string Ascii(string s) =>
        string.Concat(s.Select(c => c < 128 ? c.ToString() : $"\\u{(int)c:X4}"));

    private static void Ranking()
    {
        string[] lib = [@"C:\Lib"];

        // Exact title before a title merely containing the word (input in the "wrong" order).
        Order("en: exact title before containing title", "berachot", lib,
            [@"C:\Lib\berachot.pdf", @"C:\Lib\berachot notes.pdf"],
            @"C:\Lib\berachot notes.pdf", @"C:\Lib\berachot.pdf");
        Order("he: exact title before containing title", Berachot, lib,
            [$@"C:\Lib\{Berachot}.pdf", $@"C:\Lib\{Berachot} {Notes}.pdf"],
            $@"C:\Lib\{Berachot} {Notes}.pdf", $@"C:\Lib\{Berachot}.pdf");

        // Whole word before substring-only.
        Order("en: whole word before substring", "shas", lib,
            [@"C:\Lib\shas index.pdf", @"C:\Lib\shasnotes.pdf"],
            @"C:\Lib\shasnotes.pdf", @"C:\Lib\shas index.pdf");
        Order("he: whole word before substring", Gemara, lib,
            [$@"C:\Lib\{Gemara} {Notes}.docx", $@"C:\Lib\{Gemara}{Notes}.docx"],
            $@"C:\Lib\{Gemara}{Notes}.docx", $@"C:\Lib\{Gemara} {Notes}.docx");
        Order("uppercase query matches lowercased paths", "SHAS", lib,
            [@"C:\Lib\shas index.pdf", @"C:\Lib\shasnotes.pdf"],
            @"C:\Lib\shasnotes.pdf", @"C:\Lib\shas index.pdf");

        // Two words: exact > adjacent > scattered in order > reversed (order outranks span).
        Order("en: 2 words exact, adjacent, scattered in order, reversed", "bava kamma", lib,
            [@"C:\Lib\bava kamma.pdf", @"C:\Lib\bava kamma notes.pdf", @"C:\Lib\bava notes kamma.pdf", @"C:\Lib\kamma bava notes.pdf"],
            @"C:\Lib\bava notes kamma.pdf", @"C:\Lib\kamma bava notes.pdf", @"C:\Lib\bava kamma notes.pdf", @"C:\Lib\bava kamma.pdf");
        Order("he: 2 words exact, adjacent, scattered in order, reversed", $"{Bava} {Kamma}", lib,
            [$@"C:\Lib\{Bava} {Kamma}.pdf", $@"C:\Lib\{Bava} {Kamma} {Notes}.pdf", $@"C:\Lib\{Bava} {Notes} {Kamma}.pdf", $@"C:\Lib\{Kamma} {Bava} {Notes}.pdf"],
            $@"C:\Lib\{Bava} {Notes} {Kamma}.pdf", $@"C:\Lib\{Kamma} {Bava} {Notes}.pdf", $@"C:\Lib\{Bava} {Kamma} {Notes}.pdf", $@"C:\Lib\{Bava} {Kamma}.pdf");

        // Word in a subfolder (category-path role): literal but below a title match.
        Order("title match before folder-only match", "bava", lib,
            [@"C:\Lib\bava.pdf", @"C:\Lib\bava\x.pdf"],
            @"C:\Lib\bava\x.pdf", @"C:\Lib\bava.pdf");

        // Separators in file names are word breaks (fixed: they used to glue words).
        Order("underscore/hyphen names match words whole", "bava kamma", lib,
            [@"C:\Lib\bava_kamma.pdf", @"C:\Lib\bava-kamma-notes.pdf", @"C:\Lib\bavakamma.pdf"],
            @"C:\Lib\bavakamma.pdf", @"C:\Lib\bava-kamma-notes.pdf", @"C:\Lib\bava_kamma.pdf");

        // Root order is the final tiebreak (user's order, not alphabetical).
        Order("root order tiebreak", "x", [@"C:\B", @"C:\A"],
            [@"C:\B\x.pdf", @"C:\A\x.pdf"],
            @"C:\A\x.pdf", @"C:\B\x.pdf");
        Order("same root: path order tiebreak", "x", lib,
            [@"C:\Lib\a\x.pdf", @"C:\Lib\b\x.pdf"],
            @"C:\Lib\b\x.pdf", @"C:\Lib\a\x.pdf");

        // The root's own folder name counts like a category name: its files are found (a whole
        // word, so literal), a title match still first.
        Order("root folder name matches like a category", "talmud", [@"C:\Talmud"],
            [@"C:\Talmud\talmud notes.pdf", @"C:\Talmud\berachot.pdf"],
            @"C:\Talmud\berachot.pdf", @"C:\Talmud\talmud notes.pdf");
        Order("he: root folder name matches like a category", Gemara, [$@"C:\{Gemara}"],
            [$@"C:\{Gemara}\{Berachot}.pdf"],
            $@"C:\{Gemara}\{Berachot}.pdf");
        Order("folder + title words: talmud berachot", "talmud berachot", [@"C:\Talmud"],
            [@"C:\Talmud\berachot.pdf"],
            @"C:\Talmud\berachot.pdf");

        // Synthetic catalog hit (exact book title, root 1) vs the same exact title as a file
        // (root 0): equal accuracy, so root order decides.
        var roots = new List<UserSpaceRoot> { new(null, @"C:\Lib"), new(7, null) };
        var catalogHit = new CatalogTocHit
        {
            BookId = 42, Level = 0, TreeOrder = 1, TocPath = "", IsLiteral = true, MatchesTitleOrAuthor = true,
            TitleCovered = true, TitleDistinctWords = 1, TitleLengthGap = 0, LeafCovered = true, WordsMatched = 1,
            CategoryAncestors = [7, 1],
        };
        var mixed = UserSpaceSearch.Rank("berachot", roots, [catalogHit],
            [new PathEntry(@"c:\lib\berachot notes.pdf", "0"), new PathEntry(@"c:\lib\berachot.pdf", "0")], CancellationToken.None);
        string Desc(UserSpaceSearchHit h) => h.BookId > 0 ? $"book{h.BookId}" : h.FilePath;
        var seq = mixed.Select(Desc).ToList();
        Check(seq.SequenceEqual([@"c:\lib\berachot.pdf", "book42", @"c:\lib\berachot notes.pdf"]),
            "catalog exact title interleaves with files by accuracy, root order on tie: " + string.Join(" | ", seq));
    }
}
