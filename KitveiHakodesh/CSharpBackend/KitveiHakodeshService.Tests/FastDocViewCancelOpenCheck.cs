using KitveiHakodesh.Core.FastDocView;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Core's open cancellation by the page's open id:
///   --fastdocview-cancel-open &lt;file that takes a while to open cold, e.g. a big .doc&gt;
/// A cancel that arrives before its open registered (requests race) still stops that open; a cancel during the open
/// stops it; a cancel for another id does not touch an open; nothing stays open after a cancelled open.
/// </summary>
public static class FastDocViewCancelOpenCheck
{
    public static int Run(string source)
    {
        int failures = 0;
        void Expect(bool ok, string what)
        {
            if (!ok) failures++;
            Console.WriteLine((ok ? "  ok:   " : "  FAIL: ") + what);
        }
        string IndexCache() => FastDocViewOpenDocuments.IndexCacheDirectory;
        void DropSidecars()
        {
            if (!Directory.Exists(IndexCache())) return;
            string sidecar = Path.GetFileNameWithoutExtension(global::FastDocView.WordDocumentIndexCacheLocation.IndexPathFor(source, IndexCache()));
            foreach (string file in Directory.GetFiles(IndexCache(), sidecar + "*")) File.Delete(file);
        }

        using var documents = new FastDocViewOpenDocuments();
        bool Cancelled(Action open)
        {
            try { open(); return false; }
            catch (OperationCanceledException) { return true; }
        }

        DropSidecars();
        documents.CancelOpen("early");
        Expect(Cancelled(() => documents.Open(source, "early")), "a cancel that arrived before the open registered stops it");

        DropSidecars();
        var opening = Task.Run(() => Cancelled(() => documents.Open(source, "during")));
        Thread.Sleep(60);
        documents.CancelOpen("during");
        Expect(opening.Wait(30_000) && opening.Result, "a cancel during the open stops it");

        DropSidecars();
        documents.CancelOpen("someone-else");
        FastDocViewSummary opened = documents.Open(source, "untouched");
        Expect(opened.BlockCount > 0, "an open with another id is not touched");
        documents.Close(opened.DocumentId);
        Expect(!Cancelled(() => documents.Close(documents.Open(source, "early").DocumentId)), "an early cancel is used once only");
        Console.WriteLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }
}
