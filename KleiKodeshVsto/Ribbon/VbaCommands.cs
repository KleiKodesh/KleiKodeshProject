using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KleiKodesh.Ribbon
{
    /// <summary>
    /// The add-in's commands for VBA macros. Each command answers to its English
    /// ribbon id and to its Hebrew ribbon label (spaces as underscores):
    ///
    ///     Dim kk As Object
    ///     Set kk = Application.COMAddIns("KleiKodesh").Object
    ///     kk.Nakdan
    ///     kk.נקדן_דיקטה
    ///
    /// Everything forwards to AddinCommands, the same code the ribbon runs. The
    /// Hebrew names mirror AddinText - keep them in step when a label changes. They
    /// need Windows' "language for non-Unicode programs" set to Hebrew, as the VBA
    /// editor itself does to type them.
    /// Deliberately NOT exposed: Settings and DuplicatePane. Both act on the add-in's
    /// own panes rather than on the document, so they make no sense in a macro.
    /// Do not add them back when adding new commands here. TogglePaneFocus is the
    /// exception on purpose: moving keyboard focus is exactly what a shortcut is for.
    /// </summary>
    [ComVisible(true)]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IVbaCommands
    {
        void KitveiHakodesh();
        void כתבי_הקודש();

        void Kiwix();
        void ספריית_קיוויקס();

        void WebSites();
        void דרך_האתרים();

        void DocDesign();
        void עיצוב_תורני();

        void Nakdan();
        void נקדן_דיקטה();

        void RegexFind();
        void חיפוש_רגקס();

        void About();
        void אודות();

        void SearchText();
        void העתק_לחיפוש_בכתבי_הקודש();

        void SearchBook();
        void חיפוש_ספר_בכתבי_הקודש();

        void CompareText();
        void השווה_עם_כתבי_הקודש();

        void OpenLink();
        void פתח_קישור_בכתבי_הקודש();

        void TogglePaneFocus();
        void מעבר_בין_מסמך_לחלונית();
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class VbaCommands : IVbaCommands
    {
        // A macro calls in here synchronously, in the middle of VBA's call into Word.
        // Building the Kitvei Hakodesh pane in that state leaves its WebView2 blank
        // (issue #331: first macro run with the pane closed shows a white pane), so
        // never run a command inside the call: queue it on Word's UI message loop
        // and return. It then runs exactly like a ribbon click.
        static Control _poster;

        static void Post(Action command)
        {
            if (_poster == null)
            {
                _poster = new Control();
                _ = _poster.Handle;   // window created on this (Word's UI) thread
            }
            _poster.BeginInvoke(command);
        }

        public void KitveiHakodesh() => Post(() => AddinCommands.Run("KitveiHakodesh"));
        public void כתבי_הקודש() => KitveiHakodesh();

        public void Kiwix() => Post(() => AddinCommands.Run("Kiwix"));
        public void ספריית_קיוויקס() => Kiwix();

        public void WebSites() => Post(() => AddinCommands.Run("WebSites"));
        public void דרך_האתרים() => WebSites();

        public void DocDesign() => Post(() => AddinCommands.Run("DocDesign"));
        public void עיצוב_תורני() => DocDesign();

        public void Nakdan() => Post(() => AddinCommands.Run("Nakdan"));
        public void נקדן_דיקטה() => Nakdan();

        public void RegexFind() => Post(() => AddinCommands.Run("RegexFind"));
        public void חיפוש_רגקס() => RegexFind();

        public void About() => Post(() => AddinCommands.Run("About"));
        public void אודות() => About();

        public void SearchText() => Post(() => AddinCommands.SearchSelection("fts"));
        public void העתק_לחיפוש_בכתבי_הקודש() => SearchText();

        public void SearchBook() => Post(() => AddinCommands.SearchSelection("catalog"));
        public void חיפוש_ספר_בכתבי_הקודש() => SearchBook();

        public void CompareText() => Post(() => AddinCommands.CompareSelectionWithPane());
        public void השווה_עם_כתבי_הקודש() => CompareText();

        public void OpenLink() => Post(() => AddinCommands.OpenSelectionLink());
        public void פתח_קישור_בכתבי_הקודש() => OpenLink();

        public void TogglePaneFocus() => Post(() => AddinCommands.TogglePaneFocus());
        public void מעבר_בין_מסמך_לחלונית() => TogglePaneFocus();
    }
}
