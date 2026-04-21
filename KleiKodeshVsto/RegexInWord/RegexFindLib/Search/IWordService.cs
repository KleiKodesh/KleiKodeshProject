using Microsoft.Office.Interop.Word;
using System.Collections.Generic;

namespace RegexFindLib.Search
{
    /// <summary>
    /// Abstracts all Word Interop access so the model (RegexSearch) and ViewModel
    /// never reference Vsto, Globals, or any infrastructure directly.
    /// Implemented once in RegexFindLib.Helpers.WordService and injected at construction.
    /// </summary>
    public interface IWordService
    {
        Application Application { get; }
        Document ActiveDocument { get; }
        Selection Selection { get; }

        /// <summary>Returns all in-use style names from the active document.</summary>
        IEnumerable<string> GetStyleNames();
    }
}
