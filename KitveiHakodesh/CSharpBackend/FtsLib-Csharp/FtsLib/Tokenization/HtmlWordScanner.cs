using System.Collections.Generic;
using System.Text;

namespace FtsLib.Tokenization
{
    /// <summary>
    /// Base class for single-pass HTML-aware text scanners: FtsLib's HTML layer with Otzaria's
    /// word rule (otzaria_search_engine v5.0.0 hebrew_tokenizer.rs; <see cref="WordRule"/> is
    /// the same rule on plain text). The seforim engine's scanner (hebrew-fts scanner.rs) is the
    /// same algorithm, so both index and locate the same words.
    ///
    /// Rules:
    /// - A word starts at a letter or digit of any script (Hebrew, ASCII, and every other
    ///   alphanumeric char; no Hebrew/Latin split) and runs over letters, digits and word marks,
    ///   any length, one letter included.
    /// - Transparent chars (. [ ] * ~ ^ $ \ + backtick, zero-width and bidi controls) between
    ///   word chars join them and are dropped. A run of quotes between word chars joins them when
    ///   it is one geresh, two gereshes or one gershayim (typographic quotes count). After a word,
    ///   a geresh belongs to it (a trailing geresh is kept); a gershayim does not.
    /// - The term (<see cref="_buffer"/>) is quote-free (FtsLib's terms carry no quotes), marks
    ///   dropped, presentation forms folded, lowercased in every script (Rust to_lowercase).
    /// - The second reading of a paired reading (X) [Y] takes X's position.
    /// - HTML: inline tags are transparent (a word continues across them); block tags and
    ///   sup/sub end the word; every entity (or a bare &amp;) is a separator at its '&amp;'.
    /// - A word whose term has <see cref="HebrewChars.MaxTermBytes"/> bytes or more is still
    ///   reported (it keeps its position); the index <see cref="Tokenizer"/> leaves it out.
    ///
    /// Tag/entity logic lives in <see cref="HtmlBlockTags"/>. Not thread-safe: one instance per
    /// thread.
    /// </summary>
    internal abstract class HtmlWordScanner
    {
        /// <summary>The normalized term of the word being reported (valid inside <see cref="OnWord"/>).</summary>
        protected readonly StringBuilder _buffer = new StringBuilder(64);

        // Tag name buffer: fixed size, no allocation per tag.
        private readonly char[] _tagName = new char[16];

        // Word state: 0 = outside a word, 1 = in a word, 2 = a word followed by quotes /
        // transparent chars not yet known to belong to it (the pending run).
        private const int Outside = 0, InWord = 1, Pending = 2;
        private int _state;
        private int _wordStart, _wordVisibleStart;
        private readonly List<int> _pendingChar = new List<int>(8);
        private readonly List<int> _pendingRaw = new List<int>(8);
        private int _gereshCount, _gershayimCount, _firstQuote;
        private bool _firstIsGeresh;

        // Otzaria's term before lowercasing: its UTF-8 bytes (quotes included) and the kind of
        // its last two chars (0 other, 1 geresh ', 2 gershayim ") for its geresh rules.
        private int _termBytes, _lastKind, _prevKind;

        // Paired readings (Otzaria paired_reading_after).
        private int _prev;                 // char of the last visible element (a break is ' ')
        private bool _afterParen;          // the current word follows '('
        private bool _secondReading;       // the current word follows ") [" after a word in '('
        private const int GapNone = 0, GapSuffix = 1, GapSpace = 2, GapOpen = 3;
        private int _gap;
        private bool _held;                // a second reading waiting for its ']'
        private string _heldText;
        private int _heldRawStart, _heldRawEnd, _heldVisible, _heldTermBytes;
        private int _words;                // positions given so far
        private int _reportTermBytes;

        /// <summary>UTF-8 bytes of the reported word's term before lowercasing, quotes included
        /// (what Otzaria's RemoveLongFilter measures; valid inside <see cref="OnWord"/>).</summary>
        protected int WordTermBytes => _reportTermBytes;

        // ── Entry point ─────────────────────────────────────────────────────────────────────

