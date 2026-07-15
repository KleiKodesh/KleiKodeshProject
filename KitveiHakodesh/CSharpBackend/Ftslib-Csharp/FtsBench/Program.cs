using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace FtsBench
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "env";

            switch (cmd)
            {
                case "env":
                    PrintEnv();
                    return 0;

                case "dist":
                    DistDiag.Run(args);
                    return 0;

                case "codec":
                    CodecBench.Run(args);
                    return 0;

                case "bitmap":
                    BitmapBench.Run(args);
                    return 0;

                case "roundtrip":
                    RoundTrip.Run(args);
                    return 0;

                case "iter":
                    IterBench.Run(args);
                    return 0;

                default:
                    Console.WriteLine($"Unknown command '{cmd}'. Commands: env");
                    return 1;
            }
        }

        private static void PrintEnv()
        {
            Console.WriteLine("=== FtsBench runtime / SIMD probe ===");
            Console.WriteLine($"  Runtime           : {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            Console.WriteLine($"  OSArchitecture    : {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
            Console.WriteLine($"  ProcessArch       : {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            Console.WriteLine($"  Is64BitProcess    : {Environment.Is64BitProcess}");
            Console.WriteLine($"  ProcessorCount    : {Environment.ProcessorCount}");
            Console.WriteLine();
            Console.WriteLine($"  Vector.IsHardwareAccelerated : {Vector.IsHardwareAccelerated}");
            Console.WriteLine($"  Vector<byte>.Count           : {Vector<byte>.Count}   ({Vector<byte>.Count * 8} bit)");
            Console.WriteLine($"  Vector<short>.Count          : {Vector<short>.Count}");
            Console.WriteLine($"  Vector<int>.Count            : {Vector<int>.Count}");
            Console.WriteLine($"  Vector<uint>.Count           : {Vector<uint>.Count}");
            Console.WriteLine($"  Vector<long>.Count           : {Vector<long>.Count}");
            Console.WriteLine($"  Vector<ulong>.Count          : {Vector<ulong>.Count}");
            Console.WriteLine();

            // Sanity: does a real vector OR actually execute and agree with scalar?
            var a = new ulong[] { 0x1, 0x2, 0x4, 0x8, 0x10, 0x20, 0x40, 0x80 };
            var b = new ulong[] { 0x80, 0x40, 0x20, 0x10, 0x8, 0x4, 0x2, 0x1 };
            int vlen = Vector<ulong>.Count;
            var outv = new ulong[a.Length];
            for (int i = 0; i + vlen <= a.Length; i += vlen)
                Vector.BitwiseOr(new Vector<ulong>(a, i), new Vector<ulong>(b, i)).CopyTo(outv, i);
            Console.WriteLine($"  SIMD OR sanity (first word): 0x{outv[0]:X} (expect 0x81)");

            // Confirm the linked production types compiled in and are reachable.
            Console.WriteLine();
            Console.WriteLine("  Linked production types present:");
            Console.WriteLine($"    FtsLib.Search.VarInt         : {typeof(FtsLib.Search.VarInt).FullName}");
            Console.WriteLine($"    FtsLib.Search.PostingStream  : {typeof(FtsLib.Search.PostingStream).FullName}");
            Console.WriteLine($"    FtsLib.Search.PostingIterator: {typeof(FtsLib.Search.PostingIterator).FullName}");
            Console.WriteLine($"    FtsLib.Search.RoaringBitmap  : {typeof(FtsLib.Search.RoaringBitmap).FullName}");
        }
    }
}
