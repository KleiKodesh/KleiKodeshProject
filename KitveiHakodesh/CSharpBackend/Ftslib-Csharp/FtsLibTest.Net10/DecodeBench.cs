using FtsLib.Search;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace FtsLibTest
{
    /// <summary>
    /// A/B micro-benchmark for the posting-decode hot path (delta+varint). Compares a
    /// hand-inlined varint read against calling <see cref="VarInt.Read"/>, interleaved in one
    /// process. If the call path is slower, inlining VarInt.Read is a real win; if equal, the
    /// JIT/AOT already inlines it. Deterministic synthetic buffer (no I/O in timed loop).
    ///
    /// Usage:  FtsLibTest.exe decodebench [postings=8000000] [reps=25]
    /// </summary>
    internal static class DecodeBench
    {
        public static void Run(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            int n = args.Length > 1 && int.TryParse(args[1], out var pn) ? pn : 8_000_000;
            int reps = args.Length > 2 && int.TryParse(args[2], out var rp) ? rp : 25;

            // Build a representative delta+varint buffer: mostly small deltas (1–2 bytes),
            // a few larger — deterministic LCG so both builds see identical data.
            byte[] buf = new byte[n * 2 + 16];
            int len = 0; ulong lcg = 0x9E3779B97F4A7C15UL;
            for (int k = 0; k < n; k++)
            {
                lcg = lcg * 6364136223846793005UL + 1442695040888963407UL;
                uint delta = (uint)((lcg >> 40) & 0x3FF) + 1;          // 1..1024
                if ((lcg & 0x1F) == 0) delta += (uint)((lcg >> 20) & 0x1FFFF); // ~3% larger
                while (delta >= 0x80) { buf[len++] = (byte)(delta | 0x80); delta >>= 7; }
                buf[len++] = (byte)delta;
            }
            Console.WriteLine($"decodebench: {n:N0} postings, {len:N0} bytes, {reps} interleaved reps\n");

            long a = Inline(buf, len), b = Call(buf, len);
            if (a != b) { Console.WriteLine($"MISMATCH inline={a} call={b}"); return; }

            double bi = double.MaxValue, bc = double.MaxValue, si = 0, sc = 0; long sink = 0;
            for (int i = 0; i < reps; i++)
            {
                var s1 = Stopwatch.StartNew(); sink += Inline(buf, len); s1.Stop();
                var s2 = Stopwatch.StartNew(); sink += Call(buf, len);   s2.Stop();
                double x = s1.Elapsed.TotalMilliseconds, y = s2.Elapsed.TotalMilliseconds;
                bi = Math.Min(bi, x); bc = Math.Min(bc, y); si += x; sc += y;
            }
            Console.WriteLine($"hand-inline    : min {bi,7:F2} ms   mean {si / reps,7:F2} ms");
            Console.WriteLine($"VarInt.Read call: min {bc,7:F2} ms   mean {sc / reps,7:F2} ms");
            double pct = (bc - bi) / bi * 100.0;
            Console.WriteLine($"\nΔ min call vs inline: {pct,+6:F2} %   {(pct > 3 ? "→ inlining VarInt.Read is a WIN" : "→ already inlined / no opportunity")}");
            Console.WriteLine($"(sink={sink})");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static long Inline(byte[] buf, int len)
        {
            int pos = 0; uint enc = 0; long acc = 0;
            while (pos < len)
            {
                int shift = 0; uint r = 0;
                while (pos < len) { byte bb = buf[pos++]; r |= (uint)(bb & 0x7F) << shift; if ((bb & 0x80) == 0) break; shift += 7; }
                enc += r; acc += enc;
            }
            return acc;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static long Call(byte[] buf, int len)
        {
            int pos = 0; uint enc = 0; long acc = 0;
            while (pos < len) { enc += VarInt.Read(buf, ref pos, len); acc += enc; }
            return acc;
        }
    }
}
