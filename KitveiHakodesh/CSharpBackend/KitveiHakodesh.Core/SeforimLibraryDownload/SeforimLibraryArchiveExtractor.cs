using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>The archive's bytes are wrong: a damaged or missing part. (InvalidDataException,
    /// the natural base, is sealed.) Handled wherever InvalidDataException is.</summary>
    public abstract class SeforimLibraryArchiveException : Exception
    {
        protected SeforimLibraryArchiveException(string message, Exception? inner = null) : base(message, inner) { }
    }

    /// <summary>A part of the archive does not match its sha256. <see cref="PartPath"/> names it,
    /// so a download re-fetches only that part.</summary>
    public sealed class SeforimLibraryPartChecksumException : SeforimLibraryArchiveException
    {
        public SeforimLibraryPartChecksumException(string partPath, string actual, string expected)
            : base($"{System.IO.Path.GetFileName(partPath)} is damaged: sha256 {actual}, expected {expected}")
        {
            PartPath = partPath;
        }

        public string PartPath { get; }
    }

    /// <summary>The archive is incomplete: the zstd stream ends before its frame does, or a
    /// file is smaller than its manifest says - a part is missing or cut short.</summary>
    public sealed class SeforimLibraryArchiveTruncatedException : SeforimLibraryArchiveException
    {
        public SeforimLibraryArchiveTruncatedException(Exception inner) : base("the archive is truncated", inner) { }

        public SeforimLibraryArchiveTruncatedException(string message) : base(message) { }
    }

    /// <summary>One file of a compressed archive, in order, and the sha256 it must have (null: unchecked).</summary>
    public sealed record SeforimLibraryArchivePart(string Path, string? Sha256);

    /// <summary>
    /// Unpacks a seforim-schemaN.db.zst - one zstd stream holding the SQLite file, no tar -
    /// into a sibling file. A split archive is read part after part as one stream, never joined
    /// on disk. The caller renames the result into place, so a half-unpacked database is never
    /// where anything looks for one.
    /// </summary>
    public static class SeforimLibraryArchiveExtractor
    {
        private const int BufferBytes = 1 << 20;

        /// <summary>The first bytes of every SQLite database file.</summary>
        private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

        /// <summary>Decompresses <paramref name="archivePath"/> into <paramref name="outputPath"/>.</summary>
        public static Task ExtractAsync(
            string archivePath, string outputPath, Action<long, long> onProgress, CancellationToken cancellationToken) =>
            ExtractAsync(new[] { new SeforimLibraryArchivePart(archivePath, null) }, outputPath, onProgress, cancellationToken);

        /// <summary>Decompresses the concatenation of <paramref name="parts"/> into <paramref name="outputPath"/>.
        /// Each part's sha256, when given, is checked as it is read - one pass over the bytes.</summary>
        /// <param name="onProgress">Compressed bytes consumed, of all parts' length - the only
        /// total known up front.</param>
        /// <exception cref="InvalidDataException">Not a zstd stream, a part does not match its
        /// sha256, or the stream does not hold a SQLite file.</exception>
        public static async Task ExtractAsync(
            IReadOnlyList<SeforimLibraryArchivePart> parts, string outputPath, Action<long, long> onProgress, CancellationToken cancellationToken)
        {
            using var archive = new ConcatenatedPartsStream(parts);
            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferBytes, FileOptions.SequentialScan);
            long total = archive.Length;
            try
            {
                using var decompressor = new DecompressionStream(archive, BufferBytes);
                byte[] buffer = new byte[BufferBytes];
                bool headerChecked = false;
                int read;
                while ((read = await decompressor.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (!headerChecked)
                    {
                        if (read < SqliteHeader.Length || !buffer.AsSpan(0, SqliteHeader.Length).SequenceEqual(SqliteHeader))
                            throw new InvalidDataException("the archive does not hold a SQLite database");
                        headerChecked = true;
                    }
                    await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                    onProgress(archive.Position, total);
                }
                if (!headerChecked) throw new InvalidDataException("the archive is empty");
                // The decoder may stop at the end of the frame before the last bytes were read:
                // drain them so the last part's hash covers the whole file.
                archive.DrainAndVerify();
            }
            catch (ZstdException ex)
            {
                // A damaged part breaks the zstd stream before its own end is reached: read the
                // rest, so a part that fails its sha256 is named as such (it gets re-downloaded).
                archive.DrainAndVerify();
                throw new InvalidDataException("the archive is not valid zstd: " + ex.Message, ex);
            }
            catch (EndOfStreamException ex)
            {
                // ZstdSharp's answer to a stream that stops mid-frame (a last part missing, a
                // .zst cut short). A part that is present but damaged still names itself first.
                archive.DrainAndVerify();
                throw new SeforimLibraryArchiveTruncatedException(ex);
            }
        }

        /// <summary>Read-only, forward-only stream over files one after another, hashing each.</summary>
        private sealed class ConcatenatedPartsStream : Stream
        {
            private readonly IReadOnlyList<SeforimLibraryArchivePart> _parts;
            private int _index = -1;
            private FileStream? _current;
            private IncrementalHash? _hash;
            private long _position;

            public ConcatenatedPartsStream(IReadOnlyList<SeforimLibraryArchivePart> parts)
            {
                if (parts.Count == 0) throw new InvalidDataException("no archive parts");
                _parts = parts;
                Length = parts.Sum(p => new FileInfo(p.Path).Length);
                OpenNext();
            }

            public override long Length { get; }
            public override long Position { get => _position; set => throw new NotSupportedException(); }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;

            public override int Read(byte[] buffer, int offset, int count)
            {
                while (_current != null)
                {
                    int read = _current.Read(buffer, offset, count);
                    if (read > 0)
                    {
                        _hash?.AppendData(buffer, offset, read);
                        _position += read;
                        return read;
                    }
                    FinishCurrent();
                    OpenNext();
                }
                return 0;
            }

            public void DrainAndVerify()
            {
                byte[] buffer = new byte[BufferBytes];
                while (Read(buffer, 0, buffer.Length) > 0) { }
            }

            private void OpenNext()
            {
                _index++;
                if (_index >= _parts.Count) { _current = null; return; }
                _current = new FileStream(_parts[_index].Path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.SequentialScan);
                _hash = _parts[_index].Sha256 != null ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
            }

            private void FinishCurrent()
            {
                _current?.Dispose();
                _current = null;
                if (_hash == null) return;
                string actual = Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
                _hash.Dispose();
                _hash = null;
                var part = _parts[_index];
                if (!string.Equals(actual, part.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new SeforimLibraryPartChecksumException(part.Path, actual, part.Sha256!);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { _current?.Dispose(); _hash?.Dispose(); }
                base.Dispose(disposing);
            }

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
