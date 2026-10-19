using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Data.Sqlite;
using KitveiHakodesh.Core.Common;

namespace KitveiHakodesh.Core.HebrewBooks
{
    /// <summary>
    /// Keeps the bundled catalog current by walking upstream book ids past the highest one we
    /// already hold and appending whatever it finds.
    ///
    /// This scrapes someone else's site, so it is deliberately unhurried: a completed walk
    /// happens a few times a year, it waits a second between requests, and one instance never
    /// runs two walks at once (the guard is per-instance — it does not coordinate across
    /// processes, so there should only ever be one caller driving this).
    ///
    /// IT CAN STOP EARLY. When requests stop completing — offline, a filtering proxy, an
    /// outage — it gives up after a few consecutive failures and reports
    /// <see cref="HebrewBooksCatalogUpdateSkip.NoConnection"/> rather than spending its whole
    /// missing-id budget on requests that were always going to fail. Such a run is left
    /// UNSTAMPED on purpose: it established nothing, so it is still due. That means the
    /// interval below bounds how often a walk COMPLETES, not how often one starts.
    ///
    /// It writes into the catalog IN PLACE — there is one catalog file, never a second copy
    /// shadowing it. On a read-only install that means the catalog cannot grow, which is
    /// reported (<see cref="HebrewBooksCatalogUpdateSkip.CatalogNotWritable"/>) rather than
    /// silently ignored; search keeps working on what shipped.
    /// </summary>
    public sealed class HebrewBooksCatalogUpdater
    {
        private const string BookPageUrlFormat = "https://beta.hebrewbooks.org/{0}";

        /// <summary>
        /// Roughly a quarter, between COMPLETED walks. The upstream catalog grows slowly and
        /// every run is traffic on someone else's server, so monthly — what this did before —
        /// asks far more often than the data changes. A const, not a setting: nobody has asked
        /// to tune it.
        ///
        /// A run that stops early is not stamped and so is still due; a caller that retries
        /// immediately would walk again at once, which is why the service backs off instead.
        /// </summary>
        public const int UpdateIntervalDays = 90;

        /// <summary>
        /// How many ids in a row may come back empty before the walk concludes it has reached
        /// the end.
        ///
        /// MEASURED, NOT GUESSED. Upstream ids are sparse — this catalog holds 59,583 books
        /// across an id range ending at 69,871, and the gaps between consecutive ids reach
        /// 2,766 overall and 1,447 within the last ten thousand ids. The original value of 10
        /// was therefore not merely conservative but broken: the walk would have halted at the
        /// first ordinary gap, and because it resumes from the highest id it HOLDS, every
        /// later run would halt in the same place. The catalog would have stopped growing
        /// permanently, quietly.
        ///
        /// 1,500 clears every gap ever observed here. The cost is a tail of at most 1,500
        /// requests, about 25 minutes at the delay below, once a quarter.
        /// </summary>
        private const int MaxConsecutiveMissingIds = 1500;

        /// <summary>
        /// How many requests in a row may FAIL TO COMPLETE before the walk concludes there is
        /// no connection and stops.
        ///
        /// A failed request is not evidence that an id is empty, but it used to count towards
        /// the missing-id budget all the same — so an offline machine walked the full 1,500,
        /// a second apart, and spent twenty-five minutes failing before it gave up. That is
        /// the ordinary state of a laptop, not a fault.
        ///
        /// Five, not one: a single dropped request mid-walk is normal and must not abandon a
        /// run that is otherwise working. Five consecutive failures is a network that is gone,
        /// and costs five seconds to establish.
        /// </summary>
        private const int MaxConsecutiveFetchFailures = 5;

        /// <summary>A second between requests. This is a courtesy to the site being scraped,
        /// not a rate limit anyone imposed — do not shorten it to make a run finish sooner.</summary>
        private const int RequestDelayMs = 1000;

        /// <summary>Where the last run's timestamp lives: in the catalog's own _metadata table,
        /// NOT the registry. The stamp belongs to the file, so replacing the catalog replaces
        /// its history too, and a second machine reading the same file does not re-walk ground
        /// the first one covered.</summary>
        private const string LastScrapeMetadataKey = "last_scrape_date";

        // The ASP.NET label ids on a book page. If upstream renames these, every field comes
        // back empty and the walk reports finding nothing rather than writing blank rows.
        private const string TitleElementId = "cpMstr_lblHebSefername";
        private const string AuthorElementId = "cpMstr_lblHebAuth";
        private const string PlaceElementId = "cpMstr_lblHebPlace";
        private const string YearElementId = "cpMstr_lblHebDate";
        private const string PagesElementId = "cpMstr_lblPages";
        private const string TagsContainerId = "cpMstr_pnltag";

        private const string TagNodesXPath = ".//span[@class='tag'] | .//*[contains(@class,'tag')]";

        private const string InsertBookSql =
            "INSERT OR REPLACE INTO hebrewBooks " +
            "(id, title, author, placeOfPublication, year, pageCount, categories) " +
            "VALUES (@id, @title, @author, @place, @year, @pages, @categories)";