        protected void Scan(string text)
        {
            _buffer.Clear();
            _state = Outside;
            _pendingChar.Clear();
            _pendingRaw.Clear();
            _termBytes = 0; _lastKind = 0; _prevKind = 0;
            _prev = ' ';
            _afterParen = _secondReading = _held = false;
            _gap = GapNone;
            _heldText = null;
            _words = 0;

            int tagLen = 0;
            bool inTag = false, tagNameDone = false;
            int tagLt = 0;               // the current tag's '<'
            bool supTransparent = false; // the open <sup> was transparent (HtmlBlockTags.IsTransparentSup)
            int lastGt = -2;   // last '>' in the text (-1: none), read at the first '<'
            int visible = 0;
            int len = text.Length;

            for (int i = 0; i < len; i++)
            {
                char ch = text[i];

                // Fast path: a Hebrew letter inside a word.
                if (!inTag && _state == InWord && ch >= 0x05D0 && ch <= 0x05EA)
                {
                    _buffer.Append(ch);
                    _termBytes += 2;
                    _prevKind = _lastKind; _lastKind = 0;
                    _prev = ch;
                    visible++;
                    continue;
                }

                // Fast path: a Hebrew letter starting a word, no paired-reading check open.
                if (!inTag && ch >= 0x05D0 && ch <= 0x05EA && _state == Outside && _gap == GapNone && !_held)
                {
                    _state = InWord;
                    _wordStart = i;
                    _wordVisibleStart = visible;
                    _afterParen = _prev == '(';
                    _secondReading = false;
                    _buffer.Clear();
                    _buffer.Append(ch);
                    _termBytes = 2; _lastKind = 0; _prevKind = 0;
                    _prev = ch;
                    visible++;
                    continue;
                }

                // Fast path: a space ending a word, or between words, no paired-reading check open.
                if (!inTag && ch == ' ' && !_held)
                {
                    if (_state == InWord && !_afterParen && !_secondReading)
                        Finish(i); // leaves the gap check closed: no '(' before the word
                    if (_state == Outside && _gap == GapNone)
                    {
                        _prev = ' ';
                        visible++;
                        continue;
                    }
                }

                if (inTag)
                {
                    if (ch == '>')
                    {
                        bool breaks;
                        if (HtmlBlockTags.IsSup(_tagName, tagLen))
                        {
                            if (text[tagLt + 1] == '/') { breaks = !supTransparent; supTransparent = false; }
                            else { supTransparent = HtmlBlockTags.IsTransparentSup(text, tagLt, i); breaks = !supTransparent; }
                        }
                        else breaks = HtmlBlockTags.IsWordBreakTag(_tagName, tagLen);
                        if (breaks)
                            Element(' ', i, visible);
                        inTag = false;
                        tagLen = 0;
                    }
                    else if (!tagNameDone)
                    {
                        // Stop accumulating at the first space: everything after it is attributes.
                        if (ch == ' ' || ch == '\t') tagNameDone = true;
                        else if (tagLen < 16 && ch != '/') _tagName[tagLen++] = ch;
                    }
                    continue;
                }

                // An unterminated '<' (no '>' after it in the text) is not a tag: it is
                // kept as a plain separator and the rest of the line is scanned
                // (Otzaria's strip_html_for_indexing keeps it verbatim).
                if (ch == '<' && i < (lastGt == -2 ? (lastGt = text.LastIndexOf('>')) : lastGt))
                {
                    // A tag: inline tags are transparent (the word continues across them);
                    // the decision is taken at '>' once the name is known.
                    inTag = true;
                    tagLen = 0;
                    tagNameDone = false;
                    tagLt = i;
                    continue;
                }

                if (ch == '&')
                {
                    // Every entity (or a bare '&') is a separator at its '&'.
                    Element(' ', i, visible);
                    HtmlBlockTags.SkipEntity(text, len, ref i);
                    visible++;
                    continue;
                }

                int c = ch, units = 1;
                if (char.IsHighSurrogate(ch) && i + 1 < len && char.IsLowSurrogate(text[i + 1]))
                {
                    c = char.ConvertToUtf32(ch, text[i + 1]);
                    units = 2;
                }
                Element(c, i, visible);
                if (!HebrewChars.IsWordMark(c)) visible += units;
                i += units - 1;
            }

            EndWord(len);
            Release(false);
        }

