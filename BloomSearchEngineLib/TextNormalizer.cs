using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace BloomSearchEngineLib
{
    public static class TextNormalizer
    {
        [ThreadStatic] private static char[] _buffer;
        [ThreadStatic] private static StringBuilder _sb;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string NormalizeText(this string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            // Initialize thread-local buffers
            if (_buffer == null)
            {
                _buffer = new char[1024];
                _sb = new StringBuilder(1024);
            }
            else
            {
                _sb.Clear();
            }

            // Ensure buffer is large enough
            if (_buffer.Length < text.Length)
                _buffer = new char[text.Length * 2]; // Extra space to avoid frequent resizing

            int writePos = 0;
            bool inTag = false;
            bool isLineBreakTag = false;
            int tagNamePos = 0;

            // Process each character
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // === TAG START ===
                if (c == '<')
                {
                    inTag = true;
                    isLineBreakTag = false;
                    tagNamePos = 0;
                    continue;
                }

                // === INSIDE TAG ===
                if (inTag)
                {
                    if (c == '>')
                    {
                        if (isLineBreakTag)
                            _buffer[writePos++] = ' ';
                        inTag = false;
                    }
                    else if (tagNamePos < 4)
                    {
                        char lc = (c >= 'A' && c <= 'Z') ? (char)(c | 32) : c;

                        // Skip '/' for closing tags
                        if (c == '/' && tagNamePos == 0)
                            continue;

                        if (tagNamePos == 0)
                        {
                            isLineBreakTag = (lc == 'b' || lc == 'p' || lc == 'd');
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

                // === CHARACTER PROCESSING ===
                // Write directly to char array (MUCH faster than individual StringBuilder.Append calls)

                // Hebrew maqaf and underscore → space
                if (c == '\u05BE' || c == '_')
                {
                    _buffer[writePos++] = ' ';
                }
                // Hebrew alphabet (U+05D0 to U+05EA)
                else if (c >= '\u05D0' && c <= '\u05EA')
                {
                    _buffer[writePos++] = c;
                }
                // Common whitespace
                else if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    _buffer[writePos++] = ' ';
                }
                // Latin uppercase → lowercase
                else if (c >= 'A' && c <= 'Z')
                {
                    _buffer[writePos++] = (char)(c | 32);
                }
                // Latin lowercase
                else if (c >= 'a' && c <= 'z')
                {
                    _buffer[writePos++] = c;
                }
                // All else stripped
            }

            // Single append operation instead of hundreds/thousands
            _sb.Append(_buffer, 0, writePos);
            return _sb.ToString();
        }
    }
}