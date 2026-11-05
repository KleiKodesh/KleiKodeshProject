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
        Console.WriteLine(_failures == 0 ? "TORATEMET: PASS" : $"TORATEMET: FAIL ({_failures})");
        return _failures;
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
            // Ordered by the Hebrew name: shemot (\u05E9) comes after bereshit (\u05D1) although SHEMOT < BERESHIT is false anyway; force a case where the orders differ.
            Check(sub.Folders.Count == 2 && sub.FolderTitles.All(t => t.Length > 0), "subfolders titled");
            Check(string.CompareOrdinal(sub.FolderTitles[0], sub.FolderTitles[1]) < 0, "ordered by the Hebrew name");
            var book = LocalFolderListing.List(Path.Combine(books, "001_TORA", "01_BERESHIT"));
            Check(book.Files.Count == 1 && book.FileTitles[0] == BereshitHe, "file title");
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

            // Claimed once: a folder the user removed does not come back.
            string state = Path.Combine(root, "state", "defaults.txt");
            var first = UserSpaceDefaultFolders.Claim(state, ProbeFor(docs1));
            Check(first.Folders.Count == 2, "first claim hands out both folders");
            var second = UserSpaceDefaultFolders.Claim(state, ProbeFor(docs1));
            Check(second.Folders.Count == 0, "second claim: nothing again (removed on purpose stays removed)");
            string docs5 = Case("late");
            Directory.CreateDirectory(Path.Combine(docs5, "ToratEmetInstall", "Books"));
            Directory.CreateDirectory(Path.Combine(docs5, "ToratEmetUserData", "MyBooks"));
            string state2 = Path.Combine(root, "state", "late.txt");
            Check(UserSpaceDefaultFolders.Claim(state2, ProbeFor(docs5)).Folders.Count == 1, "MyBooks empty at first: only Books");
            File.WriteAllText(Path.Combine(docs5, "ToratEmetUserData", "MyBooks", "c.txt"), "x");
            var late = UserSpaceDefaultFolders.Claim(state2, ProbeFor(docs5));
            Check(late.Folders.Count == 1 && late.Folders[0].EndsWith("MyBooks"), "MyBooks is offered once it holds a .txt");
            Check(UserSpaceDefaultFolders.Claim(state2, ProbeFor(Case("none"))).Folders.Count == 0, "nothing installed: nothing handed out");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
