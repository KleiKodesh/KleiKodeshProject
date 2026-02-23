using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace WebSitesLib2
{
    /// <summary>
    /// Interaction logic for WhiteListDialog.xaml
    /// </summary>
    public partial class WhiteListDialog : Window
    {
        public ObservableCollection<WebAddressModel> WebAddressModels { get; set; }

        public WhiteListDialog(ObservableCollection<WebAddressModel> webAddressModels)
        {
            InitializeComponent();
            WebAddressModels = webAddressModels ?? new ObservableCollection<WebAddressModel>();
            DataContext = this;
            CheckAllBox.IsChecked = WebAddressModels.All(m => m.IsVisible == true);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                this.Close();
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = Whitelist.SelectedItem as WebAddressModel;
            if (selected == null)
                return;

            int index = WebAddressModels.IndexOf(selected);
            if (index > 0)
            {
                WebAddressModels.Move(index, index - 1);
                Whitelist.SelectedItem = selected;
            }
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = Whitelist.SelectedItem as WebAddressModel;
            if (selected == null)
                return;

            int index = WebAddressModels.IndexOf(selected);
            if (index < WebAddressModels.Count - 1)
            {
                WebAddressModels.Move(index, index + 1);
                Whitelist.SelectedItem = selected;
            }
        }

        private void OK_Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void CheckAllBox_CheckChanged(object sender, RoutedEventArgs e)
        {
            foreach (WebAddressModel entry in WebAddressModels)
                entry.IsVisible = CheckAllBox.IsChecked == true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void Window_TouchDown(object sender, TouchEventArgs e)
        {
            this.DragMove();
        }
    }
}