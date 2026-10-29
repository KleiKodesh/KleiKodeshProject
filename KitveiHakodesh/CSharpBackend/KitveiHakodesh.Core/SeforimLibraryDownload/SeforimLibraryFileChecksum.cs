using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// The sha256 of a downloaded file, hashed in one pass once the file is complete.
    ///
    /// Hashing after the fact instead of while downloading is what makes resume simple: the
    /// running hash state cannot be saved across a restart, and re-reading 2 GB from local disk
    /// costs seconds, against minutes for the download it protects.
    /// </summary>
    public static class SeforimLibraryFileChecksum
    {
        private const int BufferBytes = 1 << 20;

        /// <summary>Lower-case hex sha256 of <paramref name="path"/>.</summary>
        /// <param name="onProgress">Bytes hashed so far, of the file's length.</param>
        public static async Task<string> Sha256Async(string path, Action<long, long> onProgress, CancellationToken cancellationToken)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.SequentialScan);
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[BufferBytes];
            long total = file.Length, done = 0;
            int read;
            while ((read = await file.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                sha.AppendData(buffer, 0, read);
                done += read;
                onProgress(done, total);
            }
            return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }
    }
}
