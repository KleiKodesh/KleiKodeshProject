using Nakdan.Core;
using Nakdan.Helpers;
using Nakdan.WdStyles;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Nakdan.UI
{
    // ═══════════════════════════════════════════════════════════
    //  NAKDAN VIEW — code-behind
    //
    //  The ViewModel is created in XAML (NakdanView.xaml).
    //  The code-behind sets up the Vsto helper and style refresh.
    //
    //  Usage in KeliKodeshRibbon.cs:
    //      var nakdan = new NakdanView(Globals.ThisAddIn.Application, Globals.Factory);
    //      WpfTaskPane.Show(nakdan, "נקדן דיקטה", 520);
    // ═══════════════════════════════════════════════════════════
    public partial class NakdanView : UserControl
    {
        /// <summary>
        /// Production constructor — called from VSTO ribbon with live Word objects.
        /// </summary>
        public NakdanView(
            Microsoft.Office.Interop.Word.Application app,
            Microsoft.Office.Tools.Word.ApplicationFactory factory)
        {
            VstoHelper.Application = app;
            VstoHelper.ApplicationFactory = factory;
            InitializeComponent();
            SetupStyleRefresh();
        }

        /// <summary>
        /// Demo constructor — no Word objects needed.
        /// VstoHelper stays null; all style loading will no-op gracefully.
        /// </summary>
        public NakdanView()
        {
            InitializeComponent();
            SetupStyleRefresh();
        }

        void SetupStyleRefresh()
        {
            Loaded += OnLoaded;

            // Nikud suggestions: the selection hook is live only while the pane
            // is on screen (the VM also gates on its toggle).
            IsVisibleChanged += (_, e) =>
            {
                if (DataContext is NakdanViewModel vm)
                {
                    if ((bool)e.NewValue && vm.SuggestionsEnabled) vm.ScheduleSuggestionsUpdate();
                }
            };

            // Refresh styles when control becomes visible — deferred so it doesn't
            // block the visibility transition itself
            IsVisibleChanged += (_, e) =>
            {
                if ((bool)e.NewValue && DataContext is NakdanViewModel vm)
                    Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        vm.RefreshActiveStylesAction();
                    }), DispatcherPriority.ApplicationIdle);
            };

            // Refresh styles when control gets focus — deferred
            IgnoreStylesPopup.Opened += (_, __) =>
            {
                if (DataContext is NakdanViewModel vm)
                    Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        vm.RefreshActiveStylesAction();
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
                if (DataContext is NakdanViewModel vm)
                {
                    vm.RefreshActiveStylesAction();
                    vm.IsPaneVisible = () => IsVisible;
                    vm.AttachSelection();
                    if (vm.SuggestionsEnabled) vm.ScheduleSuggestionsUpdate();
                }
            }), DispatcherPriority.ApplicationIdle);
        }
    }
}
