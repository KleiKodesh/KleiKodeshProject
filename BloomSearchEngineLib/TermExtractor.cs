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
                _terms.Add(_wordBuilder.ToString());
                _wordBuilder.Clear();
            }
        }
    }
}
