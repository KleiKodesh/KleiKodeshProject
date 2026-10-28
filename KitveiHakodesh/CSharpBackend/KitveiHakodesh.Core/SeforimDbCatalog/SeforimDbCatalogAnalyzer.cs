using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;

namespace KitveiHakodesh.Core.SeforimDbCatalog;

/// <summary>
/// The shared normalization pipeline as a Lucene analyzer: indexing runs text through
/// SeforimDbCatalogTextNormalizer.Tokenize, exactly like query parsing does. Index time
/// and query time MUST meet at the same tokens, which is the whole reason this exists -
/// its own file because both the build (IndexWriterConfig) and the query side share it.
/// The tokenizer is nested and private: it is how this analyzer tokenizes, not a job of
/// its own, and nothing else constructs one.
///
/// One field differs: the spelling-variant skeleton field indexes each word as
/// "skeleton + U+0001 + word" (see SeforimDbCatalogTextNormalizer.DecomposeSkeleton), so the
/// query side lists every indexed word sharing a skeleton with one term-range seek instead
/// of scanning the vocabulary.
///
/// Tokenizations are memoized per text: every doc of a book carries the same catalog path
/// and authors, and each text is analyzed twice (its own field and the skeleton field).
/// One analyzer serves one build, which adds documents on one thread; the memo is cleared
/// when it grows large.
/// </summary>
internal sealed class SeforimDbCatalogAnalyzer : Analyzer
{
    private const int MaxMemoizedTexts = 50_000;
    private readonly Dictionary<string, List<string>> _tokensByText = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _skeletonTermByWord = new(StringComparer.Ordinal);

    // Per-field reuse: the default (global) strategy would hand the skeleton field's
    // tokenizer to every other field, and the other way round.
    public SeforimDbCatalogAnalyzer() : base(PER_FIELD_REUSE_STRATEGY) { }

    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        => new(new PipelineTokenizer(reader, this, fieldName == SeforimDbCatalogIndex.VariantSkeletonField));

    private List<string> TokensOf(string text)
    {
        if (_tokensByText.TryGetValue(text, out var tokens)) return tokens;
        if (_tokensByText.Count >= MaxMemoizedTexts) _tokensByText.Clear();
        tokens = SeforimDbCatalogTextNormalizer.Tokenize(text);
        _tokensByText[text] = tokens;
        return tokens;
    }

    private string SkeletonTermOf(string word)
    {
        if (_skeletonTermByWord.TryGetValue(word, out var term)) return term;
        if (_skeletonTermByWord.Count >= MaxMemoizedTexts) _skeletonTermByWord.Clear();
        term = SeforimDbCatalogTextNormalizer.DecomposeSkeleton(word).Skeleton + SeforimDbCatalogIndex.SkeletonSeparator + word;
        _skeletonTermByWord[word] = term;
        return term;
    }

    private sealed class PipelineTokenizer : Tokenizer
    {
        private readonly ICharTermAttribute _termAtt;
        private readonly SeforimDbCatalogAnalyzer _owner;
        private readonly bool _skeletonKeyed;
        private List<string>? _tokens;
        private int _pos;

        public PipelineTokenizer(TextReader input, SeforimDbCatalogAnalyzer owner, bool skeletonKeyed) : base(input)
        {
            _owner = owner;
            _skeletonKeyed = skeletonKeyed;
            _termAtt = AddAttribute<ICharTermAttribute>();
        }

        public override bool IncrementToken()
        {
            _tokens ??= _owner.TokensOf(m_input.ReadToEnd());
            if (_pos >= _tokens.Count) return false;
            ClearAttributes();
            string token = _tokens[_pos++];
            _termAtt.SetEmpty().Append(_skeletonKeyed ? _owner.SkeletonTermOf(token) : token);
            return true;
        }

        public override void Reset()
        {
            base.Reset();
            _tokens = null;
            _pos = 0;
        }
    }
}
