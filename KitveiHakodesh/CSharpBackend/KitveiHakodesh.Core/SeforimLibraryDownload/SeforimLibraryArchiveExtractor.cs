using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ZstdSharp;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// Unpacks a downloaded seforim-schemaN.db.zst — one zstd frame holding the SQLite file,
    /// no tar — into a sibling file. The caller renames it into place, so a half-unpacked
    /// database is never where anything looks for one.
    /// </summary>
    public static class SeforimLibraryArchiveExtractor
    {
        private const int BufferBytes = 1 << 20;

        /// <summary>The first bytes of every SQLite database file.</summary>
        private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

        /// <summary>Decompresses <paramref name="archivePath"/> into <paramref name="outputPath"/>.</summary>
        /// <param name="onProgress">Compressed bytes consumed, of the archive's length — the only
        /// total known up front.</param>
        /// <exception cref="InvalidDataException">Not a zstd stream, or it does not hold a SQLite file.</exception>
        public static async Task ExtractAsync(
            string archivePath, string outputPath, Action<long, long> onProgress, CancellationToken cancellationToken)
        {
            using (var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.SequentialScan))
            using (var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferBytes, FileOptions.SequentialScan))
            {
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
                }
                catch (ZstdException ex)
                {
                    throw new InvalidDataException("the archive is not valid zstd: " + ex.Message, ex);
                }
            }
        }
    }
}
