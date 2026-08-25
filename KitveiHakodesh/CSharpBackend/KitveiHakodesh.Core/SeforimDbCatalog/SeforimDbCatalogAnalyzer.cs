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
/// </summary>
internal sealed class SeforimDbCatalogAnalyzer : Analyzer
{
    protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        => new(new PipelineTokenizer(reader));

    private sealed class PipelineTokenizer : Tokenizer
    {
        private readonly ICharTermAttribute _termAtt;
        private List<string>? _tokens;
        private int _pos;

        public PipelineTokenizer(TextReader input) : base(input)
        {
            _termAtt = AddAttribute<ICharTermAttribute>();
        }

        public override bool IncrementToken()
        {
            _tokens ??= SeforimDbCatalogTextNormalizer.Tokenize(m_input.ReadToEnd());
            if (_pos >= _tokens.Count) return false;
            ClearAttributes();
            _termAtt.SetEmpty().Append(_tokens[_pos++]);
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
