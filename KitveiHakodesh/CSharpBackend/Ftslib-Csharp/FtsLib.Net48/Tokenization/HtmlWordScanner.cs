using System;
using System.Globalization;
using System.Text;

namespace FtsLib.Tokenization
{
    /// <summary>
    /// Base class for single-pass HTML-aware text scanners.
    /// Handles tag detection, entity decoding, nikud/cantillation stripping,
    /// and word boundary detection. Subclasses receive each complete word via
    /// <see cref="OnWord"/> with both its raw source span and its normalized form.
    /// Tag/entity logic lives in <see cref="HtmlBlockTags"/>.
    /// Not thread-safe — one instance per thread.
    /// </summary>
    internal abstract class HtmlWordScanner
    {
        // Normalized word buffer — reused across words, no per-word allocation.
        protected readonly StringBuilder _buffer = new StringBuilder(64);

        // Tag name buffer — fixed size, no allocation per tag.
        private readonly char[] _tagName = new char[16];
        private int  _tagLen;
        private bool _inTag;
        private int  _wordStart;     // raw index of first letter in current word
        private int  _visibleCount;  // cumulative visible chars up to current position

        // ── Entry point ──────────────────────────────────────────────

        protected void Scan(string text)
        {
            _buffer.Clear();
            _tagLen       = 0;
            _inTag        = false;
            _wordStart    = -1;
            _visibleCount = 0;

            int len = text.Length;

            for (int i = 0; i < len; i++)
            {
                char c = text[i];

                // ── HTML TAGS ────────────────────────────────────────
                if (_inTag)
                {
                    if (c == '>')
                    {
                        if (HtmlBlockTags.IsBlockTag(_tagName, _tagLen))
                            Flush(i);
                        _inTag  = false;
                        _tagLen = 0;
                    }
                    else if (_tagLen < 16 && c != ' ' && c != '\t' && c != '/')
                    {
                        _tagName[_tagLen++] = c;
                    }
                    continue;
                }

                if (c == '<')
                {
                    Flush(i);
                    _inTag  = true;
                    _tagLen = 0;
                    continue;
                }

                // ── HTML ENTITIES ────────────────────────────────────
                if (c == '&')
                {
                    if (HtmlBlockTags.IsWhitespaceEntity(text, len, ref i))
                    {
                        Flush(i);
                        _visibleCount++; // whitespace entity counts as one visible char
                    }
                    continue;
                }

                // ── MAQAF ─ word-joining hyphen acts as separator ────
                if (c == '\u05BE')
                {
                    Flush(i);
                    _visibleCount++;
                    continue;
                }

                // ── NIKUD + CANTILLATION REMOVAL ─────────────────────
                if (HebrewChars.IsStrippableMark(c))
                    continue;

                if (c > 127 && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                // ── WORD BUILDING ────────────────────────────────────
                if (HebrewChars.IsLetter(c))
                {
                    c = HebrewChars.ToLowerAscii(c);

                    // Script-boundary split: a Hebrew↔Latin transition inside a letter run is a
                    // word boundary. Prevents mixed-script blobs — OCR bleed (bדה), a Hebrew
                    // prefix on a foreign word (הOU), or entity residue (בשבת&amp) — from being
                    // indexed as one unsearchable token. Same-script runs are unaffected.
                    if (_buffer.Length > 0
                        && HebrewChars.IsLatinLower(c) != HebrewChars.IsLatinLower(_buffer[0]))
                        Flush(i); // emits the completed run (if len 2..29), resets the buffer

                    if (_buffer.Length == 0)
                        _wordStart = i; // first letter of a new word

                    _buffer.Append(c);
                    _visibleCount++;
                }
                else if (HebrewChars.IsIntraWordQuote(c) && _buffer.Length > 0)
                {
                    // Hebrew geresh/gershayim and ASCII quotes appearing inside a word
                    // (e.g. רשב"א, רש"י) are transparent connectors — skip without
                    // flushing so the word is indexed as a single token.
                    _visibleCount++;
                }
                else
                {
                    Flush(i);
                    // Non-letter separators (space, punctuation, etc.) count as visible.
                    _visibleCount++;
                }
            }

            Flush(len);
        }

        // ── Flush ────────────────────────────────────────────────────

        private void Flush(int rawEnd)
        {
            if (_buffer.Length > 1 && _buffer.Length < 30)
            {
                // Pass the visible count at the start of this word.
                int visibleStart = _visibleCount - _buffer.Length;
                OnWord(_wordStart, rawEnd, visibleStart);
            }

            _buffer.Clear();
            _wordStart = -1;
        }

        // ── Subclass hook ────────────────────────────────────────────

        /// <summary>
        /// Called for each complete word found in the source text.
        /// </summary>
        /// <param name="rawStart">
        /// Index of the first letter of the word in the original string
        /// (points into the raw HTML, before any nikud was stripped).
        /// </param>
        /// <param name="rawEnd">
        /// Index just past the separator that ended the word in the original string.
        /// </param>
        /// <param name="visibleStart">
        /// Cumulative count of visible characters up to but not including the first
        /// letter of this word. <see cref="_buffer"/> holds the normalized form at call time.
        /// </param>
        protected abstract void OnWord(int rawStart, int rawEnd, int visibleStart);

        // ── Shared letter test (used by SnippetBuilder for boundary snapping) ──

        /// <summary>Returns true for Hebrew letters (alef–tav) and ASCII a–z / A–Z.</summary>
        internal static bool IsLetter(char c) => HebrewChars.IsLetter(c);


    }
}
