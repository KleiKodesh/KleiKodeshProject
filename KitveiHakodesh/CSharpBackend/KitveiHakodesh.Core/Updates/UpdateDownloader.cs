using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace KitveiHakodesh.Core.Updates
{
    /// <summary>
    /// Fetches the installer and, later, launches it.
    ///
    /// Two things here are load-bearing and were each learned from a bug:
    ///
    /// ATOMIC DOWNLOAD. The file is written to a ".partial" and renamed only after its byte
    /// count is verified, so the real installer name never exists half-written. A truncated NSIS
    /// executable still reports its full ProductVersion — the version resource lives in the
    /// small stub at the start of the file — so a half-downloaded installer used to look
    /// complete, be announced on every launch, fail its CRC check on every close, and be
    /// skipped by the "already downloaded" check forever.
    ///
    /// ONE DOWNLOADER, ONE INSTALLER. Word and the standalone app can both reach this at the
    /// same moment. A cross-process mutex keeps two downloads from fighting over the same file,
    /// and the launcher refuses to start a second installer while one is running, because two
    /// silent installs extract into the same temp and install folders.
    /// </summary>
    public sealed class UpdateDownloader
    {
        private const string InstallerFileName = "KleiKodeshSetup.exe";
        private const string DownloadUrlFormat =
            "https://github.com/KleiKodesh/KleiKodeshProject/releases/download/{0}/{1}";

        /// <summary>Named so both hosts find the same one. Cross-process by design.</summary>
        private const string DownloadMutexName = "KleiKodesh-UpdateDownload-Mutex";

        /// <summary>Names of processes that mean an install is already under way.</summary>
        private static readonly string[] InstallerProcessNames =
        {
            "KleiKodeshSetup",
            "KleiKodeshVstoInstallerWpf",
        };

        private const int MaxAttempts = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);
        private const int CopyBufferBytes = 8192;

        private readonly string _installerPath;
        private readonly string _partialPath;

        /// <param name="downloadFolder">Where the installer is kept between download and launch.
        /// Defaults to the temp folder, which is right for a file that exists only until the
        /// next close.</param>
        public UpdateDownloader(string? downloadFolder = null)
        {
            string folder = downloadFolder ?? Path.GetTempPath();
            _installerPath = Path.Combine(folder, InstallerFileName);
            _partialPath = _installerPath + ".partial";
        }

        public string InstallerPath => _installerPath;

        /// <summary>
        /// The version stamped in the downloaded installer, or null when there is none on disk.
        /// A file stat and a header read — no network.
        /// </summary>
        public string? DownloadedVersion()
        {
            try
            {
                if (!File.Exists(_installerPath)) return null;
                string? version = FileVersionInfo.GetVersionInfo(_installerPath).ProductVersion;
                return string.IsNullOrWhiteSpace(version) ? null : version!.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Byte size of the downloaded installer, or null when there is none.
        ///
        /// Compared against the release asset's size, because the version stamp alone cannot be
        /// trusted: see the class remarks on truncated files.
        /// </summary>
        public long? DownloadedLength()
        {
            try
            {
                var file = new FileInfo(_installerPath);
                return file.Exists ? file.Length : (long?)null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The version of a downloaded installer that is newer than the installed one, or null
        /// when there is nothing to install.
        ///
        /// Disk only — no network, no async. This is the check a host runs at startup to find
        /// out whether it has an update waiting from a previous session's background download.
        ///
        /// It DELETES an installer that is not newer. That is the point rather than tidiness:
        /// leaving a same-or-older installer on disk is what made the updater announce an update
        /// on every launch and install nothing on every close.
        ///
        /// Returns null for a copy that was never installed, because this app updates by running
        /// its installer, and doing that from a portable copy would install software the user
        /// never asked for.
        /// </summary>
        public string? ReadyVersion()
        {
            string? installed = UpdateChecker.InstalledVersion();
            if (string.IsNullOrEmpty(installed)) return null;

            string? downloaded = DownloadedVersion();
            if (downloaded == null) return null;

            if (UpdateChecker.CompareVersions(downloaded, installed) > 0) return downloaded;

            DeleteDownloaded();
            return null;
        }

        /// <summary>Removes the downloaded installer and any leftover partial.</summary>
        public void DeleteDownloaded()
        {
            TryDelete(_installerPath);
            TryDelete(_partialPath);
        }

        /// <summary>
        /// Downloads the installer for a version.
        ///
        /// Returns false when another process is already downloading — that is a normal outcome
        /// on a machine running both Word and the standalone app, not a failure. Throws
        /// <see cref="UpdateDownloadFailedException"/> when the download itself does not produce
        /// a verified file; the partial is cleaned up either way, so a failure never leaves
        /// something that looks like an installer.
        /// </summary>
        /// <param name="version">The release tag, e.g. "v9.1.0".</param>
        /// <param name="downloadUrl">The resolved asset URL. When null, the URL is constructed
        /// by convention — how this behaved before releases carried an asset list.</param>
        /// <param name="expectedSize">The asset's byte count when known. A download that does
        /// not match it is rejected rather than renamed into place.</param>
        public async Task<bool> DownloadAsync(
            string version,
            string? downloadUrl = null,
            long expectedSize = 0,
            CancellationToken cancellationToken = default)
        {
            string url = downloadUrl
                ?? string.Format(DownloadUrlFormat, version, UpdateChecker.InstallerAssetName(version));

            using var mutex = new Mutex(initiallyOwned: false, DownloadMutexName, out _);
            bool held = false;
            try
            {
                // Non-blocking: if the other host is already fetching this, there is nothing to
                // gain by queueing behind it — the file it produces is the same file.
                held = mutex.WaitOne(0);
                if (!held) return false;

                await DownloadToPartialAsync(url, cancellationToken).ConfigureAwait(false);

                long downloaded = File.Exists(_partialPath) ? new FileInfo(_partialPath).Length : 0;
                if (downloaded == 0)
                    throw new UpdateDownloadFailedException(url, 0, expectedSize, null);

                if (expectedSize > 0 && downloaded != expectedSize)
                    throw new UpdateDownloadFailedException(url, downloaded, expectedSize, null);

                TryDelete(_installerPath);
                File.Move(_partialPath, _installerPath);
                return true;
            }
            catch (Exception)
            {
                TryDelete(_partialPath);
                throw;
            }
            finally
            {
                if (held) mutex.ReleaseMutex();
            }
        }

        /// <summary>
        /// Launches the downloaded installer.
        ///
        /// Returns false when it did not start something: nothing downloaded, or an installer is
        /// already running. Throws when the launch itself fails — the version this replaces
        /// showed a message box from inside the data layer, which is the orchestrator's call and
        /// impossible from a service with no window.
        ///
        /// UNELEVATED, DELIBERATELY. The install is per-user, so an elevated one would resolve
        /// its paths against the approving administrator's profile and the real user would never
        /// receive the update. Do not reintroduce the "runas" verb: the verb itself forces a
        /// consent prompt whatever the target's manifest says, and a declined or policy-denied
        /// prompt silently killed the update on every close.
        ///
        /// "--silent" means "skip the install click", NOT "run invisibly" — the installer shows
        /// its window while working, because it registers a service and a consent prompt raised
        /// from a hidden process minutes after Word closed had no visible parent and got
        /// dismissed. Pass NO OTHER ARGUMENTS: the executable on disk is always an OLDER release
        /// than this code, and a flag it does not understand can leave it hidden forever.
        /// </summary>
        public bool LaunchDownloaded()
        {
            if (!File.Exists(_installerPath)) return false;

            if (AnInstallerIsRunning()) return false;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _installerPath,
                    Arguments = "--silent",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(_installerPath),
                });
                // Process.Start can return null here on success — nothing to check.
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 0)
            {
                // ERROR_SUCCESS: Windows threw despite launching it. It started.
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // ERROR_CANCELLED — a consent prompt was declined. Current installers are
                // user-level and never prompt, but an installer downloaded back when the
                // manifest asked for admin still does. Respect the choice; the file stays for
                // the next close.
                return false;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 193)
            {
                // ERROR_BAD_EXE_FORMAT — the file is corrupt, most likely a truncated download
                // from before the partial-file scheme. Delete it so the next check fetches a
                // fresh copy instead of failing on every close forever.
                TryDelete(_installerPath);
                throw new UpdateLaunchFailedException(_installerPath, ex);
            }
            catch (Exception ex)
            {
                throw new UpdateLaunchFailedException(_installerPath, ex);
            }
        }

        private static bool AnInstallerIsRunning()
        {
            foreach (string name in InstallerProcessNames)
            {
                try
                {
                    if (Process.GetProcessesByName(name).Length > 0) return true;
                }
                catch (Exception) { /* cannot enumerate — assume not, and let the mutex decide */ }
            }
            return false;
        }

        private async Task DownloadToPartialAsync(string url, CancellationToken cancellationToken)
        {
#if !NET5_0_OR_GREATER
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
#endif
            Exception? lastFailure = null;
            long expectedFromHeader = 0;
            long received = 0;

            using var client = new HttpClient { Timeout = DownloadTimeout };
            client.DefaultRequestHeaders.Add("User-Agent", "KleiKodesh-UpdateChecker");

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt > 1) await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);

                try
                {
                    using HttpResponseMessage response = await client
                        .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);

                    if (IsWorthRetrying(response.StatusCode) && attempt < MaxAttempts) continue;
                    response.EnsureSuccessStatusCode();

                    expectedFromHeader = response.Content.Headers.ContentLength ?? 0;
                    received = 0;

                    using (Stream input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new FileStream(
                        _partialPath, FileMode.Create, FileAccess.Write, FileShare.None,
                        CopyBufferBytes, useAsync: true))
                    {
                        byte[] buffer = new byte[CopyBufferBytes];
                        int read;
                        while ((read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                        {
                            await output.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            received += read;
                        }
                    }

                    // A dropped connection ends the read loop without throwing. Anything shorter
                    // than Content-Length is a failed attempt, not a file.
                    if (expectedFromHeader > 0 && received != expectedFromHeader)
                    {
                        lastFailure = new IOException(
                            "the download ended early: " + received + " of " + expectedFromHeader + " bytes");
                        continue;
                    }

                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastFailure = ex;
                }
            }

            throw new UpdateDownloadFailedException(url, received, expectedFromHeader, lastFailure);
        }

        private static bool IsWorthRetrying(HttpStatusCode status) =>
            status == HttpStatusCode.RequestTimeout
            || status == HttpStatusCode.NotFound            // GitHub CDN propagation
            || (int)status == 429                           // rate limited
            || (int)status >= 500;

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception) { /* in use or already gone — the next attempt overwrites it */ }
        }
    }

    /// <summary>
    /// The installer did not download to a verified file.
    ///
    /// Carries the numbers rather than a sentence: <see cref="ReceivedBytes"/> against
    /// <see cref="ExpectedBytes"/> is what distinguishes "nothing arrived" from "it arrived
    /// truncated", and the host writes whatever the user should read.
    /// </summary>
    public sealed class UpdateDownloadFailedException : Exception
    {
        public string Url { get; }
        public long ReceivedBytes { get; }

        /// <summary>0 when neither the release nor the response said how big it should be.</summary>
        public long ExpectedBytes { get; }

        public UpdateDownloadFailedException(string url, long receivedBytes, long expectedBytes, Exception? inner)
            : base("the update download failed: " + receivedBytes + " of "
                   + (expectedBytes > 0 ? expectedBytes.ToString() : "an unknown number of")
                   + " bytes from " + url, inner)
        {
            Url = url;
            ReceivedBytes = receivedBytes;
            ExpectedBytes = expectedBytes;
        }
    }

    /// <summary>
    /// The downloaded installer would not start. Separate from a failed download because the
    /// answers differ: a download failure is worth retrying later, whereas this points at the
    /// file on disk — which is why the corrupt-executable case deletes it first.
    /// </summary>
    public sealed class UpdateLaunchFailedException : Exception
    {
        public string InstallerPath { get; }

        /// <summary>The Win32 error when the failure came from the shell, else 0. Present
        /// because the number is the only part of this a support question can act on.</summary>
        public int NativeErrorCode { get; }

        public UpdateLaunchFailedException(string installerPath, Exception inner)
            : base("the installer would not start: " + installerPath, inner)
        {
            InstallerPath = installerPath;
            NativeErrorCode = inner is Win32Exception win32 ? win32.NativeErrorCode : 0;
        }
    }
}
