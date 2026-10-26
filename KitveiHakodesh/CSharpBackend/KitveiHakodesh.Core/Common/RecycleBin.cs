#nullable enable
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace KitveiHakodesh.Core.Common
{
    /// <summary>How <see cref="RecycleBin.Send"/> ended.</summary>
    public enum RecycleBinOutcome
    {
        Recycled = 0,

        /// <summary>The file's drive has no Recycle Bin (network, removable, UNC), so a
        /// "recycle" would delete it permanently. Nothing was touched.</summary>
        NoRecycleBin,

        /// <summary>The move failed, or the user declined the shell's permanent-delete
        /// warning (file too big for the bin). The file is still there.</summary>
        Failed,
    }

    /// <summary>
    /// Moves a file into the Windows Recycle Bin (SHFileOperation FO_DELETE + FOF_ALLOWUNDO).
    /// Never deletes permanently by surprise: a drive with no bin is refused up front, and the
    /// rare case the shell finds itself (file bigger than the bin) gets the shell's own
    /// FOF_WANTNUKEWARNING prompt instead of a silent nuke.
    ///
    /// The bin used is the one of the account the calling process runs as, so call this from a
    /// process running as the user (the WebView2/VSTO host), never from the LocalSystem service.
    /// The struct carries its strings as raw pointers so the P/Invoke stays blittable under AOT.
    /// </summary>
    public static class RecycleBin
    {
        public static RecycleBinOutcome Send(string path)
        {
            if (!DriveHasRecycleBin(path)) return RecycleBinOutcome.NoRecycleBin;

            // pFrom is a list of paths, each NUL-terminated, the list ending in an extra NUL.
            IntPtr from = Marshal.StringToHGlobalUni(path + "\0");
            try
            {
                var op = new SHFILEOPSTRUCTW
                {
                    wFunc = FO_DELETE,
                    pFrom = from,
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
                };
                bool ok = SHFileOperationW(ref op) == 0 && op.fAnyOperationsAborted == 0;
                return ok && !File.Exists(path) ? RecycleBinOutcome.Recycled : RecycleBinOutcome.Failed;
            }
            finally
            {
                Marshal.FreeHGlobal(from);
            }
        }

        /// <summary>Only local fixed drives have a Recycle Bin.</summary>
        private static bool DriveHasRecycleBin(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (full.StartsWith(@"\\", StringComparison.Ordinal)) return false;
                string? root = Path.GetPathRoot(full);
                return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Fixed;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;
        private const ushort FOF_WANTNUKEWARNING = 0x4000;

        [StructLayout(LayoutKind.Sequential)]
        private struct SHFILEOPSTRUCTW
        {
            public IntPtr hwnd;
            public uint wFunc;
            public IntPtr pFrom;
            public IntPtr pTo;
            public ushort fFlags;
            public int fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public IntPtr lpszProgressTitle;
        }

        [DllImport("shell32.dll")]
        private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);
    }
}
