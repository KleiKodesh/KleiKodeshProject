using System.Globalization;
using System.Runtime.CompilerServices;

namespace BloomSearchEngineLib
{
    public static class TextNormalizer
    {
        private static readonly byte[] s_nsm = BuildNsmTable();

        private static byte[] BuildNsmTable()
        {
            var t = new byte[8192];
            for (int i = 0; i < 65536; i++)
                if (CharUnicodeInfo.GetUnicodeCategory((char)i) == UnicodeCategory.NonSpacingMark)
                    t[i >> 3] |= (byte)(1 << (i & 7));
            return t;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNsm(char c) => (s_nsm[c >> 3] & (1 << (c & 7))) != 0;

        public static string NormalizeText(this string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            char[] buf = new char[text.Length];
            int len = StripHtml(text, buf);
            len = RemoveNsm(buf, len);
            return new string(buf, 0, len);
        }

        private static int StripHtml(string src, char[] dst)
        {
            int len = src.Length, si = 0, di = 0;
            while (si < len)
            {
                char c = src[si];

                if (c == '&')
                {
                    int consumed = TryDecodeEntity(src, si, len, dst, ref di);
                    if (consumed > 0) { si += consumed; continue; }
                }

                if (c == '<')
                {
                    int consumed = TrySkipTag(src, si, len, dst, ref di);
                    si += consumed;
                    continue;
                }

                dst[di++] = c;
                si++;
            }
            return di;
        }

        // Returns number of source chars consumed. Writes decoded char to dst if successful.
        private static int TryDecodeEntity(string src, int si, int len, char[] dst, ref int di)
        {
            int scan = si + 1;
            while (scan < len && scan - si <= 10 && src[scan] != ';') scan++;
            if (scan >= len) return 0;

            int elen = scan - si;
            char c1 = src[si + 1];

            if (c1 == 'n' && elen == 5 && src[si+2]=='b' && src[si+3]=='s' && src[si+4]=='p') { dst[di++] = ' ';  return scan - si + 1; }
            if (c1 == 'a' && elen == 4 && src[si+2]=='m' && src[si+3]=='p')                   { dst[di++] = '&';  return scan - si + 1; }
            if (c1 == 'l' && elen == 3 && src[si+2]=='t')                                      { dst[di++] = '<';  return scan - si + 1; }
            if (c1 == 'g' && elen == 3 && src[si+2]=='t')                                      { dst[di++] = '>';  return scan - si + 1; }
            if (c1 == 'q' && elen == 5 && src[si+2]=='u' && src[si+3]=='o' && src[si+4]=='t') { dst[di++] = '"';  return scan - si + 1; }

            if (c1 == '#')
            {
                int written = TryDecodeNumericEntity(src, si + 2, scan, dst, ref di);
                if (written > 0) return scan - si + 1;
            }

            // Unknown entity — skip it
            return scan - si + 1;
        }

        // Returns number of chars written to dst (0 = failed).
        private static int TryDecodeNumericEntity(string src, int numStart, int scan, char[] dst, ref int di)
        {
            bool hex = numStart < scan && (src[numStart] == 'x' || src[numStart] == 'X');
            if (hex) numStart++;

            int val = 0;
            for (int i = numStart; i < scan; i++)
            {
                char ch = src[i];
                int d = hex
                    ? ((ch >= '0' && ch <= '9') ? ch - '0' : (ch >= 'a' && ch <= 'f') ? ch - 'a' + 10 : (ch >= 'A' && ch <= 'F') ? ch - 'A' + 10 : -1)
                    : ((ch >= '0' && ch <= '9') ? ch - '0' : -1);
                if (d < 0) return 0;
                val = hex ? (val << 4) + d : val * 10 + d;
            }

            if (val < 0 || val > 0x10FFFF) return 0;

            if (val <= 0xFFFF)
            {
                dst[di++] = (char)val;
                return 1;
            }

            val -= 0x10000;
            dst[di++] = (char)((val >> 10) + 0xD800);
            dst[di++] = (char)((val & 0x3FF) + 0xDC00);
            return 2;
        }

        // Returns number of source chars consumed (always at least 1).
        private static int TrySkipTag(string src, int si, int len, char[] dst, ref int di)
        {
            int st = si + 1;
            char quote = '\0';
            while (st < len)
            {
                char c = src[st];

                // Track entry/exit of quoted attribute values
                if (quote != '\0')
                {
                    if (c == quote) quote = '\0';
                    st++;
                    continue;
                }

                if (c == '"' || c == '\'') { quote = c; st++; continue; }

                if (c == '>')
                    break;

                // Hebrew outside of quotes means it's not a real tag — copy everything up to and including '>'
                if (c >= '\u05D0' && c <= '\u05EA')
                {
                    int end = st + 1;
                    while (end < len && src[end] != '>') end++;
                    if (end < len) end++; // include '>'
                    int count = end - si;
                    for (int i = si; i < end; i++) dst[di++] = src[i];
                    return count;
                }

                st++;
            }

            // Unterminated tag — emit the '<' literally
            if (st >= len) { dst[di++] = src[si]; return 1; }

            // Normal tag — skip it entirely (si to st inclusive)
            return st - si + 1;
        }

        private static int RemoveNsm(char[] buf, int len)
        {
            int di = 0;
            for (int si = 0; si < len; si++)
                if (!IsNsm(buf[si])) buf[di++] = buf[si];
            return di;
        }
    }
}
