using KitveiHakodeshService.Ipc;
using CoreDictionary = KitveiHakodesh.Core.Dictionary;

namespace KitveiHakodeshService.Dictionary;

/// <summary>
/// Transport wrapper over <see cref="CoreDictionary.DictionaryDbQueries"/>. Every lookup is
/// Core's; this class exists only to find the database once, to translate Core's models into
/// the wire envelopes the frontend already expects, and to keep a Core failure from taking
/// the RPC down.
///
/// The envelope property names (<c>Rows</c>, <c>IsExact</c>, <c>source_id</c>) are the
/// FRONTEND CONTRACT and deliberately differ from Core's clearer names — that mapping is
/// what this file is for. Do not "simplify" it by renaming either side.
/// </summary>
public sealed class DictionaryService
{
    private readonly ILogger<DictionaryService> _logger;
    private readonly CoreDictionary.DictionaryDbQueries? _dictionary;

    public DictionaryService(ILogger<DictionaryService> logger)
    {
        _logger = logger;

        // Locate() returns null when the dictionary is not installed. That is a STATE, not a
        // failure: every lookup below answers empty and the frontend shows "no entry".
        string? path = CoreDictionary.DictionaryDbQueries.Locate();
        if (path is null)
            logger.LogWarning("Dictionary database not found — dictionary lookups will return empty.");
        else
            _dictionary = new CoreDictionary.DictionaryDbQueries(path);
    }

    // ── Tier queries (driven by the frontend combinedLookup) ───────────────────

    public DictExactResult Exact(string term)
    {
        var result = Query(d => d.Exact(term), "dictExact");
        if (result is null) return new DictExactResult();
        return new DictExactResult { Rows = ToSenseRows(result.Senses), IsExact = result.WordExists };
    }

    public List<SenseRow> Prefix(string term) =>
        ToSenseRows(Query(d => d.Prefix(term), "dictPrefix"));

    public List<SenseRow> Contains(string term) =>
        ToSenseRows(Query(d => d.Contains(term), "dictContains"));

    // ── Related words ───────────────────────────────────────────────────────────

    public List<DictLink> Links(string term)
    {
        var links = Query(d => d.Links(term), "dictLinks");
        if (links is null) return [];

        var outp = new List<DictLink>(links.Count);
        foreach (var link in links)
            outp.Add(new DictLink { Kind = link.Kind, Word = link.Word });
        return outp;
    }

    public List<string> Synonyms(string term) =>
        Query(d => d.Synonyms(term), "dictSynonyms") ?? [];

    public List<string> Variants(string term) =>
        Query(d => d.Variants(term), "dictVariants") ?? [];

    // ── Spelling suggestions ────────────────────────────────────────────────────

    public List<string> SpellCandidates(string term) =>
        Query(d => d.SpellingCandidates(term), "dictSpellCandidates") ?? [];

    // ── Abbreviation tooltip (book view) ────────────────────────────────────────

    public DictAbbrevResult AbbrevSenses(List<string>? candidates)
    {
        var match = Query(d => d.AbbreviationSenses(candidates), "dictAbbrevSenses");
        if (match is null || !match.Found) return new DictAbbrevResult();
        return new DictAbbrevResult { Matched = match.MatchedCandidate, Rows = ToSenseRows(match.Senses) };
    }

    // ── Ketiv (spelling) existence check ────────────────────────────────────────

    public List<string> KetivVariants(List<string>? candidates) =>
        Query(d => d.ExistingHeadwords(candidates), "dictKetivVariants") ?? [];

    // ── seforim.db reference works (pointers into seforim.db) ──────────────────

    public List<CoreDictionary.SeforimHeadwordPointer> SeforimHeadwords(string source, string term, string mode) =>
        Query(d => d.SeforimHeadwords(source, term, mode), "dictSeforimHeadwords") ?? [];

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs one Core lookup. Core throws on a real database failure (rule 4 — it never
    /// swallows); this orchestrator is the layer that decides the user sees an empty result
    /// rather than a broken pane, so the catch belongs HERE and nowhere lower.
    /// Returns null when the dictionary is absent or the query failed.
    /// </summary>
    private T? Query<T>(Func<CoreDictionary.DictionaryDbQueries, T> lookup, string op) where T : class
    {
        if (_dictionary is null) return null;
        try { return lookup(_dictionary); }
        catch (Exception ex) { _logger.LogError(ex, "{Op} failed", op); return null; }
    }

    private static List<SenseRow> ToSenseRows(List<CoreDictionary.DictionarySense>? senses)
    {
        if (senses is null) return [];

        var rows = new List<SenseRow>(senses.Count);
        foreach (var sense in senses)
        {
            rows.Add(new SenseRow
            {
                Headword = sense.Headword,
                Nikud = sense.Nikud,
                Text = sense.Text,
                Source = sense.Source,
                SourceId = sense.SourceId,
            });
        }
        return rows;
    }
}
