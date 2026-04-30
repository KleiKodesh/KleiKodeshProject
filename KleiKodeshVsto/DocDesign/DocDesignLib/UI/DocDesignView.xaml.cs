using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
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
            Loaded += OnLoaded;

            // Refresh styles when control becomes visible — deferred so it doesn't
            // block the visibility transition itself
            IsVisibleChanged += (_, e) =>
            {
                if ((bool)e.NewValue && DataContext is DocDesignViewModel vm)
                    Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        vm.ParagraphsViewModel.RefreshActiveStylesAction();
                    }), DispatcherPriority.ApplicationIdle);
            };

            // Refresh styles when control gets focus — deferred
            GotFocus += (_, __) =>
            {
                if (DataContext is DocDesignViewModel vm)
                    Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        vm.ParagraphsViewModel.RefreshActiveStylesAction();
                    }), DispatcherPriority.ApplicationIdle);
            };
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;

            // Defer all Word COM initialization until after the first frame is rendered.
            // ApplicationIdle fires only when the dispatcher queue is empty — the control
            // is fully painted before any COM calls begin.
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (DataContext is DocDesignViewModel vm)
                {
                    vm.ParagraphsViewModel.RefreshActiveStylesAction();
                    vm.ParagraphsViewModel.FirstWordStyle.DeferredInit();
                    vm.SpacingViewModel.DeferredInit();
                }
            }), DispatcherPriority.ApplicationIdle);
        }
    }
}
