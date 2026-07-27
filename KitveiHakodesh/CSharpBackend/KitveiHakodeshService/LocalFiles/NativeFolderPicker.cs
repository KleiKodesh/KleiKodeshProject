using System.Runtime.InteropServices;

namespace KitveiHakodeshService.LocalFiles;

/// <summary>
/// Native folder-browse dialog for the <c>pickFolder</c> op — dev's replacement for the hosted
/// app's WinForms folder picker. The browser has no folder picker that yields an absolute
/// filesystem path (<c>showDirectoryPicker</c> hands back a handle and a display name only), so
/// the settings page could never feed a real path to the service without this.
///
/// Uses the MODERN Vista+ common item dialog: <c>IFileOpenDialog</c> with <c>FOS_PICKFOLDERS</c>,
/// the same dialog Explorer and WinForms' FolderBrowserDialog show on current Windows. That means
/// full Explorer chrome — a real path edit box, the places sidebar, search, and long-path support.
/// The legacy <c>SHBrowseForFolderW</c> tree dialog is deliberately NOT used: it looks like Win95
/// next to the rest of the app, has no usable path entry, and is capped at MAX_PATH.
///
/// AOT: activated with <c>CoCreateInstance</c> and called through raw vtable slots
/// (<c>delegate* unmanaged[Stdcall]</c>) — no COM interop marshalling, no built-in COM support
/// needed, which is the same approach DocConvertLib's AotWordConverter uses. Slot indices are
/// fixed by the interface layout and must not be reordered: every COM interface starts with the
/// three IUnknown slots (QueryInterface, AddRef, Release), then IModalWindow::Show, then the
/// IFileDialog methods in declaration order.
///
/// The dialog runs on a dedicated STA thread (shell dialogs require STA) and is owned by a
/// <see cref="DialogOwnerWindow"/> — a hidden window of ours carrying the KitveiHakodesh icon,
/// centred on the foreground window. Owning the foreground window directly would make the dialog
/// wear the host BROWSER's icon in dev.
/// </summary>
public static partial class NativeFolderPicker
{
    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("ole32.dll")]
    private static partial void CoTaskMemFree(nint pointer);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private const uint CLSCTX_INPROC_SERVER = 0x1;

    private static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
    private static readonly Guid IID_IFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");

    private const uint FOS_PICKFOLDERS = 0x00000020;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040; // real paths only, no virtual shell items
    private const uint FOS_PATHMUSTEXIST = 0x00000800;
    private const uint FOS_NOCHANGEDIR = 0x00000008;     // dialogs change CWD by default — keep ours

    private const int ERROR_CANCELLED = unchecked((int)0x800704C7);
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    // IFileOpenDialog vtable slots. 0-2 = IUnknown, 3 = IModalWindow::Show,
    // then IFileDialog's own methods; SetOptions is slot 9, GetResult slot 20.
    private const int SlotRelease = 2;
    private const int SlotShow = 3;
    private const int SlotSetOptions = 9;
    private const int SlotGetOptions = 10;
    private const int SlotSetTitle = 17;
    private const int SlotGetResult = 20;

    // IShellItem: 0-2 = IUnknown, 3 = BindToHandler, 4 = GetParent, 5 = GetDisplayName.
    private const int SlotGetDisplayName = 5;

    private static unsafe nint Vtbl(nint obj, int slot) => (*(nint**)obj)[slot];

    private static unsafe void Release(nint obj)
    {
        if (obj == 0) return;
        ((delegate* unmanaged[Stdcall]<nint, uint>)Vtbl(obj, SlotRelease))(obj);
    }

    // One dialog at a time — a second concurrent pick returns "cancelled" instead of stacking.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Show the dialog and return the picked absolute folder path, or null when the user
    /// cancelled (or another pick is already showing). Never throws.</summary>
    public static async Task<string?> PickAsync(string title)
    {
        if (!await Gate.WaitAsync(0)) return null;
        try
        {
            var completion = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { completion.TrySetResult(ShowDialog(title)); }
                catch { completion.TrySetResult(null); }
            })
            { IsBackground = true, Name = "khs-folder-picker" };
            thread.SetApartmentState(ApartmentState.STA); // shell dialogs require an STA thread
            thread.Start();
            return await completion.Task;
        }
        finally { Gate.Release(); }
    }

    private static unsafe string? ShowDialog(string title)
    {
        int hr = CoInitializeEx(0, COINIT_APARTMENTTHREADED);
        // S_FALSE (1) means already initialized on this thread — fine; only balance when we did it.
        bool shouldUninitialize = hr >= 0;
        try
        {
            if (CoCreateInstance(in CLSID_FileOpenDialog, 0, CLSCTX_INPROC_SERVER,
                    in IID_IFileOpenDialog, out nint dialog) < 0 || dialog == 0)
                return null;

            try
            {
                // Preserve the shell's defaults and add ours, rather than overwriting the set.
                var getOptions = (delegate* unmanaged[Stdcall]<nint, uint*, int>)Vtbl(dialog, SlotGetOptions);
                uint options;
                if (getOptions(dialog, &options) < 0) options = 0;

                var setOptions = (delegate* unmanaged[Stdcall]<nint, uint, int>)Vtbl(dialog, SlotSetOptions);
                if (setOptions(dialog,
                        options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM
                                | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR) < 0)
                    return null;

                fixed (char* titleText = title)
                {
                    var setTitle = (delegate* unmanaged[Stdcall]<nint, char*, int>)Vtbl(dialog, SlotSetTitle);
                    setTitle(dialog, titleText); // cosmetic — ignore failure
                }

                // Own the dialog with our own hidden window so its title-bar/taskbar icon is
                // KitveiHakodesh's, not the host browser's (which owning the foreground window
                // would inherit). Centred on the foreground window, so it still opens where the
                // user is looking.
                using var owner = new DialogOwnerWindow();

                var show = (delegate* unmanaged[Stdcall]<nint, nint, int>)Vtbl(dialog, SlotShow);
                int showResult = show(dialog, owner.Handle);
                if (showResult == ERROR_CANCELLED || showResult < 0) return null; // cancelled

                var getResult = (delegate* unmanaged[Stdcall]<nint, nint*, int>)Vtbl(dialog, SlotGetResult);
                nint item;
                if (getResult(dialog, &item) < 0 || item == 0) return null;

                try
                {
                    var getDisplayName =
                        (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Vtbl(item, SlotGetDisplayName);
                    nint pathPointer;
                    if (getDisplayName(item, SIGDN_FILESYSPATH, &pathPointer) < 0 || pathPointer == 0)
                        return null;

                    try { return Marshal.PtrToStringUni(pathPointer); }
                    finally { CoTaskMemFree(pathPointer); } // the shell allocated it — we free it
                }
                finally { Release(item); }
            }
            finally { Release(dialog); }
        }
        finally
        {
            if (shouldUninitialize) CoUninitialize();
        }
    }
}
