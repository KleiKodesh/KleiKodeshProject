using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace WebSitesLib.UI
{
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

        void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) Close();
        }

        void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(Whitelist.SelectedItem is WebAddressModel selected)) return;
            int index = WebAddressModels.IndexOf(selected);
            if (index > 0)
            {
                WebAddressModels.Move(index, index - 1);
                Whitelist.SelectedItem = selected;
            }
        }

        void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (!(Whitelist.SelectedItem is WebAddressModel selected)) return;
            int index = WebAddressModels.IndexOf(selected);
            if (index < WebAddressModels.Count - 1)
            {
                WebAddressModels.Move(index, index + 1);
                Whitelist.SelectedItem = selected;
            }
        }

        void OK_Button_Click(object sender, RoutedEventArgs e) => Close();

        void CheckAllBox_CheckChanged(object sender, RoutedEventArgs e)
        {
            foreach (var entry in WebAddressModels)
                entry.IsVisible = CheckAllBox.IsChecked == true;
        }

        void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => DragMove();

        void Window_TouchDown(object sender, TouchEventArgs e)
            => DragMove();
    }
}
