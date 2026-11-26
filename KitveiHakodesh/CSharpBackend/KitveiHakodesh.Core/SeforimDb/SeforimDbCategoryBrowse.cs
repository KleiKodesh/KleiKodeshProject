namespace KitveiHakodesh.Core.SeforimDb
{
    /// <summary>Lazy catalog browsing: one folder at a time, thin (subfolders + book ids).
    /// Row details come per rendered row from GetBookSummaries.</summary>
    public sealed partial class SeforimDbQueries
    {
        /// <summary>The id the catalog gives the synthetic folder of books whose category row is missing.</summary>
        public const int OrphanCategoryId = -999999;

        // Escapes keep this source ASCII.
        /// <summary>Top-level shelf that is the collection's own about/credits material, not seforim:
        /// hidden from browsing (first word of the title).</summary>
        private const string HiddenRootFirstWord = "\u05D0\u05D5\u05D3\u05D5\u05EA";
        private const string OrphanFolderTitle = "\u05E1\u05E4\u05E8\u05D9\u05DD \u05E0\u05D5\u05E1\u05E4\u05D9\u05DD";

        /// <summary>One folder's contents. categoryId null = the top level (minus the hidden shelf,
        /// plus the orphan folder when some book has no category).</summary>
        public CategoryContents GetCategoryContents(int? categoryId)
        {
            if (categoryId == OrphanCategoryId)
                return new CategoryContents { BookIds = ReadCategoryContents(null, orphans: true).BookIds };

            var (folders, bookIds) = ReadCategoryContents(categoryId, orphans: false);
            var contents = new CategoryContents();
            foreach (var f in folders)
            {
                bool hidden = categoryId == null
                    && f.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == HiddenRootFirstWord;
                if (!hidden) contents.Folders.Add(f);
            }
            if (categoryId == null)
            {
                // At the top level the book ids read are the orphans'.
                if (bookIds.Count > 0)
                    contents.Folders.Add(new CategoryFolderRow { Id = OrphanCategoryId, Title = OrphanFolderTitle, BookCount = bookIds.Count });
            }
            else contents.BookIds = bookIds;
            return contents;
        }

        /// <summary>A category's ancestors and itself, root first; the orphan folder is its own chain.</summary>
        public List<CategoryChainStep> GetCategoryChain(int categoryId) =>
            categoryId == OrphanCategoryId
                ? new List<CategoryChainStep> { new() { Id = OrphanCategoryId, Title = OrphanFolderTitle } }
                : ReadCategoryChain(categoryId);
    }
}
