using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace RegexFindLib.UI
{
    public partial class FormattingPanel : UserControl
    {
        public FormattingPanel()
        {
            InitializeComponent();
        }

        // ── Dependency properties so the parent view can pass lists down ──

        public static readonly DependencyProperty FontListProperty =
            DependencyProperty.Register(nameof(FontList), typeof(ObservableCollection<string>),
                typeof(FormattingPanel), new PropertyMetadata(null));

        public ObservableCollection<string> FontList
        {
            get => (ObservableCollection<string>)GetValue(FontListProperty);
            set => SetValue(FontListProperty, value);
        }

        public static readonly DependencyProperty StyleListProperty =
            DependencyProperty.Register(nameof(StyleList), typeof(ObservableCollection<string>),
                typeof(FormattingPanel), new PropertyMetadata(null));

        public ObservableCollection<string> StyleList
        {
            get => (ObservableCollection<string>)GetValue(StyleListProperty);
            set => SetValue(StyleListProperty, value);
        }
    }
}
