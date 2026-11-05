using KitveiHakodesh.Core.LocalFiles;
using KitveiHakodesh.Core.LocalFiles.ToratEmet;
using KitveiHakodesh.Core.UserSpace;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Torat Emet names and default folders (--toratemet-selftest): the Hebrew name map (path under Books), the file index terms
/// and search, the folder listing titles and order, and the default UserSpace folders on fake directory layouts.
/// Hebrew is written as \u escapes. Returns the failure count.
/// </summary>
internal static class ToratEmetNamesCheck
{
    private static int _failures;

    private static void Check(bool ok, string name)
    {
        if (ok) Console.WriteLine("  PASS " + name);
        else { _failures++; Console.Error.WriteLine("  FAIL " + name); }
    }

    private const string ToratEmetHe = "\u05EA\u05D5\u05E8\u05EA \u05D0\u05DE\u05EA";
    private const string ToraHe = "\u05EA\u05D5\u05E8\u05D4";
    private const string BereshitHe = "\u05D1\u05E8\u05D0\u05E9\u05D9\u05EA";

    public static int Run()
    {
        _failures = 0;
        Console.WriteLine("== Torat Emet names");
        Names();
        Console.WriteLine("== Torat Emet file search");
        Search();
        Console.WriteLine("== Torat Emet folder listing");
        Listing();
        Console.WriteLine("== Torat Emet default folders");
        Defaults();
        Console.WriteLine("== Torat Emet recent-items display");
        Display();
        Console.WriteLine(_failures == 0 ? "TORATEMET: PASS" : $"TORATEMET: FAIL ({_failures})");
        return _failures;
    }

