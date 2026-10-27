using KitveiHakodesh.Core.SeforimDbCatalog;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Checks that a library listing a tractate's pages as bare "ד." / "ד:" entries (no word "דף") is read as
/// pages by the catalog indexer, and that a lone list item is not.
///
///   --treeorder-selftest (runs together with the tree-order check)
/// </summary>
public static class DafBareCheck
{
    public static int SelfTest()
    {
        int failures = 0;
        void Expect(bool ok, string what)
        {
            if (ok) Console.WriteLine("  OK: " + what);
            else { failures++; Console.WriteLine("  FAIL: " + what); }
        }

        var rows = new List<SeforimDbCatalogIndex.TocRow>
        {
            new() { Id = 1, ParentId = null, BookId = 9, LineIndex = 1, Text = "דברי דוד" },
            new() { Id = 2, ParentId = 1, BookId = 9, LineIndex = 3, Text = "מאימתי" },
            new() { Id = 3, ParentId = 2, BookId = 9, LineIndex = 4, Text = "ב." },
            new() { Id = 4, ParentId = 2, BookId = 9, LineIndex = 24, Text = "ב:" },
            new() { Id = 5, ParentId = 2, BookId = 9, LineIndex = 30, Text = "ד." },
            new() { Id = 6, ParentId = 2, BookId = 9, LineIndex = 32, Text = "ד:" },
            // a list that is not a page list: only "." marks, no page with both marks
            new() { Id = 7, ParentId = 1, BookId = 9, LineIndex = 50, Text = "א." },
            new() { Id = 8, ParentId = 1, BookId = 9, LineIndex = 52, Text = "ב." },
        };
        var result = SeforimDbCatalogIndex.ExpandDafAmudim(rows);

        var dafDalet = result.FirstOrDefault(r => r.Text == "דף ד");
        Expect(dafDalet is not null && dafDalet.ParentId == 2 && dafDalet.LineIndex == 30,
            "bare \"ד.\" / \"ד:\" under a page list become the page \"דף ד\", navigating to its amud א line");
        Expect(result.Any(r => r.Text == "עמוד ב" && r.LineIndex == 32 && r.ParentId == dafDalet?.Id),
            "the \"ד:\" entry becomes its amud ב child");
        Expect(result.Any(r => r.Text == "דף ב"), "the other page of the list is a page too");
        Expect(result.Any(r => r.Text == "א." && r.ParentId == 1) && result.Any(r => r.Text == "ב." && r.ParentId == 1),
            "list items with only one mark (no page list) are left alone");
        Expect(result.Any(r => r.Text == "מאימתי"), "ordinary entries are untouched");

        string core;
        bool amudB;
        Expect(!SeforimDbCatalogTextNormalizer.TryParseBareDafText("פרק א.", out core, out amudB)
               && !SeforimDbCatalogTextNormalizer.TryParseBareDafText("12.", out core, out amudB)
               && !SeforimDbCatalogTextNormalizer.TryParseBareDafText("ד", out core, out amudB),
            "text with other words, digits or no mark is not a bare page");
        Expect(SeforimDbCatalogTextNormalizer.TryParseBareDafText("קכ\"ג:", out core, out amudB) && core == "דף קכ\"ג" && amudB,
            "a page with a gershayim numeral parses (with the amud ב mark)");
        return failures;
    }
}
