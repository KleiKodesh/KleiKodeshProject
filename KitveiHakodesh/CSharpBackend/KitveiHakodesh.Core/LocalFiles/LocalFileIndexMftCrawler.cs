// Written before nullable annotations (ported from the DocumentLocator library, which built
// with these warnings suppressed); not annotated yet.
#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace KitveiHakodesh.Core.LocalFiles
{
    /// <summary>
    /// Reads every file-system entry on an NTFS volume in one linear pass through
    /// the Master File Table using FSCTL_ENUM_USN_DATA, then resolves each entry's
    /// full path by walking the parent FRN chain.
    ///
    /// Requires the process to run as Administrator.
    /// </summary>
    public static class LocalFileIndexMftCrawler
    {
        // ── Win32 ─────────────────────────────────────────────────────────────────

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(
            IntPtr hDevice, uint dwIoControlCode,
            IntPtr lpInBuffer,  uint nInBufferSize,
            IntPtr lpOutBuffer, uint nOutBufferSize,
            out uint lpBytesReturned, IntPtr lpOverlapped);

        private const uint GENERIC_READ               = 0x80000000;
        private const uint FILE_SHARE_READ            = 0x00000001;
        private const uint FILE_SHARE_WRITE           = 0x00000002;
        private const uint OPEN_EXISTING              = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint FSCTL_GET_NTFS_VOLUME_DATA = 0x00090064;
        private const uint FSCTL_ENUM_USN_DATA        = 0x000900B3;
        private const int  ERROR_HANDLE_EOF           = 38;
        private static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        private struct MFT_ENUM_DATA
        {
            public ulong StartFileReferenceNumber;
            public long  LowUsn;
            public long  HighUsn;
        }

        // ── Skipped directories ───────────────────────────────────────────────────

        /// <summary>
        /// Directory names that are skipped entirely during path enumeration.
        /// Any path whose ancestor chain contains one of these names (case-insensitive)
        /// is excluded from the index — neither the directory itself nor any file
        /// inside it will appear in search results.
        ///
        /// These are well-known Windows system locations that never contain user
        /// documents, so indexing them wastes memory and slows down builds without
        /// providing any useful results.
        /// </summary>
        private static readonly HashSet<string> SkippedDirectoryNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            // Windows OS internals
            "Windows",
            "WinSxS",
            "System32",
            "SysWOW64",
            "SystemApps",
            "WinStore",

            // Windows update and servicing
            "SoftwareDistribution",
            "WindowsUpdate",
            "Panther",
            "Servicing",

            // Boot and recovery
            "Boot",
            "Recovery",
            "System Volume Information",

            // Package and app caches
            "Packages",
            "PackageRepository",
            "PackageStaging",
            "PackageCache",

            // Windows Installer remnants
            "Installer",
            "MsiExec",

            // Driver stores
            "DriverStore",
            "drivers",
            "inf",

            // Assembly caches
            "assembly",
            "GAC",
            "GAC_32",
            "GAC_64",
            "GAC_MSIL",
            "NativeImages",

            // .NET and runtime
            "dotnet",
            "DotNet",
            "Microsoft.NET",

            // Application temp and cache folders
            "Temp",
            "tmp",
            "cache",
            "Cache",
            "CrashDumps",
            "CrashReports",
            "WER",                        // Windows Error Reporting

            // Browser caches (not user documents)
            "GPUCache",
            "Code Cache",
            "blob_storage",
            "WebStorage",
            "ShaderCache",
            "ScriptCache",
            "CacheStorage",

            // Source control internals
            ".git",
            ".svn",
            ".hg",

            // Node / package manager artefacts
            "node_modules",
            ".npm",
            ".yarn",
            ".pnpm-store",

            // Recycling
            "$Recycle.Bin",
            "RECYCLER",
            "$RECYCLE.BIN",
        };

        /// <summary>
        /// Returns true if a single directory name is in <see cref="SkippedDirectoryNames"/>.
        /// Shared with <see cref="LocalFileIndexDirectoryCrawler"/> so both crawl strategies exclude
        /// the same folders.
        /// </summary>
        public static bool IsSkippedDirectoryName(string directoryName)
            => SkippedDirectoryNames.Contains(directoryName);

        /// <summary>
        /// Returns true if the resolved path should be excluded because one of its
        /// directory segments is in <see cref="SkippedDirectoryNames"/>.
        /// The path is expected to be in its original case (not yet lowercased).
        /// </summary>
        private static bool IsInSkippedDirectory(string fullPath)
        {
            // Walk every segment except the last one (which is the file name itself).
            // A path like "C:\Windows\System32\foo.pdf" is excluded because "Windows"
            // and "System32" are both in the skip set.
            int start = 0;
            // Skip the drive root ("C:\" = 3 chars) so we don't accidentally test "C:" as a segment.
            if (fullPath.Length >= 3 && fullPath[1] == ':') start = 3;

            int end = fullPath.LastIndexOf('\\');
            if (end < start) return false;   // no directory component

            int pos = start;
            while (pos <= end)
            {
                int next = fullPath.IndexOf('\\', pos);
                if (next < 0 || next > end) break;

                string segment = fullPath.Substring(pos, next - pos);
                if (SkippedDirectoryNames.Contains(segment))
                    return true;

                pos = next + 1;
            }
            return false;
        }

        // ── Allowed extensions ────────────────────────────────────────────────────

        /// <summary>
        /// Document file formats supported by Word, Excel, and PowerPoint per
        /// https://learn.microsoft.com/en-us/office/compatibility/office-file-format-reference
        /// plus PDF. Image, video, and clipboard-only formats are excluded.
        /// </summary>
        private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            // PDF
            ".pdf",

            // Word & Text
            ".doc", ".docx", ".docm",
            ".dot", ".dotx", ".dotm",
            ".htm", ".html",
            ".odt",
            ".rtf",
            ".txt",
        };

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns all ready NTFS drives on the system.
        /// </summary>
        public static List<DriveInfo> GetNtfsDrives()
        {
            var list = new List<DriveInfo>();
            foreach (var d in DriveInfo.GetDrives())
                if (d.IsReady && d.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                    list.Add(d);
            return list;
        }

        /// <summary>
        /// Returns true if the file name has an extension we index.
        /// Used by both LocalFileIndexMftCrawler (full build) and LocalFileIndexUsnJournal (incremental update)
        /// to apply consistent filtering.
        /// </summary>
        public static bool IsAllowedExtension(string fileName)
            => AllowedExtensions.Contains(Path.GetExtension(fileName));

        /// <summary>
        /// Reads every path on the given NTFS volume from the MFT directly and returns them
        /// as a complete list. Paths are lower-cased (the Lucene index stores them lower-cased
        /// for case-insensitive matching).
        /// <paramref name="progress"/> receives the running count of entries read.
        /// Runs eagerly and throws at call time, not on first enumeration — see the note in
        /// the body for why this must not become an iterator again.
        /// </summary>
        public static IEnumerable<string> EnumeratePaths(
            char              driveLetter,
            Action<int>       progress = null,
            CancellationToken ct       = default)
        {
            // NOT an iterator: everything that owns a resource happens here, eagerly, so the
            // volume handle is opened AND closed within this call. An iterator's try/finally
            // only runs when the enumerator is disposed, so a caller doing .First()/.Take(n)/
            // .Any() — or abandoning the enumerator — would strand the volume handle and the
            // whole MFT snapshot (hundreds of MB). Since the MFT read completed before the
            // first yield anyway, deferring bought nothing and risked exactly that.
            char drive = char.ToUpper(driveLetter);

            IntPtr hVol = CreateFileW(
                $@"\\.\{drive}:",
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);

            if (hVol == INVALID_HANDLE)
                throw new InvalidOperationException(
                    $"Cannot open volume {drive}: (error {Marshal.GetLastWin32Error()}). " +
                    "Run as Administrator.");

            Dictionary<ulong, MftEntry> entries;
            try
            {
                // Verify NTFS
                int   volBufSize = 128;
                IntPtr volBuf    = Marshal.AllocHGlobal(volBufSize);
                try
                {
                    uint ret;
                    if (!DeviceIoControl(hVol, FSCTL_GET_NTFS_VOLUME_DATA,
                            IntPtr.Zero, 0, volBuf, (uint)volBufSize, out ret, IntPtr.Zero))
                        throw new InvalidOperationException(
                            $"Volume {drive}: is not NTFS (error {Marshal.GetLastWin32Error()}).");
                }
                finally { Marshal.FreeHGlobal(volBuf); }

                // Pass 1: read all MFT records into a flat dictionary (frn → name + parent)
                entries = ReadMftEntries(hVol, progress, ct);
            }
            finally
            {
                // The handle is only needed for the MFT read above — path resolution is pure
                // dictionary work — so release it as early as possible.
                CloseHandle(hVol);
            }

            // Pass 2: resolve full paths for matching files.
            // Directories were kept only for parent chain resolution — skip them here.
            // Extension filtering already happened in Pass 1.
            var results = new List<string>();
            string root = drive + ":";
            foreach (var entry in entries.Values)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.IsDirectory) continue;
                string fullPath = BuildPath(entry, entries, root);
                if (IsInSkippedDirectory(fullPath)) continue;
                results.Add(fullPath.ToLowerInvariant());
            }
            return results;
        }

        // ── MFT reading ───────────────────────────────────────────────────────────

        private struct MftEntry
        {
            public ulong  Frn;
            public ulong  ParentFrn;
            public string Name;
            public bool   IsDirectory;
        }

        private static Dictionary<ulong, MftEntry> ReadMftEntries(
            IntPtr hVol, Action<int> progress, CancellationToken ct)
        {
            var entries = new Dictionary<ulong, MftEntry>(300_000);

            const int BUF = 512 * 1024;
            IntPtr outBuf = Marshal.AllocHGlobal(BUF);
            IntPtr inBuf  = Marshal.AllocHGlobal(Marshal.SizeOf<MFT_ENUM_DATA>());

            try
            {
                var req = new MFT_ENUM_DATA
                {
                    StartFileReferenceNumber = 0,
                    LowUsn  = 0,
                    HighUsn = long.MaxValue
                };

                int reportThreshold = 10_000;

                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    Marshal.StructureToPtr(req, inBuf, false);

                    uint bytesOut;
                    bool ok = DeviceIoControl(
                        hVol, FSCTL_ENUM_USN_DATA,
                        inBuf, (uint)Marshal.SizeOf<MFT_ENUM_DATA>(),
                        outBuf, BUF,
                        out bytesOut, IntPtr.Zero);

                    if (!ok)
                    {
                        if (Marshal.GetLastWin32Error() == ERROR_HANDLE_EOF) break;
                        throw new InvalidOperationException(
                            $"FSCTL_ENUM_USN_DATA error {Marshal.GetLastWin32Error()}.");
                    }

                    if (bytesOut <= 8) break;

                    req.StartFileReferenceNumber = (ulong)Marshal.ReadInt64(outBuf, 0);

                    int offset = 8;
                    while (offset + 60 <= (int)bytesOut)
                    {
                        uint  recLen = (uint)Marshal.ReadInt32(outBuf, offset);
                        if (recLen < 60) break;

                        ulong frn    = (ulong)Marshal.ReadInt64(outBuf, offset + 8)  & 0x0000FFFFFFFFFFFF;
                        ulong parent = (ulong)Marshal.ReadInt64(outBuf, offset + 16) & 0x0000FFFFFFFFFFFF;
                        short fnLen  = Marshal.ReadInt16(outBuf, offset + 56);
                        short fnOff  = Marshal.ReadInt16(outBuf, offset + 58);

                        if (fnLen > 0 && fnOff + fnLen <= (int)recLen)
                        {
                            string name = Marshal.PtrToStringUni(
                                IntPtr.Add(outBuf, offset + fnOff), fnLen / 2);

                            if (!string.IsNullOrEmpty(name) && name != "." && name != "..")
                            {
                                uint attrs      = (uint)Marshal.ReadInt32(outBuf, offset + 52);
                                bool isDirectory = (attrs & 0x10) != 0;

                                // Filter in Pass 1: keep directories (needed for path
                                // resolution) and files whose extension is in the allowed
                                // set. Everything else is discarded immediately — never
                                // enters the dictionary, never costs memory or Pass 2 work.
                                if (isDirectory || AllowedExtensions.Contains(Path.GetExtension(name)))
                                {
                                    entries[frn] = new MftEntry
                                    {
                                        Frn         = frn,
                                        ParentFrn   = parent,
                                        Name        = name,
                                        IsDirectory = isDirectory
                                    };
                                }
                            }
                        }

                        offset += (int)recLen;
                    }

                    if (entries.Count >= reportThreshold)
                    {
                        progress?.Invoke(entries.Count);
                        reportThreshold = entries.Count + 10_000;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(inBuf);
                Marshal.FreeHGlobal(outBuf);
            }

            progress?.Invoke(entries.Count);
            return entries;
        }

        // ── Path resolution ───────────────────────────────────────────────────────

        private static string BuildPath(
            MftEntry entry,
            Dictionary<ulong, MftEntry> all,
            string root)
        {
            var parts = new List<string>(8) { entry.Name };
            MftEntry cur = entry;

            for (int depth = 0; depth < 64; depth++)
            {
                if (cur.ParentFrn == cur.Frn) break;  // reached volume root

                MftEntry parent;
                if (!all.TryGetValue(cur.ParentFrn, out parent)) break;

                // Skip the MFT root entry (named ".") — it's not a real path component.
                // Stop walking when we reach it; the drive letter will be prepended below.
                if (parent.Name == ".")
                    break;

                parts.Add(parent.Name);
                cur = parent;
            }

            parts.Reverse();
            // Prepend the drive root ("C:") — do NOT overwrite parts[0] which is
            // the top-most real directory (e.g. "Users").
            parts.Insert(0, root);

            return string.Join("\\", parts);
        }

        // ── Targeted FRN snapshot (for incremental USN updates) ───────────────────

        /// <summary>
        /// Reads a minimal set of MFT entries — just enough to resolve the paths
        /// of recently changed files identified by the USN journal.
        ///
        /// Does a full MFT pass but discards everything not in
        /// <paramref name="frnsNeeded"/> or on their ancestor chains.
        /// Much faster than a full EnumeratePaths when the change set is small.
        /// </summary>
        public static Dictionary<ulong, MftSnapshotEntry> SnapshotEntries(
            char driveLetter, HashSet<ulong> frnsNeeded, CancellationToken ct = default)
        {
            char drive = char.ToUpper(driveLetter);

            IntPtr hVol = CreateFileW(
                $@"\\.\{drive}:",
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);

            if (hVol == INVALID_HANDLE)
                throw new InvalidOperationException(
                    $"Cannot open volume {drive}: (error {Marshal.GetLastWin32Error()}). " +
                    "Run as Administrator.");

            var result = new Dictionary<ulong, MftSnapshotEntry>(frnsNeeded.Count * 8);

            const int BUF = 512 * 1024;
            IntPtr outBuf = Marshal.AllocHGlobal(BUF);
            IntPtr inBuf  = Marshal.AllocHGlobal(Marshal.SizeOf<MFT_ENUM_DATA>());

            try
            {
                var req = new MFT_ENUM_DATA
                {
                    StartFileReferenceNumber = 0,
                    LowUsn  = 0,
                    HighUsn = long.MaxValue
                };

                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    Marshal.StructureToPtr(req, inBuf, false);

                    uint bytesOut;
                    bool ok = DeviceIoControl(
                        hVol, FSCTL_ENUM_USN_DATA,
                        inBuf, (uint)Marshal.SizeOf<MFT_ENUM_DATA>(),
                        outBuf, BUF, out bytesOut, IntPtr.Zero);

                    if (!ok)
                    {
                        if (Marshal.GetLastWin32Error() == ERROR_HANDLE_EOF) break;
                        break;
                    }
                    if (bytesOut <= 8) break;

                    req.StartFileReferenceNumber = (ulong)Marshal.ReadInt64(outBuf, 0);

                    int offset = 8;
                    while (offset + 60 <= (int)bytesOut)
                    {
                        uint  recLen = (uint)Marshal.ReadInt32(outBuf, offset);
                        if (recLen < 60) break;

                        ulong frn    = (ulong)Marshal.ReadInt64(outBuf, offset + 8)  & 0x0000FFFFFFFFFFFF;
                        ulong parent = (ulong)Marshal.ReadInt64(outBuf, offset + 16) & 0x0000FFFFFFFFFFFF;
                        uint  attrs  = (uint)Marshal.ReadInt32(outBuf, offset + 52);
                        short fnLen  = Marshal.ReadInt16(outBuf, offset + 56);
                        short fnOff  = Marshal.ReadInt16(outBuf, offset + 58);

                        bool isDir = (attrs & 0x10) != 0;

                        // Keep if it's one of the FRNs we need, OR if it's a directory
                        // (we need directories to resolve ancestor chains).
                        if ((frnsNeeded.Contains(frn) || isDir) &&
                            fnLen > 0 && fnOff + fnLen <= (int)recLen)
                        {
                            string name = Marshal.PtrToStringUni(
                                IntPtr.Add(outBuf, offset + fnOff), fnLen / 2);

                            if (!string.IsNullOrEmpty(name))
                            {
                                result[frn] = new MftSnapshotEntry
                                {
                                    Frn       = frn,
                                    ParentFrn = parent,
                                    Name      = name,
                                    IsDirectory = isDir
                                };
                            }
                        }

                        offset += (int)recLen;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(inBuf);
                Marshal.FreeHGlobal(outBuf);
                CloseHandle(hVol);
            }

            return result;
        }

        /// <summary>
        /// Resolves a full path for an FRN using a snapshot dictionary.
        /// Returns null if the ancestor chain cannot be resolved (e.g. entry was deleted)
        /// or if the path falls inside a skipped system directory.
        /// </summary>
        public static string ResolvePath(
            ulong frn, string name, ulong parentFrn,
            Dictionary<ulong, MftSnapshotEntry> snapshot, string root)
        {
            var parts = new List<string>(8) { name };
            ulong curParent = parentFrn;

            for (int depth = 0; depth < 64; depth++)
            {
                MftSnapshotEntry entry;
                if (!snapshot.TryGetValue(curParent, out entry)) return null;
                // Stop at the MFT volume root (self-referential FRN or the "." entry).
                // Do not add it — the drive letter is prepended below.
                if (entry.ParentFrn == entry.Frn || entry.Name == ".") break;
                parts.Add(entry.Name);
                curParent = entry.ParentFrn;
            }

            parts.Reverse();
            // Prepend the drive root ("C:") — do NOT overwrite parts[0] which is
            // the top-most real directory (e.g. "Users").
            parts.Insert(0, root);

            string fullPath = string.Join("\\", parts);
            return IsInSkippedDirectory(fullPath) ? null : fullPath;
        }

        /// <summary>Minimal MFT entry used by SnapshotEntries / ResolvePath.</summary>
        public struct MftSnapshotEntry
        {
            public ulong  Frn;
            public ulong  ParentFrn;
            public string Name;
            public bool   IsDirectory;
        }
    }
}
