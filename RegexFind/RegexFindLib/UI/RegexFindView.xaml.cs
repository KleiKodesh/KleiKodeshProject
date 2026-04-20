using RegexFindLib.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RegexFindLib.UI
{
    public partial class RegexFindView : UserControl
    {
        /// <summary>
        /// Production constructor — called from the VSTO ribbon with real Word objects.
        /// </summary>
        public RegexFindView(
            Microsoft.Office.Interop.Word.Application app,
            Microsoft.Office.Tools.Word.ApplicationFactory factory)
        {
            Vsto.Application = app;
            Vsto.ApplicationFactory = factory;
            Initialize(new WordService());
        }

        /// <summary>
        /// Demo/test constructor — inject any IWordService (e.g. MockWordService).
        /// No Word installation required.
        /// </summary>
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
                    vm.LoadFontsCommand.Execute(null);
                    vm.LoadRecentSearches();
                }
                RegexPalette.InsertAction = InsertSymbolAtCursor;
            };
        }

        RegexFindViewModel Vm => DataContext as RegexFindViewModel;

        // ── Regex palette tip insertion — view concern, inserts at cursor ─────

        void InsertSymbolAtCursor(string symbol)
        {
            if (string.IsNullOrEmpty(symbol) || Vm == null) return;

            // Pick the focused ComboBox (find or replace)
            var combo = Vm.FindFocused ? FindCombo : ReplaceCombo;

            // Get the inner editable TextBox from the ComboBox template
            var tb = combo.Template?.FindName("PART_EditableTextBox", combo) as TextBox;
            if (tb == null)
            {
                // Fallback: append to ViewModel property
                if (Vm.FindFocused) Vm.SearchText  += symbol;
                else                Vm.ReplaceText += symbol;
                return;
            }

            // Insert at caret position
            int caret = tb.CaretIndex;
            var text  = tb.Text ?? "";
            tb.Text   = text.Substring(0, caret) + symbol + text.Substring(caret);
            tb.CaretIndex = caret + symbol.Length;
            tb.Focus();

            // Sync back to ViewModel
            if (Vm.FindFocused) Vm.SearchText  = tb.Text;
            else                Vm.ReplaceText = tb.Text;
        }

        // ── Find ComboBox ─────────────────────────────────────────────────────

        void FindCombo_GotFocus(object sender, RoutedEventArgs e)
        {
            if (Vm != null) Vm.FindFocused = true;
        }

        void FindCombo_KeyDown(object sender, KeyEventArgs e)
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

        // ── Replace ComboBox ──────────────────────────────────────────────────

        void ReplaceCombo_GotFocus(object sender, RoutedEventArgs e)
        {
            if (Vm != null) Vm.FindFocused = false;
        }

        void ReplaceCombo_KeyDown(object sender, KeyEventArgs e)
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

        // ── History remove button (× on each item) ────────────────────────────
        // Tag is set to the item string in the DataTemplate

        void HistoryRemove_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            var text = btn.Tag as string;
            if (string.IsNullOrEmpty(text) || Vm == null) return;

            // Determine which history this belongs to by walking up to the ComboBox
            var combo = FindParentComboBox(btn);
            if (combo == null) return;

            // Find/replace by checking which collection is bound
            if (combo.ItemsSource == Vm.RecentSearches)
                SearchHistory.Find.Remove(text);
            else
                SearchHistory.Replace.Remove(text);

            Vm.LoadRecentSearches();

            // Keep dropdown open
            combo.IsDropDownOpen = true;
            e.Handled = true; // prevent ComboBox from selecting the item
        }

        static ComboBox FindParentComboBox(DependencyObject child)
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is ComboBox cb) return cb;
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        // ── Style combobox — lazy load ────────────────────────────────────────

        void StyleCombo_GotFocus(object sender, RoutedEventArgs e)
        {
            Vm?.EnsureStylesLoaded();
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
