using Microsoft.Office.Interop.Word;
using RegexFindLib.Search;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;

namespace RegexFindLib.Helpers
{
    /// <summary>
    /// Concrete implementation of IWordService.
    /// Reads from the static Vsto gateway — the only place in the lib that touches it.
    /// Injected into RegexSearch and RegexFindViewModel at construction time.
    /// </summary>
    public class WordService : IWordService
    {
        public Application Application => Vsto.Application;
        public Document ActiveDocument => Vsto.ActiveDocument;
        public Selection Selection => Vsto.Selection;

        public IEnumerable<string> GetStyleNames()
        {
            var doc = ActiveDocument;
            if (doc == null) yield break;
            foreach (Style s in doc.Styles)
            {
                string name = null;
                try { if (s.InUse) name = s.NameLocal; } catch { }
                if (name != null) yield return name;
            }
        }

        public IEnumerable<string> GetFontNames()
        {
            using (var col = new InstalledFontCollection())
                return col.Families.Select(f => f.Name).ToList();
        }
    }
}
