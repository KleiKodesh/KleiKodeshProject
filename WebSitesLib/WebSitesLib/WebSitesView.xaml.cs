using Microsoft.VisualBasic;
using System;
using System.Windows.Controls;

namespace WebSitesLib
{
    /// <summary>
    /// Interaction logic for WebSitesView.xaml
    /// </summary>
    public partial class WebSitesView : UserControl
    {
        public WebSitesView()
        {
            InitializeComponent();
            string selectedIndex = Interaction.GetSetting(AppDomain.CurrentDomain.FriendlyName, "WebSitesView", "SelectedIndex", AdressBar.SelectedIndex.ToString());
            if (int.TryParse(selectedIndex, out int index))
                AdressBar.SelectedIndex = index;
            browser.WebView.NavigationCompleted += WebView_NavigationCompleted;
            browser.Loaded += Browser_Loaded;
        }

        private void Browser_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            string lastPage = Interaction.GetSetting(AppDomain.CurrentDomain.FriendlyName, "History", "LastPage", "https://kleikodesh.github.io/");
            if (!string.IsNullOrEmpty(lastPage))
                browser.Navigate(lastPage);
        }

        private void WebView_NavigationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e) =>          
                Interaction.SaveSetting( AppDomain.CurrentDomain.FriendlyName,  "History",   "LastPage", browser.WebView.Source.ToString());

        private void AdressBar_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            Interaction.SaveSetting(AppDomain.CurrentDomain.FriendlyName, "WebSitesView", "SelectedIndex", AdressBar.SelectedIndex.ToString());

        private void GobackButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
            browser.WebView.GoBack();

        private void GoForwardButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
            browser.WebView.GoForward();

        private void RefreshButton_Click(object sender, System.Windows.RoutedEventArgs e) =>
            browser.WebView.CoreWebView2?.Reload();

        private void EditWhitListButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            vm.ShowWhiteListDialog(this.Background, this.Foreground);
        }
    }
}
