using System;
using System.Runtime.InteropServices;

namespace KitveiHakodeshLib.LocalFile
{
    /// <summary>
    /// Adds an opened file to Windows' Recent list, which is what fills the taskbar
    /// jump list's "Recent" section and Start's "Recommended", like any file viewer.
    ///
    /// Windows does this by itself only for a double-click in Explorer. A file opened
    /// in-app (picker, home-page tile, session restore) needs this call.
    ///
    /// Off unless the host opts in: in the Word add-in the process is WINWORD.EXE, and
    /// the entry would land on Word's jump list instead of ours.
    /// </summary>
    public static class WindowsRecentDocuments
    {
        /// <summary>Set by the standalone app at startup.</summary>
        public static bool Enabled { get; set; }

        private const uint SHARD_PATHW = 0x00000003;

        public static void Add(string filePath)
        {
            if (!Enabled || string.IsNullOrEmpty(filePath)) return;
            try { SHAddToRecentDocs(SHARD_PATHW, filePath); }
            catch { /* best-effort: a missing Recent entry costs nothing else */ }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern void SHAddToRecentDocs(uint flags, string path);
    }
}
