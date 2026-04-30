namespace RegexFindLib.Search
{
    /// <summary>
    /// Unified interface for find/replace engines.
    /// Implemented by RegexSearchEngine (custom .NET regex) and WordSearchEngine (Word native Find).
    /// The ViewModel talks only to this interface — engine can be swapped at runtime.
    /// </summary>
    public interface ISearchEngine
    {
        /// <summary>Results from the last Execute call. Null until first search.</summary>
        SearchResult[] Results { get; }

        /// <summary>
        /// Runs a find or find-and-replace operation.
        /// Pass replace=true to replace the current selection match.
        /// Pass replaceAll=true to replace all matches.
        /// </summary>
        void Execute(FindRequest request, bool replace = false, bool replaceAll = false);

        /// <summary>
        /// Reads the formatting of the current Word selection.
        /// Used by the eyedropper / CopyFormatting command.
        /// </summary>
        FindFormatting GetSelectionFormatting();

        /// <summary>Selects the result at the given index in the document.</summary>
        void SelectResultByIndex(int index);
    }
}
