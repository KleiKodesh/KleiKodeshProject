using System;
using System.Collections.Generic;
using FtsLib;

namespace KitveiHakodesh.Core.SeforimDbFullTextSearch
{
    /// <summary>
    /// What the searcher asks of a corpus to extend short hits over their neighbour lines: the
    /// lines at given (book id, lineIndex) positions. Implemented by the plain
    /// <see cref="SeforimDbFtsCorpus"/> and by <see cref="CompositeFtsCorpus"/>, whose book and
    /// line ids are the GLOBAL ids of <see cref="KitveiHakodesh.Core.SeforimDb.SeforimDbLibraryId"/>.
    /// </summary>
    internal interface ISeforimDbFtsPositionCorpus : IDisposable
    {
        List<SeforimDbFtsLineAtPosition> FetchLinesAtBookPositions(IReadOnlyList<(int BookId, int LineIndex)> positions);

        /// <summary>The placement (book id, lineIndex, own TOC entry) of each line, no text. An id with no line
        /// is absent. Ids, book ids and TOC entry ids are the corpus's own (global, for a composite).</summary>
        Dictionary<int, FtsDocumentPlacement> FetchPlacements(IReadOnlyList<int> ids);
    }
}