        private const string StampMetadataSql =
            "INSERT OR REPLACE INTO _metadata (key, value) VALUES (@key, @value)";

        private readonly HttpClient _http;
        private readonly HebrewBooksCatalogDbQueries _catalog;

        /// <summary>0 while no walk is running. Two concurrent walks would both resume from the
        /// same id and duplicate every request.</summary>
        private int _running;

        public HebrewBooksCatalogUpdater(HttpClient http, HebrewBooksCatalogDbQueries catalog)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>When the catalog was last walked, or null if it never was.</summary>
        public DateTime? LastUpdatedUtc()
        {
            string? stamp = _catalog.ReadMetadata(LastScrapeMetadataKey);
            if (string.IsNullOrWhiteSpace(stamp)) return null;

            return DateTime.TryParse(
                stamp, CultureInfo.InvariantCulture,
                // RoundtripKind cannot be combined with AdjustToUniversal (TryParse throws);
                // AssumeUniversal covers a stamp written without its trailing Z.
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime parsed)
                ? parsed
                : (DateTime?)null;
        }

        public bool IsDue()
        {
            DateTime? last = LastUpdatedUtc();
            return last == null || (DateTime.UtcNow - last.Value).TotalDays >= UpdateIntervalDays;
        }

        /// <summary>
        /// Walks and updates the catalog if it is due, otherwise returns saying why not.
        ///
        /// AWAIT THIS. It is a long, chatty job — the caller decides where it runs and gets a
        /// result it can report; the old version fired it into a <c>Task.Run</c> and wrote its
        /// outcome to <c>Debug.WriteLine</c>, so a run that half-failed said nothing anywhere.
        /// </summary>
        public Task<HebrewBooksCatalogUpdateResult> UpdateIfDueAsync(CancellationToken cancellationToken)
        {
            if (!IsDue())
            {
                return Task.FromResult(new HebrewBooksCatalogUpdateResult
                {
                    Ran = false,
                    SkipReason = HebrewBooksCatalogUpdateSkip.NotDueYet,
                });
            }

            return UpdateAsync(cancellationToken);
        }

