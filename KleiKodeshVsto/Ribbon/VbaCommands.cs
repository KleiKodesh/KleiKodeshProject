using System.Runtime.InteropServices;

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
    /// Do not add them back when adding new commands here.
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
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public class VbaCommands : IVbaCommands
    {
        public void KitveiHakodesh() => AddinCommands.Run("KitveiHakodesh");
        public void כתבי_הקודש() => KitveiHakodesh();

        public void Kiwix() => AddinCommands.Run("Kiwix");
        public void ספריית_קיוויקס() => Kiwix();

        public void WebSites() => AddinCommands.Run("WebSites");
        public void דרך_האתרים() => WebSites();

        public void DocDesign() => AddinCommands.Run("DocDesign");
        public void עיצוב_תורני() => DocDesign();

        public void Nakdan() => AddinCommands.Run("Nakdan");
        public void נקדן_דיקטה() => Nakdan();

        public void RegexFind() => AddinCommands.Run("RegexFind");
        public void חיפוש_רגקס() => RegexFind();

        public void About() => AddinCommands.Run("About");
        public void אודות() => About();

        public void SearchText() => AddinCommands.SearchSelection("fts");
        public void העתק_לחיפוש_בכתבי_הקודש() => SearchText();

        public void SearchBook() => AddinCommands.SearchSelection("catalog");
        public void חיפוש_ספר_בכתבי_הקודש() => SearchBook();

        public void CompareText() => AddinCommands.CompareSelectionWithPane();
        public void השווה_עם_כתבי_הקודש() => CompareText();

        public void OpenLink() => AddinCommands.OpenSelectionLink();
        public void פתח_קישור_בכתבי_הקודש() => OpenLink();
    }
}
