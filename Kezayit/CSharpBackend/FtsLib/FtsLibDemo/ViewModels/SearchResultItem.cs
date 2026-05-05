namespace FtsLibDemo.ViewModels
{
    /// <summary>
    /// A single row displayed in the search results list.
    /// </summary>
    public sealed class SearchResultItem
    {
        public int    LineId      { get; }
        public string BookTitle   { get; }
        public string Snippet     { get; }
        /// <summary>Snippet with &lt;mark&gt; tags stripped — used for text selection/copy.</summary>
        public string PlainSnippet { get; }

        public SearchResultItem(int lineId, string bookTitle, string snippet)
        {
            LineId       = lineId;
            BookTitle    = bookTitle;
            Snippet      = snippet;
            PlainSnippet = StripMark(snippet);
        }

        private static string StripMark(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s
                .Replace("<mark>",  string.Empty)
                .Replace("</mark>", string.Empty)
                .Replace("&amp;",   "&")
                .Replace("&gt;",    ">");
        }
    }
}