        /// <summary>Walks and updates the catalog regardless of when it last ran.</summary>
        public async Task<HebrewBooksCatalogUpdateResult> UpdateAsync(CancellationToken cancellationToken)
        {
            var result = new HebrewBooksCatalogUpdateResult();

            if (!_catalog.IsAvailable)
            {
                result.SkipReason = HebrewBooksCatalogUpdateSkip.CatalogMissing;
                return result;
            }

            if (!AppFileLocator.IsWritable(Path.GetDirectoryName(_catalog.DatabasePath)))
            {
                result.SkipReason = HebrewBooksCatalogUpdateSkip.CatalogNotWritable;
                return result;
            }

            if (Interlocked.Exchange(ref _running, 1) == 1)
            {
                // A walk is already going. Reporting "not due" would be a lie, but starting a
                // second one that re-requests every id the first is already fetching is worse.
                result.SkipReason = HebrewBooksCatalogUpdateSkip.NotDueYet;
                return result;
            }

            try
            {
                result.Ran = true;
                await WalkAsync(result, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw; // the host is shutting down; rows written so far stay, the walk resumes past them
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }

            return result;
        }

        private async Task WalkAsync(HebrewBooksCatalogUpdateResult result, CancellationToken cancellationToken)
        {
            int bookId = _catalog.MaxBookId() + 1;
            int consecutiveMissing = 0;
            int consecutiveFailures = 0;

            using var connection = SqliteConnectionFactory.OpenBundledWrite(_catalog.DatabasePath);

            while (consecutiveMissing < MaxConsecutiveMissingIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FetchOutcome outcome = await FetchBookAsync(bookId, cancellationToken).ConfigureAwait(false);

                if (outcome.Book != null)
                {
                    Insert(connection, outcome.Book);
                    result.BooksAdded++;
                    consecutiveMissing = 0;
                    consecutiveFailures = 0;
                }
                else if (outcome.FetchFailed)
                {
                    // Could not REACH the page. That says nothing about whether a book lives
                    // at this id, so it must not count towards the missing-id budget — doing
                    // so is what made an offline run walk 1,500 ids to learn it was offline.
                    result.FetchFailures.Add(bookId);
                    consecutiveFailures++;

                    if (consecutiveFailures >= MaxConsecutiveFetchFailures)
                    {
                        // Leave the stamp unwritten: nothing was established about the upstream
                        // catalog, so the next due check should try again rather than wait out
                        // another full interval.
                        result.SkipReason = HebrewBooksCatalogUpdateSkip.NoConnection;
                        return;
                    }
                }
                else
                {
                    // The page answered and held no book. THIS is a missing id.
                    //
                    // It does NOT reset the failure budget. Only a book actually found does,
                    // because only that proves the connection is working. Resetting here would
                    // mean an upstream alternating 404 and 503 — a filtering proxy, a flaky
                    // CDN — never accumulates five failures in a row, so it walks the whole
                    // 1,500-id budget and then STAMPS a clean completion for a run that
                    // established nothing, putting the catalog to sleep for a full interval.
                    consecutiveMissing++;
                }

                result.LastIdChecked = bookId;
                bookId++;

                await Task.Delay(RequestDelayMs, cancellationToken).ConfigureAwait(false);
            }

            Stamp(connection, DateTime.UtcNow);
        }

        private readonly struct FetchOutcome
        {
            public HebrewBook? Book { get; }

            /// <summary>True when the page could not be fetched at all, as opposed to upstream
            /// answering that there is no book with this id.</summary>
            public bool FetchFailed { get; }

            private FetchOutcome(HebrewBook? book, bool fetchFailed)
            {
                Book = book;
                FetchFailed = fetchFailed;
            }

            public static FetchOutcome Found(HebrewBook book) => new FetchOutcome(book, false);
            public static readonly FetchOutcome NoSuchBook = new FetchOutcome(null, false);
            public static readonly FetchOutcome Unreachable = new FetchOutcome(null, true);
        }

        private async Task<FetchOutcome> FetchBookAsync(int bookId, CancellationToken cancellationToken)
        {
            string html;
            try
            {
                using var response = await _http
                    .GetAsync(string.Format(BookPageUrlFormat, bookId), cancellationToken)
                    .ConfigureAwait(false);

                // NOT every failure status means "no book here". A 404 does. A 403, a 429 or
                // any 5xx means the SERVER refused or broke, which says nothing about the id —
                // and treating those as empty ids is how a blanket refusal (a rate limit, a
                // filtering proxy, an outage) walks the entire missing-id budget pretending it
                // had reached the end of the catalog. Verified in the wild: upstream answers
                // 403 to every id from some networks, which looked exactly like 1,500 empty
                // ids in a row and cost twenty-five minutes to conclude nothing.
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return FetchOutcome.NoSuchBook;
                if (!response.IsSuccessStatusCode)
                    return FetchOutcome.Unreachable;
                html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return FetchOutcome.Unreachable;
            }

            var page = new HtmlDocument();
            page.LoadHtml(html);

            string title = ElementText(page, TitleElementId);
            string author = ElementText(page, AuthorElementId);
            string place = ElementText(page, PlaceElementId);
            string year = ElementText(page, YearElementId);
            string pages = ElementText(page, PagesElementId);
            string categories = TagText(page, TagsContainerId);

            // Every field blank means there is no book here — a missing id renders the page
            // shell with its labels empty rather than returning an error status.
            if (title.Length == 0 && author.Length == 0 && place.Length == 0
                && year.Length == 0 && pages.Length == 0 && categories.Length == 0)
                return FetchOutcome.NoSuchBook;

            return FetchOutcome.Found(new HebrewBook
            {
                Id = bookId,
                Title = title,
                Author = author,
                PrintingPlace = place,
                PrintingYear = year,
                Pages = int.TryParse(pages, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pageCount)
                    ? pageCount
                    : (int?)null,
                Categories = categories,
            });
        }

        private static string ElementText(HtmlDocument page, string elementId)
        {
            HtmlNode? node = page.GetElementbyId(elementId);
            if (node == null) return "";
            return HtmlEntity.DeEntitize(node.InnerText).Replace("\n", " ").Trim();
        }

        private static string TagText(HtmlDocument page, string containerId)
        {
            HtmlNode? container = page.GetElementbyId(containerId);
            if (container == null) return "";

            var tags = new StringBuilder();
            HtmlNodeCollection? nodes = container.SelectNodes(TagNodesXPath);
            if (nodes == null) return "";

            foreach (HtmlNode node in nodes)
            {
                string tag = HtmlEntity.DeEntitize(node.InnerText).Trim();
                if (tag.Length == 0) continue;
                if (tags.Length > 0) tags.Append(';');
                tags.Append(tag);
            }

            return tags.ToString();
        }

        /// <summary>
        /// Writes one book. Every field is a parameter, so a title containing a comma or a
        /// quote lands exactly as upstream has it — the CSV version this replaces rewrote
        /// commas to " -" and silently corrupted those titles.
        /// </summary>
        private static void Insert(SqliteConnection connection, HebrewBook book)
        {
            using var command = connection.CreateCommand();
            command.CommandText = InsertBookSql;
            command.Parameters.AddWithValue("@id", book.Id);
            command.Parameters.AddWithValue("@title", book.Title);
            command.Parameters.AddWithValue("@author", book.Author);
            command.Parameters.AddWithValue("@place", book.PrintingPlace);
            command.Parameters.AddWithValue("@year", book.PrintingYear);
            command.Parameters.AddWithValue("@pages", (object?)book.Pages ?? DBNull.Value);
            command.Parameters.AddWithValue("@categories", book.Categories);
            command.ExecuteNonQuery();
        }

        private static void Stamp(SqliteConnection connection, DateTime whenUtc)
        {
            using var command = connection.CreateCommand();
            command.CommandText = StampMetadataSql;
            command.Parameters.AddWithValue("@key", LastScrapeMetadataKey);
            command.Parameters.AddWithValue("@value", whenUtc.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }
    }
}
