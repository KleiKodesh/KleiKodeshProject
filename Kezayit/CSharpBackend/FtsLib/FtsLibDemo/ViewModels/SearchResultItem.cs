namespace FtsLibDemo.ViewModels
{
    /// <summary>
    /// A single row displayed in the search results list.
    /// </summary>
    public sealed class SearchResultItem
    {
        public int    LineId    { get; }
        public string BookTitle { get; }
        public string Reference { get; }
        public string Snippet   { get; }

        public SearchResultItem(int lineId, string bookTitle, string reference, string snippet)
        {
            LineId    = lineId;
            BookTitle = bookTitle;
            Reference = reference;
            Snippet   = snippet;
        }
    }
}
