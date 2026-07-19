using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace KitveiHakodeshService.Common;

/// <summary>
/// A cheap, content-free fingerprint of a database file, used to decide whether a
/// derived index (FTS, catalog TOC, …) is stale and must be rebuilt.
///
/// Reads NO file content — just file-system metadata via two syscalls on an open
/// handle (~0.2 ms even on a 7 GB file, independent of size). The fields deliberately
/// cover each other's blind spots so there are no practical misses:
///
///   - size + mtime      — every normal write
///   - NTFS ChangeTime   — a same-size in-place edit with mtime restored (the call that
///                         restores mtime is itself a metadata change and bumps ctime)
///   - per-file USN      — assigned monotonically by NTFS on every change record;
///                         applications cannot set or restore it (catches even
///                         memory-mapped writes that update no timestamps)
///   - file id           — the file was replaced by a different file (new MFT record),
///                         even with identical size and restored timestamps
///   - -wal sidecar      — a SQLite WAL commit lands in "&lt;db&gt;-wal" before any
///                         checkpoint touches the main file; its metadata joins the stamp
///
/// Semantics: a byte-identical rewrite counts as "changed" — a write happened, and no
/// content-free detector can know the bytes matched without reading them; erring toward
/// one redundant rebuild is safe, a missed rebuild is not. On non-NTFS volumes the
/// ctime/USN fields degrade gracefully (0) and the stamp falls back to size+mtime.
///
/// Prefix the returned value with a caller-owned index-format version so a schema or
/// pipeline change to the derived index also forces a rebuild.
/// </summary>
public static class DbChangeStamp
{
    /// <summary>Compute the change stamp for <paramref name="dbPath"/>. Returns a
    /// readable pipe-delimited string suitable for a version file. A non-existent file
    /// yields a stable "missing" stamp. <paramref name="prefix"/> is prepended verbatim
    /// (e.g. the derived index's format version).</summary>
    public static string Compute(string dbPath, string prefix = "")
    {
        string head = string.IsNullOrEmpty(prefix) ? "" : prefix + "|";
        if (!File.Exists(dbPath))
            return $"{head}{dbPath.ToLowerInvariant()}|missing";

        var info = new FileInfo(dbPath);
        var (changeTime, usn, fileId) = ReadNativeStamp(dbPath);

        // SQLite WAL sidecar: in WAL mode a committed write lands in "<db>-wal" first —
        // the main file may not change until a checkpoint. A non-empty wal is part of
        // the database content, so its size + metadata join the stamp.
        string wal = "0";
        string walPath = dbPath + "-wal";
        var walInfo = new FileInfo(walPath);
        if (walInfo.Exists && walInfo.Length > 0)
        {
            var (walChangeTime, walUsn, _) = ReadNativeStamp(walPath);
            wal = $"{walInfo.Length}:{walChangeTime}:{walUsn}";
        }

        return $"{head}{dbPath.ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}" +
               $"|{changeTime}|{usn}|{fileId:X}|wal={wal}";
    }

    /// <summary>NTFS ChangeTime + per-file USN + file id via two metadata calls on a
    /// read-share handle. Returns zeros for whatever the volume/OS doesn't support.</summary>
    private static (long ChangeTime, long Usn, ulong FileId) ReadNativeStamp(string path)
    {
        if (!OperatingSystem.IsWindows()) return (0, 0, 0);
        try
        {
            using var handle = File.Open(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite | FileShare.Delete,
            });

            long changeTime = 0;
            var basic = default(FILE_BASIC_INFO);
            unsafe
            {
                if (GetFileInformationByHandleEx(handle.SafeFileHandle, 0 /*FileBasicInfo*/,
                        &basic, (uint)sizeof(FILE_BASIC_INFO)))
                    changeTime = basic.ChangeTime;
            }

            long usn = 0;
            ulong fileId = 0;
            // FSCTL_READ_FILE_USN_DATA — the file's most recent USN record header
            // (USN_RECORD_V2: FRN at offset 8, Usn at offset 24). No admin required.
            Span<byte> buf = stackalloc byte[512];
            unsafe
            {
                fixed (byte* p = buf)
                {
                    if (DeviceIoControl(handle.SafeFileHandle, 0x000900eb /*FSCTL_READ_FILE_USN_DATA*/,
                            null, 0, p, (uint)buf.Length, out uint got, null) && got >= 32)
                    {
                        fileId = BitConverter.ToUInt64(buf[8..16]);
                        usn = BitConverter.ToInt64(buf[24..32]);
                    }
                }
            }

            return (changeTime, usn, fileId);
        }
        catch
        {
            return (0, 0, 0); // stamp degrades to size+mtime on exotic volumes / access errors
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILE_BASIC_INFO
    {
        public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime;
        public uint FileAttributes;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool GetFileInformationByHandleEx(
        SafeFileHandle handle, int infoClass, void* info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool DeviceIoControl(
        SafeFileHandle handle, uint code, void* inBuf, uint inSize,
        void* outBuf, uint outSize, out uint returned, void* overlapped);
}
