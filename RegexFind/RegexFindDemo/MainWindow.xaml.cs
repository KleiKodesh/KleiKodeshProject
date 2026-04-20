using RegexFindLib.UI;
using System.Windows;

namespace RegexFindDemo
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Inject MockWordService — no Word installation needed
            var view = new RegexFindView(new MockWordService());
            ViewHost.Child = view;
        }
    }
}
