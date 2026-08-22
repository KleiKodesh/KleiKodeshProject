#if NET10_0_OR_GREATER
using System.Runtime.InteropServices;

namespace DocConvertLib;

/// <summary>
/// Converts a document to PDF by driving Microsoft Word over COM — WITHOUT the Office PIA and
/// WITHOUT <c>dynamic</c>, both of which native AOT disables (built-in COM interop + DLR codegen
/// are off in the published service). Instead it activates Word via <c>CoCreateInstance</c> and
/// calls <c>IDispatch::GetIDsOfNames</c>/<c>Invoke</c> through raw vtable function pointers.
/// Function pointers + <c>LibraryImport</c> P/Invoke are fully AOT-safe (no reflection, no runtime
/// codegen), so this links and runs under NativeAOT — verified end-to-end (RTF → valid PDF).
///
/// It is the AOT-viable equivalent of KitveiHakodeshLib WordToPdfConverter's core:
///   Word.Application (Visible=false, DisplayAlerts=0) → Documents.Open(path, ReadOnly) →
///   Document.SaveAs2(out, wdFormatPDF=17) → Close → Quit.
///
/// TWO x64 landmines (both learned the hard way): VARIANT is 24 bytes on x64 (its union's largest
/// member is BRECORD = two pointers), not 16 — a wrong size misaligns multi-arg rgvarg arrays;
/// and Document.Close is NOT Release — the interface pointer must be released too or WINWORD never
/// exits. Requires Word installed. ~3.5-4s per conversion (Word cold start).
/// </summary>
public static unsafe partial class AotWordConverter
{
    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);
    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();
    [LibraryImport("ole32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CLSIDFromProgID(string lpszProgID, out Guid lpclsid);
    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);
    // Bind to an ALREADY-RUNNING Word from the ROT. Marshal.GetActiveObject is not available
    // under AOT (it lives in the removed built-in COM interop), so call the OLE APIs directly.
    [LibraryImport("oleaut32.dll")]
    private static partial int GetActiveObject(in Guid rclsid, nint pvReserved, out nint ppunk);

    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private static Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");

    private const ushort DISPATCH_METHOD = 1, DISPATCH_PROPERTYGET = 2, DISPATCH_PROPERTYPUT = 4;
    private const int DISPID_PROPERTYPUT = -3;

    private const ushort VT_I4 = 3, VT_BSTR = 8, VT_DISPATCH = 9, VT_ERROR = 10, VT_BOOL = 11;
    private const int DISP_E_PARAMNOTFOUND = unchecked((int)0x80020004);
    private const int DISP_E_EXCEPTION = unchecked((int)0x80020009);

    // 24 bytes on x64: vt(2) + 6 reserved, then an 8-byte-aligned union whose largest member
    // (BRECORD = two pointers) is 16 bytes. A wrong size misaligns multi-arg rgvarg arrays.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct VARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public nint ptr;   // BSTR / IDispatch*
        [FieldOffset(8)] public int i4;      // I4 / BOOL / SCODE
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPPARAMS
    {
        public nint rgvarg;            // VARIANT*
        public nint rgdispidNamedArgs; // int*
        public uint cArgs;
        public uint cNamedArgs;
    }

    private static nint Vtbl(nint obj, int slot) => (*(nint**)obj)[slot];

    private static void Release(nint obj)
    {
        if (obj == 0) return;
        ((delegate* unmanaged[Stdcall]<nint, uint>)Vtbl(obj, 2))(obj);
    }

    /// <summary>IUnknown::QueryInterface (vtable slot 0) — GetActiveObject hands back an IUnknown,
    /// but every Invoke helper here needs an IDispatch.</summary>
    private static nint QueryInterface(nint obj, in Guid iid)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Vtbl(obj, 0);
        nint result;
        fixed (Guid* pIid = &iid)
        {
            if (fn(obj, pIid, &result) < 0) return 0;
        }
        return result;
    }

    private static int GetDispId(nint disp, string name)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, Guid*, ushort**, uint, uint, int*, int>)Vtbl(disp, 5);
        nint namePtr = Marshal.StringToHGlobalUni(name);
        try
        {
            ushort* p = (ushort*)namePtr;
            Guid iid = Guid.Empty;
            int dispid;
            int hr = fn(disp, &iid, &p, 1, 0, &dispid);
            if (hr < 0) throw new COMException($"GetIDsOfNames('{name}') failed", hr);
            return dispid;
        }
        finally { Marshal.FreeHGlobal(namePtr); }
    }

    /// <summary>Raw IDispatch::Invoke. <paramref name="argsReversed"/> must already be in COM order
    /// (LAST positional arg first). Returns the result VARIANT (caller owns it).</summary>
    private static VARIANT Invoke(nint disp, int dispid, ushort flags, VARIANT[] argsReversed)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, int, Guid*, uint, ushort, DISPPARAMS*, VARIANT*, nint, nint, int>)Vtbl(disp, 6);

        int n = argsReversed.Length;
        nint argMem = n > 0 ? Marshal.AllocHGlobal(sizeof(VARIANT) * n) : 0;
        nint namedMem = 0;
        try
        {
            for (int i = 0; i < n; i++) ((VARIANT*)argMem)[i] = argsReversed[i];

            var dp = new DISPPARAMS { rgvarg = argMem, cArgs = (uint)n };
            if (flags == DISPATCH_PROPERTYPUT)
            {
                namedMem = Marshal.AllocHGlobal(sizeof(int));
                *(int*)namedMem = DISPID_PROPERTYPUT;
                dp.rgdispidNamedArgs = namedMem;
                dp.cNamedArgs = 1;
            }

            Guid iid = Guid.Empty;
            VARIANT result = default;
            byte* exBuf = stackalloc byte[64]; // EXCEPINFO (x64): bstrDescription @16, scode @56
            for (int i = 0; i < 64; i++) exBuf[i] = 0;
            int hr = fn(disp, dispid, &iid, 0, flags, &dp, &result, (nint)exBuf, 0);
            if (hr < 0)
            {
                string extra = "";
                if (hr == DISP_E_EXCEPTION)
                {
                    nint descBstr = *(nint*)(exBuf + 16);
                    int scode = *(int*)(exBuf + 56);
                    string? desc = "";
                    if (descBstr != 0)
                    {
                        // EXCEPINFO strings are ours to free once copied out.
                        desc = Marshal.PtrToStringBSTR(descBstr);
                        Marshal.FreeBSTR(descBstr);
                    }
                    extra = $" [scode=0x{scode:X8} desc='{desc}']";
                }
                throw new COMException($"Word Invoke(dispid={dispid}) hr=0x{hr:X8}{extra}", hr);
            }
            return result;
        }
        finally
        {
            // Free the CONTENTS of the argument VARIANTs, not just the array holding them.
            // Bstr() allocates on the COM heap and the callee does not take ownership of
            // caller-supplied args, so without this every Bstr(path) argument leaks.
            for (int i = 0; i < n; i++) ClearVariant(ref argsReversed[i]);
            if (argMem != 0) Marshal.FreeHGlobal(argMem);
            if (namedMem != 0) Marshal.FreeHGlobal(namedMem);
        }
    }

    /// <summary>Release whatever a VARIANT owns and blank it. Safe to call twice: it clears
    /// <c>vt</c>, so a second call is a no-op. Only BSTR and IDispatch carry an allocation —
    /// I4/BOOL/ERROR are inline values with nothing to free.</summary>
    private static void ClearVariant(ref VARIANT v)
    {
        if (v.vt == VT_BSTR && v.ptr != 0) Marshal.FreeBSTR(v.ptr);
        else if (v.vt == VT_DISPATCH && v.ptr != 0) Release(v.ptr);
        v.vt = 0; // VT_EMPTY
        v.ptr = 0;
    }

    private static VARIANT Bstr(string s) => new() { vt = VT_BSTR, ptr = Marshal.StringToBSTR(s) };
    private static VARIANT I4(int v) => new() { vt = VT_I4, i4 = v };
    private static VARIANT Bool(bool b) => new() { vt = VT_BOOL, i4 = b ? -1 : 0 };
    private static VARIANT Missing() => new() { vt = VT_ERROR, i4 = DISP_E_PARAMNOTFOUND };

    // Invoke returns a VARIANT the caller owns. Every wrapper below must therefore either
    // hand that ownership on (PropGetDispatch / InvokeMethodDispatch return the raw pointer
    // and the caller Releases it) or clear it. A discarded result that happened to be an
    // IDispatch is exactly what leaves a WINWORD process running after we Quit.

    private static void PropPut(nint disp, string name, VARIANT val)
    {
        var r = Invoke(disp, GetDispId(disp, name), DISPATCH_PROPERTYPUT, [val]);
        ClearVariant(ref r);
    }

    private static nint PropGetDispatch(nint disp, string name)
    {
        var r = Invoke(disp, GetDispId(disp, name), DISPATCH_PROPERTYGET, []);
        if (r.vt != VT_DISPATCH)
        {
            ClearVariant(ref r); // wrong type, but it may still own a BSTR
            throw new COMException($"'{name}' did not return an IDispatch (vt={r.vt})");
        }
        return r.ptr; // ownership transfers to the caller
    }

    private static nint InvokeMethodDispatch(nint disp, string name, VARIANT[] argsReversed)
    {
        var r = Invoke(disp, GetDispId(disp, name), DISPATCH_METHOD, argsReversed);
        if (r.vt == VT_DISPATCH) return r.ptr; // ownership transfers to the caller
        ClearVariant(ref r);
        return 0;
    }

    private static void InvokeMethodVoid(nint disp, string name, VARIANT[] argsReversed)
    {
        // "Void" describes how we use it, not what Word returns — e.g. Documents.Open-shaped
        // calls hand back an IDispatch even when we ignore it. Clear it or it leaks.
        var r = Invoke(disp, GetDispId(disp, name), DISPATCH_METHOD, argsReversed);
        ClearVariant(ref r);
    }

    /// <summary>Convert <paramref name="sourcePath"/> to a PDF at <paramref name="outputPath"/> via
    /// Word. Throws COMException on failure (e.g. Word not installed). Releases every interface and
    /// Quits Word, so no WINWORD process is orphaned.</summary>
    public static void Convert(string sourcePath, string outputPath)
    {
        int hr = CoInitializeEx(0, COINIT_APARTMENTTHREADED);
        bool coInit = hr >= 0; // RPC_E_CHANGED_MODE (already MTA by the runtime) is fine — Word works via proxy
        nint app = 0, docs = 0, doc = 0;
        try
        {
            if (CLSIDFromProgID("Word.Application", out Guid clsid) < 0)
                throw new COMException("Word is not installed (Word.Application ProgID not found).");
            if (CoCreateInstance(in clsid, 0, CLSCTX_LOCAL_SERVER, in IID_IDispatch, out app) < 0 || app == 0)
                throw new COMException("Failed to start Word (CoCreateInstance).");

            PropPut(app, "Visible", Bool(false));
            PropPut(app, "DisplayAlerts", I4(0)); // wdAlertsNone

            docs = PropGetDispatch(app, "Documents");
            // Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles) — reversed for rgvarg.
            doc = InvokeMethodDispatch(docs, "Open", [Bool(false), Bool(true), Missing(), Bstr(sourcePath)]);
            if (doc == 0) throw new COMException("Documents.Open returned no document.");

            InvokeMethodVoid(doc, "SaveAs2", [I4(17), Bstr(outputPath)]); // wdFormatPDF = 17
            InvokeMethodVoid(doc, "Close", [I4(0)]);                       // wdDoNotSaveChanges
            Release(doc); doc = 0; // Close is NOT Release — drop the ref or Word never exits
        }
        finally
        {
            if (doc != 0) { try { InvokeMethodVoid(doc, "Close", [I4(0)]); } catch { } Release(doc); }
            if (docs != 0) Release(docs);
            if (app != 0) { try { InvokeMethodVoid(app, "Quit", []); } catch { } Release(app); }
            if (coInit) CoUninitialize();
        }
    }

    /// <summary>
    /// Open an HTML string as a new document in Word. The AOT-viable equivalent of
    /// KitveiHakodeshLib WordExporter.ExportCore, so dev's "ייצא ל-Word" behaves like the
    /// published app (where the same button goes through the WebView2 bridge instead).
    ///
    /// Word imports HTML by OPENING it as a file — there is no "insert this string" API — so the
    /// html is written to a temp .html named after the book title, exactly as hosted does. The
    /// file is deliberately NOT deleted on success: Word has it open, and deleting it out from
    /// under the user breaks Save. Reuses a running Word when there is one, and never Quits —
    /// the document is left open and visible. Must run on an STA thread (see ExportAsync).
    /// </summary>
    public static void ExportHtml(string html, string title)
    {
        int hr = CoInitializeEx(0, COINIT_APARTMENTTHREADED);
        bool coInit = hr >= 0;
        nint app = 0, docs = 0, doc = 0;

        string safeName = BuildSafeFileName(title);
        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), safeName + ".html");
        System.IO.File.WriteAllText(tempFile, html, new System.Text.UTF8Encoding(true));

        bool opened = false;
        try
        {
            if (CLSIDFromProgID("Word.Application", out Guid clsid) < 0)
                throw new COMException("Word is not installed (Word.Application ProgID not found).");

            if (GetActiveObject(in clsid, 0, out nint unk) >= 0 && unk != 0)
            {
                app = QueryInterface(unk, in IID_IDispatch);
                Release(unk);
            }
            if (app == 0 && (CoCreateInstance(in clsid, 0, CLSCTX_LOCAL_SERVER, in IID_IDispatch, out app) < 0 || app == 0))
                throw new COMException("Failed to start Word (CoCreateInstance).");

            PropPut(app, "Visible", Bool(true));

            docs = PropGetDispatch(app, "Documents");
            // Open(FileName, ConfirmConversions, ReadOnly, AddToRecentFiles) — reversed for rgvarg.
            // ReadOnly false and AddToRecentFiles false, matching the hosted call.
            doc = InvokeMethodDispatch(docs, "Open", [Bool(false), Bool(false), Bool(false), Bstr(tempFile)]);
            if (doc == 0) throw new COMException("Documents.Open returned no document.");
            opened = true;

            try { InvokeMethodVoid(app, "Activate", []); } catch { }
        }
        finally
        {
            // Only clean up the temp file if Word never got it open; otherwise leave it in place.
            if (!opened) { try { System.IO.File.Delete(tempFile); } catch { } }
            if (doc != 0) Release(doc);
            if (docs != 0) Release(docs);
            // NEVER Quit — the user is left looking at the exported document.
            if (app != 0) Release(app);
            if (coInit) CoUninitialize();
        }
    }

    /// <summary>Strips characters that are invalid in a file name, so a book title can be used as
    /// one. Mirrors KitveiHakodeshLib WordExporter.BuildSafeFileName.</summary>
    private static string BuildSafeFileName(string title)
    {
        string name = string.IsNullOrWhiteSpace(title) ? "KitveiHakodesh" : title.Trim();
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        // Keep well clear of MAX_PATH once the temp dir and extension are added.
        return name.Length > 100 ? name.Substring(0, 100) : name;
    }

    /// <summary>
    /// Paste whatever is on the Windows clipboard into Word at the cursor. The AOT-viable
    /// equivalent of KitveiHakodeshLib WordExporter.PasteAtCursorCore, so dev behaves like the
    /// published app (where the same menu item goes through the WebView2 bridge instead).
    ///
    /// Unlike <see cref="Convert"/> this REUSES a running Word when there is one — pasting into a
    /// hidden throwaway instance would be useless, the user wants it in the document they are
    /// looking at. Only spawns Word when none is running, and never Quits: the window is left
    /// open and activated for the user. Must run on an STA thread (see PasteAsync).
    /// </summary>
    public static void PasteAtCursor()
    {
        int hr = CoInitializeEx(0, COINIT_APARTMENTTHREADED);
        bool coInit = hr >= 0;
        nint app = 0, docs = 0, doc = 0, window = 0, selection = 0;
        try
        {
            if (CLSIDFromProgID("Word.Application", out Guid clsid) < 0)
                throw new COMException("Word is not installed (Word.Application ProgID not found).");

            // Prefer the instance the user already has open.
            if (GetActiveObject(in clsid, 0, out nint unk) >= 0 && unk != 0)
            {
                app = QueryInterface(unk, in IID_IDispatch);
                Release(unk);
            }
            if (app == 0)
            {
                if (CoCreateInstance(in clsid, 0, CLSCTX_LOCAL_SERVER, in IID_IDispatch, out app) < 0 || app == 0)
                    throw new COMException("Failed to start Word (CoCreateInstance).");
            }

            // Visible always — the point is for the user to see the pasted text. A reused
            // instance is already visible; a spawned one would otherwise stay hidden.
            PropPut(app, "Visible", Bool(true));

            docs = PropGetDispatch(app, "Documents");
            if (GetInt(docs, "Count") == 0)
                doc = InvokeMethodDispatch(docs, "Add", []);

            // Selection hangs off the active window, matching the hosted path
            // (app.ActiveDocument.ActiveWindow.Selection).
            window = PropGetDispatch(app, "ActiveWindow");
            selection = PropGetDispatch(window, "Selection");

            // wdFormatSurroundingFormattingWithEmphasis (20) is Word's "Merge Formatting": the
            // text takes the destination's font instead of the web default Word would otherwise
            // import as direct character formatting. Falls back to a plain Paste, exactly like
            // the hosted path — PasteAndFormat is format-sensitive and can reject a payload
            // plain Paste accepts, and dropping the user's paste entirely is worse.
            try { InvokeMethodVoid(selection, "PasteAndFormat", [I4(20)]); }
            catch { InvokeMethodVoid(selection, "Paste", []); }

            try { InvokeMethodVoid(app, "Activate", []); } catch { }
        }
        finally
        {
            if (selection != 0) Release(selection);
            if (window != 0) Release(window);
            if (doc != 0) Release(doc);
            if (docs != 0) Release(docs);
            // NEVER Quit here — the user is left looking at the document. Releasing our ref is
            // enough; Word stays up because it has its own UI reference.
            if (app != 0) Release(app);
            if (coInit) CoUninitialize();
        }
    }

    /// <summary>Read an integer property (e.g. Documents.Count).</summary>
    private static int GetInt(nint disp, string name)
    {
        var r = Invoke(disp, GetDispId(disp, name), DISPATCH_PROPERTYGET, []);
        int value = r.vt == VT_I4 ? r.i4 : 0;
        ClearVariant(ref r); // a non-I4 answer may still own a BSTR or interface pointer
        return value;
    }

}

