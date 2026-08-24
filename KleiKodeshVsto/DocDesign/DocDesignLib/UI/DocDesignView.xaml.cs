using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DocDesign.Helpers;
using DocDesign.UI;

namespace DocDesign
{
    public partial class DocDesignView : UserControl
    {
        // Styles are re-read from Word at most this often, however hard the
        // pane is clicked. TickCount rather than DateTime: it cannot jump when
        // the clock is adjusted, and wrap-around merely allows one early refresh.
        const int StyleRefreshThrottleMs = 5000;
        int _lastStyleRefreshTick = Environment.TickCount - StyleRefreshThrottleMs;

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

            // The selection watcher lives with the view. Loaded/Unloaded rather
            // than the constructor, because the XAML builds the view model even
            // for a view that is never shown.
            Loaded += (_, __) =>
            {
                if (DataContext is DocDesignViewModel vm) vm.SpacingViewModel.Attach();
            };
            Unloaded += (_, __) =>
            {
                if (DataContext is DocDesignViewModel vm) vm.SpacingViewModel.Live = false;
            };

            // Visibility drives two things: the style lists refresh when the
            // pane shows (ungated on purpose - this is the recovery path for
            // changes a style count cannot see), and the selection watcher
            // idles while the pane is hidden, so a hidden pane costs nothing
            // per caret move. DeferredInit resyncs the spacing values on show.
            IsVisibleChanged += (_, e) =>
            {
                if (!(DataContext is DocDesignViewModel vm)) return;
                bool visible = (bool)e.NewValue;
                vm.SpacingViewModel.Live = visible;
                if (!visible) return;
                vm.SpacingViewModel.DeferredInit();
                vm.ParagraphsViewModel.RefreshActiveStylesAction();
                vm.ParagraphsViewModel.FirstWordStyle.RefreshStyles();
            };

            // Refresh styles when focus returns to the pane - the user may have
            // changed styles in Word in the meantime. GotFocus bubbles from every
            // child, so unthrottled this ran two full doc.Styles enumerations
            // (~370 styles, three COM reads each) on EVERY click inside the pane,
            // which is what made it feel sticky. The throttle makes clicking
            // around inside the pane free while still catching a return from
            // Word; the idle dispatch lets the click paint before the COM work.
            // (Not IsKeyboardFocusWithinChanged: under ElementHost, WPF keyboard
            // focus can survive the user clicking out into Word, so the flag
            // cannot be trusted to flip and fire again on return.)
            GotFocus += (_, __) =>
            {
                if (!(DataContext is DocDesignViewModel vm)) return;
                if (Environment.TickCount - _lastStyleRefreshTick < StyleRefreshThrottleMs) return;
                _lastStyleRefreshTick = Environment.TickCount;
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    // Count-and-document check first: a couple of COM reads that
                    // skip the full enumerations when nothing was added or
                    // removed. Each list gates on ITS OWN stamp - the picker
                    // toggle refreshes only ActiveStyles, and a shared stamp
                    // would let it silence a FirstWordStyle refresh. The
                    // ungated refreshes (pane shown) catch what a count misses.
                    if (vm.ParagraphsViewModel.StylesLookChanged())
                        vm.ParagraphsViewModel.RefreshActiveStylesAction();
                    if (vm.ParagraphsViewModel.FirstWordStyle.StylesLookChanged())
                        vm.ParagraphsViewModel.FirstWordStyle.RefreshStyles();
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
                    vm.ParagraphsViewModel.FirstWordStyle.DeferredInit(); // also calls RefreshStyles internally
                    vm.SpacingViewModel.DeferredInit();
                }
            }), DispatcherPriority.ApplicationIdle);
        }
    }
}
