using System.Windows.Controls;

namespace DocDesign
{
    public partial class DocDesignView : UserControl
    {
        /// <summary>
        /// Production constructor — called from VSTO ribbon with live Word objects.
        /// </summary>
        public DocDesignView(
            Microsoft.Office.Interop.Word.Application app,
            Microsoft.Office.Tools.Word.ApplicationFactory factory)
        {
            Vsto.Application = app;
            Vsto.ApplicationFactory = factory;
            InitializeComponent();
        }

        /// <summary>
        /// Demo constructor — no Word objects needed.
        /// Vsto stays null; all commands will no-op gracefully.
        /// </summary>
        public DocDesignView()
        {
            InitializeComponent();
        }
    }
}
