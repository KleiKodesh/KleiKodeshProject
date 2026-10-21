using System;
using System.Globalization;
using System.Text;

namespace FtsLib.Tokenization
{
    /// <summary>
    /// Base class for single-pass HTML-aware text scanners.
    /// Handles tag detection, entity decoding, nikud/cantillation stripping,
    /// and word boundary detection.
    ///
    /// Word model = Otzaria's (otzaria_search_engine hebrew_tokenizer.rs: a token is a run of
    /// letters AND digits, any length, one letter included): every run of letters (Hebrew,
    /// ASCII) and ASCII digits is a word, whatever its length. Digits are script-neutral (they
    /// join the run they are in: ב5, 5ab). A word whose term is over
    /// <see cref="HebrewChars.MaxTermBytes"/> is still reported here (it is a word for distances);
    /// the index <see cref="Tokenizer"/> leaves it out of the terms (Otzaria's RemoveLongFilter). Subclasses receive each complete word via
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
        private bool _tagNameDone;   // first space seen — attribute chars must not join the name
        private int  _wordStart;     // raw index of first letter in current word
        private int  _wordVisibleStart; // visible chars before the first letter of the current word
        private int  _visibleCount;  // cumulative visible chars up to current position
        private int  _runScript;     // 0 = no letter yet in the word (digits only), 1 = Hebrew, 2 = Latin
        private int  _pendingQuotes; // intra-word quotes since the word's last letter/digit
        private int  _innerQuotes;   // quotes BETWEEN the word's letters/digits (kept in its term by
                                     // engines that keep quotes; they count one byte toward MaxTermBytes)

        /// <summary>Quotes between the letters of the word being reported (valid inside
        /// <see cref="OnWord"/>): רש"י has 1, יצחק" has 0 (a trailing quote is punctuation).</summary>
        protected int WordInnerQuotes => _innerQuotes;

        // ── Entry point ──────────────────────────────────────────────

        protected void Scan(string text)
        {
            _buffer.Clear();
            _tagLen       = 0;
            _inTag        = false;
            _tagNameDone  = false;
            _wordStart    = -1;
            _visibleCount = 0;
            _runScript    = 0;
            _pendingQuotes = 0;
            _innerQuotes  = 0;

            int len = text.Length;

            for (int i = 0; i < len; i++)
            {
                char c = text[i];

                // ── HTML TAGS ────────────────────────────────────────
                if (_inTag)
                {
                    if (c == '>')
                    {
                        if (HtmlBlockTags.IsWordBreakTag(_tagName, _tagLen))
                            Flush(i);
                        _inTag  = false;
                        _tagLen = 0;
                    }
                    else if (!_tagNameDone)
                    {
                        // Stop accumulating at the first space — everything after
                        // it is attributes, which must not corrupt the tag name
                        // the flush decision below depends on.
                        if (c == ' ' || c == '\t') _tagNameDone = true;
                        else if (_tagLen < 16 && c != '/') _tagName[_tagLen++] = c;
                    }
                    continue;
                }

                if (c == '<')
                {
                    // Word building continues ACROSS the tag — no flush here.
                    // Inline formatting (<b>, <i>, <small>, <span>…) interrupts
                    // words mid-letter (emphasised letters, e.g. ורא<b>ה</b>);
                    // flushing on every '<' indexed those words as unfindable
                    // fragments. The flush decision is deferred to '>' above,
                    // once the tag name is known: block tags and sup/sub break
                    // the word, everything else is transparent.
                    _inTag       = true;
                    _tagLen      = 0;
                    _tagNameDone = false;
                    continue;
                }

                // ── HTML ENTITIES ────────────────────────────────────
                if (c == '&')
                {
                    // Every entity — well-formed or a bare '&' — is a word
                    // separator: the corpus has no letter entities (verified
                    // 2026-07-22), only whitespace (&nbsp;, &thinsp;) and
                    // punctuation (&amp;, &lt;, &gt;) forms, all of which end
                    // the word. The old classifier consumed non-whitespace
                    // entities INVISIBLY, joining the surrounding fragments
                    // into one term that exists nowhere.
                    //
                    // Flush at the '&' (the entity is the separator, so it is NOT
                    // part of the word's raw span). Flushing AFTER SkipEntity used
                    // the ';' as rawEnd, which pulled the entity BODY (&thinsp,
                    // minus its ';') into the token span — the snippet renderer
                    // then copied that incomplete entity verbatim and v-html showed
                    // it as literal text (e.g. "…ידעתיו&thinsp;למען…"). Ending the
                    // word at '&' keeps the whole entity outside every token span.
                    Flush(i);
                    HtmlBlockTags.SkipEntity(text, len, ref i);
                    _visibleCount++; // the entity renders as one visible char
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
                if (HebrewChars.IsWordChar(c))
                {
                    c = HebrewChars.ToLowerAscii(c);

                    // Script-boundary split: a Hebrew↔Latin transition inside a letter run is a
                    // word boundary. Prevents mixed-script blobs — OCR bleed (bדה), a Hebrew
                    // prefix on a foreign word (הOU), or entity residue (בשבת&amp) — from being
                    // indexed as one unsearchable token. Same-script runs are unaffected.
                    // Digits belong to no script: they never split and join the run (ב5).
                    if (!HebrewChars.IsDigit(c))
                    {
                        int script = HebrewChars.IsLatinLower(c) ? 2 : 1;
                        if (_runScript != 0 && script != _runScript)
                            Flush(i); // emits the completed run, resets the buffer
                        _runScript = script;
                    }

                    if (_buffer.Length == 0)
                    {
                        _wordStart = i; // first letter/digit of a new word
                        _wordVisibleStart = _visibleCount;
                    }

                    _innerQuotes  += _pendingQuotes; // the quotes before this char are inside the word
                    _pendingQuotes = 0;
                    _buffer.Append(c);
                    _visibleCount++;
                }
                else if (HebrewChars.IsIntraWordQuote(c) && _buffer.Length > 0)
                {
                    // Hebrew geresh/gershayim and ASCII quotes appearing inside a word
                    // (e.g. רשב"א, רש"י) are transparent connectors — skip without
                    // flushing so the word is indexed as a single token.
                    _pendingQuotes++;
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
            // Every word counts (Otzaria's word model): one letter, a digit run, any length.
            if (_buffer.Length > 0)
            {
                // Pass the visible count at the start of this word, recorded when its
                // first letter arrived. Deriving it as _visibleCount - _buffer.Length was
                // wrong for any word with quotes (רש"י, or a trailing quote as in יצחק"):
                // the count includes the quotes, the buffer holds letters only, so the
                // start came out late by the number of quotes. That shifted the context
                // trim of long snippets (found by the tantivy-engine parity run).
                OnWord(_wordStart, rawEnd, _wordVisibleStart);
            }

            _buffer.Clear();
            _wordStart     = -1;
            _runScript     = 0;
            _pendingQuotes = 0;
            _innerQuotes   = 0;
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

        // ── Shared letter test (used by SnippetWindowLocator for boundary snapping) ──

        /// <summary>Returns true for Hebrew letters (alef–tav) and ASCII a–z / A–Z (not digits).</summary>
        internal static bool IsLetter(char c) => HebrewChars.IsLetter(c);


    }
}
