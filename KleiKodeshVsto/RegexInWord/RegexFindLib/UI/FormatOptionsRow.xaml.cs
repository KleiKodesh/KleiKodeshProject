using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RegexFindLib.UI
{
    /// <summary>
    /// Format options toolbar — font size, bold, italic, underline, super/subscript, color, clear, copy.
    /// Pure Control — no .xaml file.
    /// Template lives in Themes/FormatOptionsRowStyles.xaml (merged by RegexFindDictionary.xaml).
    /// </summary>
    public class FormatOptionsRow : Control
    {
        // ── Dependency Properties ─────────────────────────────────────────────

        public static readonly DependencyProperty FontSizeValueProperty =
            DependencyProperty.Register(nameof(FontSizeValue), typeof(float),
                typeof(FormatOptionsRow), new FrameworkPropertyMetadata(0f,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public float FontSizeValue
        {
            get => (float)GetValue(FontSizeValueProperty);
            set => SetValue(FontSizeValueProperty, value);
        }

        public static readonly DependencyProperty BoldProperty =
            DependencyProperty.Register(nameof(Bold), typeof(bool?),
                typeof(FormatOptionsRow), new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public bool? Bold
        {
            get => (bool?)GetValue(BoldProperty);
            set => SetValue(BoldProperty, value);
        }

        public static readonly DependencyProperty ItalicProperty =
            DependencyProperty.Register(nameof(Italic), typeof(bool?),
                typeof(FormatOptionsRow), new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public bool? Italic
        {
            get => (bool?)GetValue(ItalicProperty);
            set => SetValue(ItalicProperty, value);
        }

        public static readonly DependencyProperty UnderlineProperty =
            DependencyProperty.Register(nameof(Underline), typeof(bool?),
                typeof(FormatOptionsRow), new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public bool? Underline
        {
            get => (bool?)GetValue(UnderlineProperty);
            set => SetValue(UnderlineProperty, value);
        }

        public static readonly DependencyProperty SuperscriptProperty =
            DependencyProperty.Register(nameof(Superscript), typeof(bool?),
                typeof(FormatOptionsRow), new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public bool? Superscript
        {
            get => (bool?)GetValue(SuperscriptProperty);
            set => SetValue(SuperscriptProperty, value);
        }

        public static readonly DependencyProperty SubscriptProperty =
            DependencyProperty.Register(nameof(Subscript), typeof(bool?),
                typeof(FormatOptionsRow), new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public bool? Subscript
        {
            get => (bool?)GetValue(SubscriptProperty);
            set => SetValue(SubscriptProperty, value);
        }

        public static readonly DependencyProperty TextColorProperty =
            DependencyProperty.Register(nameof(TextColor), typeof(Color?),
                typeof(FormatOptionsRow), new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public Color? TextColor
        {
            get => (Color?)GetValue(TextColorProperty);
            set => SetValue(TextColorProperty, value);
        }

        public static readonly DependencyProperty TextColorWordDecimalProperty =
            DependencyProperty.Register(nameof(TextColorWordDecimal), typeof(int?),
                typeof(FormatOptionsRow), new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        public int? TextColorWordDecimal
        {
            get => (int?)GetValue(TextColorWordDecimalProperty);
            set => SetValue(TextColorWordDecimalProperty, value);
        }

        public static readonly DependencyProperty ClearCommandProperty =
            DependencyProperty.Register(nameof(ClearCommand), typeof(ICommand),
                typeof(FormatOptionsRow));
        public ICommand ClearCommand
        {
            get => (ICommand)GetValue(ClearCommandProperty);
            set => SetValue(ClearCommandProperty, value);
        }

        public static readonly DependencyProperty CopyCommandProperty =
            DependencyProperty.Register(nameof(CopyCommand), typeof(ICommand),
                typeof(FormatOptionsRow));
        public ICommand CopyCommand
        {
            get => (ICommand)GetValue(CopyCommandProperty);
            set => SetValue(CopyCommandProperty, value);
        }
    }
}