/// <summary>
/// Async/STA wrapper for <see cref="AotWordConverter.PasteAtCursor"/>. Separate class because
/// <c>AotWordConverter</c> is <c>unsafe</c> and C# forbids <c>await</c> in an unsafe context.
/// </summary>
public static class AotWordPaste
{
    // One paste at a time — automating Word re-entrantly is fragile.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Run the paste on a dedicated STA thread and return null on success, or an error
    /// message. COM automation of Word from a thread-pool (MTA) thread only works through a
    /// marshaling proxy and is flaky for UI operations, so give it a real STA thread — the same
    /// reason NativeFolderPicker does this for shell dialogs. Never throws.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static Task<string?> PasteAsync()
        => RunOnStaAsync("khs-word-paste", AotWordConverter.PasteAtCursor,
            "פעולת הדבקה אחרת מתבצעת כעת.");

    /// <summary>Open an HTML export in Word on a dedicated STA thread. Returns null on success or
    /// an error message; never throws. See PasteAsync for why STA.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static Task<string?> ExportAsync(string html, string title)
        => RunOnStaAsync("khs-word-export", () => AotWordConverter.ExportHtml(html, title),
            "פעולת ייצוא אחרת מתבצעת כעת.");

    /// <summary>Serializes Word automation onto one STA thread at a time. Rejects rather than
    /// queues when busy — driving Word re-entrantly is fragile, and the caller is a UI action.</summary>
    private static async Task<string?> RunOnStaAsync(string threadName, Action work, string busyMessage)
    {
        if (!await Gate.WaitAsync(0)) return busyMessage;
        try
        {
            var completion = new TaskCompletionSource<string?>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { work(); completion.TrySetResult(null); }
                catch (Exception ex) { completion.TrySetResult(ex.Message); }
            })
            { IsBackground = true, Name = threadName };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return await completion.Task;
        }
        finally { Gate.Release(); }
    }
}
#endif
