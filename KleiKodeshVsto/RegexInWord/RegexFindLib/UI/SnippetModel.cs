using System.Text.RegularExpressions;

namespace RegexFindLib.UI
{
    /// <summary>
    /// ViewModel-layer representation of a single search result for display.
    /// Built by RegexFindViewModel from the model's SearchResult.
    /// The view (SnippetBlock) renders this — no HTML, no Word interop.
    /// </summary>
    public class SnippetModel
    {
        public string Before { get; }
        public string Match { get; }
        public string After { get; }

        public SnippetModel(string before, string match, string after)
        {
            Before = Normalize(before);
            Match  = Normalize(match);
            After  = Normalize(after);
        }

        // Collapse any run of whitespace that contains at least one newline
        // (paragraph marks, line breaks, tabs, etc.) into a single space.
        static string Normalize(string text) =>
            string.IsNullOrEmpty(text)
                ? ""
                : Regex.Replace(text, @"[ \t]*[\r\n\v\f]+[ \t]*", " ").Trim();
    }
}
