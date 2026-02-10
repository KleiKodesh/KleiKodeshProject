using System.Collections.Generic;
using System.Text;

namespace BloomSearchEngineLib
{
    public sealed class TermExtractor
    {
        private readonly HashSet<string> _terms = new HashSet<string>();
        private readonly StringBuilder _wordBuilder = new StringBuilder(64);

        public HashSet<string> ExtractTermsFromLines(List<string> lines)
        {
            _terms.Clear();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                ProcessLine(line);
            }

            return _terms;
        }

        private void ProcessLine(string text)
        {
            bool inTag = false;
            bool isLineBreakTag = false;
            int tagNamePos = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // === TAG HANDLING (same logic as NormalizeText) ===
                if (c == '<')
                {
                    FlushWord(); // Save current word before tag
                    inTag = true;
                    isLineBreakTag = false;
                    tagNamePos = 0;
                    continue;
                }

                if (inTag)
                {
                    if (c == '>')
                    {
                        inTag = false;
                    }
                    else if (tagNamePos < 4)
                    {
                        char lc = (c >= 'A' && c <= 'Z') ? (char)(c | 32) : c;

                        if (c == '/' && tagNamePos == 0)
                            continue;

                        if (tagNamePos == 0)
                        {
                            isLineBreakTag = (lc == 'b' || lc == 'p' || lc == 'd');
                        }
                        else if (tagNamePos == 1 && isLineBreakTag)
                        {
                            if (lc != 'r' && lc != 'i')
                                isLineBreakTag = false;
                        }
                        else if (tagNamePos == 2 && isLineBreakTag)
                        {
                            if (lc != 'v')
                                isLineBreakTag = false;
                        }

                        if (lc >= 'a' && lc <= 'z')
                            tagNamePos++;
                    }
                    continue;
                }

                // === CHARACTER PROCESSING ===
                // Whitespace/separators → flush current word
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' ||
                    c == '\u05BE' || c == '_')
                {
                    FlushWord();
                }
                // Hebrew alphabet → add to current word
                else if (c >= '\u05D0' && c <= '\u05EA')
                {
                    _wordBuilder.Append(c);
                }
                // Latin uppercase → lowercase and add
                else if (c >= 'A' && c <= 'Z')
                {
                    _wordBuilder.Append((char)(c | 32));
                }
                // Latin lowercase → add directly
                else if (c >= 'a' && c <= 'z')
                {
                    _wordBuilder.Append(c);
                }
                // Everything else is ignored (stripped)
            }

            // Don't forget the last word in the line
            FlushWord();
        }

        private void FlushWord()
        {
            if (_wordBuilder.Length > 0)
            {
                string word = _wordBuilder.ToString();
                int len = word.Length;

                // Add up to 3 n-grams based on word length for prefix/middle/suffix search
                if (len >= 7)
                {
                    // Long words: add first 3, variable-length middle, and last 3 chars
                    // Middle takes whatever is left after prefix and suffix
                    _terms.Add(word.Substring(0, 3));                    // prefix: first 3
                    _terms.Add(word.Substring(3, len - 6));              // middle: everything between prefix and suffix
                    _terms.Add(word.Substring(len - 3, 3));              // suffix: last 3
                }
                else if (len == 6)
                {
                    // 6-char words: add first 3, middle 3, and last 3
                    _terms.Add(word.Substring(0, 3));      // prefix (0-2)
                    _terms.Add(word.Substring(2, 3));      // middle (2-4) - overlaps
                    _terms.Add(word.Substring(3, 3));      // suffix (3-5)
                }
                else if (len == 5)
                {
                    // 5-char words: add first 3, middle 3, and last 3
                    _terms.Add(word.Substring(0, 3));      // prefix (0-2)
                    _terms.Add(word.Substring(1, 3));      // middle (1-3)
                    _terms.Add(word.Substring(2, 3));      // suffix (2-4)
                }
                else if (len == 4)
                {
                    // 4-char words: add first 3 and last 3 (will overlap by 2)
                    _terms.Add(word.Substring(0, 3));      // prefix
                    _terms.Add(word.Substring(1, 3));      // suffix
                }
                else if (len == 3)
                {
                    // 3-char words: just the word itself
                    _terms.Add(word);
                }
                else if (len == 2)
                {
                    // 2-char words: just the word itself
                    _terms.Add(word);
                }
                // For 1-char words, add as-is
                else if (len == 1)
                {
                    _terms.Add(word);
                }

                _wordBuilder.Clear();
            }
        }
    }
}