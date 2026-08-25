using System.Collections.Generic;

namespace KitveiHakodesh.Core.Dictionary
{
    /// <summary>
    /// The shapes dictionary lookups return. Plain data, grouped in one file because they are
    /// read as one contract; no serialization attributes — the transport decides the wire
    /// format, Core never encodes one.
    /// </summary>

    /// <summary>
    /// One sense of one headword. <paramref name="Nikud"/> is the pointed spelling, stored
    /// per SENSE rather than per word so homographs that differ only in pointing stay
    /// distinct. <paramref name="Source"/> names the lexicon it came from.
    /// </summary>
    public sealed record DictionarySense(
        string Headword,
        string? Nikud,
        string Text,
        string? Source,
        int? SourceId);

    /// <summary>A related word and the relation that connects it.</summary>
    public sealed record DictionaryLink(string Kind, string Word);

    /// <summary>
    /// The result of an exact lookup. <paramref name="WordExists"/> separates "no such word"
    /// from "the word is known but has no senses" — the caller shows different things for
    /// those, and an empty row list alone cannot tell them apart.
    /// </summary>
    public sealed record DictionaryExactResult(
        List<DictionarySense> Senses,
        bool WordExists);

    /// <summary>
    /// Which expansion of an abbreviation resolved, and its senses.
    /// <see cref="None"/> when nothing did.
    /// </summary>
    public sealed record DictionaryAbbreviationMatch(
        string? MatchedCandidate,
        List<DictionarySense> Senses)
    {
        public static DictionaryAbbreviationMatch None { get; } =
            new(null, new List<DictionarySense>());

        public bool Found => MatchedCandidate != null;
    }
}
