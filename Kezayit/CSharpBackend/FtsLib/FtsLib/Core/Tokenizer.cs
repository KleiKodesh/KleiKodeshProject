using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FtsLib
{
    public sealed class Tokenizer
    {
        private readonly HashSet<string> _terms = new HashSet<string>();
        private readonly StringBuilder   _buffer = new StringBuilder(64);

        // Tag name buffer — reused, no allocation per tag
        private readonly char[] _tagName = new char[16];
        private int              _tagLen;
        private bool             _inTag;

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
            _tagLen = 0;
            _inTag  = false;

            int len = text.Length;

            for (int i = 0; i < len; i++)
            {
                char c = text[i];

                // ---------------- HTML TAGS ----------------
                if (_inTag)
                {
                    if (c == '>')
                    {
                        if (IsBlockTag(_tagName, _tagLen))
                            Flush();
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

                // ---------------- HTML ENTITIES ----------------
                if (c == '&')
                {
                    HandleEntity(text, len, ref i);
                    continue;
                }

                // ---------------- NIKUD REMOVAL ----------------
                // Hebrew nikud (U+05B0–U+05C7) and other non-spacing marks
                if (c >= '\u05B0' && c <= '\u05C7')
                    continue;

                if (c > 127 && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                // ---------------- WORD BUILDING ----------------
                if (IsLetter(c))
                {
                    // ASCII uppercase → lowercase (branchless bit trick)
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

        // No allocation — works directly on the char[] tag name buffer
        private static bool IsBlockTag(char[] name, int len)
        {
            if (len == 0) return false;

            // Skip leading '/' (closing tag) or '!' (comment/doctype)
            int start = (name[0] == '/' || name[0] == '!') ? 1 : 0;
            int tlen  = len - start;
            if (tlen == 0) return false;

            // Lowercase first char for comparison
            char c0 = name[start];
            if (c0 >= 'A' && c0 <= 'Z') c0 = (char)(c0 | 32);

            switch (tlen)
            {
                case 1:
                    return c0 == 'p';

                case 2:
                {
                    char c1 = name[start + 1];
                    if (c1 >= 'A' && c1 <= 'Z') c1 = (char)(c1 | 32);
                    if (c0 == 'b' && c1 == 'r') return true; // br
                    if (c0 == 'h' && c1 == 'r') return true; // hr
                    if (c0 == 'l' && c1 == 'i') return true; // li
                    if (c0 == 'u' && c1 == 'l') return true; // ul
                    if (c0 == 'o' && c1 == 'l') return true; // ol
                    if (c0 == 't' && c1 == 'r') return true; // tr
                    if (c0 == 't' && c1 == 'd') return true; // td
                    if (c0 == 't' && c1 == 'h') return true; // th
                    if (c0 == 'd' && c1 == 'd') return true; // dd
                    if (c0 == 'd' && c1 == 't') return true; // dt
                    if (c0 == 'h')                            // h1-h6
                    {
                        char d = c1 >= 'A' && c1 <= 'Z' ? (char)(c1 | 32) : c1;
                        return d >= '1' && d <= '6';
                    }
                    return false;
                }

                case 3:
                {
                    char c1 = name[start + 1]; if (c1 >= 'A' && c1 <= 'Z') c1 = (char)(c1 | 32);
                    char c2 = name[start + 2]; if (c2 >= 'A' && c2 <= 'Z') c2 = (char)(c2 | 32);
                    if (c0 == 'd' && c1 == 'i' && c2 == 'v') return true; // div
                    if (c0 == 'p' && c1 == 'r' && c2 == 'e') return true; // pre
                    if (c0 == 'n' && c1 == 'a' && c2 == 'v') return true; // nav
                    return false;
                }

                case 4:
                {
                    char c1 = name[start + 1]; if (c1 >= 'A' && c1 <= 'Z') c1 = (char)(c1 | 32);
                    char c2 = name[start + 2]; if (c2 >= 'A' && c2 <= 'Z') c2 = (char)(c2 | 32);
                    char c3 = name[start + 3]; if (c3 >= 'A' && c3 <= 'Z') c3 = (char)(c3 | 32);
                    if (c0 == 'm' && c1 == 'a' && c2 == 'i' && c3 == 'n') return true; // main
                    return false;
                }

                case 5:
                {
                    // table, aside
                    char c1 = name[start + 1]; if (c1 >= 'A' && c1 <= 'Z') c1 = (char)(c1 | 32);
                    char c2 = name[start + 2]; if (c2 >= 'A' && c2 <= 'Z') c2 = (char)(c2 | 32);
                    char c3 = name[start + 3]; if (c3 >= 'A' && c3 <= 'Z') c3 = (char)(c3 | 32);
                    char c4 = name[start + 4]; if (c4 >= 'A' && c4 <= 'Z') c4 = (char)(c4 | 32);
                    if (c0 == 't' && c1 == 'a' && c2 == 'b' && c3 == 'l' && c4 == 'e') return true;
                    if (c0 == 'a' && c1 == 's' && c2 == 'i' && c3 == 'd' && c4 == 'e') return true;
                    return false;
                }

                default:
                    // Longer tags: header, footer, figure, section, article, blockquote,
                    // figcaption, caption
                    return MatchesLongBlockTag(name, start, tlen);
            }
        }

        private static bool MatchesLongBlockTag(char[] name, int start, int tlen)
        {
            // Build a lowercase string only for the longer/rarer tags
            var sb = new System.Text.StringBuilder(tlen);
            for (int i = start; i < start + tlen; i++)
            {
                char c = name[i];
                if (c >= 'A' && c <= 'Z') c = (char)(c | 32);
                sb.Append(c);
            }
            string tag = sb.ToString();
            switch (tag)
            {
                case "header": case "footer": case "figure": case "section":
                case "article": case "caption": case "figcaption": case "blockquote":
                    return true;
                default:
                    return false;
            }
        }

        // No Substring allocation — scans entity in-place
        private void HandleEntity(string text, int len, ref int i)
        {
            int start = i + 1;
            int end   = start;

            while (end < len && end - start < 10 && text[end] != ';')
                end++;

            if (end >= len || text[end] != ';')
                return; // malformed

            i = end; // advance past ';'

            int elen = end - start;
            if (elen == 0) return;

            char e0 = text[start];

            // Fast path for common whitespace entities
            if (e0 == 'n' && elen == 4
                && text[start+1]=='b' && text[start+2]=='s' && text[start+3]=='p')
            { Flush(); return; } // nbsp

            if (e0 == 'e' && elen == 4
                && text[start+1]=='n' && text[start+2]=='s' && text[start+3]=='p')
            { Flush(); return; } // ensp

            if (e0 == 'e' && elen == 4
                && text[start+1]=='m' && text[start+2]=='s' && text[start+3]=='p')
            { Flush(); return; } // emsp

            // Numeric entities: &#160; &#8194; &#8195; &#8201;
            if (e0 == '#' && elen > 1)
            {
                int val = 0;
                bool ok = true;
                for (int k = start + 1; k < end; k++)
                {
                    char d = text[k];
                    if (d < '0' || d > '9') { ok = false; break; }
                    val = val * 10 + (d - '0');
                }
                if (ok && (val == 160 || val == 8194 || val == 8195 || val == 8201))
                    Flush();
            }
            // All other entities are invisible — no action
        }

        private void Flush()
        {
            if (_buffer.Length > 1 && _buffer.Length < 30)
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
