using KitveiHakodesh.Core.SeforimDbFullTextSearch;

namespace KitveiHakodeshService.SeforimDb;

/// <summary>
/// Transport wrapper over <see cref="SeforimDbFtsRelatedFormExpander"/> — the related-form
/// query rewrite. Core owns the artifact, the SQL and the rewrite; what is left here is DI
/// lifetime and one log line when the artifact is missing.
///
/// The hosted leg (KitveiHakodeshLib) rewrites queries through the SAME Core expander, so the
/// two hosts can no longer answer one query differently.
///
/// The old SEARCH_EXPANSION_DB environment override is gone: Core reads no environment
/// variables and finds the artifact by probing. Nothing in this repo ever SET the variable —
/// it was only ever read — so there is nothing to migrate.
/// </summary>
public sealed class SearchExpansionService
{
    /// <summary>Max expansion terms added per query word. Expansion breadth is a
    /// per-term knob — it is NOT a result cap (results are never capped).</summary>
    public const int PerTermLimit = SeforimDbFtsRelatedFormExpander.PerTermLimit;

    private readonly SeforimDbFtsRelatedFormExpander _expander = new();

    public SearchExpansionService(ILogger<SearchExpansionService> logger)
    {
        if (!_expander.IsAvailable)
            logger.LogInformation(
                "Search-expansion artifact not installed — queries are searched unexpanded.");
    }

    /// <summary>True when the artifact is installed. False is a STATE, not a failure: the
    /// expander is inert and queries pass through unchanged.</summary>
    public bool IsAvailable => _expander.IsAvailable;

    /// <summary>Rewrites a query so each plain Hebrew word gains its related forms as OR
    /// alternatives. Never throws — see Core for why expansion degrades rather than fails.</summary>
    public string RewriteQuery(string query, int perTerm = PerTermLimit) =>
        _expander.RewriteQuery(query, perTerm);
}
