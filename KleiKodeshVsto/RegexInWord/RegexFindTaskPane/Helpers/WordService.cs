using Microsoft.Office.Interop.Word;
using RegexFindTaskPane.Search;
using System.Collections.Generic;

namespace RegexFindTaskPane.Helpers
{
    public class WordService : IWordService
    {
        public Application Application => Vsto.Application;
        public Document ActiveDocument => Vsto.ActiveDocument;
        public Selection Selection => Vsto.Selection;

        public int GetStylesCount()
        {
            try { return ActiveDocument?.Styles.Count ?? -1; }
            catch { return -1; }
        }

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
    }
}
