using DocDesign.Columns;
using DocDesign.Paragraphs;
using DocDesign.Spacing;

namespace DocDesign.UI
{
    public class DocDesignViewModel
    {
        public ParagraphsViewModel ParagraphsViewModel { get; } = new ParagraphsViewModel();
        public ColumnsViewModel ColumnsViewModel { get; } = new ColumnsViewModel();
        public SpacingViewModel SpacingViewModel { get; } = new SpacingViewModel();
        public DocDesignViewModel() { }

    }
}
