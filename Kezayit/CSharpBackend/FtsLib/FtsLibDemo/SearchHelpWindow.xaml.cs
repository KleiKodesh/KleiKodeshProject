using System.Windows;

namespace FtsLibDemo
{
    public partial class SearchHelpWindow : Window
    {
        public SearchHelpWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
