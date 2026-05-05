using System;
using System.Globalization;
using System.Text;

namespace FtsLib.Core
{
    /// <summary>
    /// Base class for single-pass HTML-aware text scanners.
    /// Handles tag detection, entity decoding, nikud/cantillation stripping,
    /// and word boundary detection. Subclasses receive each complete word via
    /// <see cref="OnWord"/> with both its raw source span and its normalized form.
    /// Tag/entity logic lives in <see cref="HtmlScannerHelpers"/>.
    /// Not thread-safe — one instance per thread.
    /// </summary>
    internal abstract class HtmlTextScanner
    {
        // Normalized word buffer — reused across words, no per-word allocation.
        protected readonly StringBuilder _buffer = new StringBuilder(64);

        // Tag name buffer — fixed size, no allocation per tag.
        private readonly char[] _tagName = new char[16];
        private int  _tagLen;
        private bool _inTag;
        private int  _wordStart; // raw index of first letter in current word

        // ── Entry point ──────────────────────────────────────────────

        protected void Scan(string text)
        {
            _buffer.Clear();
            _tagLen    = 0;
            _inTag     = false;
            _wordStart = -1;

            int len = text.Length;

            for (int i = 0; i < len; i++)
            {
                char c = text[i];

                // ── HTML TAGS ────────────────────────────────────────
                if (_inTag)
                {
                    if (c == '>')
                    {
                        if (HtmlScannerHelpers.IsBlockTag(_tagName, _tagLen))
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
                    _inTag  = true;
                    _tagLen = 0;
                    continue;
                }

                // ── HTML ENTITIES ────────────────────────────────────
                if (c == '&')
                {
                    if (HtmlScannerHelpers.IsWhitespaceEntity(text, len, ref i))
                        Flush(i);
                    continue;
                }

                // ── MAQAF ─ word-joining hyphen acts as separator ────
                if (c == '\u05BE')
                {
                    Flush(i);
                    continue;
                }

                // ── NIKUD + CANTILLATION REMOVAL ─────────────────────
                // U+0591–U+05AF  Hebrew cantillation marks (טעמים)
                // U+05B0–U+05BD  Hebrew nikud (ניקוד) — combining vowel points
                // U+05BF          rafe — combining
                // U+05C1–U+05C2  shin/sin dot — combining
                // U+05C4–U+05C5  upper/lower dot — combining
                // U+05C7          qamats qatan — combining
                //
                // NOT stripped (act as word separators via the else/Flush path):
                // U+05BE  maqaf — handled explicitly above
                // U+05C0  paseq ׀ — punctuation
                // U+05C3  sof pasuq ׃ — punctuation
                // U+05C6  nun hafukha — punctuation
                if (c >= '\u0591' && c <= '\u05C7'
                    && c != '\u05C0'   // paseq ׀
                    && c != '\u05C3'   // sof pasuq ׃
                    && c != '\u05C6')  // nun hafukha
                    continue;

                if (c > 127 && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                // ── WORD BUILDING ────────────────────────────────────
                if (IsLetter(c))
                {
                    if (c >= 'A' && c <= 'Z')
                        c = (char)(c | 32);

                    if (_buffer.Length == 0)
                        _wordStart = i; // first letter of a new word

                    _buffer.Append(c);
                }
                else
                {
                    Flush(i);
                }
            }

            Flush(len);
        }

        // ── Flush ────────────────────────────────────────────────────

        private void Flush(int rawEnd)
        {
            if (_buffer.Length > 1 && _buffer.Length < 30)
                OnWord(_wordStart, rawEnd);

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
        /// <see cref="_buffer"/> holds the normalized form at call time.
        /// </param>
        protected abstract void OnWord(int rawStart, int rawEnd);

        // ── Shared letter test (used by SnippetBuilder for boundary snapping) ──

        /// <summary>Returns true for Hebrew letters (alef–tav) and ASCII a–z / A–Z.</summary>
        internal static bool IsLetter(char c)
            => (c >= 'a' && c <= 'z')
            || (c >= 'A' && c <= 'Z')
            || (c >= '\u05D0' && c <= '\u05EA');
    }
}
