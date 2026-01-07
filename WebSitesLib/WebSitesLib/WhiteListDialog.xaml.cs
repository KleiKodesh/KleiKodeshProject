using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace WebSitesLib
{
    /// <summary>
    /// Interaction logic for WhiteListDialog.xaml
    /// </summary>
    public partial class WhiteListDialog : Window
    {
        WebSitesViewModel _viewModel;
        
        public WhiteListDialog(WebSitesViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            this.DataContext = _viewModel;
            
            CheckAllBox.IsChecked = viewModel.Adresses.All(adress => adress.IsVisible == true);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                this.Close();
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = Whitelist.SelectedItem as WebAdressModel;
            if (selected == null) return;

            if (_viewModel != null)
            {
                int index = _viewModel.Adresses.IndexOf(selected);
                if (index > 0)
                {
                    _viewModel.Adresses.Move(index, index - 1);
                    Whitelist.SelectedItem = selected;
                }
            }   
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = Whitelist.SelectedItem as WebAdressModel;
            if (selected == null) return;

            if (_viewModel != null)
            {
                int index = _viewModel.Adresses.IndexOf(selected);
                if (index < _viewModel.Adresses.Count - 1)
                {
                    _viewModel.Adresses.Move(index, index + 1);
                    Whitelist.SelectedItem = selected;
                }
            }
        }

        private void OK_Button_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void CheckAllBox_CheckChanged(object sender, RoutedEventArgs e)
        {
            foreach (WebAdressModel entry in _viewModel.Adresses)
                entry.IsVisible = CheckAllBox.IsChecked == true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void Window_TouchDown(object sender, TouchEventArgs e)
        {
            this.DragMove();
        }
    }
}
