using System;
using System.Runtime.InteropServices;
using System.Text;

namespace OfficePaneChrome
{
    /// <summary>Win32 / DWM declarations used by OfficePaneChrome.</summary>
    internal static class Native
    {
        public const uint WM_SIZE = 0x0005, WM_PAINT = 0x000F, WM_SETCURSOR = 0x0020, WM_NCDESTROY = 0x0082, WM_NCCALCSIZE = 0x0083,
            WM_NCHITTEST = 0x0084, WM_NCMOUSEMOVE = 0x00A0, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2, WM_NCRBUTTONUP = 0x00A5,
            WM_SYSCOMMAND = 0x0112, WM_TIMER = 0x0113, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
            WM_LBUTTONDBLCLK = 0x0203, WM_CAPTURECHANGED = 0x0215, WM_NCLBUTTONDBLCLK = 0x00A3, WM_DPICHANGED = 0x02E0, WM_NCMOUSELEAVE = 0x02A2, WM_MOUSELEAVE = 0x02A3, SRCCOPY = 0x00CC0020;

        public const int HTCLIENT = 1, HTCAPTION = 2, HTMINBUTTON = 8, HTMAXBUTTON = 9, HTTOP = 12, HTCLOSE = 20;
        public const int GWLP_HWNDPARENT = -8, GA_ROOT = 2;

        public struct RECT { public int Left, Top, Right, Bottom; }
        public struct POINT { public int X, Y; }
        public struct MARGINS { public int Left, Right, Top, Bottom; }
        public struct NCCALCSIZE_PARAMS { public RECT rgrc0, rgrc1, rgrc2; public IntPtr lppos; }
        public struct TRACKMOUSEEVENT { public int cbSize; public int dwFlags; public IntPtr hwndTrack; public int dwHoverTime; }
        public delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, IntPtr data);

        [DllImport("comctl32.dll")] public static extern bool SetWindowSubclass(IntPtr h, SubclassProc p, UIntPtr id, IntPtr data);
        [DllImport("comctl32.dll")] public static extern bool RemoveWindowSubclass(IntPtr h, SubclassProc p, UIntPtr id);
        [DllImport("comctl32.dll")] public static extern IntPtr DefSubclassProc(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr h, IntPtr r, bool erase);
        [DllImport("user32.dll")] public static extern bool ValidateRect(IntPtr h, ref RECT r);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern IntPtr SetCursor(IntPtr c);
        [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr inst, IntPtr id);
        // SetWindowLongPtr is a real export only in 64-bit user32; 32-bit Office needs SetWindowLong.
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern IntPtr SetWindowLong32(IntPtr h, int i, IntPtr v);
        public static IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v) => IntPtr.Size == 8 ? SetWindowLongPtr64(h, i, v) : SetWindowLong32(h, i, v);
        [DllImport("user32.dll")] public static extern int GetSystemMetricsForDpi(int i, uint dpi);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT t);
        [DllImport("user32.dll")] public static extern IntPtr SetCapture(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetMessageExtraInfo();
        /// <summary>
        /// The mouse message being handled came from a finger or pen (Windows marks those with 0xFF5157xx). Touch has no
        /// hover: native controls show no hover look or tooltip for it.
        /// </summary>
        public static bool FromTouchOrPen() => (unchecked((uint)GetMessageExtraInfo().ToInt64()) & 0xFFFFFF00u) == 0xFF515700u;   // low 32 bits: no sign extension in 32-bit Office
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetSystemMenu(IntPtr h, bool revert);
        [DllImport("user32.dll")] public static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr h, IntPtr tpm);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmExtendFrameIntoClientArea(IntPtr h, ref MARGINS m);
        [DllImport("gdi32.dll")] public static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern uint GetLayout(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern uint SetLayout(IntPtr dc, uint l);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr d, int x, int y, int w, int h, IntPtr s, int sx, int sy, uint rop);

        public static string ClassName(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
        public static System.Drawing.Point PointFromLParam(IntPtr l) { long v = l.ToInt64(); return new System.Drawing.Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF)); }
    }
}
