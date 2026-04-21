using Microsoft.Office.Interop.Word;
using System.Collections.Generic;
using System.Linq;

namespace RegexFindLib.Helpers
{
    /// <summary>
    /// Static gateway to the Word application instance.
    /// Set once from the VSTO host via RegexFindView constructor.
    /// </summary>
    public static class Vsto
    {
        public static Microsoft.Office.Tools.Word.ApplicationFactory ApplicationFactory { get; set; }
        public static Application Application { get; set; }
        public static Selection Selection => Application?.Selection;
        public static Document ActiveDocument => Application?.ActiveDocument;
        public static IEnumerable<Style> ActiveStyles =>
            ActiveDocument?.Styles.Cast<Style>().Where(s => s.InUse);
    }
}
