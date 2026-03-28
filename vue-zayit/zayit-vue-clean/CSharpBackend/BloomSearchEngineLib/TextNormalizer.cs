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
            return (s_nonSpacingMarkTable[c >> 3] & (1 << (c & 7))) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHebrew(char c)
        {
            return (uint)(c - '\u05D0') <= ('\u05EA' - '\u05D0');
        }

        public static string NormalizeText(this string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            char[] buf = new char[text.Length];

            int len = StripHtmlTags(text, buf);
            len = RemoveNonSpacingMarks(buf, len);

            return new string(buf, 0, len);
        }

        private static int StripHtmlTags(string src, char[] dst)
        {
            int len = src.Length;
            int si = 0, di = 0;

            while (si < len)
            {
                char c = src[si];

                // =========================
                // FAST PATH (most chars)
                // =========================
                if (c != '<' && c != '&')
                {
                    dst[di++] = c;
                    si++;
                    continue;
                }

                // =========================
                // ENTITY
                // =========================
                if (c == '&')
                {
                    int start = si;
                    int scan = si + 1;

                    while (scan < len && scan - start <= 10 && src[scan] != ';')
                        scan++;

                    if (scan < len)
                    {
                        int entityLen = scan - start;
                        char c1 = src[start + 1];

                        // ---- named ----
                        if (c1 == 'n' && entityLen == 5 &&
                            src[start + 2] == 'b' &&
                            src[start + 3] == 's' &&
                            src[start + 4] == 'p')
                        {
                            dst[di++] = ' ';
                            si = scan + 1;
                            continue;
                        }

                        if (c1 == 'a' && entityLen == 4 &&
                            src[start + 2] == 'm' &&
                            src[start + 3] == 'p')
                        {
                            dst[di++] = '&';
                            si = scan + 1;
                            continue;
                        }

                        if (c1 == 'l' && entityLen == 3 &&
                            src[start + 2] == 't')
                        {
                            dst[di++] = '<';
                            si = scan + 1;
                            continue;
                        }

                        if (c1 == 'g' && entityLen == 3 &&
                            src[start + 2] == 't')
                        {
                            dst[di++] = '>';
                            si = scan + 1;
                            continue;
                        }

                        if (c1 == 'q' && entityLen == 5 &&
                            src[start + 2] == 'u' &&
                            src[start + 3] == 'o' &&
                            src[start + 4] == 't')
                        {
                            dst[di++] = '"';
                            si = scan + 1;
                            continue;
                        }

                        // ---- numeric ----
                        if (c1 == '#')
                        {
                            int numStart = start + 2;
                            int value = 0;
                            bool isHex = false;

                            if (numStart < scan)
                            {
                                char x = src[numStart];
                                if (x == 'x' || x == 'X')
                                {
                                    isHex = true;
                                    numStart++;
                                }
                            }

                            for (int i = numStart; i < scan; i++)
                            {
                                char ch = src[i];

                                if (isHex)
                                {
                                    int d =
                                        (ch >= '0' && ch <= '9') ? ch - '0' :
                                        (ch >= 'a' && ch <= 'f') ? ch - 'a' + 10 :
                                        (ch >= 'A' && ch <= 'F') ? ch - 'A' + 10 : -1;

                                    if (d < 0) { value = -1; break; }
                                    value = (value << 4) + d;
                                }
                                else
                                {
                                    if (ch < '0' || ch > '9') { value = -1; break; }
                                    value = value * 10 + (ch - '0');
                                }
                            }

                            if (value >= 0 && value <= 0x10FFFF)
                            {
                                if (value <= 0xFFFF)
                                {
                                    dst[di++] = (char)value;
                                }
                                else
                                {
                                    value -= 0x10000;
                                    dst[di++] = (char)((value >> 10) + 0xD800);
                                    dst[di++] = (char)((value & 0x3FF) + 0xDC00);
                                }

                                si = scan + 1;
                                continue;
                            }
                        }

                        si = scan + 1;
                        continue;
                    }
                }

                // =========================
                // TAG
                // =========================
                int scanTag = si + 1;

                while (scanTag < len && src[scanTag] != '>')
                {
                    if (IsHebrew(src[scanTag]))
                    {
                        int end = scanTag + 1;
                        while (end < len && src[end] != '>') end++;

                        if (end < len) end++;

                        while (si < end)
                            dst[di++] = src[si++];

                        goto ContinueOuter;
                    }
                    scanTag++;
                }

                if (scanTag >= len)
                {
                    dst[di++] = src[si++];
                    continue;
                }

                si = scanTag + 1;

            ContinueOuter:;
            }

            return di;
        }

        private static int RemoveNonSpacingMarks(char[] buf, int len)
        {
            int di = 0;

            for (int si = 0; si < len; si++)
            {
                char c = buf[si];
                if ((s_nonSpacingMarkTable[c >> 3] & (1 << (c & 7))) == 0)
                    buf[di++] = c;
            }

            return di;
        }
    }
}