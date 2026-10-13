using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpCompress.Compressors.LZMA;

namespace PayloadPacker
{
    /// <summary>
    /// Packs a directory tree into the chunked-LZMA payload archive consumed by
    /// AddinInstaller.ExtractAsync.
    ///
    /// Usage: PayloadPacker.exe &lt;sourceDir&gt; &lt;outputFile&gt;
    ///
    /// The format is defined in Build/Installer/Helpers/PayloadArchive.cs — keep
    /// the two in sync. Writer and reader are deliberately trivial: a header, then
    /// a (path, length, bytes) body in a fixed order, cut into LZMA chunks.
    /// </summary>
    internal static class Program
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("KKPKG2\n");

        // 16 MB chunks: big enough that cross-file matches still mostly land inside a
        // chunk (the dictionary is far smaller), small enough to give every core work.
        private const int ChunkSize = 16 * 1024 * 1024;
        private const int DictionarySize = 1 << 20;   // SharpCompress default
        private const int NumFastBytes = 32;          // SharpCompress default

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
            Console.WriteLine("Packing {0:N0} files ({1:N2} MB) with chunked LZMA...",
                files.Count, rawTotal / 1048576.0);

            string outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

            var sw = Stopwatch.StartNew();
            byte[] body = BuildBody(sourceDir, files);
            TimeSpan readTime = sw.Elapsed;

            // The body is cut into fixed-size chunks that compress in parallel. One
            // solid stream took 3+ minutes in this managed encoder; chunks use every
            // core. The chunk size is a constant, not derived from the core count, so
            // identical inputs still produce identical bytes on any machine.
            int chunkCount = (int)((body.LongLength + ChunkSize - 1) / ChunkSize);
            var packedChunks = new PackedChunk[chunkCount];
            Parallel.For(0, chunkCount, i =>
            {
                int offset = i * ChunkSize;
                int length = Math.Min(ChunkSize, body.Length - offset);
                packedChunks[i] = CompressChunk(body, offset, length);
            });
            TimeSpan compressTime = sw.Elapsed - readTime;

            using (var outFile = File.Create(outputPath))
            {
                outFile.Write(Magic, 0, Magic.Length);
                outFile.Write(BitConverter.GetBytes(files.Count), 0, 4);
                outFile.Write(BitConverter.GetBytes(chunkCount), 0, 4);
                foreach (PackedChunk chunk in packedChunks)
                {
                    outFile.Write(chunk.Properties, 0, chunk.Properties.Length);
                    outFile.Write(BitConverter.GetBytes((long)chunk.Data.Length), 0, 8);
                    outFile.Write(BitConverter.GetBytes((long)chunk.RawLength), 0, 8);
                    outFile.Write(chunk.Data, 0, chunk.Data.Length);
                }
            }

            long packed = new FileInfo(outputPath).Length;
            Console.WriteLine(
                "Payload packed: {0:N2} MB -> {1:N2} MB ({2:P1}) in {3:N0}s",
                rawTotal / 1048576.0,
                packed / 1048576.0,
                packed / (double)rawTotal,
                sw.Elapsed.TotalSeconds);
            Console.WriteLine(
                "  read {0:N1}s, compress {1:N1}s ({2} chunks), write {3:N1}s",
                readTime.TotalSeconds,
                compressTime.TotalSeconds,
                chunkCount,
                (sw.Elapsed - readTime - compressTime).TotalSeconds);
        }

        /// <summary>
        /// The uncompressed body: (path, length, bytes) per file, in the given order.
        /// </summary>
        private static byte[] BuildBody(string sourceDir, List<string> files)
        {
            int prefix = sourceDir.TrimEnd('\\').Length + 1;
            using (var body = new MemoryStream())
            {
                foreach (string file in files)
                {
                    // Store relative, backslash-separated paths: this is what
                    // ShouldSkipOnUpdate and IsServiceExe match against, and both
                    // normalise '/' to '\\' before comparing.
                    string relative = file.Substring(prefix).Replace('/', '\\');
                    byte[] pathBytes = Encoding.UTF8.GetBytes(relative);
                    byte[] data = File.ReadAllBytes(file);

                    body.Write(BitConverter.GetBytes(pathBytes.Length), 0, 4);
                    body.Write(pathBytes, 0, pathBytes.Length);
                    body.Write(BitConverter.GetBytes((long)data.Length), 0, 8);
                    body.Write(data, 0, data.Length);
                }
                return body.ToArray();
            }
        }

        private sealed class PackedChunk
        {
            public byte[] Properties;   // the 5-byte LZMA header the decoder needs
            public byte[] Data;
            public int RawLength;
        }

        private static PackedChunk CompressChunk(byte[] body, int offset, int length)
        {
            var props = new LzmaEncoderProperties(false, DictionarySize, NumFastBytes);
            using (var packed = new MemoryStream())
            {
                byte[] header;
                using (var lzma = new LzmaStream(props, false, packed))
                {
                    lzma.Write(body, offset, length);
                    header = lzma.Properties;
                }
                return new PackedChunk { Properties = header, Data = packed.ToArray(), RawLength = length };
            }
        }
    }
}
