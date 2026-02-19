using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Word = Microsoft.Office.Interop.Word;

namespace KleiKodesh.Helpers
{
    public static class WordWindowHelper
    {
        private const int GWL_HWNDPARENT = -8;

        // Add constants for width ratio and zoom
        private const float WidthRatio = 0.35f; // 50% of screen width
        //private const int DocumentZoom = 80;  // 100% zoom

        #region Win32 Owner Support

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(
            IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern IntPtr SetWindowLong32(
            IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr SetWindowLongPtr(
            IntPtr hWnd,
            int nIndex,
            IntPtr dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        #endregion

        public static Word.Document OpenSoftSnapLeft()
        {
            Word.Application app = Globals.ThisAddIn.Application;

            if (app.ActiveWindow == null)
                throw new InvalidOperationException("No active Word window.");

            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "בחר קובץ";
                ofd.Multiselect = false;

                if (ofd.ShowDialog(new Win32Window(
                        new IntPtr(app.ActiveWindow.Hwnd)))
                    != DialogResult.OK)
                    return null;

                return OpenAndSnap(ofd.FileName);
            }
        }

        private static Word.Document OpenAndSnap(string filePath)
        {
            Word.Application app = Globals.ThisAddIn.Application;

            IntPtr ownerHwnd = new IntPtr(app.ActiveWindow.Hwnd);

            Word.Document doc = app.Documents.Open(
                FileName: filePath,
                ReadOnly: false,
                Visible: true);

            doc.Activate();

            Word.Window newWindow = app.ActiveWindow;

            IntPtr newHwnd = new IntPtr(newWindow.Hwnd);
            SetWindowLongPtr(newHwnd, GWL_HWNDPARENT, ownerHwnd);

            SnapApplicationLeft(app, newHwnd);

            // Set document zoom
            //newWindow.View.Zoom.Percentage = DocumentZoom;

            return doc;
        }

        private static void SnapApplicationLeft(
    Word.Application app,
    IntPtr hwnd)
        {
            Word.Window window = app.ActiveWindow;
            if (window == null)
                return;

            window.WindowState = Word.WdWindowState.wdWindowStateNormal;

            Screen screen = Screen.FromHandle(hwnd);
            var bounds = screen.WorkingArea;

            float pixelToPoint;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                pixelToPoint = 72f / g.DpiX;
            }

            app.Left = (int)Math.Round(bounds.Left * pixelToPoint);
            app.Top = (int)Math.Round(bounds.Top * pixelToPoint);
            app.Width = (int)Math.Round((bounds.Width * WidthRatio) * pixelToPoint);
            app.Height = (int)Math.Round(bounds.Height * pixelToPoint);

            // Center horizontally by adjusting the horizontal scroll
            int visibleWidthPoints = (int)Math.Round((bounds.Width * WidthRatio) * pixelToPoint);
            int docWidthPoints = window.ActivePane.Pages.Count > 0
                ? window.ActivePane.Pages[1].Width
                : visibleWidthPoints;

            int horizontalOffset = Math.Max((docWidthPoints - visibleWidthPoints) / 2, 0);

            window.HorizontalPercentScrolled = (int)((float)horizontalOffset / docWidthPoints * 100);
        }


        private class Win32Window : IWin32Window
        {
            private readonly IntPtr _handle;

            public Win32Window(IntPtr handle)
            {
                _handle = handle;
            }

            public IntPtr Handle => _handle;
        }
    }

}
