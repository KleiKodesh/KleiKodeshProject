using System;
using System.IO;
using System.Text;

namespace KleiKodeshVstoInstallerWpf.Helpers
{
    /// <summary>
    /// Reader for the solid-LZMA payload archive (KleiKodesh.pkg).
    ///
    /// WHY THIS EXISTS INSTEAD OF A ZIP
    /// --------------------------------
    /// The installer's launch time was dominated by NSIS decompressing the whole
    /// payload to %TEMP% *before* the WPF window could appear (~7.6 s on a 153 MB
    /// payload). Pre-compressing the payload ourselves and telling NSIS to use
    /// `SetCompressor zlib` turns the NSIS step into a near-straight byte copy
    /// (~0.9 s), and moves the decompression cost into the install phase where a
    /// progress bar already exists.
    ///
    /// A zip cannot do that job: .NET Framework 4.8's ZipArchive only understands
    /// Stored and Deflate, and Deflate compresses each entry independently — on
    /// this payload that costs ~16 MB versus solid LZMA (48.7 MB vs 33.0 MB).
    /// Solid LZMA gets the size back because it dedupes across files (the three
    /// tesseract .traineddata blobs, x64/x86 SQLite.Interop.dll, pdf.mjs and its
    /// .map, Kiwix bundle.js and bundle.min.js).
    ///
    /// FORMAT
    /// ------
    /// Everything after the header is ONE LZMA stream (SharpCompress LZipStream),
    /// so the archive is strictly sequential — there is no random access by design.
    ///
    ///   "KKPKG1\n"                      magic, 7 bytes ASCII
    ///   int32   entryCount              little-endian
    ///   --- begin LZip stream ---
    ///   per entry, in order:
    ///     int32  pathByteCount
    ///     byte[] path                   UTF-8, backslash-separated, relative
    ///     int64  length                 uncompressed byte count
    ///     byte[] data                   raw file bytes
    ///   --- end LZip stream ---
    ///
    /// Directories are NOT stored as entries; the extractor creates them from each
    /// entry's path. The old zip carried explicit directory entries (recognised by
    /// an empty Name) — the sequential reader has no equivalent and does not need
    /// one, since every file's parent is created on demand.
    /// </summary>
    internal static class PayloadArchive
    {
        /// <summary>File name of the payload archive, as written next to the installer exe.</summary>
        public const string FileName = "KleiKodesh.pkg";

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("KKPKG1\n");

        /// <summary>One entry's metadata, handed to the extraction callback.</summary>
        public sealed class Entry
        {
            /// <summary>Relative path with backslash separators, e.g. "KitveiHakodesh\index.html".</summary>
            public string Path { get; set; }

            /// <summary>Uncompressed length in bytes.</summary>
            public long Length { get; set; }
        }

        /// <summary>
        /// Reads the header and returns the entry count, leaving <paramref name="stream"/>
        /// positioned at the first byte of the compressed body.
        /// </summary>
        public static int ReadHeader(Stream stream)
        {
            var magic = new byte[Magic.Length];
            ReadExactly(stream, magic, magic.Length);
            for (int i = 0; i < Magic.Length; i++)
            {
                if (magic[i] != Magic[i])
                    throw new InvalidDataException(
                        "Payload archive header is not valid (expected KKPKG1).");
            }

            var countBytes = new byte[4];
            ReadExactly(stream, countBytes, 4);
            return BitConverter.ToInt32(countBytes, 0);
        }

        /// <summary>
        /// Reads the next entry's metadata from the decompressed body stream.
        /// The caller must then consume exactly <see cref="Entry.Length"/> bytes
        /// before calling this again — the stream is sequential and unseekable.
        /// </summary>
        public static Entry ReadEntryHeader(Stream body)
        {
            var lenBytes = new byte[4];
            ReadExactly(body, lenBytes, 4);
            int pathLen = BitConverter.ToInt32(lenBytes, 0);

            var pathBytes = new byte[pathLen];
            ReadExactly(body, pathBytes, pathLen);

            var sizeBytes = new byte[8];
            ReadExactly(body, sizeBytes, 8);

            return new Entry
            {
                Path   = Encoding.UTF8.GetString(pathBytes),
                Length = BitConverter.ToInt64(sizeBytes, 0)
            };
        }

        /// <summary>
        /// Copies exactly <paramref name="count"/> bytes from <paramref name="source"/>
        /// to <paramref name="destination"/>. Needed because the body stream is a
        /// continuous LZMA stream — reading past an entry would consume the next one.
        /// </summary>
        public static void CopyExactly(Stream source, Stream destination, long count)
        {
            var buffer = new byte[81920];
            long remaining = count;
            while (remaining > 0)
            {
                int want = (int)Math.Min(buffer.Length, remaining);
                int got  = source.Read(buffer, 0, want);
                if (got <= 0)
                    throw new EndOfStreamException(
                        "Payload archive truncated: expected " + count + " bytes, " +
                        remaining + " missing.");
                destination.Write(buffer, 0, got);
                remaining -= got;
            }
        }

        /// <summary>Reads exactly <paramref name="count"/> bytes or throws.</summary>
        private static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int got = stream.Read(buffer, offset, count - offset);
                if (got <= 0)
                    throw new EndOfStreamException("Payload archive truncated in header.");
                offset += got;
            }
        }
    }
}
