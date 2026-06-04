using System;
using System.Globalization;
using System.IO;
using System.Text;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;

namespace SearchEngine.Tokenization
{
    /// <summary>
    /// Custom analyzer for Hebrew text that matches FtsLib tokenization:
    /// - Strips HTML tags and entities
    /// - Removes nikud (diacritics) and cantillation marks
    /// - Removes geresh/gershayim
    /// - Lowercases ASCII
    /// - Filters words by length (2–29 chars)
    /// - Uses maqaf as word separator
    /// </summary>
    public sealed class HebrewAnalyzer : Analyzer
    {
        protected override TokenStreamComponents CreateComponents(string fieldName, TextReader reader)
        {
            var tokenizer = new HebrewTokenizer(reader);
            return new TokenStreamComponents(tokenizer);
        }

        public override int GetPositionIncrementGap(string fieldName) => 0;
    }

    /// <summary>
    /// Tokenizer that implements FtsLib-compatible Hebrew text processing.
    /// Single-pass, HTML-aware: tag content is skipped entirely (including
    /// any letters inside attribute values), matching the behaviour of
    /// <c>FtsLib.Tokenization.HtmlWordScanner</c>.
    /// </summary>
    internal sealed class HebrewTokenizer : Tokenizer
    {
        private readonly ICharTermAttribute _termAttr;
        private readonly IOffsetAttribute   _offsetAttr;
        private readonly StringBuilder      _buffer = new StringBuilder(64);
        private int  _offset;
        private bool _eof;
        private bool _inTag;   // true while inside a <...> tag

        public HebrewTokenizer(TextReader input)
            : base(input)
        {
            _termAttr   = AddAttribute<ICharTermAttribute>();
            _offsetAttr = AddAttribute<IOffsetAttribute>();
        }

        public override bool IncrementToken()
        {
            while (true)
            {
                ClearAttributes();

                if (_eof)
                    return false;

                _buffer.Clear();
                int startOffset = _offset;
                int c;

                // ── Skip until we find the start of a word ────────────────
                while ((c = m_input.Read()) != -1)
                {
                    _offset++;
                    char ch = (char)c;

                    if (_inTag)
                    {
                        if (ch == '>') _inTag = false;
                        continue;
                    }
                    if (ch == '<') { _inTag = true; continue; }
                    if (ch == '\u05BE') continue; // maqaf
                    if (ch == '\u05F3' || ch == '\u05F4' || ch == '"') continue;
                    if (ch >= '\u0591' && ch <= '\u05C7'
                        && ch != '\u05C0' && ch != '\u05C3' && ch != '\u05C6') continue;
                    if (ch > 127 && CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                        continue;

                    if (IsLetter(ch))
                    {
                        startOffset = _offset - 1;
                        if (ch >= 'A' && ch <= 'Z') ch = (char)(ch | 32);
                        _buffer.Append(ch);
                        break;
                    }
                }

                if (c == -1)
                {
                    _eof = true;
                    return false;
                }

                // ── Accumulate the rest of the word ──────────────────────
                while ((c = m_input.Read()) != -1)
                {
                    _offset++;
                    char ch = (char)c;

                    if (ch == '<') { _inTag = true; break; }
                    if (ch == '\u05BE') break;
                    if (ch == '\u05F3' || ch == '\u05F4' || ch == '"') continue;
                    if (ch >= '\u0591' && ch <= '\u05C7'
                        && ch != '\u05C0' && ch != '\u05C3' && ch != '\u05C6') continue;
                    if (ch > 127 && CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                        continue;

                    if (IsLetter(ch))
                    {
                        if (ch >= 'A' && ch <= 'Z') ch = (char)(ch | 32);
                        _buffer.Append(ch);
                    }
                    else
                    {
                        break;
                    }
                }

                if (c == -1) _eof = true;

                // ── Length filter (2–29 chars, matching FtsLib) ───────────
                // Loop rather than recurse to avoid stack overflow on long runs of
                // single-character tokens.
                if (_buffer.Length < 2 || _buffer.Length >= 30)
                    continue; // restart the outer loop to find the next word

                _termAttr.SetEmpty().Append(_buffer);
                _offsetAttr.SetOffset(startOffset, _offset);
                return true;
            }
        }

        public override void Reset()
        {
            base.Reset();
            _offset = 0;
            _eof    = false;
            _inTag  = false;
            _buffer.Clear();
        }

        private static bool IsLetter(char c)
            => (c >= 'a' && c <= 'z')
            || (c >= 'A' && c <= 'Z')
            || (c >= '\u05D0' && c <= '\u05EA');
    }
}
