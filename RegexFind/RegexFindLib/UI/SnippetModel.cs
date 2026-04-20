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
            Before = before ?? "";
            Match = match ?? "";
            After = after ?? "";
        }
    }
}
