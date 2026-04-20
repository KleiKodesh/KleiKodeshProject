using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RegexFindLib.UI
{
    public partial class FormatOptionsRow : UserControl
    {
        public FormatOptionsRow()
        {
            InitializeComponent();
        }

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

        // ── Mutual exclusion: superscript and subscript can't both be true ────

        void SuperscriptCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (Subscript == true)
                Subscript = null;
        }

        void SubscriptCheck_Checked(object sender, RoutedEventArgs e)
        {
            if (Superscript == true)
                Superscript = null;
        }
    }
}
