using System.Text;

namespace AssocBuilder;

/// <summary>
/// Hebrew tokenizer for the association builder.
///
/// Must stay behaviourally identical to build_index.py's clean_verse/tokenize,
/// or the C# and Python indexes are not comparable. The two traps it encodes
/// were both real bugs that silently cost quality (FINDINGS.md §3):
///
///   1. Maqaf U+05BE sits INSIDE the nikud/te'amim range U+0591-U+05C7.
///      Stripping the whole range deletes the word separator and glues pairs
///      into one token. It must fall through as a separator instead.
///
///   2. HTML tags sit INSIDE words (`&lt;big&gt;B&lt;/big&gt;ereshit`). Tags must be
///      DELETED, not replaced with a space, or the word splits in two.
///
/// Single-pass and allocation-light: one scan over the source, writing letters
/// into a reusable buffer and emitting a token at every separator.
/// </summary>
internal static class HebrewTokenizer
{
    private const char HebrewFirst = 'א';   // alef
    private const char HebrewLast  = 'ת';   // tav
    private const char Maqaf       = '־';
    private const char NikudFirst  = '֑';
    private const char NikudLast   = 'ׇ';

    /// <summary>Final forms folded to base forms so מלך / מלכים share an alphabet.
    /// Purely orthographic — NOT stemming.</summary>
    private static char FoldFinal(char c) => c switch
    {
        'ך' => 'כ',   // kaf
        'ם' => 'מ',   // mem
        'ן' => 'נ',   // nun
        'ף' => 'פ',   // pe
        'ץ' => 'צ',   // tsadi
        _        => c,
    };

    /// <summary>
    /// Tokenizes one line of source HTML directly into <paramref name="into"/>.
    ///
    /// Fuses the four Python regex passes (tag strip, entity strip, marker strip,
    /// nikud strip) plus the split into a single character scan. At 448M tokens
    /// the regex approach is the dominant cost; this is not premature.
    /// </summary>
    internal static void Tokenize(string src, List<string> into, StringBuilder buf)
    {
        into.Clear();
        buf.Clear();

        int i = 0, n = src.Length;
        while (i < n)
        {
            char c = src[i];

            // ── HTML tag: skip entirely, emitting NOTHING (not a separator) ──
            if (c == '<')
            {
                int close = src.IndexOf('>', i + 1);
                if (close < 0) break;              // malformed tail — drop it
                i = close + 1;
                continue;
            }

            // ── Entity (&thinsp; &#8201;): a separator, like the Python `sub(" ")` ──
            if (c == '&')
            {
                int semi = src.IndexOf(';', i + 1);
                if (semi > i && semi - i <= 10)
                {
                    Flush(into, buf);
                    i = semi + 1;
                    continue;
                }
                // A bare '&' is just a non-Hebrew char; fall through.
            }

            // ── Editorial markers: (א) verse numbers, {פ}/{ס} parasha marks ──
            // Python used a regex bounded to 4 inner chars; same bound here.
            if (c is '(' or '{' or '[')
            {
                char want = c switch { '(' => ')', '{' => '}', _ => ']' };
                int close = src.IndexOf(want, i + 1);
                if (close > i && close - i <= 5)
                {
                    Flush(into, buf);
                    i = close + 1;
                    continue;
                }
            }

            // ── Nikud / te'amim: dropped, but NOT the maqaf ──
            if (c >= NikudFirst && c <= NikudLast && c != Maqaf)
            {
                i++;
                continue;                          // vanishes; does not break the word
            }

            // ── Hebrew letter: part of the current token ──
            if (c >= HebrewFirst && c <= HebrewLast)
            {
                buf.Append(FoldFinal(c));
                i++;
                continue;
            }

            // ── Anything else (maqaf included) is a separator ──
            Flush(into, buf);
            i++;
        }
        Flush(into, buf);
    }

    private static void Flush(List<string> into, StringBuilder buf)
    {
        if (buf.Length > 0)
        {
            into.Add(buf.ToString());
            buf.Clear();
        }
    }
}
