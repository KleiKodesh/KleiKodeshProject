namespace KitveiHakodeshService.HebrewBooks;

using CoreHebrewBooks = global::KitveiHakodesh.Core.HebrewBooks;

/// <summary>
/// Keeps the bundled HebrewBooks catalog growing, in the background, without anyone asking.
///
/// WHY THE SERVICE AND NOT THE APP. A walk is a long, slow job — a second between requests, as
/// a courtesy to the site being scraped — so it needs a process that outlives a window the user
/// closes. The service is already that process.
///
/// WHEN IT RUNS. Once per interval, decided by a stamp inside the catalog file itself, not by
/// this class: <c>UpdateIfDueAsync</c> answers "not due yet" and costs one metadata read when
/// there is nothing to do. The stamp living in the FILE is what makes this safe to ask often —
/// replacing the catalog replaces its history, and a second machine reading the same file does
/// not re-walk ground someone else covered.
///
/// WHY IT CHECKS MORE OFTEN THAN IT RUNS. A service can be restarted or left running for
/// months, so a once-at-startup check would either miss the due date entirely or only ever fire
/// on a machine that reboots. It wakes periodically and asks; the answer is almost always no.
///
/// RESUMING. Nothing is remembered here. The walk resumes from the highest id the catalog
/// holds, so a run cut short by a shutdown simply continues next time, and an interrupted run
/// is left UNSTAMPED so it is still due.
/// </summary>
public sealed class HebrewBooksCatalogUpdateService(
    ILogger<HebrewBooksCatalogUpdateService> logger,
    HttpClient http) : BackgroundService
{
    private int _running;

    /// <summary>A catalog walk is running; the service must not stop under it
    /// (Http/OwnerWatcher).</summary>
    public bool IsBusy => Volatile.Read(ref _running) == 1;

    /// <summary>
    /// How long to wait before the FIRST check. The service has just started, which is when
    /// everything else it owns is competing for the disk — the indexes check their stamps, the
    /// pipe comes up, the frontend asks its opening questions. A catalog walk is the least
    /// urgent thing here and can afford to let that settle.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How often to ASK whether a walk is due — not how often one runs. The interval between
    /// actual runs is Core's <c>UpdateIntervalDays</c>, enforced by the stamp in the catalog.
    /// Twelve hours keeps a long-running service from drifting far past its due date while
    /// costing one metadata read twice a day.
    /// </summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);

    /// <summary>
    /// How long to wait after a walk that ENDED WITHOUT STAMPING before trying again.
    ///
    /// An unstamped run stays due, so the normal check interval would start another walk at
    /// the next tick. That is right when the machine was simply offline — five requests, five
    /// seconds, nothing lost. It is wrong when the upstream is reachable but flaky: the walk
    /// gets most of the way through the missing-id budget, trips the failure budget near the
    /// end, and re-walks the same fifteen hundred ids twelve hours later, forever, with no
    /// stamp ever written to stop it.
    ///
    /// Three days is long enough that the pathological case costs a couple of walks a week
    /// rather than sixty, and short enough that a machine which comes back online picks the
    /// work up long before the quarterly interval would have.
    /// </summary>
    private static readonly TimeSpan RetryAfterUnstampedRun = TimeSpan.FromDays(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            bool reportedMissing = false;

            while (!stoppingToken.IsCancellationRequested)
            {
                // Located each time round, not once at startup. A catalog can ARRIVE after the
                // service is up — an installer finishing, a repair, an update dropping the file
                // in — and this service is built to run for months, so a one-shot probe would
                // mean never noticing until a restart. It is one file probe twice a day.
                string? catalogPath = CoreHebrewBooks.HebrewBooksCatalogDbQueries.Locate();
                if (catalogPath is null)
                {
                    // Said once, not twice a day forever.
                    if (!reportedMissing)
                    {
                        logger.LogWarning(
                            "HebrewBooks catalog not found — background updates idle until one appears");
                        reportedMissing = true;
                    }
                    await Task.Delay(CheckInterval, stoppingToken);
                    continue;
                }

                reportedMissing = false;
                var updater = new CoreHebrewBooks.HebrewBooksCatalogUpdater(
                    http, new CoreHebrewBooks.HebrewBooksCatalogDbQueries(catalogPath));

                bool stampedCleanly;
                Volatile.Write(ref _running, 1);
                try { stampedCleanly = await RunOnceAsync(updater, stoppingToken); }
                finally { Volatile.Write(ref _running, 0); }

                // A run that ended without stamping is still due, so asking again at the normal
                // cadence would restart it immediately. Back off instead — see the constant.
                await Task.Delay(stampedCleanly ? CheckInterval : RetryAfterUnstampedRun,
                                 stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down. Rows written so far stay; the walk resumes past them next run.
        }
    }

    /// <summary>
    /// One due-check, and the walk it may start. Returns whether the catalog was left STAMPED
    /// — false when nothing ran, when the walk stopped early, or when it threw, all of which
    /// leave the run still due and so want a longer wait before the next attempt.
    /// </summary>
    private async Task<bool> RunOnceAsync(
        CoreHebrewBooks.HebrewBooksCatalogUpdater updater, CancellationToken stoppingToken)
    {
        try
        {
            CoreHebrewBooks.HebrewBooksCatalogUpdateResult result =
                await updater.UpdateIfDueAsync(stoppingToken);

            if (!result.Ran)
            {
                // Not due is the overwhelmingly common answer and says nothing worth a line in
                // the log. The others are states someone may need to explain later.
                if (result.SkipReason != CoreHebrewBooks.HebrewBooksCatalogUpdateSkip.NotDueYet)
                    logger.LogInformation(
                        "HebrewBooks catalog update skipped: {Reason}", result.SkipReason);

                // Nothing ran. "Not due" means the stamp is fresh, so the normal cadence is
                // right; the other reasons are standing conditions that a backoff would not
                // help with either. Either way, treat it as no reason to wait longer.
                return true;
            }

            if (result.SkipReason == CoreHebrewBooks.HebrewBooksCatalogUpdateSkip.NoConnection)
            {
                // Expected on a laptop. Information, not a warning: the run is left unstamped
                // and the next check tries again.
                logger.LogInformation(
                    "HebrewBooks catalog update stopped — upstream unreachable (added {Added} before giving up)",
                    result.BooksAdded);
                return false;
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                logger.LogWarning(
                    "HebrewBooks catalog update failed after {Added} book(s): {Error}",
                    result.BooksAdded, result.Error);
                return false;
            }

            logger.LogInformation(
                "HebrewBooks catalog updated — {Added} book(s) added, walked to id {LastId}, {Failures} fetch failure(s)",
                result.BooksAdded, result.LastIdChecked, result.FetchFailures.Count);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw; // shutting down — let ExecuteAsync end the loop
        }
        catch (Exception ex)
        {
            // LOAD-BEARING, not merely tidy. An exception escaping a BackgroundService takes
            // the WHOLE HOST down by default (BackgroundServiceExceptionBehavior.StopHost),
            // so an unhandled fault in a background catalog walk would stop the service that
            // serves every search. Catching it also keeps the loop alive for the next attempt.
            logger.LogWarning(ex, "HebrewBooks catalog update threw");
            return false;
        }
    }
}
