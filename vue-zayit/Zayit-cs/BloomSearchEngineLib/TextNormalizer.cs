using System.Globalization;
using System.Runtime.CompilerServices;

namespace BloomSearchEngineLib
{
    public static class TextNormalizer
    {
        private static readonly byte[] s_nonSpacingMarkTable = BuildNonSpacingMarkTable();

        private static byte[] BuildNonSpacingMarkTable()
        {
            var table = new byte[8192];
            for (int i = 0; i < 65536; i++)
            {
                if (CharUnicodeInfo.GetUnicodeCategory((char)i) == UnicodeCategory.NonSpacingMark)
                    table[i >> 3] |= (byte)(1 << (i & 7));
            }
            return table;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNonSpacingMark(char c)
        {
            int i = c;
            return (s_nonSpacingMarkTable[i >> 3] & (1 << (i & 7))) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHebrew(char c) => (uint)(c - '\u05D0') <= (uint)('\u05EA' - '\u05D0');

        public static string NormalizeText(this string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            char[] buf = new char[text.Length];

            int strippedLen = StripHtmlTags(text, buf);
            int finalLen = RemoveNonSpacingMarks(buf, strippedLen);

            return new string(buf, 0, finalLen);
        }

        private static int StripHtmlTags(string src, char[] dst)
        {
            int len = src.Length;
            int di = 0;
            int si = 0;

            while (si < len)
            {
                char c = src[si];
                if (c != '<')
                {
                    dst[di++] = c;
                    si++;
                    continue;
                }

                // Scan for '>' and check for Hebrew
                int scan = si + 1;
                bool hasHebrew = false;

                while (scan < len && src[scan] != '>')
                {
                    if (IsHebrew(src[scan]))
                    {
                        hasHebrew = true;
                        scan++;
                        while (scan < len && src[scan] != '>') scan++;
                        break;
                    }
                    scan++;
                }

                if (scan >= len)
                {
                    // No closing '>' — treat '<' as plain text
                    dst[di++] = src[si++];
                    continue;
                }

                if (hasHebrew)
                {
                    // Copy entire tag including < and >
                    int tagEnd = scan + 1;
                    while (si < tagEnd)
                        dst[di++] = src[si++];
                }
                else
                {
                    // Skip tag
                    si = scan + 1;
                }
            }

            return di;
        }

        // In-place: output is always <= input index so reads and writes never collide
        private static int RemoveNonSpacingMarks(char[] buf, int len)
        {
            int di = 0;
            for (int si = 0; si < len; si++)
            {
                char c = buf[si];
                if (!IsNonSpacingMark(c))
                    buf[di++] = c;
            }
            return di;
        }
    }
}