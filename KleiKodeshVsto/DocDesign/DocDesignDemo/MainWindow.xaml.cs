using DocDesign;
using System.Windows;
using System.Windows.Media;

namespace DocDesignDemo
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // No Word installation needed — parameterless constructor
            var view = new DocDesignView();

            // Uncomment to test dark mode:
            // view.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            // view.Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 212));

            ViewHost.Child = view;
        }
    }
}
