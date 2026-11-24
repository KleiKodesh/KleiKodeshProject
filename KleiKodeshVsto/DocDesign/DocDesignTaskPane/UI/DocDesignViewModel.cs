using DocDesignTaskPane.Columns;
using DocDesignTaskPane.Paragraphs;
using DocDesignTaskPane.Spacing;

namespace DocDesignTaskPane.UI
{
    public class DocDesignViewModel
    {
        public ParagraphsViewModel ParagraphsViewModel { get; } = new ParagraphsViewModel();
        public ColumnsViewModel ColumnsViewModel { get; } = new ColumnsViewModel();
        public SpacingViewModel SpacingViewModel { get; } = new SpacingViewModel();
        public DocDesignViewModel() { }

    }
}
