using Microsoft.Office.Interop.Word;
using RegexFindLib.Search;
using System.Collections.Generic;

namespace RegexFindDemo
{
    public class MockWordService : IWordService
    {
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
    }
}
