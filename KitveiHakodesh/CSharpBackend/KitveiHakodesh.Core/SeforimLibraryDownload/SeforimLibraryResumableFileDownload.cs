using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodesh.Core.SeforimLibraryDownload
{
    /// <summary>
    /// Fetches one release asset into a .part file that survives the process: if the process
    /// stops, the next run continues from the bytes already on disk instead of starting over.
    ///
    /// Resuming uses an HTTP Range request guarded by If-Range with the ETag saved in the
    /// <see cref="SeforimLibraryDownloadResumeRecord"/>. GitHub's asset host (verified
    /// 2026-10-26) answers 206 with Content-Range and a strong ETag. If the file changed
    /// upstream, the server ignores the range and sends the whole file with 200 — the part is
    /// then truncated and refilled, never appended to.
    ///
    /// A dropped connection mid-file is retried here with the bytes kept. Only when every
    /// retry fails does the exception reach the caller, and the part still stays for the next run.
    /// </summary>
    public sealed class SeforimLibraryResumableFileDownload
    {
        private const int CopyBufferBytes = 1 << 20;
        private const int MaxAttempts = 6;

        /// <summary>No byte for this long means the connection is dead, whatever TCP thinks.</summary>
        private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

        private readonly HttpClient _http;

        public SeforimLibraryResumableFileDownload(HttpClient http)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
        }

        /// <summary>Downloads <paramref name="asset"/> into <paramref name="partPath"/> until it
        /// holds exactly <see cref="SeforimLibraryReleaseAsset.Size"/> bytes.</summary>
        /// <param name="onProgress">Bytes on disk so far, of the asset's size. Called often; throttle in the caller.</param>
        public async Task DownloadAsync(
            string releaseTag,
            SeforimLibraryReleaseAsset asset,
            string partPath,
            string resumeRecordPath,
            Action<long, long> onProgress,
            CancellationToken cancellationToken)
        {
            var record = SeforimLibraryDownloadResumeRecord.Load(resumeRecordPath);
            if (record == null || !record.Describes(releaseTag, asset))
            {
                DeleteIfExists(partPath);
                record = new SeforimLibraryDownloadResumeRecord
                {
                    ReleaseTag = releaseTag,
                    AssetName = asset.Name,
                    Size = asset.Size,
                    Sha256 = asset.Sha256,
                };
                record.Save(resumeRecordPath);
            }

            for (int attempt = 1; ; attempt++)
            {
                long onDisk = LengthOf(partPath);
                onProgress(onDisk, asset.Size);
                if (onDisk == asset.Size) return;
                if (onDisk > asset.Size)
                {
                    // More bytes than the asset has: not this file. Start over.
                    DeleteIfExists(partPath);
                    continue;
                }

                try
                {
                    await FetchRemainderAsync(asset, partPath, onDisk, record, resumeRecordPath, onProgress, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < MaxAttempts && IsTransient(ex, cancellationToken))
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 << attempt)), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task FetchRemainderAsync(
            SeforimLibraryReleaseAsset asset,
            string partPath,
            long onDisk,
            SeforimLibraryDownloadResumeRecord record,
            string resumeRecordPath,
            Action<long, long> onProgress,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
            bool resuming = onDisk > 0 && record.ETag != null;
            if (resuming)
            {
                request.Headers.Range = new RangeHeaderValue(onDisk, null);
                request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(record.ETag!));
            }

            using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stall.CancelAfter(StallTimeout);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                // The range is past the end: the part is not a prefix of this file.
                DeleteIfExists(partPath);
                throw new HttpRequestException("416 on resume; restarting from zero");
            }
            response.EnsureSuccessStatusCode();

            bool appending = resuming && response.StatusCode == HttpStatusCode.PartialContent;
            if (!appending)
            {
                // A full body — first fetch, a part with no ETag to guard it, or the file
                // changed upstream. Whatever is on disk is not a prefix of it.
                onDisk = 0;
                string? etag = response.Headers.ETag is { IsWeak: false } strong ? strong.ToString() : null;
                if (!string.Equals(record.ETag, etag, StringComparison.Ordinal))
                {
                    record.ETag = etag;
                    record.Save(resumeRecordPath);
                }
            }

            using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var destination = new FileStream(
                partPath, appending ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read,
                CopyBufferBytes, FileOptions.SequentialScan);

            byte[] buffer = new byte[CopyBufferBytes];
            long written = onDisk;
            while (true)
            {
                stall.CancelAfter(StallTimeout);
                int read = await source.ReadAsync(buffer, 0, buffer.Length, stall.Token).ConfigureAwait(false);
                if (read == 0) break;
                await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                written += read;
                onProgress(written, asset.Size);
            }
        }

        /// <summary>Worth another try with the bytes kept: the network, or our own stall timer —
        /// never the user's cancel, and never a disk error.</summary>
        private static bool IsTransient(Exception ex, CancellationToken userCancel) =>
            ex is HttpRequestException
            || (ex is OperationCanceledException && !userCancel.IsCancellationRequested)
            || (ex is IOException && ex.InnerException is System.Net.Sockets.SocketException);

        private static long LengthOf(string path)
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : 0;
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
