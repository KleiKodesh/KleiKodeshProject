using System.Runtime.InteropServices;

namespace KitveiHakodeshService.LocalFiles;

/// <summary>
/// Native Windows open-file dialog for the <c>pickLocalFile</c> op — lets DEV use the real C#
/// picker instead of the browser's <c>&lt;input type=file&gt;</c>. The browser picker only yields
/// a blob (no absolute path), so picked files couldn't persist across reloads; this returns the
/// real path, and the normal path-based restore (openLocalFile → /file handle) then applies.
///
/// Uses the classic Win32 <c>GetOpenFileNameW</c> (comdlg32) through <c>LibraryImport</c> — a flat
/// C API with no COM/WinForms dependency, so it links clean under native AOT. The dialog runs on
/// a dedicated STA thread (common dialogs expect STA) and is owned by the current foreground
/// window (the browser that issued the RPC), which keeps it on top. Filter mirrors the hosted
/// KitveiHakodeshLib picker exactly.
/// </summary>
public static partial class NativeFilePicker
{
    [LibraryImport("comdlg32.dll", EntryPoint = "GetOpenFileNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetOpenFileNameW(ref OPENFILENAMEW ofn);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    private const uint OFN_EXPLORER = 0x00080000;
    private const uint OFN_FILEMUSTEXIST = 0x00001000;
    private const uint OFN_PATHMUSTEXIST = 0x00000800;
    private const uint OFN_NOCHANGEDIR = 0x00000008; // dialogs change CWD by default — keep ours

    // x64 layout of OPENFILENAMEW — every field blittable (pointers as nint), natural alignment.
    [StructLayout(LayoutKind.Sequential)]
    private struct OPENFILENAMEW
    {
        public uint lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public nint lpstrFilter;
        public nint lpstrCustomFilter;
        public uint nMaxCustFilter;
        public uint nFilterIndex;
        public nint lpstrFile;
        public uint nMaxFile;
        public nint lpstrFileTitle;
        public uint nMaxFileTitle;
        public nint lpstrInitialDir;
        public nint lpstrTitle;
        public uint Flags;
        public ushort nFileOffset;
        public ushort nFileExtension;
        public nint lpstrDefExt;
        public nint lCustData;
        public nint lpfnHook;
        public nint lpTemplateName;
        public nint pvReserved;
        public uint dwReserved;
        public uint FlagsEx;
    }

    // Same document family the hosted picker offers (LocalFileHandler), Win32 \0-separated form.
    public const string DocumentFilter =
        "מסמכים (*.pdf;*.doc;*.docx;*.docm;*.dot;*.dotx;*.dotm;*.htm;*.html;*.odt;*.rtf;*.txt)\0" +
        "*.pdf;*.doc;*.docx;*.docm;*.dot;*.dotx;*.dotm;*.htm;*.html;*.odt;*.rtf;*.txt\0" +
        "כל הקבצים (*.*)\0*.*\0\0";

    /// <summary>SQLite database filter — used by the settings page's seforim DB path picker.
    /// Mirrors the hosted app's own DB picker so both offer the same choices.</summary>
    public const string DatabaseFilter =
        "מסד נתונים (*.db;*.sqlite;*.sqlite3)\0*.db;*.sqlite;*.sqlite3\0" +
        "כל הקבצים (*.*)\0*.*\0\0";

    // One dialog at a time — a second concurrent pick returns "cancelled" instead of stacking.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Show the dialog and return the picked absolute path, or null when the user
    /// cancelled (or another pick is already showing). Never throws.
    /// <paramref name="filter"/> must be the Win32 \0-separated form (see the constants above);
    /// it is passed to the dialog verbatim and MUST end with a double \0.</summary>
    public static async Task<string?> PickAsync(
        string filter = DocumentFilter, string title = "פתח קובץ")
    {
        if (!await Gate.WaitAsync(0)) return null;
        try
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var t = new Thread(() =>
            {
                try { tcs.TrySetResult(ShowDialog(filter, title)); }
                catch { tcs.TrySetResult(null); }
            })
            { IsBackground = true, Name = "khs-file-picker" };
            t.SetApartmentState(ApartmentState.STA); // common dialogs expect an STA thread
            t.Start();
            return await tcs.Task;
        }
        finally { Gate.Release(); }
    }

    private static unsafe string? ShowDialog(string filter, string title)
    {
        const int MaxPath = 32768; // long-path capable buffer
        char[] fileBuf = new char[MaxPath];

        fixed (char* pFile = fileBuf)
        fixed (char* pFilter = filter)
        fixed (char* pTitle = title)
        {
            var ofn = new OPENFILENAMEW
            {
                lStructSize = (uint)sizeof(OPENFILENAMEW),
                hwndOwner = GetForegroundWindow(), // the browser — keeps the dialog on top of it
                lpstrFilter = (nint)pFilter,
                nFilterIndex = 1,
                lpstrFile = (nint)pFile,
                nMaxFile = MaxPath,
                lpstrTitle = (nint)pTitle,
                Flags = OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR,
            };

            if (!GetOpenFileNameW(ref ofn)) return null; // cancelled (or dialog error — treat alike)

            int len = Array.IndexOf(fileBuf, '\0');
            if (len <= 0) return null;
            return new string(fileBuf, 0, len);
        }
    }
}
