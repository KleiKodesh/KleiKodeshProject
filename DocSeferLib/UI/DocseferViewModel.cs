using DocSeferLib.Columns;
using DocSeferLib.Paragraphs;
using DocSeferLib.Spacing;

namespace DocSeferLib.UI
{
    public class DocseferViewModel
    {
        public ParagraphsViewModel ParagraphsViewModel { get; } = new ParagraphsViewModel();
        public ColumnsViewModel ColumnsViewModel { get; } = new ColumnsViewModel();
        public SpacingViewModel SpacingViewModel { get; } = new SpacingViewModel();
        public DocseferViewModel() { }

    }
}
