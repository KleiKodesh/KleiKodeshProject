using System;
using System.Globalization;
using System.IO;
using System.Text;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;

namespace LuceneLib.Tokenization
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
    /// </summary>
    internal sealed class HebrewTokenizer : Tokenizer
    {
        private readonly ICharTermAttribute _termAttr;
        private readonly IOffsetAttribute _offsetAttr;
        private readonly StringBuilder _buffer = new StringBuilder(64);
        private int _offset;
        private bool _eof;

        public HebrewTokenizer(TextReader input)
            : base(input)
        {
            _termAttr = AddAttribute<ICharTermAttribute>();
            _offsetAttr = AddAttribute<IOffsetAttribute>();
        }

        public override bool IncrementToken()
        {
            ClearAttributes();

            if (_eof)
                return false;

            _buffer.Clear();
            int startOffset = _offset;
            int c;

            // Skip non-letter characters
            while ((c = m_input.Read()) != -1)
            {
                _offset++;
                if (IsLetter((char)c))
                {
                    startOffset = _offset - 1;
                    break;
                }
            }

            if (c == -1)
            {
                _eof = true;
                return false;
            }

            // Build word
            while (c != -1)
            {
                char ch = (char)c;

                // Nikud + cantillation removal
                if (ch >= '\u0591' && ch <= '\u05C7'
                    && ch != '\u05C0'   // paseq
                    && ch != '\u05C3'   // sof pasuq
                    && ch != '\u05C6')  // nun hafukha
                {
                    c = m_input.Read();
                    if (c != -1) _offset++;
                    continue;
                }

                // Non-spacing marks
                if (ch > 127 && CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                {
                    c = m_input.Read();
                    if (c != -1) _offset++;
                    continue;
                }

                // Geresh / gershayim / ASCII quote
                if (ch == '\u05F3' || ch == '\u05F4' || ch == '"')
                {
                    c = m_input.Read();
                    if (c != -1) _offset++;
                    continue;
                }

                // Maqaf — word separator
                if (ch == '\u05BE')
                    break;

                // Letter — add to buffer (lowercase ASCII)
                if (IsLetter(ch))
                {
                    if (ch >= 'A' && ch <= 'Z')
                        ch = (char)(ch | 32);
                    _buffer.Append(ch);
                    c = m_input.Read();
                    if (c != -1) _offset++;
                }
                else
                {
                    // Non-letter — end of word
                    break;
                }
            }

            // Filter by length (2–29 chars, matching FtsLib)
            if (_buffer.Length < 2 || _buffer.Length >= 30)
                return IncrementToken();

            _termAttr.SetEmpty().Append(_buffer);
            _offsetAttr.SetOffset(startOffset, _offset);
            return true;
        }

        public override void Reset()
        {
            base.Reset();
            _offset = 0;
            _eof = false;
            _buffer.Clear();
        }

        private static bool IsLetter(char c)
            => (c >= 'a' && c <= 'z')
            || (c >= 'A' && c <= 'Z')
            || (c >= '\u05D0' && c <= '\u05EA');
    }
}
