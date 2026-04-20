using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace KleiKodeshVstoInstallerWpf
{
    public partial class WhitelistEditorDialog : Window
    {
        public ObservableCollection<WhitelistEntry> Entries { get; }

        public WhitelistEditorDialog(ObservableCollection<WhitelistEntry> entries)
        {
            InitializeComponent();
            Entries = entries;
            SitesList.ItemsSource = Entries;
        }

        private void MoveUpButton_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as Button)?.Tag as WhitelistEntry;
            if (item == null) return;
            int idx = Entries.IndexOf(item);
            if (idx > 0) { Entries.Move(idx, idx - 1); SitesList.SelectedItem = item; }
        }

        private void MoveDownButton_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as Button)?.Tag as WhitelistEntry;
            if (item == null) return;
            int idx = Entries.IndexOf(item);
            if (idx >= 0 && idx < Entries.Count - 1) { Entries.Move(idx, idx + 1); SitesList.SelectedItem = item; }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var item = (sender as Button)?.Tag as WhitelistEntry;
            if (item != null) Entries.Remove(item);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)     => DialogResult = true;
        private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void CheckAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var entry in Entries) entry.IsVisible = true;
        }

        private void UncheckAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var entry in Entries) entry.IsVisible = false;
        }
    }
}