        // ── The word rule ───────────────────────────────────────────────────────────────────

        /// <summary>One visible element at raw index <paramref name="raw"/> (a break is ' ').</summary>
        private void Element(int c, int raw, int visible)
        {
            switch (_state)
            {
                case Outside:
                    OutsideElement(c, raw, visible);
                    break;
                case InWord:
                    if (HebrewChars.IsWordChar(c))
                        Push(c);
                    else if (HebrewChars.IsQuote(c) || HebrewChars.IsTransparent(c))
                    {
                        _pendingChar.Clear();
                        _pendingRaw.Clear();
                        _gereshCount = _gershayimCount = 0;
                        _firstIsGeresh = false;
                        _state = Pending;
                        Pend(c, raw);
                    }
                    else
                    {
                        Finish(raw);
                        OutsideElement(c, raw, visible);
                    }
                    break;
                default: // Pending
                    if (HebrewChars.IsQuote(c) || HebrewChars.IsTransparent(c))
                        Pend(c, raw);
                    else if (HebrewChars.IsWordChar(c))
                    {
                        int g = _gereshCount, d = _gershayimCount;
                        if ((g == 0 && d == 0) || (g == 1 && d == 0) || (g == 2 && d == 0) || (g == 0 && d == 1))
                        {
                            for (int k = 0; k < _pendingChar.Count; k++) Push(_pendingChar[k]);
                            Push(c);
                            _state = InWord;
                        }
                        else
                        {
                            Finish(_pendingRaw[0]);
                            Replay(0);
                            OutsideElement(c, raw, visible);
                        }
                    }
                    else
                    {
                        EndWord(raw);
                        OutsideElement(c, raw, visible);
                    }
                    break;
            }
            _prev = c;
        }

        private void Pend(int c, int raw)
        {
            if (HebrewChars.IsQuote(c))
            {
                if (_gereshCount + _gershayimCount == 0)
                {
                    _firstQuote = _pendingChar.Count;
                    _firstIsGeresh = HebrewChars.IsGeresh(c);
                }
                if (HebrewChars.IsGeresh(c)) _gereshCount++; else _gershayimCount++;
            }
            _pendingChar.Add(c);
            _pendingRaw.Add(raw);
        }

        /// <summary>An element outside a word: the paired-reading checks, then maybe a new word.</summary>
        private void OutsideElement(int c, int raw, int visible)
        {
            bool open = _gap == GapOpen;
            AfterWord(c);
            if (HebrewChars.StartsWord(c))
            {
                _state = InWord;
                _wordStart = raw;
                _wordVisibleStart = visible;
                _afterParen = _prev == '(';
                _secondReading = open;
                _gap = GapNone;
                _buffer.Clear();
                _termBytes = 0; _lastKind = 0; _prevKind = 0;
                Push(c);
            }
        }

        /// <summary>A non-word element (or the end of the text) after the current word: ends it
        /// per Otzaria (a trailing geresh is kept).</summary>
        private void EndWord(int raw)
        {
            if (_state == InWord)
                Finish(raw);
            else if (_state == Pending)
            {
                int from = 0;
                if (_firstIsGeresh)
                {
                    Push(_pendingChar[_firstQuote]);
                    from = _firstQuote + 1;
                }
                Finish(from < _pendingRaw.Count ? _pendingRaw[from] : raw);
                Replay(from);
            }
        }

        /// <summary>Feeds the pending chars from <paramref name="from"/> on (left out of the word)
        /// to the paired-reading checks.</summary>
        private void Replay(int from)
        {
            for (int k = from; k < _pendingChar.Count; k++) AfterWord(_pendingChar[k]);
        }

