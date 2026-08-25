using System.Collections.Generic;
using MessagePack;

namespace KitveiHakodesh.Core.Dictionary
{
    /// <summary>
    /// One sense of one headword. <see cref="Nikud"/> is the pointed spelling, stored per
    /// SENSE rather than per word so homographs that differ only in pointing stay distinct.
    /// <see cref="Source"/> names the lexicon it came from.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class DictionarySense
    {
        public string Headword { get; set; } = "";
        public string? Nikud { get; set; }
        public string Text { get; set; } = "";
        public string? Source { get; set; }
        public int? SourceId { get; set; }
    }

    /// <summary>A related word and the relation that connects it.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class DictionaryLink
    {
        public string Kind { get; set; } = "";
        public string Word { get; set; } = "";
    }

    /// <summary>
    /// The result of an exact lookup. <see cref="WordExists"/> separates "no such word" from
    /// "the word is known but has no senses" — the caller shows different things for those,
    /// and an empty row list alone cannot tell them apart.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class DictionaryExactResult
    {
        public List<DictionarySense> Senses { get; set; } = new List<DictionarySense>();
        public bool WordExists { get; set; }
    }

    /// <summary>
    /// Which expansion of an abbreviation resolved, and its senses.
    /// <see cref="MatchedCandidate"/> is null when none did.
    /// </summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed class DictionaryAbbreviationMatch
    {
        public string? MatchedCandidate { get; set; }
        public List<DictionarySense> Senses { get; set; } = new List<DictionarySense>();

        [IgnoreMember]
        public bool Found => MatchedCandidate != null;
    }
}
