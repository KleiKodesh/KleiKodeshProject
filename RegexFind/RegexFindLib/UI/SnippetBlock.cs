using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Renders a SnippetModel as a justified TextBlock with the match
    /// highlighted in accent color. No HTML parsing — pure model binding.
    /// </summary>
    public class SnippetBlock : TextBlock
    {
        static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));

        public static readonly DependencyProperty SnippetProperty =
            DependencyProperty.Register(nameof(Snippet), typeof(SnippetModel),
                typeof(SnippetBlock),
                new PropertyMetadata(null, OnSnippetChanged));

        public SnippetModel Snippet
        {
            get => (SnippetModel)GetValue(SnippetProperty);
            set => SetValue(SnippetProperty, value);
        }

        static void OnSnippetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SnippetBlock tb)
                tb.Rebuild(e.NewValue as SnippetModel);
        }

        void Rebuild(SnippetModel model)
        {
            Inlines.Clear();
            TextWrapping  = TextWrapping.Wrap;
            TextAlignment = TextAlignment.Justify;
            FontSize      = 12;

            if (model == null) return;

            if (!string.IsNullOrEmpty(model.Before))
                Inlines.Add(new Run(model.Before));

            if (!string.IsNullOrEmpty(model.Match))
                Inlines.Add(new Run(model.Match)
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = AccentBrush
                });

            if (!string.IsNullOrEmpty(model.After))
                Inlines.Add(new Run(model.After));
        }
    }
}
