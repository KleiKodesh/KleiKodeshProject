using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Renders a SnippetModel — context text with the match highlighted in accent.
    /// Derives from Control so Foreground/Background inherit from the theme automatically.
    /// Template defined in RegexFindDictionary.xaml (PART_Text).
    /// </summary>
    [TemplatePart(Name = "PART_Text", Type = typeof(TextBlock))]
    public class SnippetBlock : Control
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
            if (d is SnippetBlock sb)
                sb.Rebuild(e.NewValue as SnippetModel);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            Rebuild(Snippet);
        }

        void Rebuild(SnippetModel model)
        {
            var tb = GetTemplateChild("PART_Text") as TextBlock;
            if (tb == null) return;

            tb.Inlines.Clear();
            if (model == null) return;

            if (!string.IsNullOrEmpty(model.Before))
                tb.Inlines.Add(new Run(model.Before));

            if (!string.IsNullOrEmpty(model.Match))
                tb.Inlines.Add(new Run(model.Match)
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = AccentBrush
                });

            if (!string.IsNullOrEmpty(model.After))
                tb.Inlines.Add(new Run(model.After));
        }
    }
}
