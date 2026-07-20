using FtsLib.Tokenization;
using Microsoft.Data.Sqlite;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// Decisive A/B micro-benchmark for the tokenizer refactor: does routing the per-character
    /// rules through <see cref="HebrewChars"/> (static + AggressiveInlining) cost anything vs the
    /// former hand-inlined expressions? Both loops do IDENTICAL work over the SAME char[] and are
    /// timed ALTERNATELY in one process, so machine drift / frequency scaling cancels out.
    /// If the two mins match, the JIT inlines the helpers → zero cost.
    ///
    /// Usage:  FtsLibTest.exe charbench [maxLines=400000] [reps=21]
    /// </summary>
    internal static class CharBench
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            int maxLines = args.Length > 1 && int.TryParse(args[1], out var ml) ? ml : 400000;
            int reps = args.Length > 2 && int.TryParse(args[2], out var rp) ? rp : 21;

            string db = BuildTest.ResolveDbPath();
            var sb = new StringBuilder();
            using (var c = new SqliteConnection($"Data Source={db};Mode=ReadOnly;Cache=Shared"))
            {
                c.Open(); var cmd = c.CreateCommand();
                cmd.CommandText = $"SELECT content FROM line WHERE content IS NOT NULL LIMIT {maxLines}";
                using var r = cmd.ExecuteReader();
                while (r.Read()) sb.Append(r.GetString(0));
            }
            char[] buf = sb.ToString().ToCharArray();
            Console.WriteLine($"charbench: {buf.Length:N0} chars; {reps} interleaved reps\n");

            // warmup
            long w1 = Inline(buf), w2 = Helper(buf);
            if (w1 != w2) { Console.WriteLine($"MISMATCH: inline={w1} helper={w2} (loops not equivalent!)"); return; }

            double bi = double.MaxValue, bh = double.MaxValue, si = 0, sh = 0;
            long sink = 0;
            for (int i = 0; i < reps; i++)
            {
                var swA = Stopwatch.StartNew(); sink += Inline(buf); swA.Stop();
                var swB = Stopwatch.StartNew(); sink += Helper(buf); swB.Stop();
                double a = swA.Elapsed.TotalMilliseconds, b = swB.Elapsed.TotalMilliseconds;
                bi = Math.Min(bi, a); bh = Math.Min(bh, b); si += a; sh += b;
            }
            Console.WriteLine($"inline  : min {bi,7:F2} ms   mean {si / reps,7:F2} ms");
            Console.WriteLine($"helper  : min {bh,7:F2} ms   mean {sh / reps,7:F2} ms");
            double pct = (bh - bi) / bi * 100.0;
            Console.WriteLine($"\nΔ min helper vs inline: {pct,+6:F2} %   {(Math.Abs(pct) < 2.0 ? "→ within noise: NO SLOWDOWN" : "→ investigate")}");
            Console.WriteLine($"(sink={sink})");
        }

        // Hand-inlined classification (mirrors the pre-refactor HtmlWordScanner branches).
        [MethodImpl(MethodImplOptions.NoInlining)]
        static long Inline(char[] buf)
        {
            long acc = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                char c = buf[i];
                bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= 0x05D0 && c <= 0x05EA);
                bool mark = c >= 0x0591 && c <= 0x05C7 && c != 0x05C0 && c != 0x05C3 && c != 0x05C6;
                bool quote = c == 0x0022 || c == 0x05F4 || c == 0x0027 || c == 0x05F3;
                char lc = (c >= 'A' && c <= 'Z') ? (char)(c | 32) : c;
                bool lat = lc >= 'a' && lc <= 'z';
                acc += (letter ? 1 : 0) + (mark ? 2 : 0) + (quote ? 4 : 0) + lc + (lat ? 8 : 0);
            }
            return acc;
        }

        // Same work, through the shared HebrewChars primitives.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static long Helper(char[] buf)
        {
            long acc = 0;
            for (int i = 0; i < buf.Length; i++)
            {
                char c = buf[i];
                bool letter = HebrewChars.IsLetter(c);
                bool mark = HebrewChars.IsStrippableMark(c);
                bool quote = HebrewChars.IsIntraWordQuote(c);
                char lc = HebrewChars.ToLowerAscii(c);
                bool lat = HebrewChars.IsLatinLower(lc);
                acc += (letter ? 1 : 0) + (mark ? 2 : 0) + (quote ? 4 : 0) + lc + (lat ? 8 : 0);
            }
            return acc;
        }
    }
}
