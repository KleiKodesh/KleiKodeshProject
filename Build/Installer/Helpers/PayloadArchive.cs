using SharpCompress.Compressors.LZMA;
using System;
using System.IO;
using System.Text;

namespace KleiKodeshVstoInstallerWpf.Helpers
{
    /// <summary>
    /// Reader for the chunked-LZMA payload archive (KleiKodesh.pkg).
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
    /// The body is cut into fixed-size chunks (16 MB uncompressed), each its own raw
    /// LZMA stream, so the packer can compress them in parallel: one solid stream
    /// took 3+ minutes of every build. Chunk boundaries fall anywhere, even inside a
    /// file; OpenBody joins the chunks back into one sequential body stream. There
    /// is no random access by design.
    ///
    ///   "KKPKG2\n"                      magic, 7 bytes ASCII
    ///   int32   entryCount              little-endian
    ///   int32   chunkCount
    ///   per chunk:
    ///     byte[5] lzmaProperties
    ///     int64   packedLength
    ///     int64   rawLength
    ///     byte[]  packed data           raw LZMA (SharpCompress LzmaStream)
    ///
    ///   The joined body, per entry, in order:
    ///     int32  pathByteCount
    ///     byte[] path                   UTF-8, backslash-separated, relative
    ///     int64  length                 uncompressed byte count
    ///     byte[] data                   raw file bytes
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

        /// <summary>
        /// Command-line switch carrying an explicit path to the payload archive:
        ///   --payload "C:\...\Temp\KleiKodeshInstaller\KleiKodesh.pkg"
        ///
        /// Needed because the installer relaunches itself elevated for ניקוי עמוק
        /// (AdminHelper.RelaunchAsAdmin). "runas" starts the new process as the
        /// ADMINISTRATOR account, so %LOCALAPPDATA% — and therefore %TEMP%, where the
        /// NSIS wrapper staged the payload — resolves to a different user profile.
        /// Only the exe crosses that boundary, not its sibling files, so the elevated
        /// instance must be told where the payload actually is.
        ///
        /// This was invisible while the payload was an embedded resource: it travelled
        /// inside the exe and survived any relaunch.
        /// </summary>
        public const string PathSwitch = "--payload";

        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("KKPKG2\n");

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
                        "Payload archive header is not valid (expected KKPKG2).");
            }

            return ReadInt32(stream);
        }

        /// <summary>
        /// Opens the decompressed body. Call after <see cref="ReadHeader"/>; the
        /// returned stream reads the chunks one after another as a single stream.
        /// </summary>
        public static Stream OpenBody(Stream stream)
        {
            return new ChunkedBodyStream(stream, ReadInt32(stream));
        }

        /// <summary>
        /// Read-only, forward-only stream over the body's LZMA chunks. Each chunk's
        /// packed bytes are read into memory before decoding (a chunk is a few MB),
        /// so the decoder can never read past its own chunk into the next one.
        /// </summary>
        private sealed class ChunkedBodyStream : Stream
        {
            private readonly Stream _source;
            private int _chunksLeft;
            private Stream _current;

            public ChunkedBodyStream(Stream source, int chunkCount)
            {
                _source = source;
                _chunksLeft = chunkCount;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                while (true)
                {
                    if (_current != null)
                    {
                        int got = _current.Read(buffer, offset, count);
                        if (got > 0) return got;
                        _current.Dispose();
                        _current = null;
                    }
                    if (_chunksLeft == 0) return 0;
                    _current = OpenNextChunk();
                    _chunksLeft--;
                }
            }

            private Stream OpenNextChunk()
            {
                var properties = new byte[5];
                ReadExactly(_source, properties, 5);
                long packedLength = ReadInt64(_source);
                long rawLength = ReadInt64(_source);

                var packed = new byte[packedLength];
                ReadExactly(_source, packed, packed.Length);
                return new LzmaStream(properties, new MemoryStream(packed), packedLength, rawLength);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _current?.Dispose();
                base.Dispose(disposing);
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

        private static int ReadInt32(Stream stream)
        {
            var bytes = new byte[4];
            ReadExactly(stream, bytes, 4);
            return BitConverter.ToInt32(bytes, 0);
        }

        private static long ReadInt64(Stream stream)
        {
            var bytes = new byte[8];
            ReadExactly(stream, bytes, 8);
            return BitConverter.ToInt64(bytes, 0);
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
