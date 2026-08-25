using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>
    /// A cheap, content-free fingerprint of a database file, used to decide whether something
    /// derived from it — a full-text index, a catalog index — is stale.
    ///
    /// Reads NO file content: two metadata syscalls on an open handle, about 0.2 ms even on a
    /// 7 GB file and independent of its size. The fields deliberately cover each other's blind
    /// spots, so there is no practical way for a write to slip past all of them:
    ///
    ///   size + mtime      every ordinary write
    ///   NTFS ChangeTime   a same-size in-place edit with mtime restored — the very call that
    ///                     restores mtime is itself a metadata change, and bumps ctime
    ///   per-file USN      assigned monotonically by NTFS on every change record; an
    ///                     application cannot set or restore it, so this catches even
    ///                     memory-mapped writes that update no timestamp at all
    ///   file id           the file was REPLACED by a different one (new MFT record), even at
    ///                     identical size with every timestamp restored
    ///   -wal sidecar      a SQLite WAL commit lands in "&lt;db&gt;-wal" first and may not touch
    ///                     the main file until a checkpoint, so the sidecar joins the stamp
    ///
    /// A byte-identical rewrite counts as CHANGED. A write happened, and no content-free
    /// detector can know the bytes matched without reading them — one redundant rebuild is
    /// safe, a missed one is not. On non-NTFS volumes the ctime and USN fields degrade to 0
    /// and the fingerprint falls back to size plus mtime.
    ///
    /// Prefix the result with the derived index's own format version, so changing the pipeline
    /// forces a rebuild too.
    /// </summary>
    public static class DbFileFingerprint
    {
        /// <summary>
        /// Fingerprints <paramref name="databasePath"/> as a readable pipe-delimited string fit
        /// for a version file. A file that is not there yields a stable "missing" value, so a
        /// disconnected drive does not read as a change every time.
        /// </summary>
        /// <param name="prefix">Prepended verbatim — normally the derived index's format version.</param>
        public static string Compute(string databasePath, string prefix = "")
        {
            string head = string.IsNullOrEmpty(prefix) ? "" : prefix + "|";
            if (!File.Exists(databasePath))
                return head + databasePath.ToLowerInvariant() + "|missing";

            var file = new FileInfo(databasePath);
            NativeStamp stamp = ReadNativeStamp(databasePath);

            string wal = "0";
            var walFile = new FileInfo(databasePath + "-wal");
            if (walFile.Exists && walFile.Length > 0)
            {
                NativeStamp walStamp = ReadNativeStamp(walFile.FullName);
                wal = walFile.Length + ":" + walStamp.ChangeTime + ":" + walStamp.Usn;
            }

            return head + databasePath.ToLowerInvariant()
                 + "|" + file.Length
                 + "|" + file.LastWriteTimeUtc.Ticks
                 + "|" + stamp.ChangeTime
                 + "|" + stamp.Usn
                 + "|" + stamp.FileId.ToString("X")
                 + "|wal=" + wal;
        }

        private struct NativeStamp
        {
            public long ChangeTime;
            public long Usn;
            public ulong FileId;
        }

        /// <summary>
        /// NTFS ChangeTime, per-file USN and file id, from two metadata calls on a read-share
        /// handle. Returns zeros for whatever the volume or OS does not support — an exotic
        /// filesystem should cost accuracy, not throw.
        ///
        /// The handle is opened with FileShare.ReadWrite | Delete because the point is to
        /// fingerprint a database another process may be writing right now; an exclusive open
        /// would fail exactly when the answer matters most.
        /// </summary>
        private static unsafe NativeStamp ReadNativeStamp(string path)
        {
            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);

                var stamp = default(NativeStamp);

                var basic = default(FileBasicInfo);
                if (GetFileInformationByHandleEx(
                        stream.SafeFileHandle, FileBasicInfoClass, &basic, (uint)sizeof(FileBasicInfo)))
                    stamp.ChangeTime = basic.ChangeTime;

                // FSCTL_READ_FILE_USN_DATA hands back the file's most recent USN record header
                // (USN_RECORD_V2: the file reference number at offset 8, the USN at 24).
                // No elevation required.
                byte[] buffer = new byte[512];
                fixed (byte* raw = buffer)
                {
                    if (DeviceIoControl(stream.SafeFileHandle, FsctlReadFileUsnData,
                            null, 0, raw, (uint)buffer.Length, out uint returned, null)
                        && returned >= 32)
                    {
                        stamp.FileId = BitConverter.ToUInt64(buffer, 8);
                        stamp.Usn = BitConverter.ToInt64(buffer, 24);
                    }
                }

                return stamp;
            }
            catch (Exception)
            {
                return default; // degrade to size + mtime
            }
        }

        private const int FileBasicInfoClass = 0;
        private const uint FsctlReadFileUsnData = 0x000900eb;

        [StructLayout(LayoutKind.Sequential)]
        private struct FileBasicInfo
        {
            public long CreationTime;
            public long LastAccessTime;
            public long LastWriteTime;
            public long ChangeTime;
            public uint FileAttributes;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern unsafe bool GetFileInformationByHandleEx(
            SafeFileHandle handle, int infoClass, void* info, uint size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern unsafe bool DeviceIoControl(
            SafeFileHandle handle, uint code, void* inBuffer, uint inSize,
            void* outBuffer, uint outSize, out uint returned, void* overlapped);
    }
}
