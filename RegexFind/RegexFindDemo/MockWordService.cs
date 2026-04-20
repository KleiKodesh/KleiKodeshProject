using Microsoft.Office.Interop.Word;
using RegexFindLib.Search;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;

namespace RegexFindDemo
{
    /// <summary>
    /// Stub IWordService for the demo app — no Word installation required.
    /// Search/replace operations are no-ops; font and style lists are real.
    /// </summary>
    public class MockWordService : IWordService
    {
        // Word interop stubs — return null; the UI won't crash, operations just do nothing
        public Application Application => null;
        public Document     ActiveDocument => null;
        public Selection    Selection => null;

        public IEnumerable<string> GetStyleNames() => new[]
        {
            "Normal", "Heading 1", "Heading 2", "Heading 3",
            "Body Text", "Caption", "Quote", "Intense Quote",
            "List Paragraph", "Title", "Subtitle", "Emphasis",
            "Strong", "Book Title", "Default Paragraph Style"
        };

        public IEnumerable<string> GetFontNames()
        {
            using (var col = new InstalledFontCollection())
                return col.Families.Select(f => f.Name).ToList();
        }
    }
}
