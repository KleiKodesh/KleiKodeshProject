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
    /// Reads the NTFS USN Change Journal on a single volume since a saved cursor,
    /// yielding only the records that represent file-system changes since the last
    /// time we checked.
    ///
    /// This is the incremental counterpart to LocalFileIndexMftCrawler:
    ///   - LocalFileIndexMftCrawler  : full pass, used when no index exists yet
    ///   - LocalFileIndexUsnJournal  : delta pass, used on every subsequent startup
    ///
    /// Requires the process to run as Administrator (same as LocalFileIndexMftCrawler).
    /// </summary>
    public static class LocalFileIndexUsnJournal
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
        private const uint FSCTL_QUERY_USN_JOURNAL    = 0x000900F4;
        private const uint FSCTL_READ_USN_JOURNAL     = 0x000900BB;
        private const int  ERROR_HANDLE_EOF           = 38;
        private const int  ERROR_JOURNAL_NOT_ACTIVE   = 1179;
        private const int  ERROR_JOURNAL_ENTRY_DELETED = 1181;  // cursor too old — must full-rebuild
        private static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);

        // USN_REASON flags we care about
        private const uint USN_REASON_FILE_CREATE      = 0x00000100;
        private const uint USN_REASON_FILE_DELETE      = 0x00000200;
        private const uint USN_REASON_RENAME_NEW_NAME  = 0x00002000;
        private const uint USN_REASON_RENAME_OLD_NAME  = 0x00001000;

        [StructLayout(LayoutKind.Sequential)]
        private struct USN_JOURNAL_DATA
        {
            public ulong UsnJournalID;
            public long  FirstUsn;
            public long  NextUsn;
            public long  LowestValidUsn;
            public long  MaxUsn;
            public ulong MaximumSize;
            public ulong AllocationDelta;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct READ_USN_JOURNAL_DATA
        {
            public long  StartUsn;
            public uint  ReasonMask;
            public uint  ReturnOnlyOnClose;
            public ulong Timeout;
            public ulong BytesToWaitFor;
            public ulong UsnJournalID;
        }

        // ── Public types ──────────────────────────────────────────────────────────

        public enum ChangeReason { Created, Deleted, RenamedOldName, RenamedNewName }

        public struct ChangeRecord
        {
            /// <summary>File Reference Number of the changed entry.</summary>
            public ulong Frn;
            /// <summary>FRN of the parent directory.</summary>
            public ulong ParentFrn;
            /// <summary>File or directory name (not full path).</summary>
            public string Name;
            public bool   IsDirectory;
            public ChangeReason Reason;
        }

        /// <summary>
        /// Snapshot of the journal position, saved to disk after each update.
        /// On next startup, pass this back to <see cref="ReadChanges"/> to get
        /// only the delta.
        /// </summary>
        public struct Cursor
        {
            public ulong JournalId;  // if the journal was recreated, JournalId changes → full rebuild
            public long  NextUsn;    // position to start reading from next time
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns true when the process has sufficient privileges to open the
        /// specified NTFS volume for USN journal access (requires Administrator).
        /// Use this as a cheap pre-flight check before starting a live-update loop —
        /// a non-elevated process should exit the loop silently rather than
        /// triggering error callbacks on every attempt.
        /// </summary>
        public static bool CanOpenVolume(char driveLetter)
        {
            char drive = char.ToUpper(driveLetter);
            IntPtr h = CreateFileW(
                $@"\\.\{drive}:",
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);
            if (h == INVALID_HANDLE) return false;
            CloseHandle(h);
            return true;
        }

        /// <summary>
        /// Queries the current journal state and returns a cursor pointing to the
        /// end of the journal (i.e. "no changes yet"). Call this after a full MFT
        /// build to establish the baseline for future incremental updates.
        /// </summary>
        public static Cursor GetCurrentCursor(char driveLetter)
        {
            char drive = char.ToUpper(driveLetter);
            IntPtr hVol = OpenVolume(drive);
            try
            {
                var data = QueryJournal(hVol, drive);
                return new Cursor { JournalId = data.UsnJournalID, NextUsn = data.NextUsn };
            }
            finally { CloseHandle(hVol); }
        }

        /// <summary>
        /// Reads all change records since <paramref name="cursor"/> and advances
        /// the cursor to the current journal end.
        ///
        /// Returns null if the journal has been recreated or the cursor is too old —
        /// the caller must fall back to a full MFT rebuild in that case.
        ///
        /// Because C# iterators cannot have ref parameters, the advanced cursor is
        /// returned via <see cref="ReadChangesResult"/>; call that after enumerating.
        /// </summary>
        public static IEnumerable<ChangeRecord> ReadChanges(
            char driveLetter, Cursor cursor, out ReadChangesResult result)
        {
            result = new ReadChangesResult { Ok = false, AdvancedCursor = cursor };
            var records   = new List<ChangeRecord>();
            var finalResult = new ReadChangesResult { Ok = false, AdvancedCursor = cursor };

            char drive = char.ToUpper(driveLetter);
            IntPtr hVol;
            try { hVol = OpenVolume(drive); }
            catch { result = finalResult; return records; }

            try
            {
                USN_JOURNAL_DATA jd;
                try { jd = QueryJournal(hVol, drive); }
                catch { result = finalResult; return records; }

                if (jd.UsnJournalID != cursor.JournalId)
                {
                    result = finalResult;   // journal recreated — caller must rebuild
                    return records;
                }

                const int BUF = 512 * 1024;
                IntPtr outBuf = Marshal.AllocHGlobal(BUF);
                IntPtr inBuf  = Marshal.AllocHGlobal(Marshal.SizeOf<READ_USN_JOURNAL_DATA>());
                try
                {
                    var req = new READ_USN_JOURNAL_DATA
                    {
                        StartUsn          = cursor.NextUsn,
                        ReasonMask        = USN_REASON_FILE_CREATE      |
                                            USN_REASON_FILE_DELETE      |
                                            USN_REASON_RENAME_OLD_NAME  |
                                            USN_REASON_RENAME_NEW_NAME,
                        ReturnOnlyOnClose = 0,
                        Timeout           = 0,
                        BytesToWaitFor    = 0,
                        UsnJournalID      = cursor.JournalId
                    };

                    long advancedUsn = cursor.NextUsn;

                    while (true)
                    {
                        Marshal.StructureToPtr(req, inBuf, false);

                        uint bytesOut;
                        bool ok = DeviceIoControl(
                            hVol, FSCTL_READ_USN_JOURNAL,
                            inBuf, (uint)Marshal.SizeOf<READ_USN_JOURNAL_DATA>(),
                            outBuf, BUF, out bytesOut, IntPtr.Zero);

                        if (!ok)
                        {
                            int err = Marshal.GetLastWin32Error();
                            if (err == ERROR_HANDLE_EOF) break;
                            if (err == ERROR_JOURNAL_ENTRY_DELETED)
                            {
                                result = finalResult;  // cursor too old
                                return records;
                            }
                            throw new InvalidOperationException(
                                $"FSCTL_READ_USN_JOURNAL error {err} on {drive}:");
                        }

                        if (bytesOut <= 8) break;

                        advancedUsn   = Marshal.ReadInt64(outBuf, 0);
                        req.StartUsn  = advancedUsn;

                        int offset = 8;
                        while (offset + 60 <= (int)bytesOut)
                        {
                            uint  recLen = (uint)Marshal.ReadInt32(outBuf, offset);
                            if (recLen < 60) break;

                            uint   reason = (uint)Marshal.ReadInt32(outBuf, offset + 40);
                            uint   attrs  = (uint)Marshal.ReadInt32(outBuf, offset + 52);
                            ulong  frn    = (ulong)Marshal.ReadInt64(outBuf, offset + 8)  & 0x0000FFFFFFFFFFFF;
                            ulong  parent = (ulong)Marshal.ReadInt64(outBuf, offset + 16) & 0x0000FFFFFFFFFFFF;
                            short  fnLen  = Marshal.ReadInt16(outBuf, offset + 56);
                            short  fnOff  = Marshal.ReadInt16(outBuf, offset + 58);

                            if (fnLen > 0 && fnOff + fnLen <= (int)recLen)
                            {
                                string name = Marshal.PtrToStringUni(
                                    IntPtr.Add(outBuf, offset + fnOff), fnLen / 2);

                                if (!string.IsNullOrEmpty(name))
                                {
                                    bool isDir = (attrs & 0x10) != 0;
                                    if (isDir || LocalFileIndexMftCrawler.IsAllowedExtension(name))
                                    {
                                        records.Add(new ChangeRecord
                                        {
                                            Frn         = frn,
                                            ParentFrn   = parent,
                                            Name        = name,
                                            IsDirectory = isDir,
                                            Reason      = ReasonFromFlags(reason)
                                        });
                                    }
                                }
                            }

                            offset += (int)recLen;
                        }
                    }

                    finalResult = new ReadChangesResult
                    {
                        Ok             = true,
                        AdvancedCursor = new Cursor { JournalId = cursor.JournalId, NextUsn = advancedUsn }
                    };
                }
                finally
                {
                    Marshal.FreeHGlobal(inBuf);
                    Marshal.FreeHGlobal(outBuf);
                }
            }
            finally { CloseHandle(hVol); }

            result = finalResult;
            return records;
        }

        /// <summary>Result of a <see cref="ReadChanges"/> call.</summary>
        public struct ReadChangesResult
        {
            /// <summary>True if the journal was readable and the cursor is valid.</summary>
            public bool   Ok;
            /// <summary>Cursor advanced to the end of the records that were read.</summary>
            public Cursor AdvancedCursor;
        }

        /// <summary>Result of a <see cref="WaitForChanges"/> call.</summary>
        public struct WaitResult
        {
            /// <summary>True when new records arrived and the cursor is still valid.
            /// False when the journal was recreated, the cursor is too old, or the
            /// wait was cancelled — caller should rebuild or exit.</summary>
            public bool Ok;
        }

        /// <summary>
        /// Blocks the calling thread in a kernel wait (zero CPU, ~4 KB buffer) until
        /// at least one new USN record is available on the specified drive.
        ///
        /// Uses <c>FSCTL_READ_USN_JOURNAL</c> with <c>BytesToWaitFor=1</c> — the
        /// kernel parks the thread until data arrives; no polling, no Sleep loop.
        ///
        /// Cancellation is handled by a registered callback that closes the volume
        /// handle, which causes the blocked <c>DeviceIoControl</c> to return
        /// immediately with <c>ERROR_OPERATION_ABORTED</c> (error 995).
        ///
        /// Returns <c>Ok=false</c> on cancellation, journal recreation, or a stale
        /// cursor — the caller must exit the loop or trigger a full rebuild.
        /// </summary>
        public static WaitResult WaitForChanges(char driveLetter, Cursor cursor, CancellationToken ct)
        {
            char drive = char.ToUpper(driveLetter);

            IntPtr hVol;
            try { hVol = OpenVolume(drive); }
            catch { return new WaitResult { Ok = false }; }

            // Cancellation: close the handle so DeviceIoControl returns immediately.
            CancellationTokenRegistration reg = ct.Register(() =>
            {
                try { CloseHandle(hVol); } catch { }
            });

            try
            {
                USN_JOURNAL_DATA jd;
                try { jd = QueryJournal(hVol, drive); }
                catch { return new WaitResult { Ok = false }; }

                if (jd.UsnJournalID != cursor.JournalId)
                    return new WaitResult { Ok = false }; // journal recreated

                const int BUF = 4096; // minimal — we only need the new USN, not the records
                IntPtr outBuf = Marshal.AllocHGlobal(BUF);
                IntPtr inBuf  = Marshal.AllocHGlobal(Marshal.SizeOf<READ_USN_JOURNAL_DATA>());
                try
                {
                    var req = new READ_USN_JOURNAL_DATA
                    {
                        StartUsn          = cursor.NextUsn,
                        ReasonMask        = USN_REASON_FILE_CREATE     |
                                            USN_REASON_FILE_DELETE     |
                                            USN_REASON_RENAME_OLD_NAME |
                                            USN_REASON_RENAME_NEW_NAME,
                        ReturnOnlyOnClose = 0,
                        Timeout           = 0,
                        BytesToWaitFor    = 1, // ← kernel blocks until ≥1 byte of new data
                        UsnJournalID      = cursor.JournalId,
                    };
                    Marshal.StructureToPtr(req, inBuf, false);

                    uint bytesOut;
                    bool ok = DeviceIoControl(
                        hVol, FSCTL_READ_USN_JOURNAL,
                        inBuf,  (uint)Marshal.SizeOf<READ_USN_JOURNAL_DATA>(),
                        outBuf, BUF, out bytesOut, IntPtr.Zero);

                    if (!ok)
                    {
                        int err = Marshal.GetLastWin32Error();
                        if (ct.IsCancellationRequested)         return new WaitResult { Ok = false };
                        if (err == ERROR_JOURNAL_ENTRY_DELETED) return new WaitResult { Ok = false };
                        if (err == ERROR_HANDLE_EOF)            return new WaitResult { Ok = true };  // no new data yet — harmless
                        return new WaitResult { Ok = false };
                    }

                    return new WaitResult { Ok = true };
                }
                finally
                {
                    Marshal.FreeHGlobal(inBuf);
                    Marshal.FreeHGlobal(outBuf);
                }
            }
            finally
            {
                reg.Dispose();
                // Only close the handle if cancellation didn't already do it.
                if (!ct.IsCancellationRequested)
                    try { CloseHandle(hVol); } catch { }
            }
        }

        // ── Cursor persistence ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the path where the USN cursor for a given drive is stored,
        /// alongside the Lucene index.
        /// </summary>
        public static string CursorPath(string indexPath, char driveLetter)
            => Path.Combine(indexPath, $"usn_cursor_{char.ToUpper(driveLetter)}.bin");

        public static void SaveCursor(string indexPath, char driveLetter, Cursor cursor)
        {
            string path = CursorPath(indexPath, driveLetter);
            using (var w = new BinaryWriter(File.Open(path, FileMode.Create, FileAccess.Write)))
            {
                w.Write(cursor.JournalId);
                w.Write(cursor.NextUsn);
            }
        }

        /// <summary>
        /// Loads the saved cursor. Returns false if no cursor file exists
        /// (first run — caller should do a full MFT build).
        /// </summary>
        public static bool TryLoadCursor(string indexPath, char driveLetter, out Cursor cursor)
        {
            string path = CursorPath(indexPath, driveLetter);
            cursor = default;
            if (!File.Exists(path)) return false;
            try
            {
                using (var r = new BinaryReader(File.Open(path, FileMode.Open, FileAccess.Read)))
                {
                    cursor.JournalId = r.ReadUInt64();
                    cursor.NextUsn   = r.ReadInt64();
                }
                return true;
            }
            catch { return false; }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static IntPtr OpenVolume(char drive)
        {
            IntPtr h = CreateFileW(
                $@"\\.\{drive}:",
                GENERIC_READ,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);

            if (h == INVALID_HANDLE)
                throw new InvalidOperationException(
                    $"Cannot open volume {drive}: (error {Marshal.GetLastWin32Error()}). " +
                    "Run as Administrator.");
            return h;
        }

        private static USN_JOURNAL_DATA QueryJournal(IntPtr hVol, char drive)
        {
            int size = Marshal.SizeOf<USN_JOURNAL_DATA>();
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                uint bytesOut;
                if (!DeviceIoControl(hVol, FSCTL_QUERY_USN_JOURNAL,
                        IntPtr.Zero, 0, buf, (uint)size, out bytesOut, IntPtr.Zero))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == ERROR_JOURNAL_NOT_ACTIVE)
                        throw new InvalidOperationException(
                            $"USN journal is not active on {drive}:. " +
                            "Enable it with: fsutil usn createjournal m=1000 a=100 C:");
                    throw new InvalidOperationException(
                        $"FSCTL_QUERY_USN_JOURNAL error {err} on {drive}:");
                }
                return Marshal.PtrToStructure<USN_JOURNAL_DATA>(buf);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        private static ChangeReason ReasonFromFlags(uint reason)
        {
            if ((reason & USN_REASON_FILE_DELETE)     != 0) return ChangeReason.Deleted;
            if ((reason & USN_REASON_RENAME_OLD_NAME) != 0) return ChangeReason.RenamedOldName;
            if ((reason & USN_REASON_RENAME_NEW_NAME) != 0) return ChangeReason.RenamedNewName;
            return ChangeReason.Created;
        }
    }
}
