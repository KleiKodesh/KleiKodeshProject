using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FtsLib
{
    public sealed class Tokenizer
    {
        private readonly HashSet<string> _terms = new HashSet<string>();
        private readonly StringBuilder _buffer = new StringBuilder(64);
        private readonly StringBuilder _tagBuf = new StringBuilder(32);

        private bool _inTag;

        public HashSet<string> Extract(string text)
        {
            _terms.Clear();

            if (string.IsNullOrEmpty(text))
                return _terms;

            Process(text);
            return _terms;
        }

        private void Process(string text)
        {
            _buffer.Clear();
            _tagBuf.Clear();
            _inTag = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // ---------------- HTML TAGS ----------------
                if (_inTag)
                {
                    if (c == '>')
                    {
                        // Decide based on tag name whether to flush
                        if (IsBlockTag(_tagBuf.ToString()))
                            Flush();

                        _inTag = false;
                        _tagBuf.Clear();
                    }
                    else
                    {
                        // Collect tag name chars only (stop at space or /)
                        if (_tagBuf.Length < 16 && c != ' ' && c != '\t' && c != '/')
                            _tagBuf.Append(c);
                    }

                    continue;
                }

                if (c == '<')
                {
                    _inTag = true;
                    _tagBuf.Clear();
                    continue;
                }

                // ---------------- HTML ENTITIES ----------------
                // Whitespace entities (nbsp etc.) break words; others are invisible
                if (c == '&')
                {
                    HandleEntity(text, ref i);
                    continue;
                }

                // ---------------- NIKUD REMOVAL ----------------
                if (CharUnicodeInfo.GetUnicodeCategory(c) ==
                    UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                // ---------------- WORD BUILDING ----------------
                if (IsLetter(c))
                {
                    if (c >= 'A' && c <= 'Z')
                        c = (char)(c | 32);

                    _buffer.Append(c);
                }
                else
                {
                    Flush();
                }
            }

            Flush();
        }

        /// <summary>
        /// Returns true for block-level tags that imply a visual break between
        /// content and should therefore act as word separators.
        /// Closing tags (starting with '!') and unknown tags are treated as invisible.
        /// </summary>
        private static bool IsBlockTag(string raw)
        {
            if (raw.Length == 0) return false;

            // Strip leading '/' for closing tags — closing a block tag also breaks
            int start = raw[0] == '!' ? 1 : 0; // skip '!' for comments/doctype
            if (raw[0] == '/') start = 1;

            // Lowercase the tag name for comparison
            var tag = raw.Substring(start).ToLowerInvariant();

            switch (tag)
            {
                case "p":
                case "div":
                case "br":
                case "hr":
                case "li":
                case "ul":
                case "ol":
                case "tr":
                case "td":
                case "th":
                case "h1": case "h2": case "h3":
                case "h4": case "h5": case "h6":
                case "blockquote":
                case "pre":
                case "section":
                case "article":
                case "header":
                case "footer":
                case "nav":
                case "aside":
                case "dd":
                case "dt":
                case "figure":
                case "figcaption":
                case "main":
                case "table":
                case "caption":
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Handles a '&amp;' at position <paramref name="i"/>.
        /// Whitespace entities (nbsp, #160, etc.) flush the current buffer.
        /// All other entities are skipped invisibly.
        /// </summary>
        private void HandleEntity(string text, ref int i)
        {
            int start = i + 1;
            int end = start;

            while (end < text.Length && end - start < 10 && text[end] != ';')
                end++;

            if (end >= text.Length || text[end] != ';')
                return; // malformed — leave i unchanged, '&' silently ignored

            var entity = text.Substring(start, end - start);
            i = end; // advance past ';'

            // Whitespace entities act as word separators
            if (entity == "nbsp" || entity == "ensp" || entity == "emsp" ||
                entity == "thinsp" || entity == "hairsp" ||
                (entity.Length > 1 && entity[0] == '#' &&
                 int.TryParse(entity.Substring(1), out int cp) &&
                 (cp == 160 || cp == 8194 || cp == 8195 || cp == 8201)))
            {
                Flush();
            }
            // All other entities (shy, amp, lt, gt, zwj, zwnj, …) are invisible
        }

        private void Flush()
        {
            if (_buffer.Length == 0)
                return;

            _terms.Add(_buffer.ToString());
            _buffer.Clear();
        }

        private static bool IsLetter(char c)
        {
            return (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '\u05D0' && c <= '\u05EA');
        }
    }
}
