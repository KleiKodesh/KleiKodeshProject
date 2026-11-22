using Microsoft.Office.Interop.Word;
using RegexFindTaskPane.Search;
using System.Collections.Generic;

namespace RegexFindTaskPaneDemo
{
    public class MockWordService : IWordService
    {
        public Application Application => null;
        public Document     ActiveDocument => null;
        public Selection    Selection => null;

        public int GetStylesCount() => 15;

        public IEnumerable<string> GetStyleNames() => new[]
        {
            "Normal", "Heading 1", "Heading 2", "Heading 3",
            "Body Text", "Caption", "Quote", "Intense Quote",
            "List Paragraph", "Title", "Subtitle", "Emphasis",
            "Strong", "Book Title", "Default Paragraph Style"
        };
    }
}