    private static void Display()
    {
        string b = @"C:\x\ToratEmetInstall\Books", m = @"C:\x\ToratEmetUserData\MyBooks";
        string root = ToratEmetFolders.BooksRootTitle, mine = ToratEmetFolders.MyBooksRootTitle;
        var r = LocalPathDisplay.Resolve(
            new[] { b + @"\001_TORA\01_BERESHIT\a01_Genesis.txt", b + @"\001_TORA\01_BERESHIT", b, m + @"\Sub\my.txt", m, @"C:\Docs\a.pdf", b + @"\001_TORA\NO_SUCH.txt" },
            new[] { false, true, true, false, true, false, false });
        Check(r.Titles.Count == 7 && r.Paths.Count == 7, "batch: one answer per path");
        Check(r.Titles[0] == BereshitHe && r.Paths[0] == root + @"\" + ToraHe + @"\" + BereshitHe + (char)92 + BereshitHe, "book: Hebrew title, root + Hebrew folders, ends with the Hebrew title");
        Check(r.Titles[1] == BereshitHe && r.Paths[1] == root + @"\" + ToraHe + @"\" + BereshitHe, "folder: Hebrew name and path");
        Check(r.Titles[2] == root && r.Paths[2] == root, "Books root: root name");
        Check(r.Titles[3] == "" && r.Paths[3] == mine + @"\Sub\my.txt", "MyBooks file: root name + raw rest, no title");
        Check(r.Titles[4] == mine && r.Paths[4] == mine, "MyBooks root: root name");
        Check(r.Titles[5] == "" && r.Paths[5] == "", "ordinary file untouched");
        Check(r.Titles[6] == "" && r.Paths[6] == root + @"\" + ToraHe + @"\NO_SUCH.txt", "unknown book: no title, path still translated");
    }

    private static void Names()
    {
        // The install may be anywhere and named anything holding ToratEmet.
        foreach (string install in new[] { @"C:\Users\x\Documents\ToratEmetInstall", @"D:\Apps\MyToratEmet2" })
        {
            Check(ToratEmetNames.FolderTitle(install + @"\Books\001_TORA") == ToraHe, "folder 001_TORA under " + install);
            Check(ToratEmetNames.FolderTitle(install + @"\Books\001_TORA\01_BERESHIT") == BereshitHe, "folder 01_BERESHIT");
            Check(ToratEmetNames.FileTitle(install + @"\Books\001_TORA\01_BERESHIT\a01_Genesis.txt") == BereshitHe, "file a01_Genesis.txt");
            Check(ToratEmetNames.FileTitle((install + @"\books\001_tora\01_bereshit\A01_GENESIS.TXT")) == BereshitHe, "case-insensitive");
            Check(ToratEmetNames.FolderTitle(install + @"\Books") == null, "the Books folder itself has no title");
        }
        foreach (string install in new[] { @"C:\Users\x\Documents\ToratEmetInstall", @"d:\apps\mytoratemet2" })
        {
            Check(ToratEmetNames.DisplayFolder(install + @"\Books\001_TORA\01_BERESHIT") == install + @"\Books\" + ToraHe + @"\" + BereshitHe, "display folder under " + install);
            Check(ToratEmetNames.DisplayFolder(install + @"\Books\001_TORA\NO_SUCH_FOLDER") == install + @"\Books\" + ToraHe + @"\NO_SUCH_FOLDER", "display folder: unknown folder keeps its name");
            Check(ToratEmetNames.DisplayFolder(install + @"\Books") == "", "display folder: the Books folder itself");
        }
        Check(ToratEmetNames.DisplayFolder(@"\\srv\share\ToratEmetInstall\Books\001_TORA") == @"\\srv\share\ToratEmetInstall\Books\" + ToraHe, "display folder: network path keeps its leading backslashes");
        Check(ToratEmetNames.DisplayFolder(@"C:\x\ToratEmetUserData\MyBooks\001_TORA") == "", "display folder: user data untouched");
        Check(ToratEmetNames.DisplayFolder(@"C:\Other\Books\001_TORA") == "", "display folder: other Books untouched");
        Check(ToratEmetNames.FolderTitle(@"C:\x\ToratEmetUserData\MyBooks\001_TORA") == null, "user data stays untranslated (folder)");
        Check(ToratEmetNames.FileTitle(@"C:\x\ToratEmetUserData\MyBooks\001_TORA\01_BERESHIT\a01_Genesis.txt") == null, "user data stays untranslated (file)");
        Check(ToratEmetNames.FolderTitle(@"C:\x\Other\Books\001_TORA") == null, "a Books folder outside ToratEmet is not translated");
        var terms = ToratEmetNames.SearchTerms(@"C:\x\ToratEmetInstall\Books\001_TORA\01_BERESHIT\a01_Genesis.txt");
        Check(terms.Contains(ToratEmetHe) && terms.Contains(ToraHe) && terms.Contains(BereshitHe), "search terms: phrase, folder name, book name");
        var userTerms = ToratEmetNames.SearchTerms(@"C:\x\ToratEmetUserData\MyBooks\a.txt");
        Check(userTerms.Count == 1 && userTerms[0] == ToratEmetHe, "user data: only the phrase");
        Check(ToratEmetNames.SearchTerms(@"C:\x\Other\a.txt").Count == 0, "no terms elsewhere");
    }

    private static void Search()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"toratemet-index-{Environment.ProcessId}");
        try
        {
            using var index = new LocalFileIndex(dir);
            string install = @"C:\Users\x\Documents\ToratEmetInstall\Books";
            string mine = @"C:\Users\x\Documents\ToratEmetUserData\MyBooks";
            index.RebuildFrom([
                install + @"\001_TORA\01_BERESHIT\a01_Genesis.txt",
                install + @"\001_TORA\02_SHEMOT\b02_exodus.txt",
                mine + @"\mine1.txt",
                mine + @"\sub\mine2.txt",
                @"C:\Other\plain.txt",
            ]);
            int total;
            var all = index.Search(ToratEmetHe, null, 0, out total);
            Check(all.Count == 4, $"the Hebrew phrase finds every file of both folders ({all.Count})");
            var genesis = index.Search(BereshitHe, null, 0, out total);
            Check(genesis.Count == 1 && genesis[0].Path.EndsWith("a01_genesis.txt"), "the Hebrew book name finds the book");
            var tora = index.Search(ToraHe + " " + ToratEmetHe, null, 0, out total);
            Check(tora.Count == 2, $"folder name + phrase ({tora.Count})");
            var scoped = index.Search(ToratEmetHe, null, [install], 0, out total);
            Check(scoped.Count == 2, $"scoped to a folder (UserSpace search) finds by the Hebrew phrase ({scoped.Count})");
            var scopedName = index.Search(BereshitHe, null, [install], 0, out total);
            Check(scopedName.Count == 1, "scoped: Hebrew book name");
            Check(index.Search("plain", null, 0, out total).Count == 1, "plain names still found");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        BigSearch();
    }

    /// <summary>More matches than any page size: every file of both folders must be found by the phrase.</summary>
    private static void BigSearch()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"toratemet-bigindex-{Environment.ProcessId}");
        try
        {
            using var index = new LocalFileIndex(dir);
            string install = @"C:\Users\x\Documents\ToratEmetInstall\Books";
            string mine = @"C:\Users\x\Documents\ToratEmetUserData\MyBooks";
            var paths = new List<string>();
            for (int i = 0; i < 600; i++) paths.Add($@"{install}\{100 + i % 7:000}_X\{i % 11:00}_Y\book{i}.txt");
            for (int i = 0; i < 400; i++) paths.Add($@"{mine}\sub{i % 5}\mine{i}.txt");
            for (int i = 0; i < 50; i++) paths.Add($@"C:\Other\plain{i}.txt");
            index.RebuildFrom(paths);
            int total;
            var all = index.Search(ToratEmetHe, null, 5000, out total);
            Check(all.Count == 1000 && total == 1000, $"phrase finds all 1000 files of both folders ({all.Count}/{total})");
            Check(all.Count(e => e.Path.Contains("toratemetinstall")) == 600, "all 600 install files");
            Check(all.Count(e => e.Path.Contains("toratemetuserdata")) == 400, "all 400 user files");
            Check(index.Search(ToratEmetHe, null, 0, out total).Count == 1000, "no limit: 1000");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static void Listing()
    {
        string root = Path.Combine(Path.GetTempPath(), $"toratemet-list-{Environment.ProcessId}");
        try
        {
            string books = Path.Combine(root, "ToratEmetInstall", "Books");
            Directory.CreateDirectory(Path.Combine(books, "001_TORA", "01_BERESHIT"));
            Directory.CreateDirectory(Path.Combine(books, "001_TORA", "02_SHEMOT"));
            File.WriteAllText(Path.Combine(books, "001_TORA", "01_BERESHIT", "a01_Genesis.txt"), "x");
            var top = LocalFolderListing.List(books);
            Check(top.Folders.Count == 1 && top.FolderTitles.Count == 1 && top.FolderTitles[0] == ToraHe, "Books listing: Hebrew folder title");
            var sub = LocalFolderListing.List(Path.Combine(books, "001_TORA"));
            Check(sub.Folders.Count == 2 && sub.FolderTitles.All(t => t.Length > 0), "subfolders titled");
            // The order is the on-disk name order; the Hebrew title is display only.
            Check(sub.Folders.SequenceEqual(sub.Folders.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)), "listing order = on-disk name order");
            // Disk order and Hebrew order differ: 02_BAVLI < 10_AHAVAT_ISRAEL on disk, but the Hebrew name of AHAVAT_ISRAEL (alef) sorts before BAVLI (tav).
            string odd = Path.Combine(books, "050_ODD");
            Directory.CreateDirectory(Path.Combine(odd, "02_BAVLI"));
            Directory.CreateDirectory(Path.Combine(odd, "10_AHAVAT_ISRAEL"));
            File.WriteAllText(Path.Combine(odd, "b_file.txt"), "x");
            File.WriteAllText(Path.Combine(odd, "a_file.txt"), "x");
            var oddList = LocalFolderListing.List(odd);
            Check(oddList.Folders.Count == 2 && oddList.Folders[0] == "02_BAVLI" && oddList.Folders[1] == "10_AHAVAT_ISRAEL", "disk order kept although the Hebrew names sort the other way");
            Check(oddList.FolderTitles.Count == 2 && oddList.FolderTitles.All(t => t.Length > 0) && string.CompareOrdinal(oddList.FolderTitles[0], oddList.FolderTitles[1]) > 0, "the Hebrew titles are shown but do not set the order");
            Check(oddList.Files.SequenceEqual(new[] { "a_file.txt", "b_file.txt" }), "files in disk name order");
            var book = LocalFolderListing.List(Path.Combine(books, "001_TORA", "01_BERESHIT"));
            Check(book.Files.Count == 1 && book.FileTitles[0] == BereshitHe, "file title");
            // A book with no BooksConfig section: its own first title line (401_mishna_brura_smicha_lerabanut.txt).
            Check(ToratEmetNames.FileTitle(@"C:\x\ToratEmetInstall\Books\042_HALACHA2\401_mishna_brura_smicha_lerabanut.txt") == "\u05DE\u05E9\u05E0\u05D4 \u05D1\u05E8\u05D5\u05E8\u05D4", "fallback: the book's own first $ title");
            string mine = Path.Combine(root, "ToratEmetUserData", "MyBooks", "001_TORA");
            Directory.CreateDirectory(mine);
            Check(LocalFolderListing.List(Path.Combine(root, "ToratEmetUserData", "MyBooks")).FolderTitles.All(t => t.Length == 0), "MyBooks untranslated");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static void Defaults()
    {
        string root = Path.Combine(Path.GetTempPath(), $"toratemet-defaults-{Environment.ProcessId}");
        try
        {
            ToratEmetFolders.Probe ProbeFor(string docs, params string[] registered) => new()
            {
                DocumentsFolder = () => docs,
                RegisteredInstallRoots = () => registered,
            };
            string Case(string name) { string d = Path.Combine(root, name); Directory.CreateDirectory(d); return d; }

            // Both present under a fake Documents (the default layout).
            string docs1 = Case("both");
            Directory.CreateDirectory(Path.Combine(docs1, "ToratEmetInstall", "Books"));
            Directory.CreateDirectory(Path.Combine(docs1, "ToratEmetUserData", "MyBooks", "sub"));
            File.WriteAllText(Path.Combine(docs1, "ToratEmetUserData", "MyBooks", "sub", "a.txt"), "x");
            var both = ToratEmetFolders.Locate(ProbeFor(docs1));
            Check(both.Count == 2 && both[0].EndsWith(@"ToratEmetInstall\Books") && both[1].EndsWith(@"MyBooks"), "both present (default layout)");
            const string personalHe = "\u05EA\u05D5\u05E8\u05EA \u05D0\u05DE\u05EA - \u05E1\u05E4\u05E8\u05D9\u05DD \u05D0\u05D9\u05E9\u05D9\u05D9\u05DD";
            Check(ToratEmetFolders.RootTitle(both[0], ProbeFor(docs1)) == ToratEmetHe, "root title: Books = Torat Emet");
            Check(ToratEmetFolders.RootTitle(both[1], ProbeFor(docs1)) == personalHe, "root title: MyBooks = Torat Emet - personal books");
            Check(ToratEmetFolders.RootTitle(both[0].ToUpperInvariant(), ProbeFor(docs1)) == ToratEmetHe, "root title: case-insensitive path");
            Check(ToratEmetFolders.RootTitle(Path.Combine(docs1, "Other", "Books"), ProbeFor(docs1)) == "", "root title: any other Books folder keeps its name");
            Check(ToratEmetFolders.RootTitle(Path.Combine(both[0], "001_TORA"), ProbeFor(docs1)) == "", "root title: a subfolder is not a root");
            Check(UserSpaceDefaultFolders.Titles(new[] { both[1], @"C:\x\y" }, ProbeFor(docs1)).Titles.SequenceEqual(new[] { personalHe, "" }), "root titles: parallel list");

            // Install only.
            string docs2 = Case("installonly");
            Directory.CreateDirectory(Path.Combine(docs2, "ToratEmetInstall", "Books"));
            var only = ToratEmetFolders.Locate(ProbeFor(docs2));
            Check(only.Count == 1 && only[0].EndsWith(@"Books"), "install only");

            // MyBooks without a .txt file is not added.
            string docs3 = Case("emptymybooks");
            Directory.CreateDirectory(Path.Combine(docs3, "ToratEmetInstall", "Books"));
            Directory.CreateDirectory(Path.Combine(docs3, "ToratEmetUserData", "MyBooks", "sub"));
            File.WriteAllText(Path.Combine(docs3, "ToratEmetUserData", "MyBooks", "sub", "a.pdf"), "x");
            Check(ToratEmetFolders.Locate(ProbeFor(docs3)).Count == 1, "MyBooks without a .txt is skipped");

            // A registered install elsewhere + its sibling user data; the Documents defaults add nothing twice.
            string docs4 = Case("registered");
            string apps = Case("apps");
            Directory.CreateDirectory(Path.Combine(apps, "TE", "Books"));
            Directory.CreateDirectory(Path.Combine(apps, "ToratEmetUserData", "MyBooks"));
            File.WriteAllText(Path.Combine(apps, "ToratEmetUserData", "MyBooks", "b.txt"), "x");
            var reg = ToratEmetFolders.Locate(ProbeFor(docs4, Path.Combine(apps, "TE"), Path.Combine(apps, "TE")));
            Check(reg.Count == 2 && reg[0].EndsWith(@"TE\Books"), $"registered install + sibling user data, no duplicates ({reg.Count})");
            var regDocs = ToratEmetFolders.Locate(ProbeFor(docs1, Path.Combine(docs1, "ToratEmetInstall")));
            Check(regDocs.Count == 2, "registry and defaults pointing to the same folders: no duplicates");

            // The service only FINDS the folders and keeps no state (the host remembers what it added, beside the roots).
            Check(UserSpaceDefaultFolders.Find(ProbeFor(docs1)).Folders.Count == 2, "find lists both folders, every time (stateless)");
            Check(UserSpaceDefaultFolders.Find(ProbeFor(docs1)).Folders.Count == 2, "a second find lists them again");
            string docs5 = Case("late");
            Directory.CreateDirectory(Path.Combine(docs5, "ToratEmetInstall", "Books"));
            Directory.CreateDirectory(Path.Combine(docs5, "ToratEmetUserData", "MyBooks"));
            Check(UserSpaceDefaultFolders.Find(ProbeFor(docs5)).Folders.Count == 1, "MyBooks empty at first: only Books");
            File.WriteAllText(Path.Combine(docs5, "ToratEmetUserData", "MyBooks", "c.txt"), "x");
            var late = UserSpaceDefaultFolders.Find(ProbeFor(docs5));
            Check(late.Folders.Count == 2 && late.Folders[1].EndsWith("MyBooks"), "MyBooks is listed once it holds a .txt");
            Check(UserSpaceDefaultFolders.Find(ProbeFor(Case("none"))).Folders.Count == 0, "nothing installed: nothing listed");

            // No cap: a .txt after many other entries is found; a cancelled walk stops.
            string deep = Case("deep");
            for (int i = 0; i < 25; i++) Directory.CreateDirectory(Path.Combine(deep, "d" + i, "s"));
            for (int i = 0; i < 25; i++) File.WriteAllText(Path.Combine(deep, "d" + i, "s", "f.pdf"), "x");
            File.WriteAllText(Path.Combine(deep, "d24", "s", "z.txt"), "x");
            Check(ToratEmetFolders.ContainsTxtFile(deep), "a .txt after many other entries is found");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            bool stopped = false;
            try { ToratEmetFolders.ContainsTxtFile(deep, cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "the walk is cancellable");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
