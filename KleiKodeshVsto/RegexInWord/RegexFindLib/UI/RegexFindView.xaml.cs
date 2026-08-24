using RegexFindLib.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RegexFindLib.UI
{
    public partial class RegexFindView : UserControl
    {
        // Styles are re-read from Word at most this often, however hard the
        // pane is clicked. TickCount rather than DateTime: it cannot jump when
        // the clock is adjusted, and wrap-around merely allows one early refresh.
        const int StyleRefreshThrottleMs = 5000;
        int _lastStyleRefreshTick = System.Environment.TickCount - StyleRefreshThrottleMs;

        public RegexFindView(
            Microsoft.Office.Interop.Word.Application app,
            Microsoft.Office.Tools.Word.ApplicationFactory factory)
        {
            Vsto.Application = app;
            Vsto.ApplicationFactory = factory;
            Initialize(new WordService());
        }

        public RegexFindView(RegexFindLib.Search.IWordService wordService)
        {
            Initialize(wordService);
        }

        void Initialize(RegexFindLib.Search.IWordService wordService)
        {
            DataContext = new RegexFindViewModel(wordService);
            InitializeComponent();

            Loaded += OnLoaded;

            // Refresh styles when control becomes visible
            IsVisibleChanged += (_, e) =>
            {
                if ((bool)e.NewValue && DataContext is RegexFindViewModel vm)
                    vm.EnsureStylesLoaded();
            };

            // Refresh styles when focus returns to the pane - the user may have
            // changed styles in Word in the meantime. GotFocus bubbles from every
            // child, so unthrottled this ran a full doc.Styles enumeration on
            // EVERY click inside the pane, which is what made it feel sticky.
            // The throttle makes clicking around inside the pane free while
            // still catching a return from Word; the idle dispatch lets the
            // click paint before the COM work. (Not IsKeyboardFocusWithinChanged:
            // under ElementHost, WPF keyboard focus can survive the user
            // clicking out into Word, so the flag cannot be trusted to flip
            // and fire again on return.)
            GotFocus += (_, __) =>
            {
                if (!(DataContext is RegexFindViewModel vm)) return;
                if (System.Environment.TickCount - _lastStyleRefreshTick < StyleRefreshThrottleMs) return;
                _lastStyleRefreshTick = System.Environment.TickCount;
                Dispatcher.BeginInvoke(new System.Action(() =>
                    vm.EnsureStylesLoaded()),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;

            // Defer all data loading until after the first frame is rendered.
            // DispatcherPriority.ApplicationIdle fires only when the UI is idle —
            // the control is fully painted and visible before any loading begins.
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                RegexFindViewModel.LoadRecentSearches();
                RegexFindViewModel.ScheduleFontLoad();

                if (DataContext is RegexFindViewModel vm)
                    vm.EnsureStylesLoaded();

                RegexPalette.InsertAction = InsertSymbolAtCursor;
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        RegexFindViewModel Vm => DataContext as RegexFindViewModel;

        // ── Regex palette insertion ───────────────────────────────────────────

        void InsertSymbolAtCursor(string symbol)
        {
            if (string.IsNullOrEmpty(symbol) || Vm == null) return;

            var tb = Vm.FindFocused ? FindBox : ReplaceBox;
            int caret = tb.CaretIndex;
            var text  = tb.Text ?? "";
            tb.Text   = text.Substring(0, caret) + symbol + text.Substring(caret);
            tb.CaretIndex = caret + symbol.Length;
            tb.Focus();

            if (Vm.FindFocused) Vm.SearchText  = tb.Text;
            else                Vm.ReplaceText = tb.Text;
        }

        // ── Find TextBox ──────────────────────────────────────────────────────

        void FindBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (Vm != null) Vm.FindFocused = true;
        }

        void FindBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                e.Handled = true;
                if (Vm == null) return;

                if (Vm.Results.Count > 0)
                {
                    if (e.KeyboardDevice.Modifiers == ModifierKeys.Shift)
                        Vm.AdvanceToPreviousResult();
                    else
                        Vm.AdvanceToNextResult();
                }
                else
                    Vm.SearchCommand.Execute(null);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (Vm != null) Vm.SearchText = "";
            }
        }

        void FindHistoryBtn_Click(object sender, RoutedEventArgs e)
        {
            FindHistoryPopup.IsOpen = true;
        }

        void FindHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                FindHistoryPopup.IsOpen = false;
                FindBox.Focus();
                FindBox.CaretIndex = FindBox.Text?.Length ?? 0;
            }
        }

        // ── Replace TextBox ───────────────────────────────────────────────────

        void ReplaceBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (Vm != null) Vm.FindFocused = false;
        }

        void ReplaceBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return)
            {
                e.Handled = true;
                Vm?.ReplaceCommand.Execute(null);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (Vm != null) Vm.ReplaceText = "";
            }
        }

        void ReplaceHistoryBtn_Click(object sender, RoutedEventArgs e)
        {
            ReplaceHistoryPopup.IsOpen = true;
        }

        void ReplaceHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0)
            {
                ReplaceHistoryPopup.IsOpen = false;
                ReplaceBox.Focus();
                ReplaceBox.CaretIndex = ReplaceBox.Text?.Length ?? 0;
            }
        }

        // ── Results list ──────────────────────────────────────────────────────

        void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox lb && lb.SelectedItem != null)
                lb.ScrollIntoView(lb.SelectedItem);
        }

        // ── Results keyboard navigation ───────────────────────────────────────

        void ResultsList_KeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is ListBox lb) || Vm == null) return;

            int count = lb.Items.Count;
            if (count == 0) return;

            int current = lb.SelectedIndex;

            switch (e.Key)
            {
                case Key.Down:
                    e.Handled = true;
                    lb.SelectedIndex = current < count - 1 ? current + 1 : current;
                    lb.ScrollIntoView(lb.SelectedItem);
                    break;
                case Key.Up:
                    e.Handled = true;
                    lb.SelectedIndex = current > 0 ? current - 1 : 0;
                    lb.ScrollIntoView(lb.SelectedItem);
                    break;
                case Key.Return:
                    e.Handled = true;
                    if (current >= 0) Vm.SelectedResultIndex = current;
                    break;
            }
        }
    }
}
