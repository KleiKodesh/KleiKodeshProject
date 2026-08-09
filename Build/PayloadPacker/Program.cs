using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SharpCompress.Compressors;
using SharpCompress.Compressors.LZMA;

namespace PayloadPacker
{
    /// <summary>
    /// Packs a directory tree into the solid-LZMA payload archive consumed by
    /// AddinInstaller.ExtractAsync.
    ///
    /// Usage: PayloadPacker.exe &lt;sourceDir&gt; &lt;outputFile&gt;
    ///
    /// The format is defined in Build/Installer/Helpers/PayloadArchive.cs — keep
    /// the two in sync. Writer and reader are deliberately trivial: a header, then
    /// one LZMA stream containing (path, length, bytes) triples in a fixed order.
    /// </summary>
    internal static class Program
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("KKPKG1\n");

        private static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: PayloadPacker.exe <sourceDir> <outputFile>");
                return 2;
            }

            string sourceDir = Path.GetFullPath(args[0]);
            string outputPath = Path.GetFullPath(args[1]);

            if (!Directory.Exists(sourceDir))
            {
                Console.Error.WriteLine("Source directory not found: " + sourceDir);
                return 2;
            }

            try
            {
                Pack(sourceDir, outputPath);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PayloadPacker failed: " + ex);
                return 1;
            }
        }

        private static void Pack(string sourceDir, string outputPath)
        {
            // Sort for a deterministic archive: identical inputs must produce
            // identical bytes so rebuilds are reproducible and diffable.
            List<string> files = Directory
                .GetFiles(sourceDir, "*", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            long rawTotal = files.Sum(f => new FileInfo(f).Length);
            Console.WriteLine("Packing {0:N0} files ({1:N2} MB) with solid LZMA...",
                files.Count, rawTotal / 1048576.0);

            string outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

            var sw = Stopwatch.StartNew();
            int prefix = sourceDir.TrimEnd('\\').Length + 1;

            using (var outFile = File.Create(outputPath))
            {
                outFile.Write(Magic, 0, Magic.Length);
                outFile.Write(BitConverter.GetBytes(files.Count), 0, 4);

                // Everything past the header is a single LZMA stream, so the
                // compressor sees the whole payload as one input and can match
                // across file boundaries — that solid window is the entire point.
                using (var lz = new LZipStream(outFile, CompressionMode.Compress))
                {
                    var buffer = new byte[81920];

                    foreach (string file in files)
                    {
                        // Store relative, backslash-separated paths: this is what
                        // ShouldSkipOnUpdate and IsServiceExe match against, and both
                        // normalise '/' to '\\' before comparing.
                        string relative = file.Substring(prefix).Replace('/', '\\');
                        byte[] pathBytes = Encoding.UTF8.GetBytes(relative);
                        var info = new FileInfo(file);

                        lz.Write(BitConverter.GetBytes(pathBytes.Length), 0, 4);
                        lz.Write(pathBytes, 0, pathBytes.Length);
                        lz.Write(BitConverter.GetBytes(info.Length), 0, 8);

                        using (var src = File.OpenRead(file))
                        {
                            long remaining = info.Length;
                            while (remaining > 0)
                            {
                                int want = (int)Math.Min(buffer.Length, remaining);
                                int got = src.Read(buffer, 0, want);
                                if (got <= 0)
                                    throw new EndOfStreamException(
                                        "File shrank while packing: " + file);
                                lz.Write(buffer, 0, got);
                                remaining -= got;
                            }
                        }
                    }
                }
            }

            long packed = new FileInfo(outputPath).Length;
            Console.WriteLine(
                "Payload packed: {0:N2} MB -> {1:N2} MB ({2:P1}) in {3:N0}s",
                rawTotal / 1048576.0,
                packed / 1048576.0,
                packed / (double)rawTotal,
                sw.Elapsed.TotalSeconds);
        }
    }
}
