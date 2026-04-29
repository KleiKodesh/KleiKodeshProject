using System.Windows.Controls;
using DocDesign.Helpers;
using DocDesign.UI;

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
            SetupStyleRefresh();
        }

        /// <summary>
        /// Demo constructor — no Word objects needed.
        /// Vsto stays null; all commands will no-op gracefully.
        /// </summary>
        public DocDesignView()
        {
            InitializeComponent();
            SetupStyleRefresh();
        }

        void SetupStyleRefresh()
        {
            // Refresh styles when control becomes visible
            IsVisibleChanged += (_, e) =>
            {
                if ((bool)e.NewValue && DataContext is DocDesignViewModel vm)
                    vm.ParagraphsViewModel.RefreshActiveStylesAction();
            };

            // Refresh styles when control gets focus
            GotFocus += (_, __) =>
            {
                if (DataContext is DocDesignViewModel vm)
                    vm.ParagraphsViewModel.RefreshActiveStylesAction();
            };
        }
    }
}
