using KitveiHakodesh.Core.HebrewBooks;

namespace KitveiHakodeshService.Tests;

/// <summary>
/// Release CI entry point (--hb-catalog-update &lt;catalog.db&gt;): walks upstream for new
/// HebrewBooks ids and writes them into the given catalog in place, ignoring the 90-day
/// interval the running service uses. Exit 0 = walk finished; 1 = it stopped early or
/// failed (rows written before that stay). The workflow treats 1 as a warning.
/// </summary>
public static class HebrewBooksCatalogReleaseUpdate
{
    public static async Task<int> Run(string catalogPath)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var catalog = new HebrewBooksCatalogDbQueries(Path.GetFullPath(catalogPath));
        int before = catalog.MaxBookId();
        Console.WriteLine($"HebrewBooks catalog: {catalogPath}, highest id before: {before}");

        var result = await new HebrewBooksCatalogUpdater(http, catalog).UpdateAsync(CancellationToken.None);

        Console.WriteLine($"ran={result.Ran} added={result.BooksAdded} lastIdChecked={result.LastIdChecked} " +
                          $"fetchFailures={result.FetchFailures.Count} skip={result.SkipReason} error={result.Error ?? "-"}");
        return result.Ran && result.SkipReason == HebrewBooksCatalogUpdateSkip.None && result.Error is null ? 0 : 1;
    }
}
