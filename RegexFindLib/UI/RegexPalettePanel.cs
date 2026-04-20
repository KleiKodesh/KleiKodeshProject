using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Scrollable list of regex tips — mirrors the HTML regex-palette panel.
    /// </summary>
    public class RegexPalettePanel : Control
    {
        static readonly (string Symbol, string Meaning, string Example)[] Tips =
        {
            (".",          "כל תו בודד",                              "a.b → acb, a1b"),
            ("*",          "אפס או יותר מהאלמנט הקודם",              "a* → '', a, aa"),
            ("+",          "אחד או יותר מהאלמנט הקודם",              "a+ → a, aa"),
            ("?",          "אפס או אחד מהאלמנט הקודם",               "a? → '', a"),
            ("\\d",        "ספרה",                                    "\\d → 0-9"),
            ("\\w",        "תו מילה",                                 "\\w → a-z, 0-9, _"),
            ("\\s",        "רווח לבן",                                "\\s → רווח, טאבים, שורות חדשות"),
            ("[abc]",      "כל אחד מ-א', ב', או ג'",                 "[אבג] → א' או ב' או ג'"),
            ("[0-9]",      "כל ספרה",                                 "[0-9] → ספרה כלשהי"),
            ("^",          "תחילת מחרוזת או שורה",                   "^אבג → המחרוזת אבג בתחילה"),
            ("$",          "סיום מחרוזת או שורה",                    "אבג$ → המחרוזת אבג בסוף"),
            ("|",          "אלטרנטיבה (או)",                          "שור|כבש → שור או כבש"),
            ("(...)",      "קבוצת לכידה",                             "(אב)+ → אב, אבאב"),
            ("\\b",        "גבול מילה",                               "\\bכבש\\b → כבש אבל לא כבשה"),
            ("(?:...)",    "קבוצה לא לוכדת",                          "(?:ab)+ → ללא לכידה"),
            ("(?=...)",    "ציפייה חיובית",                           "א(?=ב) → א לפני ב"),
            ("(?!...)",    "ציפייה שלילית",                           "א(?!ב) → א לא לפני ב"),
            ("(?<=...)",   "ציפייה לאחור חיובית",                     "(?<=א)ב → ב אחרי א"),
            ("(?<!...)",   "ציפייה לאחור שלילית",                     "(?<!א)ב → ב לא אחרי א"),
            ("{n}",        "בדיוק n חזרות",                           "a{3} → aaa"),
            ("{n,m}",      "בין n ל-m חזרות",                         "a{2,4} → aa, aaa, aaaa"),
            (".*?",        "התאמה עצלה",                              "(ב.*?) → בב מתוך בבבב"),
            ("$1",         "התייחסות לקבוצה",                         "$1 → קבוצה מספר 1"),
            ("$0",         "כל הטקסט שנמצא",                          "$0 → כל הטקסט שהתאים"),
            ("$1, $2",     "קבוצות לכידה מרובות",                     "(\\w+) (\\w+) → $2 $1"),
        };

        static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8));
        static readonly Brush SecBrush    = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
        }

        protected override Size MeasureOverride(Size constraint) => constraint;

        // Build the visual tree on first layout
        ScrollViewer _scroll;

        protected override Size ArrangeOverride(Size arrangeBounds)
        {
            if (_scroll == null)
                BuildContent();
            _scroll?.Arrange(new Rect(arrangeBounds));
            return arrangeBounds;
        }

        void BuildContent()
        {
            var stack = new StackPanel { Margin = new Thickness(5) };

            foreach (var (symbol, meaning, example) in Tips)
            {
                var item = new StackPanel
                {
                    Margin = new Thickness(0, 0, 0, 2),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                // Symbol + meaning row
                var row = new WrapPanel();
                row.Children.Add(new TextBlock
                {
                    Text = symbol,
                    FontFamily = new FontFamily("Courier New"),
                    FontWeight = FontWeights.Bold,
                    Foreground = AccentBrush,
                    MinWidth = 70,
                    Margin = new Thickness(0, 0, 8, 0)
                });
                row.Children.Add(new TextBlock
                {
                    Text = meaning,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
                });
                item.Children.Add(row);

                // Example row
                item.Children.Add(new TextBlock
                {
                    Text = example,
                    FontFamily = new FontFamily("Courier New"),
                    FontSize = 11,
                    Foreground = SecBrush,
                    Margin = new Thickness(70, 0, 0, 0)
                });

                // Hover effect via Border wrapper
                var border = new Border
                {
                    Padding = new Thickness(8, 4, 8, 4),
                    CornerRadius = new CornerRadius(4),
                    Child = item
                };
                border.MouseEnter += (_, __) => border.Background = new SolidColorBrush(Color.FromArgb(0x0A, 0, 0, 0));
                border.MouseLeave += (_, __) => border.Background = Brushes.Transparent;

                stack.Children.Add(border);
            }

            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = stack
            };

            AddVisualChild(_scroll);
            AddLogicalChild(_scroll);
        }

        protected override int VisualChildrenCount => _scroll != null ? 1 : 0;
        protected override System.Windows.Media.Visual GetVisualChild(int index) => _scroll;
    }
}
