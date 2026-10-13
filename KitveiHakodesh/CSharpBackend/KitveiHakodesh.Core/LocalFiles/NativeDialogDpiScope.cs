#nullable enable
using System;
using System.Runtime.InteropServices;

namespace KitveiHakodesh.Core.LocalFiles;

/// <summary>
/// Makes the CALLING THREAD per-monitor-v2 DPI aware for the lifetime of the scope, then
/// restores whatever context it had. Wrap a native dialog call in it so the dialog renders crisp
/// at any scale and rescales when dragged between monitors, regardless of the process default.
/// No-op when the OS does not support it (SetThreadDpiAwarenessContext returns 0).
/// </summary>
internal readonly struct NativeDialogDpiScope : IDisposable
{
    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 is the pseudo-handle -4.
    private static readonly nint PerMonitorAwareV2 = -4;

    // DllImport, not LibraryImport: this file is also linked into the net48 host
    // (KitveiHakodeshLib), which has no LibraryImport generator. All-nint = blittable, so it
    // needs no marshalling under native AOT either.
    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint dpiContext);

    private readonly nint _previous;

    private NativeDialogDpiScope(nint previous) => _previous = previous;

    public static NativeDialogDpiScope Enter()
    {
        nint previous = 0;
        try { previous = SetThreadDpiAwarenessContext(PerMonitorAwareV2); }
        catch (EntryPointNotFoundException) { /* pre-1607 Windows: nothing to do */ }
        return new NativeDialogDpiScope(previous);
    }

    public void Dispose()
    {
        if (_previous != 0)
            SetThreadDpiAwarenessContext(_previous);
    }
}