        /// <summary>Ends the current word at raw index <paramref name="rawEnd"/>.</summary>
        private void Finish(int rawEnd)
        {
            _state = Outside;
            if (_secondReading)
            {
                _held = true;
                _heldText = _buffer.ToString();
                _heldRawStart = _wordStart;
                _heldRawEnd = rawEnd;
                _heldVisible = _wordVisibleStart;
                _heldTermBytes = _termBytes;
            }
            else
            {
                _words++;
                _reportTermBytes = _termBytes;
                OnWord(_wordStart, rawEnd, _wordVisibleStart, _words - 1);
            }
            _gap = _afterParen ? GapSuffix : GapNone;
        }

        /// <summary>A visible element outside any word, fed to the paired-reading checks.</summary>
        private void AfterWord(int c)
        {
            if (_held)
            {
                if (HebrewChars.IsReadingSuffix(c)) return;
                Release(c == ']');
            }
            switch (_gap)
            {
                case GapSuffix:
                    _gap = c == ')' ? GapSpace : HebrewChars.IsReadingSuffix(c) ? GapSuffix : GapNone;
                    break;
                case GapSpace:
                    _gap = c == '[' ? GapOpen : HebrewChars.IsWhitespace(c) ? GapSpace : GapNone;
                    break;
                default:
                    _gap = GapNone;
                    break;
            }
        }

        /// <summary>Reports the held second reading: at its first reading's position when its ']'
        /// came, else at its own.</summary>
        private void Release(bool paired)
        {
            if (!_held) return;
            _held = false;
            int position;
            if (paired) position = _words - 1;
            else { _words++; position = _words - 1; }
            _buffer.Clear();
            _buffer.Append(_heldText);
            _reportTermBytes = _heldTermBytes;
            OnWord(_heldRawStart, _heldRawEnd, _heldVisible, position);
            _buffer.Clear();
        }

        // ── Term normalization (Otzaria's advance + LowerCaser) ────────────────────────────

        private void Push(int c)
        {
            if (HebrewChars.IsWordMark(c) && c != 0xFB1E) return; // marks: not in the term
            if (HebrewChars.IsTransparent(c)) return;
            if (HebrewChars.IsGeresh(c))
            {
                if (_lastKind == 1)
                {
                    _termBytes--;
                    _lastKind = _prevKind; _prevKind = 0;
                    PushGershayimDeduped();
                }
                else
                {
                    _termBytes++;
                    _prevKind = _lastKind; _lastKind = 1;
                }
                return;
            }
            if (HebrewChars.IsGershayim(c))
            {
                PushGershayimDeduped();
                return;
            }
            string folded = HebrewChars.FoldPresentationForm(c);
            if (folded != null)
            {
                foreach (char f in folded)
                    if (!HebrewChars.IsAttachedMark(f)) PushLower(f);
                return;
            }
            PushLower(c);
        }

        private void PushGershayimDeduped()
        {
            if (_lastKind != 2)
            {
                _termBytes++;
                _prevKind = _lastKind; _lastKind = 2;
            }
        }

        private void PushLower(int c)
        {
            _termBytes += c < 0x80 ? 1 : c < 0x800 ? 2 : c < 0x10000 ? 3 : 4;
            HebrewChars.AppendLower(_buffer, c);
            _prevKind = _lastKind; _lastKind = 0;
        }

        // ── Subclass hook ───────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called for each word found in the source text, in order.
        /// </summary>
        /// <param name="rawStart">Index of the first char of the word in the original string.</param>
        /// <param name="rawEnd">
        /// Index where the next visible element after the word starts (the separator, a quote or
        /// transparent char left out of the word, an entity's '&amp;', a block tag's '&gt;', or the
        /// end of the text): inline tags right after the word are inside its span.
        /// </param>
        /// <param name="visibleStart">
        /// Visible width of the text before the word (chars outside tags in UTF-16 units, an
        /// entity 1, word marks 0). <see cref="_buffer"/> holds the normalized term at call time.
        /// </param>
        /// <param name="position">
        /// The word's position: one per word, except the second reading of a paired reading
        /// (X) [Y], which takes X's position.
        /// </param>
        protected abstract void OnWord(int rawStart, int rawEnd, int visibleStart, int position);
    }
}
