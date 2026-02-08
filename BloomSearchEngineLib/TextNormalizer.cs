using System;
using System.Text;

namespace BloomSearchEngineLib
{
    public static class TextNormalizer
    {
        [ThreadStatic] private static StringBuilder _sb;

        public static string Normalize(this string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            if (_sb == null)
                _sb = new StringBuilder(text.Length);
            else
                _sb.Clear();

            bool inTag = false;
            bool isLineBreakTag = false;
            int tagNamePos = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '<')
                {
                    inTag = true;
                    isLineBreakTag = false;
                    tagNamePos = 0;
                    continue;
                }

                if (inTag)
                {
                    if (c == '>')
                    {
                        if (isLineBreakTag)
                            _sb.Append(' ');
                        inTag = false;
                    }
                    else if (tagNamePos < 4)
                    {
                        char lc = (c >= 'A' && c <= 'Z') ? (char)(c | 32) : c;

                        // Skip '/' for closing tags
                        if (c == '/' && tagNamePos == 0)
                        {
                            continue; // Don't increment tagNamePos
                        }

                        if (tagNamePos == 0)
                        {
                            if (lc == 'b' || lc == 'p' || lc == 'd')
                                isLineBreakTag = true;
                            else
                                isLineBreakTag = false;
                        }
                        else if (tagNamePos == 1 && isLineBreakTag)
                        {
                            // 'br' → r, 'p' → anything, 'div' → i
                            if (lc != 'r' && lc != 'i')
                                isLineBreakTag = false;
                        }
                        else if (tagNamePos == 2 && isLineBreakTag)
                        {
                            // 'div' → v
                            if (lc != 'v')
                                isLineBreakTag = false;
                        }

                        if (lc >= 'a' && lc <= 'z')
                            tagNamePos++;
                    }
                    continue;
                }


                // Hebrew maqaf and underscore → space
                if (c == '\u05BE' || c == '_')
                {
                    _sb.Append(' ');
                }
                // Hebrew alphabet (U+05D0 to U+05EA)
                else if (c >= '\u05D0' && c <= '\u05EA')
                {
                    _sb.Append(c);
                }
                // Common whitespace
                else if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    _sb.Append(' ');
                }
                // Latin alphabet → lowercase
                else if (c >= 'A' && c <= 'Z')
                {
                    _sb.Append((char)(c | 32));
                }
                else if (c >= 'a' && c <= 'z')
                {
                    _sb.Append(c);
                }
                // All else stripped
            }

            return _sb.ToString();
        }
    }
}