using System.Windows;
using System.Windows.Controls;

namespace WebSitesLib
{
    public partial class WebSitesView
    {
        public WebSitesView()
        {
            InitializeComponent();
        }
        private void OverflowButton_Click(object sender, RoutedEventArgs e)
            => OverflowPopup.IsOpen = true;

        private void OverflowPopup_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => OverflowPopup.IsOpen = false;
    }
}
