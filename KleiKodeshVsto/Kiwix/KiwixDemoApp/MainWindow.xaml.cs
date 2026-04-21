using KiwixLib;
using System.Windows;

namespace KiwixDemoApp
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            kiwixHost.Child = new KiwixWebview();
        }
    }
}
