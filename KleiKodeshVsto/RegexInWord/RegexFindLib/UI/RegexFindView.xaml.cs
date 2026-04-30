using RegexFindLib.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RegexFindLib.UI
{
    public partial class RegexFindView : UserControl
    {
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

            Loaded += (_, __) =>
            {
                if (DataContext is RegexFindViewModel vm)
                {
                    RegexFindViewModel.LoadRecentSearches();
                    // Load styles once at initialization
                    vm.EnsureStylesLoaded();
                }
                RegexPalette.InsertAction = InsertSymbolAtCursor;
            };

            // Refresh styles when control becomes visible
            IsVisibleChanged += (_, e) =>
            {
                if ((bool)e.NewValue && DataContext is RegexFindViewModel vm)
                    vm.EnsureStylesLoaded();
            };

            // Refresh styles when control gets focus
            GotFocus += (_, __) =>
            {
                if (DataContext is RegexFindViewModel vm)
                    vm.EnsureStylesLoaded();
            };
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
                Vm?.SearchCommand.Execute(null);
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
