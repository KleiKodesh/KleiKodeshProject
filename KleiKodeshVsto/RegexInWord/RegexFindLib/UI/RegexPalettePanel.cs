using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Scrollable list of regex/wildcard tips. Template defined in PaletteStyles.xaml.
    /// Tips are driven externally via the Tips DP — bound to ViewModel.PaletteTips.
    /// InsertAction is wired by the view to insert the symbol at the cursor.
    /// </summary>
    public class RegexPalettePanel : Control
    {
        // ── Tips data — exposed as a DP so the template can bind to it ────────
        public static readonly DependencyProperty TipsProperty =
            DependencyProperty.Register(nameof(Tips), typeof(ObservableCollection<RegexTip>),
                typeof(RegexPalettePanel));

        public ObservableCollection<RegexTip> Tips
        {
            get => (ObservableCollection<RegexTip>)GetValue(TipsProperty);
            set => SetValue(TipsProperty, value);
        }

        // ── InsertAction — wired by the view ──────────────────────────────────
        public static readonly DependencyProperty InsertActionProperty =
            DependencyProperty.Register(nameof(InsertAction), typeof(Action<string>),
                typeof(RegexPalettePanel));

        public Action<string> InsertAction
        {
            get => (Action<string>)GetValue(InsertActionProperty);
            set => SetValue(InsertActionProperty, value);
        }
    }

    public class RegexTip
    {
        public string Symbol  { get; }
        public string Meaning { get; }
        public string Example { get; }

        public RegexTip(string symbol, string meaning, string example)
        {
            Symbol  = symbol;
            Meaning = meaning;
            Example = example;
        }
    }
}
