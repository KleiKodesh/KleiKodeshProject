using RegexFindLib.UI;
using System.Windows;
using System.Windows.Media;

namespace RegexFindDemo
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Inject MockWordService — no Word installation needed
            var view = new RegexFindView(new MockWordService());
            //view.Background = Brushes.Black;
            //view.Foreground = Brushes.WhiteSmoke;
            ViewHost.Child = view;
        }
    }
}
